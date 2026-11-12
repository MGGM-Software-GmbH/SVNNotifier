using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace CHD.SVN_Notifier;

/// <summary>Reserviert Arbeitskopien vor dem Prozessstart durch exklusives Anlegen einer gemeinsamen Datei.</summary>
internal sealed class WorkingCopyLease : IDisposable
{
    internal const string FileName = "svnnotifier-operation-v1.lock";
    private readonly List<Claim> claims = new();
    private Reservation? reservation;
    private static readonly Lazy<string> hostIdentity = new(ReadHostIdentity);
    internal string Root { get; }
    internal bool ReleaseFailed { get; private set; }

    private WorkingCopyLease(string root) => Root = root;

    private sealed class Claim(FileStream stream)
    {
        internal FileStream Stream { get; } = stream;
        internal string Content { get; set; } = "";
    }

    private sealed record Reservation(int protocol, string token, string ownerHost, int ownerPid, long ownerStartTicks,
        string action, DateTimeOffset startedUtc, string state, int? processId = null, long? processStartTicks = null,
        string executableName = "");

    /// <summary>Die rechnergebundene Kennung verhindert, dass lokale PIDs einer fremden Windows-Installation ausgewertet werden.</summary>
    private static string ReadHostIdentity()
    {
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = registry.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        string identity = key?.GetValue("MachineGuid") as string ?? throw new InvalidOperationException("Windows-Rechnerkennung fehlt.");
        if (!Guid.TryParse(identity, out var machineGuid)) throw new InvalidOperationException("Ungültige Windows-Rechnerkennung.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(machineGuid.ToString("N"))));
    }

    /// <summary>Reservierungsdateien behalten bis zur geprüften Freigabe dasselbe gegen Ersetzen geschützte Windows-Handle.</summary>
    private static FileStream OpenClaim(string path, bool create)
    {
        var handle = CreateFileW(ExtendedWindowsPath(path), create ? 0xC0010000u : 0x80010000u, create ? 1u : 0u,
            IntPtr.Zero, create ? 1u : 3u, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
        try { return new FileStream(handle, create ? FileAccess.ReadWrite : FileAccess.Read); }
        catch { handle.Dispose(); throw; }
    }

    /// <summary>Die Löschmarkierung gilt dem geprüften Handle, niemals einem inzwischen ausgetauschten Dateipfad.</summary>
    private static void DeleteClaim(FileStream stream)
    {
        byte delete = 1;
        if (!SetFileInformationByHandle(stream.SafeFileHandle, 4, ref delete, 1))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>Schreibt den Startzustand vor dem Startaufruf dauerhaft; ohne Kindprozessidentität ist eine umfassendere Prozessprüfung nötig.</summary>
    internal void MarkStarting() => WriteReservation(reservation! with { state = "starting" });

    /// <summary>Identifiziert den tatsächlichen SVN-Prozess auch bei später wiederverwendeter PID.</summary>
    internal void MarkStarted(Process process) => WriteReservation(reservation! with
    {
        state = "started", processId = process.Id, processStartTicks = process.StartTime.ToUniversalTime().Ticks
    });

    /// <summary>Jede betroffene Wurzel erhält denselben nachweisbaren Prozesszustand ohne Befehlsargumente.</summary>
    private void WriteReservation(Reservation value)
    {
        reservation = value;
        string content = JsonSerializer.Serialize(value);
        byte[] bytes = Encoding.UTF8.GetBytes(content + "\n");
        foreach (var claim in claims)
        {
            // Ein abgebrochener Zustandswechsel darf den zuvor dauerhaft geschriebenen Startzustand nicht zerstören.
            claim.Stream.Position = claim.Stream.Length;
            claim.Stream.Write(bytes);
            claim.Stream.Flush(true);
            claim.Content += content + "\n";
        }
    }

    /// <summary>Eine nicht lesbare Prozessidentität ist kein Nachweis für ein Prozessende.</summary>
    private static bool IsProcessInstanceGone(int processId, long startTicks)
    {
        if (processId <= 0 || startTicks <= 0) throw new InvalidOperationException("Ungültige Prozessidentität.");
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited || process.StartTime.ToUniversalTime().Ticks != startTicks;
        }
        catch (ArgumentException) { return true; /* Diese PID existiert nicht mehr. */ }
    }

    /// <summary>Alte Metadaten kennen keine Startzeit. Eine erst nach der Reservierung entstandene PID gehört nicht mehr zum Besitzer.</summary>
    private static bool IsLegacyOwnerGone(Reservation value)
    {
        try
        {
            using var process = Process.GetProcessById(value.ownerPid);
            return process.HasExited || process.StartTime.ToUniversalTime() > value.startedUtc.UtcDateTime;
        }
        catch (ArgumentException) { return true; /* Die alte Besitzer-PID existiert nicht mehr. */ }
    }

    /// <summary>Ohne bestätigte Kindprozessidentität warten wir auf das Ende aller möglicherweise beteiligten Windows-SVN-Programme.</summary>
    private static bool HasPossibleSvnProcess(string executableName, Func<string, bool>? isProcessRunning)
    {
        var names = new[] { "svn", "TortoiseProc", Config.SVNpath, Config.TortoiseSVNpath, executableName }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(Path.GetFileNameWithoutExtension)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string? name in names)
        {
            if (isProcessRunning is not null)
            {
                if (isProcessRunning(name!)) return true;
                continue;
            }
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Any(process => !process.HasExited)) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }

    /// <summary>Metadaten ohne Rechnerkennung werden nur für lokale Windows-Arbeitskopien automatisch übernommen.</summary>
    private static bool IsLocalWorkingCopy(string root) => !root.StartsWith(@"\\", StringComparison.Ordinal) &&
        new DriveInfo(Path.GetPathRoot(root)!).DriveType == DriveType.Fixed;

    /// <summary>Übernimmt verwaiste eigene Reservierungen unter exklusivem Handle. Aktive Vorgänge und fremde Dateien bleiben geschützt.</summary>
    internal static bool TryRecover(string root, out string reason, Func<string, bool>? isProcessRunning = null)
    {
        reason = "coordination-busy";
        try
        {
            using var stream = OpenClaim(Path.Combine(root, ".svn", FileName), false);
            if (stream.Length > 8192) { reason = "coordination-busy:invalid-reservation"; return false; }
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
            string content = reader.ReadToEnd();
            int lastLine = content.LastIndexOf('\n');
            // Alte Dateien enthalten ein einzelnes JSON-Objekt. Neue Dateien sind ein Journal mit abgeschlossenen Zeilen.
            // Eine beim Absturz nur teilweise geschriebene letzte Zeile hat noch keinen Prozessstart bestätigt.
            int previousLine = lastLine > 0 ? content.LastIndexOf('\n', lastLine - 1) : -1;
            string lastRecord = lastLine < 0 ? content : content[(previousLine + 1)..lastLine];
            Reservation? value;
            try { value = JsonSerializer.Deserialize<Reservation>(lastRecord); }
            catch (JsonException) when ("{\"protocol\":1,".StartsWith(content, StringComparison.Ordinal) ||
                "{\"protocol\":2,".StartsWith(content, StringComparison.Ordinal) ||
                content.StartsWith("{\"protocol\":1,", StringComparison.Ordinal) || content.StartsWith("{\"protocol\":2,", StringComparison.Ordinal))
            {
                // Auch der erste Schreibvorgang kann beim Absturz abbrechen. Ein belegtes Handle wurde bereits ausgeschlossen.
                // Solche eigenen Dateireste werden erst ohne möglicherweise beteiligte SVN-Prozesse übernommen.
                var recordedHost = System.Text.RegularExpressions.Regex.Match(content, "\"ownerHost\":\"(?<host>[^\"]*)\"");
                if (!IsLocalWorkingCopy(root) || recordedHost.Success && recordedHost.Groups["host"].Value != hostIdentity.Value)
                { reason = "coordination-busy:other-host"; return false; }
                if (HasPossibleSvnProcess("", isProcessRunning))
                { reason = "coordination-busy:possible-svn-process-active"; return false; }
                DeleteClaim(stream);
                reason = "incomplete-reservation-recovered";
                return true;
            }
            if (value is null || value.protocol is not (1 or 2) || !Guid.TryParseExact(value.token, "N", out _) ||
                value.ownerPid <= 0 || value.startedUtc == default || string.IsNullOrWhiteSpace(value.action))
            { reason = "coordination-busy:unverifiable-reservation"; return false; }
            if (value.protocol == 1 && !IsLocalWorkingCopy(root))
            { reason = "coordination-busy:other-host"; return false; }
            if (value.protocol == 2 && value.ownerHost != hostIdentity.Value)
            { reason = "coordination-busy:other-host"; return false; }
            if (!(value.protocol == 1 ? IsLegacyOwnerGone(value) : IsProcessInstanceGone(value.ownerPid, value.ownerStartTicks)))
            { reason = "coordination-busy:owner-active"; return false; }
            if (value.protocol == 1 || value.state == "starting")
            {
                if (value.protocol == 2 && string.IsNullOrWhiteSpace(value.executableName))
                { reason = "coordination-busy:invalid-reservation"; return false; }
                if (HasPossibleSvnProcess(value.executableName, isProcessRunning))
                { reason = "coordination-busy:possible-svn-process-active"; return false; }
            }
            else if (value.state == "started" && value.processId is int processId && value.processStartTicks is long startTicks)
            {
                if (!IsProcessInstanceGone(processId, startTicks))
                { reason = "coordination-busy:process-active"; return false; }
            }
            else if (value.state != "reserved" || value.processId is not null || value.processStartTicks is not null)
            { reason = "coordination-busy:process-start-unconfirmed"; return false; }
            DeleteClaim(stream);
            reason = "abandoned-reservation-recovered";
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is 2 or 3)
        { reason = "reservation-absent"; return false; }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 32)
        { return false; /* Ein anderer Notifier hält sein Handle weiterhin offen. */ }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or JsonException or System.Security.SecurityException)
        { reason = "coordination-busy:recovery-" + exception.GetType().Name; return false; }
    }

    /// <summary>Ermittelt die Wurzel ohne einen ungeschützten SVN-Aufruf oder Zugriff auf Datenbankinhalte.</summary>
    internal static string[] FindRoots(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath)) fullPath = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException();
        if (OperatingSystem.IsWindows()) fullPath = CanonicalWindowsPath(fullPath);
        var roots = new List<string>();
        for (DirectoryInfo? directory = new(fullPath); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, ".svn", "wc.db")))
                roots.Add(directory.FullName);
        if (roots.Count == 0) throw new InvalidOperationException("Keine SVN-Arbeitskopiewurzel gefunden.");
        // Eine äußere Arbeitskopie reservieren wir ebenfalls, damit eingebettete Externals nicht überlappen.
        return roots.ToArray();
    }

    /// <summary>Ein belegter oder nicht schreibbarer Koordinationspfad erlaubt keinen Prozessstart.</summary>
    internal static WorkingCopyLease? TryAcquire(string path, string action, out string root, out string reason,
        string executable = "", Action<string>? recovered = null)
    {
        root = Path.GetFullPath(path);
        reason = "";
        WorkingCopyLease? lease = null;
        try
        {
            var roots = FindRoots(path);
            root = roots[0];
            lease = new(root);
            using var owner = Process.GetCurrentProcess();
            lease.reservation = new(2, Guid.NewGuid().ToString("N"), hostIdentity.Value, owner.Id,
                owner.StartTime.ToUniversalTime().Ticks, action, DateTimeOffset.UtcNow, "reserved", executableName: Path.GetFileName(executable));
            foreach (string claimRoot in roots.Reverse())
            {
                string claimPath = Path.Combine(claimRoot, ".svn", FileName);
                FileStream stream;
                try
                {
                    stream = OpenClaim(claimPath, true);
                }
                catch (Win32Exception) when (File.Exists(claimPath))
                {
                    if (!TryRecover(claimRoot, out reason))
                    {
                        lease.Dispose();
                        return null;
                    }
                    recovered?.Invoke(claimRoot);
                    // Genau ein erneuter Versuch. Eine inzwischen gestartete andere Instanz gewinnt ihre Reservierung.
                    try { stream = OpenClaim(claimPath, true); }
                    catch (Win32Exception) when (File.Exists(claimPath))
                    {
                        reason = "coordination-busy";
                        lease.Dispose();
                        return null;
                    }
                }
                lease.claims.Add(new Claim(stream));
                lease.WriteReservation(lease.reservation);
            }
            return lease;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or System.Security.SecurityException)
        {
            lease?.Dispose();
            reason = "coordination-error:" + exception.GetType().Name;
            return null;
        }
    }

    /// <summary>Entfernt nur eigene, unveränderte Reservierungen. Fremde oder unlesbare Dateien bleiben bestehen.</summary>
    public void Dispose()
    {
        foreach (var claim in claims.AsEnumerable().Reverse())
        {
            try
            {
                claim.Stream.Position = 0;
                using var reader = new StreamReader(claim.Stream, Encoding.UTF8, false, 1024, leaveOpen: true);
                if (reader.ReadToEnd() == claim.Content)
                    DeleteClaim(claim.Stream);
                else ReleaseFailed = true;
            }
            catch (IOException) { ReleaseFailed = true; /* Bleibt sichtbar belegt, falls die Freigabe nicht möglich ist. */ }
            catch (UnauthorizedAccessException) { ReleaseFailed = true; /* Keine fremde Sperre entfernen oder Rechte verändern. */ }
            catch (Win32Exception) { ReleaseFailed = true; /* Bei fehlender Löschbestätigung bleibt die Datei zur späteren Prüfung bestehen. */ }
            finally { claim.Stream.Dispose(); }
        }
        claims.Clear();
    }

    /// <summary>Vereinheitlicht auch Junctions, Laufwerksaliase und UNC-Pfade über das geöffnete Verzeichnis.</summary>
    private static string CanonicalWindowsPath(string path)
    {
        using var handle = CreateFileW(ExtendedWindowsPath(path), 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
        string result = buffer.ToString();
        if (result.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + result[8..];
        return result.StartsWith(@"\\?\", StringComparison.Ordinal) ? result[4..] : result;
    }

    /// <summary>Native Dateiöffnungen benötigen auch ohne globale Windows-Option die erweiterte Pfadsyntax.</summary>
    private static string ExtendedWindowsPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal)) return fullPath;
        return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + fullPath[2..] : @"\\?\" + fullPath;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref byte information, uint size);
}
