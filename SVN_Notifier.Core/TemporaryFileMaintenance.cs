using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CHD.SVN_Notifier;

/// <summary>Bereinigt eigene temporäre Dateien, ohne aktive Übergaben oder fremde Verzeichnisbäume zu löschen.</summary>
internal sealed class TemporaryFileMaintenance : IDisposable
{
    internal const string StagePrefix = ".svnnotifier-update-";
    internal const string StageLock = "active.lock";
    internal static readonly string[] StageFiles = ["package.zip", "payload.exe", "installer.exe", "previous.exe", "update.json", "installer-error.txt", StageLock];
    private const string CacheLock = ".svnnotifier-cache.lock";
    private readonly string executable;
    private readonly string temporaryRoot;
    private readonly string bundleRoot;
    private readonly string notificationsRoot;
    private readonly string currentBundle;
    private readonly Action<string> report;
    private readonly Func<bool> otherInstances;
    private readonly CancellationTokenSource cancellation = new();
    private FileStream? bundleLease;
    private string? configuration;
    private Task? worker;

    /// <summary>Der explizite Testpfad hält sämtliche Gegenproben von den echten Benutzerdateien fern.</summary>
    internal TemporaryFileMaintenance(string executable, string temporaryRoot, string bundleRoot, string notificationsRoot,
        string currentBundle, Action<string> report, Func<bool>? otherInstances = null)
    {
        this.executable = Path.GetFullPath(executable);
        this.temporaryRoot = Path.GetFullPath(temporaryRoot);
        this.bundleRoot = Path.GetFullPath(bundleRoot);
        this.notificationsRoot = Path.GetFullPath(notificationsRoot);
        this.currentBundle = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentBundle));
        this.report = report;
        this.otherInstances = otherInstances ?? OtherInstancesRunning;
        if (IsCachePath(this.currentBundle))
            Attempt(() => bundleLease = OpenLease(Path.Combine(this.currentBundle, CacheLock)));
    }

    /// <summary>Startet die Wartung auch vor einer möglicherweise fehlschlagenden Shell-Registrierung.</summary>
    internal void Start(TimeSpan? interval = null)
    {
        if (worker is not null) throw new InvalidOperationException("Die Dateiwartung läuft bereits.");
        TimeSpan period = interval ?? TimeSpan.FromMinutes(15);
        if (period <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        worker = Task.Run(async () =>
        {
            Sweep();
            using var timer = new PeriodicTimer(period);
            try { while (await timer.WaitForNextTickAsync(cancellation.Token)) Sweep(); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        });
    }

    internal Task Completion => worker ?? Task.CompletedTask;

    internal void Configure(string path)
    {
        Volatile.Write(ref configuration, Path.GetFullPath(path));
        Attempt(() => CleanConfiguration(Path.GetFullPath(path)));
    }

    /// <summary>Aktive Downloads und Installer teilen eine Sperrdatei; nur der Bereiniger benötigt exklusiven Zugriff.</summary>
    internal static FileStream OpenLease(string path) => new(path, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);

    internal static (string Directory, FileStream Lease) CreateStage(string target)
    {
        string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!, StagePrefix + Guid.NewGuid().ToString("N"));
        FileStream? lease = null;
        IniFile.WithFileLock(Path.GetFullPath(target) + ".maintenance", () =>
        {
            Directory.CreateDirectory(directory);
            lease = OpenLease(Path.Combine(directory, StageLock));
        });
        return (directory, lease!);
    }

    /// <summary>Jeder Bereich wird getrennt geprüft; ein Schreibschutz verhindert nicht die Wartung anderer eigener Dateien.</summary>
    internal void Sweep()
    {
        Attempt(CleanStartupLogs);
        string? activeConfiguration = Volatile.Read(ref configuration);
        if (activeConfiguration is not null) Attempt(() => CleanConfiguration(activeConfiguration));
        Attempt(CleanStages);
        Attempt(CleanCaches);
        Attempt(() =>
        {
            if (!SafeDirectory(notificationsRoot)) return;
            foreach (string directory in Directory.EnumerateDirectories(notificationsRoot))
                if (Regex.IsMatch(Path.GetFileName(directory), "^[A-Fa-f0-9]{24}$") && SafeDirectory(directory))
                    new NotificationContextStore(directory, exception => report(exception.ToString())).RemoveExpired();
        });
    }

    private void CleanStartupLogs()
    {
        if (!SafeDirectory(temporaryRoot)) return;
        foreach (string file in Directory.EnumerateFiles(temporaryRoot, "SVNNotifier-startup-*.log"))
        {
            var match = Regex.Match(Path.GetFileName(file), @"^SVNNotifier-startup-(\d+)-(\d+)\.log$");
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int processId) ||
                !long.TryParse(match.Groups[2].Value, out long started) || File.GetLastWriteTimeUtc(file) >= DateTime.UtcNow.AddDays(-7)) continue;
            Attempt(() => { if (!ProcessRunning(processId, started)) DeleteFile(file); });
        }
    }

    private void CleanConfiguration(string path)
    {
        string directory = Path.GetDirectoryName(path)!;
        if (!SafeDirectory(directory)) return;
        IniFile.WithFileLock(path, () =>
        {
            foreach (string file in Directory.EnumerateFiles(directory, Path.GetFileName(path) + ".*.tmp"))
            {
                string suffix = Path.GetFileName(file)[(Path.GetFileName(path).Length + 1)..^4];
                if (Guid.TryParseExact(suffix, "N", out _)) DeleteFile(file);
            }
        });
        // Importdateien älterer Ausgaben besitzen keine Zielzuordnung. Frische Kandidaten bleiben deshalb erhalten.
        if (otherInstances()) return;
        foreach (string file in Directory.EnumerateFiles(directory, ".svnnotifier-import-*.ini"))
            if (Guid.TryParseExact(Path.GetFileName(file)[".svnnotifier-import-".Length..^4], "N", out _) && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1)) DeleteFile(file);
    }

    private void CleanStages()
    {
        string parent = Path.GetDirectoryName(executable)!;
        if (!SafeDirectory(parent)) return;
        bool others = otherInstances();
        foreach (string directory in Directory.EnumerateDirectories(parent, StagePrefix + "*"))
            Attempt(() =>
            {
                if (!Guid.TryParseExact(Path.GetFileName(directory)[StagePrefix.Length..], "N", out _) || !SafeDirectory(directory)) return;
                // Alte Ausgaben besitzen noch keine Sperrdatei. Solange eine weitere Instanz läuft, bleiben diese Übergaben erhalten.
                if (!File.Exists(Path.Combine(directory, StageLock)) && others) return;
                DeleteStage(directory, executable);
            });
    }

    private void CleanCaches()
    {
        if (!SafeDirectory(bundleRoot)) return;
        bool others = otherInstances();
        foreach (string name in new[] { "SVN_Notifier", "installer", Path.GetFileNameWithoutExtension(executable) }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string parent = Path.Combine(bundleRoot, name);
            if (!SafeDirectory(parent)) continue;
            foreach (string directory in Directory.EnumerateDirectories(parent))
                Attempt(() =>
                {
                    if (SamePath(directory, currentBundle) || !IsCachePath(directory)) return;
                    if (others && !File.Exists(Path.Combine(directory, CacheLock))) return;
                    // Das generische Installer-Verzeichnis wird nur mit der eigenen Assembly als Herkunftsnachweis bereinigt.
                    if (name.Equals("installer", StringComparison.OrdinalIgnoreCase) &&
                        (!File.Exists(Path.Combine(directory, "SVN_Notifier.dll")) ||
                         System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(directory, "SVN_Notifier.dll")).Name != "SVN_Notifier")) return;
                    DeleteTree(directory);
                });
        }
    }

    /// <summary>Prüft alle Dateien und Sperren vor der ersten Löschung; Verknüpfungen werden niemals verfolgt.</summary>
    internal static void DeleteStage(string directory, string target)
        => IniFile.WithFileLock(Path.GetFullPath(target) + ".maintenance", () => DeleteStageCore(directory, target));

    private static void DeleteStageCore(string directory, string target)
    {
        directory = Path.GetFullPath(directory);
        string name = Path.GetFileName(directory);
        if (!SamePath(Path.GetDirectoryName(directory)!, Path.GetDirectoryName(Path.GetFullPath(target))!) ||
            !name.StartsWith(StagePrefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[StagePrefix.Length..], "N", out _) || !SafeDirectory(directory))
            throw new InvalidDataException("Ungültiges Update-Verzeichnis.");
        if (Directory.EnumerateDirectories(directory).Any() || Directory.EnumerateFiles(directory).Any(file => !StageFiles.Contains(Path.GetFileName(file), StringComparer.Ordinal)))
            throw new InvalidDataException("Unerwarteter Inhalt im Update-Verzeichnis.");
        DeleteTree(directory);
    }

    private static void DeleteTree(string directory)
    {
        var directories = new List<string>();
        var files = new List<string>();
        void Visit(string current)
        {
            if (!SafeDirectory(current)) throw new InvalidDataException("Verknüpfung im temporären Verzeichnis.");
            directories.Add(current);
            files.AddRange(Directory.EnumerateFiles(current));
            foreach (string child in Directory.EnumerateDirectories(current)) Visit(child);
        }
        Visit(directory);
        var handles = new List<FileStream>();
        try
        {
            foreach (string file in files)
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Verknüpfung in temporären Dateien.");
                handles.Add(new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete));
            }
            foreach (string file in files) File.Delete(file);
        }
        finally { foreach (var handle in handles) handle.Dispose(); }
        foreach (string path in directories.AsEnumerable().Reverse()) Directory.Delete(path);
    }

    private static void DeleteFile(string file)
    {
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) return;
        using var handle = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
        File.Delete(file);
    }

    private bool IsCachePath(string directory)
    {
        string? parent = Path.GetDirectoryName(directory);
        if (parent is null || !SamePath(Path.GetDirectoryName(parent) ?? "", bundleRoot) || !SafeDirectory(directory)) return false;
        return Regex.IsMatch(Path.GetFileName(directory), "^[A-Za-z0-9_-]{12}$") ||
            (Regex.IsMatch(Path.GetFileName(directory), "^[a-f0-9]{1,8}$") && Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-1));
    }

    private static bool SafeDirectory(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        return Directory.Exists(directory);
    }

    private static bool SamePath(string first, string second) => string.Equals(Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private bool OtherInstancesRunning()
    {
        foreach (string name in new[] { "SVN_Notifier", "installer", Path.GetFileNameWithoutExtension(executable) }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Any(process => process.Id != Environment.ProcessId && !process.HasExited)) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }

    private static bool ProcessRunning(int processId, long started)
    {
        try { using var process = Process.GetProcessById(processId); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == started; }
        catch (ArgumentException) { return false; }
    }

    private void Attempt(Action action)
    {
        try { action(); }
        catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33) { /* Aktive Dateien behalten ihre laufende Reservierung. */ }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or BadImageFormatException)
        { report(exception.ToString()); }
    }

    public void Dispose() { cancellation.Cancel(); bundleLease?.Dispose(); }
}
