using System.Diagnostics;
using System.Buffers.Binary;
using System.Text.Json;

namespace CHD.SVN_Notifier;

internal sealed record ApplicationUpdatePlan(string TargetPath, string WorkingDirectory, int ParentId, long ParentStartTicks,
    string TargetHash, string PayloadHash, string Version, string Runtime, string ReadyEvent, int Language);

/// <summary>Ersetzt die beendete portable Anwendung in einem getrennten Prozess.</summary>
internal static class ApplicationUpdateInstaller
{
    /// <summary>Mehrdatei-Entwicklungsbuilds werden nicht durch eine einzelne EXE überschrieben.</summary>
    internal static bool IsPortable => portable.Value;
    private static readonly Lazy<bool> portable = new(() => IsSingleFileBundle(Environment.ProcessPath ?? throw new InvalidOperationException("Der Programmpfad ist nicht verfügbar.")));

    /// <summary>Prüft den .NET-Bundlemarker statt des Assemblypfads, der bei vollständiger Extraktion auch bei Einzel-EXEs gesetzt ist.</summary>
    internal static bool IsSingleFileBundle(string executable)
    {
        // Format und feste Signatur gemäß Microsoft.NET.HostModel.Bundle.Bundler: acht Byte Headeroffset vor dem Marker.
        ReadOnlySpan<byte> signature = [0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
            0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32, 0x13, 0xf5, 0xb9, 0xe6,
            0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae];
        using var input = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[65536 + 40];
        int retained = 0, count;
        while ((count = input.Read(buffer, retained, buffer.Length - retained)) > 0)
        {
            int length = retained + count;
            int position = buffer.AsSpan(0, length).IndexOf(signature);
            if (position >= 0)
            {
                if (position < sizeof(long)) return false;
                long offset = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(position - sizeof(long), sizeof(long)));
                return offset > 0 && offset < input.Length;
            }
            retained = Math.Min(40, length);
            buffer.AsSpan(length - retained, retained).CopyTo(buffer);
        }
        return false;
    }

    /// <summary>Startet eine Kopie der aktuellen Anwendung und wartet auf deren Installationsbereitschaft.</summary>
    internal static async Task StartAsync(PreparedApplicationUpdate update, int language)
    {
        if (!IsPortable)
            throw new InvalidOperationException("Automatic installation requires the portable release executable.");
        var target = Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unavailable.");
        using var parent = Process.GetCurrentProcess();
        var eventName = @"Local\SVNNotifier.Update." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        var plan = new ApplicationUpdatePlan(target, Environment.CurrentDirectory, parent.Id, parent.StartTime.ToUniversalTime().Ticks,
            await ApplicationUpdater.HashAsync(target, CancellationToken.None), update.ExecutableHash,
            update.Version.ToString(), ApplicationUpdater.RuntimeIdentifier, eventName, language);
        var planPath = Path.Combine(update.DirectoryPath, "update.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan));
        var installerPath = Path.Combine(update.DirectoryPath, "installer.exe");
        File.Copy(target, installerPath, false);
        var start = new ProcessStartInfo(installerPath) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = update.DirectoryPath };
        start.ArgumentList.Add("--apply-update");
        start.ArgumentList.Add(planPath);
        using var installer = Process.Start(start) ?? throw new InvalidOperationException("The update installer could not be started.");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!ready.WaitOne(0))
        {
            if (installer.HasExited)
            {
                var errorPath = Path.Combine(update.DirectoryPath, "installer-error.txt");
                var reason = File.Exists(errorPath) ? await File.ReadAllTextAsync(errorPath) : $"Installer exit code: {installer.ExitCode}";
                throw new InvalidOperationException(reason);
            }
            if (DateTime.UtcNow >= deadline)
            {
                installer.Kill();
                installer.WaitForExit();
                throw new TimeoutException("The update installer did not become ready. The application was not replaced.");
            }
            await Task.Delay(100);
        }
    }

    /// <summary>Prüft die Übergabe und wartet vor dem atomaren Austausch auf genau den ursprünglichen Prozess.</summary>
    internal static void Apply(string planPath)
    {
        try
        {
            ApplyCore(planPath);
        }
        catch (Exception exception)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(planPath))!;
            var name = Path.GetFileName(directory);
            if (Environment.ProcessPath == Path.Combine(directory, "installer.exe") &&
                name.StartsWith(ApplicationUpdater.StagePrefix, StringComparison.Ordinal) &&
                Guid.TryParseExact(name[ApplicationUpdater.StagePrefix.Length..], "N", out _))
                File.WriteAllText(Path.Combine(directory, "installer-error.txt"), exception.Message);
            throw;
        }
    }

    /// <summary>Führt den geprüften Dateiaustausch aus; Fehler werden an den ursprünglichen Prozess übergeben.</summary>
    private static void ApplyCore(string planPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(planPath))!;
        var plan = JsonSerializer.Deserialize<ApplicationUpdatePlan>(File.ReadAllText(planPath))
            ?? throw new InvalidDataException("The update plan is empty.");
        var helperPath = Environment.ProcessPath ?? "";
        if (Path.GetFullPath(helperPath) != Path.Combine(directory, "installer.exe") ||
            Path.GetFileName(planPath) != "update.json" || Path.GetDirectoryName(directory) != Path.GetDirectoryName(plan.TargetPath) ||
            !Path.GetFileName(directory).StartsWith(ApplicationUpdater.StagePrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(Path.GetFileName(directory)[ApplicationUpdater.StagePrefix.Length..], "N", out _) ||
            !Path.IsPathFullyQualified(plan.TargetPath) || Path.GetFileName(plan.TargetPath) != ApplicationUpdater.ExecutableName ||
            !plan.ReadyEvent.StartsWith(@"Local\SVNNotifier.Update.", StringComparison.Ordinal) ||
            !Version.TryParse(plan.Version, out var version) || version <= ApplicationUpdater.CurrentVersion ||
            plan.Runtime != ApplicationUpdater.RuntimeIdentifier)
            throw new InvalidDataException("Invalid application update plan.");
        var payload = Path.Combine(directory, "payload.exe");
        ApplicationUpdater.ValidateExecutable(payload, version, plan.Runtime);
        if (ApplicationUpdater.HashAsync(payload, CancellationToken.None).GetAwaiter().GetResult() != plan.PayloadHash)
            throw new InvalidDataException("The staged executable has changed.");
        using var parent = Process.GetProcessById(plan.ParentId);
        if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStartTicks ||
            !string.Equals(parent.MainModule?.FileName, plan.TargetPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The original application process does not match.");
        using (var ready = EventWaitHandle.OpenExisting(plan.ReadyEvent))
            ready.Set();
        if (!parent.WaitForExit(60000))
            throw new TimeoutException("The original application is still running. It was not replaced.");
        if (ApplicationUpdater.HashAsync(plan.TargetPath, CancellationToken.None).GetAwaiter().GetResult() != plan.TargetHash)
            throw new InvalidDataException("The installed executable has changed. It was not replaced.");
        if (ApplicationUpdater.HashAsync(payload, CancellationToken.None).GetAwaiter().GetResult() != plan.PayloadHash)
            throw new InvalidDataException("The staged executable has changed. Installation stopped.");
        File.Replace(payload, plan.TargetPath, Path.Combine(directory, "previous.exe"));
        using var self = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(plan.TargetPath) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = plan.WorkingDirectory };
        start.ArgumentList.Add("--update-cleanup");
        start.ArgumentList.Add(directory);
        start.ArgumentList.Add(self.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add(self.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var restarted = Process.Start(start) ?? throw new InvalidOperationException("The updated application could not be restarted.");
    }

    /// <summary>Entfernt nach dem Neustart die eigene Installationskopie, sobald deren Prozess beendet ist.</summary>
    internal static async Task CleanupAsync(string directory, int helperId, long helperStartTicks)
    {
        try
        {
            using var helper = Process.GetProcessById(helperId);
            if (helper.StartTime.ToUniversalTime().Ticks == helperStartTicks)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await helper.WaitForExitAsync(timeout.Token);
            }
        }
        catch (ArgumentException)
        {
            // Der Hilfsprozess kann vor dem Start der neuen Anwendung bereits beendet sein.
        }
        ApplicationUpdater.DeleteStage(directory, Environment.ProcessPath!);
    }
}
