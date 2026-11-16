using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CHD.SVN_Notifier;

internal sealed record NotificationContext(string Id, NotificationKind Kind, IReadOnlyList<string> Paths,
    string Executable, string Configuration, string WorkingDirectory, int ProcessId, long ProcessStartTicks, DateTimeOffset Created);

/// <summary>Die Windows-Payload enthält nur eine ID; Pfade und Konfiguration stammen aus dem eigenen Kontextspeicher.</summary>
internal sealed class NotificationContextStore
{
    private readonly string directory;
    private readonly Action<Exception> report;
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    internal NotificationContextStore(string directory, Action<Exception>? report = null)
    {
        this.directory = Path.GetFullPath(directory);
        this.report = report ?? (exception => Trace.TraceError(exception.ToString()));
    }
    internal static string Identity(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant())))[..24];
    internal static string PipeName(int processId, long startTicks) => "SVNNotifier.Notification." + processId + "." + startTicks;

    internal NotificationContext Create(NotificationRequest request, string executable, string configuration)
    {
        using var process = Process.GetCurrentProcess();
        var context = new NotificationContext(Guid.NewGuid().ToString("N"), request.Kind, request.Paths.ToArray(),
            Path.GetFullPath(executable), Path.GetFullPath(configuration), Environment.CurrentDirectory,
            process.Id, process.StartTime.ToUniversalTime().Ticks, DateTimeOffset.UtcNow);
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, context.Id + ".json");
        string temporary = target + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, context);
                output.Flush(true);
            }
            File.Move(temporary, target);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return context;
    }

    internal NotificationContext Read(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Ungültiger Benachrichtigungskontext.");
        string path = Path.Combine(directory, id + ".json");
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Benachrichtigungskontext ist zu groß.");
        // Leser teilen Löschzugriffe, damit atomare Veröffentlichung und Bereinigung keine Lesesperre erzeugen.
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var context = JsonSerializer.Deserialize<NotificationContext>(input) ?? throw new InvalidDataException("Benachrichtigungskontext fehlt.");
        if (context.Id != id || !Enum.IsDefined(context.Kind) || context.Paths is null ||
            !Path.IsPathFullyQualified(context.Executable) || !Path.IsPathFullyQualified(context.Configuration) ||
            !Path.IsPathFullyQualified(context.WorkingDirectory) || context.Paths.Any(path => !Path.IsPathFullyQualified(path)))
            throw new InvalidDataException("Ungültiger Benachrichtigungskontext.");
        return context;
    }

    internal IEnumerable<NotificationContext> ReadConfiguration(string configuration)
    {
        if (!Directory.Exists(directory)) yield break;
        RemoveExpired();
        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var context = ReadForReconciliation(file);
            if (context is not null && string.Equals(context.Configuration, configuration, StringComparison.OrdinalIgnoreCase)) yield return context;
        }
    }

    /// <summary>Isoliert beschädigte Kontexte mit Diagnose, ohne gültige Klickziele anderer Meldungen zu blockieren.</summary>
    private NotificationContext? ReadForReconciliation(string file)
    {
        try { return Read(Path.GetFileNameWithoutExtension(file)); }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            report(exception);
            try { if (File.Exists(file)) File.Move(file, file + "." + Guid.NewGuid().ToString("N") + ".invalid"); }
            catch (Exception quarantineError) when (quarantineError is IOException or UnauthorizedAccessException) { report(quarantineError); }
            return null;
        }
    }

    /// <summary>Bewahrt Klickziele länger als die maximal dreitägige Windows-Meldung und entfernt ältere eigene Dateien.</summary>
    internal void RemoveExpired()
    {
        var cutoff = DateTime.UtcNow.Subtract(Retention);
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            string name = Path.GetFileName(file);
            if (name.Length < 32 || !Guid.TryParseExact(name[..32], "N", out _) ||
                !(name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".json.tmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))) continue;
            try { if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { report(exception); }
        }
    }

    internal static bool IsOriginRunning(NotificationContext context)
    {
        try
        {
            using var process = Process.GetProcessById(context.ProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == context.ProcessStartTicks;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return false; }
    }
}
