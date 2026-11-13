using System.Diagnostics;
using System.Text.Json;

namespace CHD.SVN_Notifier;

internal sealed record SvnProcessResult(int? ExitCode, string Output, string Error, bool Cancelled, bool TimedOut = false, int TimeoutSeconds = 0)
{
    internal bool Success => ExitCode == 0 && !Cancelled;
}

/// <summary>Hält die Reservierung vom angenommenen Auftrag bis zum nachgewiesenen Prozessende.</summary>
internal sealed class SvnOperationRunner
{
    private readonly object gate = new();
    private readonly HashSet<Task<SvnProcessResult>> active = new();
    private readonly CancellationTokenSource stop = new();
    private CancellationTokenSource applicationUpdateStop = new();
    private readonly Action<string> diagnostic;
    private readonly Func<ProcessStartInfo, Process?> start;
    private readonly Action<string, string>? rejected;
    private bool stopping;
    private bool pausedForApplicationUpdate;

    internal SvnOperationRunner(Action<string> diagnostic, Func<ProcessStartInfo, Process?>? start = null, Action<string, string>? rejected = null)
    {
        this.diagnostic = diagnostic;
        this.start = start ?? Process.Start;
        this.rejected = rejected;
    }

    internal bool HasActiveOperations { get { lock (gate) return active.Any(task => !task.IsCompleted); } }

    /// <summary>Prüft beim Start ausschließlich die Wurzeln konfigurierter Einträge, bevor neue Aufträge angenommen werden.</summary>
    internal void RecoverReservations(IEnumerable<string> paths)
    {
        lock (gate)
        {
            var checkedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                try
                {
                    foreach (string root in WorkingCopyLease.FindRoots(path))
                    {
                        if (!checkedRoots.Add(root)) continue;
                        bool recovered = WorkingCopyLease.TryRecover(root, out string reason);
                        if (reason != "reservation-absent")
                            Log(root, "recovery", recovered ? "recover" : "recovery-skip", null, null, reason);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Log(path, "recovery", "recovery-skip", null, null, "coordination-error:" + exception.GetType().Name);
                }
            }
        }
    }

