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
    internal NotificationContextStore(string directory) => this.directory = Path.GetFullPath(directory);
    internal static string Identity(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant())))[..24];
    internal static string PipeName(int processId, long startTicks) => "SVNNotifier.Notification." + processId + "." + startTicks;

    internal NotificationContext Create(NotificationRequest request, string executable, string configuration)
    {
        using var process = Process.GetCurrentProcess();
        var context = new NotificationContext(Guid.NewGuid().ToString("N"), request.Kind, request.Paths.ToArray(),
            Path.GetFullPath(executable), Path.GetFullPath(configuration), Environment.CurrentDirectory,
            process.Id, process.StartTime.ToUniversalTime().Ticks, DateTimeOffset.UtcNow);
        Directory.CreateDirectory(directory);
        using var output = new FileStream(Path.Combine(directory, context.Id + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(output, context);
        return context;
    }

    internal NotificationContext Read(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Ungültiger Benachrichtigungskontext.");
        string path = Path.Combine(directory, id + ".json");
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Benachrichtigungskontext ist zu groß.");
        using var input = File.OpenRead(path);
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
        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var context = Read(Path.GetFileNameWithoutExtension(file));
            if (string.Equals(context.Configuration, configuration, StringComparison.OrdinalIgnoreCase)) yield return context;
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
