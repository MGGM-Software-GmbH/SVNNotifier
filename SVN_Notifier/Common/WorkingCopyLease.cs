using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace CHD.SVN_Notifier;

/// <summary>Reserviert Arbeitskopien vor dem Prozessstart durch exklusives Anlegen einer gemeinsamen Datei.</summary>
internal sealed class WorkingCopyLease : IDisposable
{
    internal const string FileName = "svnnotifier-operation-v1.lock";
    private readonly List<(string Path, string Content, FileStream Stream)> claims = new();
    internal string Root { get; }
    internal bool ReleaseFailed { get; private set; }

    private WorkingCopyLease(string root) => Root = root;

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
    internal static WorkingCopyLease? TryAcquire(string path, string action, out string root, out string reason)
    {
        root = Path.GetFullPath(path);
        reason = "";
        WorkingCopyLease? lease = null;
        try
        {
            var roots = FindRoots(path);
            root = roots[0];
            lease = new(root);
            foreach (string claimRoot in roots.Reverse())
            {
                string claimPath = Path.Combine(claimRoot, ".svn", FileName);
                FileStream stream;
                try
                {
                    stream = new FileStream(claimPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                }
                catch (IOException) when (File.Exists(claimPath))
                {
                    reason = "coordination-busy";
                    lease.Dispose();
                    return null;
                }
                string content = JsonSerializer.Serialize(new { protocol = 1, token = Guid.NewGuid().ToString("N"), ownerPid = Environment.ProcessId, action, startedUtc = DateTimeOffset.UtcNow });
                lease.claims.Add((claimPath, content, stream));
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes);
                stream.Flush(true);
            }
            return lease;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
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
            claim.Stream.Dispose();
            try
            {
                if (File.ReadAllText(claim.Path, Encoding.UTF8) == claim.Content)
                    File.Delete(claim.Path);
                else ReleaseFailed = true;
            }
            catch (IOException) { ReleaseFailed = true; /* Bleibt sichtbar belegt, falls die Freigabe nicht möglich ist. */ }
            catch (UnauthorizedAccessException) { ReleaseFailed = true; /* Keine fremde Sperre entfernen oder Rechte verändern. */ }
        }
        claims.Clear();
    }

    /// <summary>Vereinheitlicht auch Junctions, Laufwerksaliase und UNC-Pfade über das geöffnete Verzeichnis.</summary>
    private static string CanonicalWindowsPath(string path)
    {
        using var handle = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
        string result = buffer.ToString();
        if (result.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + result[8..];
        return result.StartsWith(@"\\?\", StringComparison.Ordinal) ? result[4..] : result;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