    /// <summary>Reserviert synchron, bevor ein verzögerter Worker den Prozess starten darf.</summary>
    internal Task<SvnProcessResult>? TryStart(string path, string action, string executable, IEnumerable<string> arguments,
        bool readOnly, int timeoutSeconds = 0, CancellationToken cancellation = default, bool captureOutput = true, bool tortoiseSyntax = false)
    {
        lock (gate)
        {
            if (stopping || pausedForApplicationUpdate)
            {
                Log(path, action, "skip", null, null, stopping ? "application-stopping" : "application-update");
                return null;
            }
            var lease = WorkingCopyLease.TryAcquire(path, action, out string root, out string reason, executable,
                recoveredRoot => Log(recoveredRoot, action, "recover", null, null, "abandoned-reservation-recovered"));
            if (lease is null)
            {
                Log(root, action, "skip", null, null, reason);
                rejected?.Invoke(root, reason);
                return null;
            }
            try
            {
                Log(root, action, "reserve", null, null, "");
                var values = arguments.ToArray();
                var updateCancellation = applicationUpdateStop.Token;
                var completion = Task.Run(() => ExecuteAsync(lease, action, executable, values, readOnly, timeoutSeconds, cancellation, captureOutput, tortoiseSyntax, updateCancellation));
                active.RemoveWhere(task => task.IsCompleted);
                active.Add(completion);
                return completion;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }
    }

    /// <summary>Bricht nur lesende Prüfungen ab. Schreibende Vorgänge behalten die Reservierung bis zum Ende.</summary>
    internal Task StopAsync()
    {
        lock (gate)
        {
            stopping = true;
            stop.Cancel();
            return Task.WhenAll(active.ToArray());
        }
    }

    /// <summary>Sperrt neue Aufträge und beendet für ein bestätigtes Programmupdate auch eigene Schreibvorgänge und Dialoge.</summary>
    internal Task PauseForApplicationUpdateAsync(TimeSpan? timeout = null)
    {
        lock (gate)
        {
            pausedForApplicationUpdate = true;
            applicationUpdateStop.Cancel();
            return Task.WhenAll(active.ToArray()).WaitAsync(timeout ?? TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>Erlaubt nach einem Installationsfehler neue Aufträge; alte Prozesse behalten ihre Reservierung bis zum Ende.</summary>
    internal void ResumeAfterApplicationUpdate()
    {
        lock (gate)
        {
            if (!pausedForApplicationUpdate) return;
            var previous = applicationUpdateStop;
            applicationUpdateStop = new CancellationTokenSource();
            pausedForApplicationUpdate = false;
            _ = Task.WhenAll(active.ToArray()).ContinueWith(_ => previous.Dispose(), TaskScheduler.Default);
        }
    }

    /// <summary>Liest beide Streams parallel und gibt auch bei Timeout erst nach dem Prozessende frei.</summary>
    private async Task<SvnProcessResult> ExecuteAsync(WorkingCopyLease lease, string action, string executable,
        string[] arguments, bool readOnly, int timeoutSeconds, CancellationToken cancellation, bool captureOutput, bool tortoiseSyntax,
        CancellationToken updateCancellation)
    {
        Process? process = null;
        CancellationTokenRegistration updateRegistration = default;
        bool cancelled = false;
        bool timedOut = false;
        int? exitCode = null;
        string outcome = "process-start-error";
        try
        {
            // Das Zeitlimit besitzt eine eigene Quelle, damit ein Benutzerabbruch nicht als Timeout gemeldet wird.
            using var timeoutCancellation = new CancellationTokenSource();
            if (readOnly && timeoutSeconds > 0) timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, cancellation, updateCancellation, timeoutCancellation.Token);
            if (linked.IsCancellationRequested)
            {
                outcome = "cancelled-before-start";
                return new(null, "", "Vorgang abgebrochen.", true);
            }
            var info = new ProcessStartInfo(executable)
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = captureOutput, RedirectStandardError = captureOutput
            };
            if (tortoiseSyntax)
                info.Arguments = string.Join(" ", arguments.Select(FormatTortoiseArgument));
            else
                foreach (string argument in arguments) info.ArgumentList.Add(argument);
            lease.MarkStarting();
            process = start(info) ?? throw new InvalidOperationException("Prozess nicht gestartet.");
            // Die Registrierung greift auch bei einem verzögerten Start und bei einem anschließenden Diagnosefehler.
            // Nur das bestätigte Programmupdate beendet Schreibprozesse; normales Beenden wartet weiterhin darauf.
            updateRegistration = updateCancellation.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* Der Prozess kann inzwischen beendet sein. */ }
                catch (System.ComponentModel.Win32Exception) { /* Ohne nachgewiesenes Ende darf der Installer nicht starten. */ }
            });
            // Beide Streams werden sofort parallel gelesen. Ein voller Fehlerstream darf weder Timeout noch Freigabe blockieren.
            // GUI-Programme erhalten keine Pipes: länger lebende GUI-Hilfsprozesse können geerbte Handles offen halten.
            var output = captureOutput ? process.StandardOutput.ReadToEndAsync() : Task.FromResult("");
            var error = captureOutput ? process.StandardError.ReadToEndAsync() : Task.FromResult("");
            lease.MarkStarted(process);
            Log(lease.Root, action, "begin", process.Id, null, "");
            try
            {
                await process.WaitForExitAsync(readOnly ? linked.Token : updateCancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                timedOut = timeoutCancellation.IsCancellationRequested && !stop.IsCancellationRequested &&
                    !cancellation.IsCancellationRequested && !updateCancellation.IsCancellationRequested;
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* Der Prozess kann inzwischen beendet sein. */ }
                catch (System.ComponentModel.Win32Exception) { /* Reservierung bleibt bis zum tatsächlichen Ende bestehen. */ }
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            exitCode = process.ExitCode;
            cancelled |= updateCancellation.IsCancellationRequested;
            outcome = updateCancellation.IsCancellationRequested ? "application-update" : timedOut ? "timeout" : cancelled ? "cancelled" : exitCode == 0 ? "completed" : "process-error";
            return new(exitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false), cancelled, timedOut, timedOut ? timeoutSeconds : 0);
        }
        catch (Exception exception)
        {
            // Keine Argumente oder SVN-Ausgaben in die Diagnose übernehmen: Diese können Zugangsdaten enthalten.
            outcome = "process-error:" + exception.GetType().Name;
            return new(exitCode, "", "SVN-Prozessfehler: " + exception.GetType().Name, cancelled, timedOut, timedOut ? timeoutSeconds : 0);
        }
        finally
        {
            // Auch ein Diagnosefehler nach Process.Start darf die Reservierung niemals vorzeitig freigeben.
            if (process is not null)
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
                exitCode = process.ExitCode;
            }
            try { Log(lease.Root, action, "end", process?.Id, exitCode, outcome); }
            finally
            {
                updateRegistration.Dispose();
                process?.Dispose();
                lease.Dispose();
                if (lease.ReleaseFailed) Log(lease.Root, action, "release-error", null, exitCode, "claim-retained");
            }
        }
    }

    /// <summary>Der TortoiseSVN-Parser erwartet /path:"Wert" statt eines als Ganzes zitierten Arguments.</summary>
    private static string FormatTortoiseArgument(string argument)
    {
        if (!argument.StartsWith('/') || argument.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0)
            throw new ArgumentException("Ungültiges TortoiseSVN-Argument.");
        int separator = argument.IndexOf(':');
        string key = separator < 0 ? argument : argument[..separator];
        if (key.Contains(' ')) throw new ArgumentException("Ungültiger TortoiseSVN-Schalter.");
        return separator < 0 ? key : key + ":\"" + argument[(separator + 1)..] + "\"";
    }

    /// <summary>Protokolliert ausschließlich freigegebene Metadaten, niemals Befehlszeilen oder Ausgaben.</summary>
    private void Log(string root, string action, string phase, int? processId, int? exitCode, string reason) =>
        diagnostic(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, workingCopy = root, action, phase, processId, exitCode, reason }));
}
