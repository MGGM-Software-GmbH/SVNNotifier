using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using CHD.SVN_Notifier;

internal static class CheckNowTests
{
    /// <summary>Prüft manuelle Priorität mit echten, kontrolliert angehaltenen Leseprozessen und isolierten Arbeitskopien.</summary>
    internal static async Task<int> RunAsync(string output)
    {
        int checks = 0;
        int previousTimeout = Config.SvnCommandTimeout;
        bool previousPause = Config.DoPauseAfterApplicationStartup;
        int previousPauseInterval = Config.PauseAfterApplicationStartupInterval;
        var previousFolders = Config.ReadSvnFolders();
        void Check(bool result, string description)
        {
            if (!result) throw new InvalidOperationException(description);
            checks++;
            Console.WriteLine("Jetzt prüfen: " + description);
        }
        try
        {
            Config.SvnCommandTimeout = 0;
            Config.DoPauseAfterApplicationStartup = false;
            foreach (var scenario in new[] { (Stage: "status", Shared: false, Forced: false), (Stage: "info", Shared: false, Forced: false), (Stage: "status", Shared: true, Forced: false), (Stage: "status", Shared: false, Forced: true) })
            {
                string root = Path.Combine(output, "check-now-" + scenario.Stage + "-" + scenario.Shared + "-" + scenario.Forced);
                var automatic = CreateItem(Path.Combine(root, "A"));
                var remaining = CreateItem(Path.Combine(root, "B"));
                var target = CreateItem(scenario.Shared ? Path.Combine(automatic.Path, "Unterordner") : Path.Combine(root, "Z"), !scenario.Shared);
                target.Enabled = false;
                Config.SaveSvnFolders(new BindingList<SvnItem> { automatic, remaining, target });
                using var coordinator = new ApplicationCoordinator(action => action());
                automatic = coordinator.Folders.Single(item => item.Path == automatic.Path);
                remaining = coordinator.Folders.Single(item => item.Path == remaining.Path);
                target = coordinator.Folders.Single(item => item.Path == target.Path);
                string hold = Path.Combine(automatic.Path, "hold-" + scenario.Stage);
                File.WriteAllText(hold, "Angehaltene automatische Leseprüfung ohne Zeitlimit.");
                var order = new ConcurrentQueue<string>();
                var manualFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                coordinator.StatusChanged += text =>
                {
                    foreach (var item in coordinator.Folders)
                        if (text == Loc.Status_Checking(item.Path)) order.Enqueue(item.Path);
                    if (text == Loc.Status_Checking(target.Path)) File.Delete(hold);
                };
                coordinator.Changed += () =>
                {
                    if (target.Status == SvnStatus.Unknown || manualFinished.Task.IsCompleted) return;
                    Check(remaining.Status == SvnStatus.Unknown, "Manuelles Ziel wird vor der übrigen automatischen Runde geprüft: " + scenario);
                    manualFinished.TrySetResult();
                };
                try
                {
                    if (scenario.Forced) coordinator.CheckNow(automatic);
                    Task running = coordinator.PollAsync();
                    await WaitForFileAsync(Path.Combine(automatic.Path, scenario.Stage + ".pid"));
                    int interruptedProcess = int.Parse(File.ReadAllText(Path.Combine(automatic.Path, scenario.Stage + ".pid")));
                    coordinator.CheckNow(target);
                    coordinator.CheckNow(target);
                    await manualFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Check(ProcessEnded(interruptedProcess), "Unterbrochener Leseprozess endet vor dem manuellen Ergebnis: " + scenario);
                    await running.WaitAsync(TimeSpan.FromSeconds(10));
                    await coordinator.PollAsync();
                    Check(order.SequenceEqual(new[] { automatic.Path, target.Path, automatic.Path, remaining.Path }),
                        "Manuelle Prüfung erfolgt genau einmal; unterbrochene und übrige Prüfung werden nachgeholt: " + scenario);
                    Check(coordinator.Errors.Count == 0 && automatic.Status == SvnStatus.NeedUpdate && automatic.RetryReady,
                        "Vorgezogene Prüfung erzeugt keinen Repository-Fehler und keine Wiederholsperre: " + scenario);
                    using var released = WorkingCopyLease.TryAcquire(automatic.Path, "check-now-released", out _, out _);
                    Check(released is not null, "Reservierung wird auch bei manueller Priorität freigegeben: " + scenario);
                }
                finally { File.Delete(hold); await coordinator.PollAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            }

            var current = CreateItem(Path.Combine(output, "check-now-current"));
            Config.SaveSvnFolders(new BindingList<SvnItem> { current });
            using (var coordinator = new ApplicationCoordinator(action => action()))
            {
                current = coordinator.Folders.Single();
                string hold = Path.Combine(current.Path, "hold-status");
                File.WriteAllText(hold, "Bereits laufende Prüfung des ausgewählten Eintrags.");
                try
                {
                    Task running = coordinator.PollAsync();
                    await WaitForFileAsync(Path.Combine(current.Path, "status.pid"));
                    int processId = int.Parse(File.ReadAllText(Path.Combine(current.Path, "status.pid")));
                    coordinator.CheckNow(current);
                    Check(!ProcessEnded(processId) && ReferenceEquals(running, coordinator.PollAsync()), "Bereits laufende Zielprüfung wird weiterverwendet.");
                    File.Delete(hold);
                    await running.WaitAsync(TimeSpan.FromSeconds(5));
                    await coordinator.PollAsync();
                    Check(StatusCalls(current) == 1, "Laufendes Ziel wird durch erneuten Klick nicht doppelt geprüft.");
                }
                finally { File.Delete(hold); await coordinator.PollAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            }

            foreach (bool enabled in new[] { false, true })
            {
                var deferred = CreateItem(Path.Combine(output, "check-now-deferred-" + enabled));
                deferred.Enabled = enabled;
                Config.SaveSvnFolders(new BindingList<SvnItem> { deferred });
                using var coordinator = new ApplicationCoordinator(action => action());
                deferred = coordinator.Folders.Single();
                deferred.Status = SvnStatus.UpToDate;
                try
                {
                    using (var occupied = WorkingCopyLease.TryAcquire(deferred.Path, "check-now-occupied", out _, out _))
                    {
                        Check(occupied is not null, "Gegenprobe reserviert die Arbeitskopie: " + enabled);
                        coordinator.CheckNow(deferred);
                        coordinator.CheckNow(deferred);
                        await coordinator.PollAsync();
                        Check(!deferred.RetryReady && StatusCalls(deferred) == 0 && deferred.Status == SvnStatus.UpToDate,
                            "Belegte Arbeitskopie behält Status und Wiederholsperre: " + enabled);
                    }
                    await coordinator.PollAsync();
                    Check(StatusCalls(deferred) == 0, "Manuelle Prüfung umgeht die 15-sekündige Wiederholsperre nicht: " + enabled);
                    // Nur die Testuhr vorsetzen; der Produktionspfad muss den erhaltenen Auftrag selbst nachholen.
                    typeof(SvnItem).GetField("retryAfterUtcTicks", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(deferred, 0L);
                    await coordinator.PollAsync();
                    Check(StatusCalls(deferred) == 1 && deferred.Status == SvnStatus.NeedUpdate,
                        "Zurückgestellter Auftrag wird ohne weiteren Klick genau einmal nachgeholt: " + enabled);
                }
                finally { await coordinator.PollAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            }

            var pending = CreateItem(Path.Combine(output, "check-now-pending-manual"));
            var update = CreateItem(Path.Combine(output, "check-now-pending-autoupdate"));
            pending.Enabled = false;
            update.AutoUpdate = true;
            Config.SaveSvnFolders(new BindingList<SvnItem> { pending, update });
            using (var coordinator = new ApplicationCoordinator(action => action()))
            {
                pending = coordinator.Folders.Single(item => item.Path == pending.Path);
                update = coordinator.Folders.Single(item => item.Path == update.Path);
                try
                {
                    using (var occupied = WorkingCopyLease.TryAcquire(pending.Path, "pending-manual", out _, out _))
                    {
                        Check(occupied is not null, "Gegenprobe reserviert das manuelle Ziel vor dem Auto-Update.");
                        coordinator.CheckNow(pending);
                        await coordinator.PollAsync();
                        Check(!pending.RetryReady && update.Status == SvnStatus.NeedUpdate && !update._IsAutoUpdating,
                            "Auch ein zurückgestellter manueller Auftrag verhindert den Start eines neuen Auto-Updates.");
                    }
                    typeof(SvnItem).GetField("retryAfterUtcTicks", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pending, 0L);
                    await coordinator.PollAsync();
                    Check(StatusCalls(pending) == 1, "Zurückgestelltes manuelles Ziel wird vor weiteren Auto-Updates nachgeholt.");
                }
                finally
                {
                    update.AutoUpdate = false;
                    await SvnTools.WaitForWorkingCopyAsync(update).WaitAsync(TimeSpan.FromSeconds(10));
                }
            }

            var paused = CreateItem(Path.Combine(output, "check-now-paused-automatic"));
            var manual = CreateItem(Path.Combine(output, "check-now-paused-manual"));
            manual.Enabled = false;
            Config.SaveSvnFolders(new BindingList<SvnItem> { paused, manual });
            Config.DoPauseAfterApplicationStartup = true;
            Config.PauseAfterApplicationStartupInterval = 60;
            using (var coordinator = new ApplicationCoordinator(action => action()))
            {
                manual = coordinator.Folders.Single(item => item.Path == manual.Path);
                try
                {
                    coordinator.Suspended = true;
                    coordinator.CheckNow(manual);
                    await coordinator.PollAsync();
                    Check(StatusCalls(manual) == 0, "Ausgesetzte Verarbeitung startet keine manuelle Prüfung.");
                    coordinator.Suspended = false;
                    await coordinator.PollAsync();
                    Check(StatusCalls(manual) == 1 && StatusCalls(paused) == 0, "Manueller Auftrag bleibt vorgemerkt und hebt nur für sein Ziel die Startpause auf.");
                    manual.DeferOperation();
                    coordinator.CheckNow(manual);
                    await coordinator.PollAsync();
                    coordinator.Folders.Remove(manual);
                    typeof(SvnItem).GetField("retryAfterUtcTicks", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manual, 0L);
                    await coordinator.PollAsync();
                    Check(StatusCalls(manual) == 1, "Entferntes manuelles Ziel wird nicht mehr geprüft.");
                }
                finally { await coordinator.PollAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            }
            return checks;
        }
        finally
        {
            Config.SvnCommandTimeout = previousTimeout;
            Config.DoPauseAfterApplicationStartup = previousPause;
            Config.PauseAfterApplicationStartupInterval = previousPauseInterval;
            Config.SaveSvnFolders(previousFolders);
        }
    }

    /// <summary>Erzeugt ausschließlich synthetische Arbeitskopien mit protokollierbarem Testtreiber.</summary>
    private static SvnItem CreateItem(string path, bool root = true)
    {
        Directory.CreateDirectory(path);
        if (root)
        {
            Directory.CreateDirectory(Path.Combine(path, ".svn"));
            File.WriteAllText(Path.Combine(path, ".svn", "wc.db"), "Isolierte synthetische Arbeitskopie.");
        }
        File.WriteAllText(Path.Combine(path, "record-commands"), "Isolierte Prozessnachweise.");
        return new SvnItem(path, PathType.Directory);
    }

    /// <summary>Zählt tatsächlich gestartete Statusprozesse unabhängig vom angezeigten Modellstatus.</summary>
    private static int StatusCalls(SvnItem item) => File.Exists(Path.Combine(item.Path, "command-calls"))
        ? File.ReadAllLines(Path.Combine(item.Path, "command-calls")).Count(command => command == "status") : 0;

    /// <summary>Wartet begrenzt auf den nachgewiesenen Start des angehaltenen Testprozesses.</summary>
    private static async Task WaitForFileAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(path) || new FileInfo(path).Length == 0) await Task.Delay(20, timeout.Token);
    }

    /// <summary>Prüft das Prozessende vor einer erneuten Reservierung derselben Arbeitskopie.</summary>
    private static bool ProcessEnded(int processId)
    {
        try { using var process = Process.GetProcessById(processId); return process.HasExited; }
        catch (ArgumentException) { return true; }
    }
}
