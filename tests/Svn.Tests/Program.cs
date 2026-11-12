using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CHD.SVN_Notifier;

internal static class Program
{
    private static int checks;
    private static readonly string executable = Environment.ProcessPath!;

    /// <summary>Prüft echte Prozesslebenszyklen mit eigenen synthetischen Arbeitskopien und optional einem lokalen SVN-Repository.</summary>
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        if (args[0] == "--child")
        {
            if (args.Contains("noisy"))
                for (int index = 0; index < 4096; index++) { Console.Out.WriteLine(new string('x', 128)); Console.Error.WriteLine(new string('y', 128)); }
            await Task.Delay(int.Parse(args[1]));
            return int.Parse(args[2]);
        }
        if (args[0] == "--child-tree")
        {
            var childInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "--child", "60000", "0" }) childInfo.ArgumentList.Add(argument);
            using var child = Process.Start(childInfo)!;
            await File.WriteAllTextAsync(args[1] + ".writing", child.Id.ToString());
            File.Move(args[1] + ".writing", args[1]);
            await Task.Delay(60000);
            return 0;
        }
        if (args[0] == "--hold")
        {
            if (args.Length > 3)
            {
                Console.WriteLine("ready");
                while (!File.Exists(args[3])) await Task.Delay(1);
            }
            using var claim = WorkingCopyLease.TryAcquire(args[1], "status", out _, out _);
            if (claim is null) return 75;
            Console.WriteLine("acquired");
            await Task.Delay(int.Parse(args[2]));
            return 0;
        }
        if (args[0] == "--crash-holder")
        {
            using var claim = WorkingCopyLease.TryAcquire(args[1], "status", out _, out _, args[3]);
            if (claim is null) return 75;
            if (args[2] == "reserved") Console.WriteLine("acquired");
            else
            {
                claim.MarkStarting();
                var childInfo = new ProcessStartInfo(args[3]) { UseShellExecute = false, CreateNoWindow = true };
                foreach (string argument in new[] { "--child", "60000", "0" }) childInfo.ArgumentList.Add(argument);
                using var child = Process.Start(childInfo)!;
                if (args[2] == "started") claim.MarkStarted(child);
                Console.WriteLine(child.Id);
            }
            await Task.Delay(60000);
            return 0;
        }
        if (args[0] is "status" or "info" or "update" || args[0].StartsWith("/command:"))
        {
            if (args[0] is "status" or "info" or "update")
            {
                // Der SVN-Parser behandelt @ als Peg-Revision und führendes Minus als Option.
                int separator = Array.IndexOf(args, "--");
                if (separator != args.Length - 2 || !args[^1].EndsWith('@')) return 10;
                string target = args[^1][..^1];
                if (!Path.IsPathFullyQualified(target) || !(Directory.Exists(target) || File.Exists(target))) return 11;
                if (args[0] == "status" && File.Exists(Path.Combine(target, "hold-status")))
                {
                    await File.WriteAllTextAsync(Path.Combine(target, "status.pid"), Environment.ProcessId.ToString());
                    await Task.Delay(60000);
                }
            }
            if (args[0].StartsWith("/command:"))
            {
                if (args[0] == "/command:cleanup" && (args.Length != 2 || !args[1].StartsWith("/path:"))) return 12;
                // TortoiseSVN wertet die rohe Befehlszeile aus, nicht die vom .NET-Argumentparser aufbereitete Liste.
                var path = System.Text.RegularExpressions.Regex.Match(Marshal.PtrToStringUni(GetCommandLineW())!, "(?:^| )/path:(?:\"(?<path>[^\"]*)\"|(?<path>[^ ]*))");
                if (!path.Success || !Directory.Exists(path.Groups["path"].Value)) return 9;
            }
            if (args[0] == "update" && File.Exists(Path.Combine(args[^1][..^1], "notification-autoupdate-fixture")))
                File.WriteAllText(Path.Combine(args[^1][..^1], "notification-update-completed"), "Isoliertes erfolgreiches Testupdate.");
            if (args[0] == "status" && File.Exists(Path.Combine(args[^1][..^1], "notification-keyboard-fixture")))
                File.AppendAllText(Path.Combine(args[^1][..^1], "notification-status-calls"), DateTime.UtcNow.ToString("O") + Environment.NewLine);
            if (args[0] == "status") Console.WriteLine(File.Exists(Path.Combine(args[^1][..^1], "notification-update-completed"))
                ? "<status><target><entry path='fixture'><wc-status item='normal' props='none'><commit revision='2'/></wc-status></entry><against revision='2'/></target></status>"
                : "<status><target><entry path='fixture'><wc-status item='normal' props='none'><commit revision='1'/></wc-status><repos-status item='modified'/></entry><against revision='2'/></target></status>");
            if (args[0] == "info") Console.WriteLine("<info><entry revision='2'><url>file:///fixture</url></entry></info>");
            await Task.Delay(100);
            return 0;
        }
        try
        {
            string output = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(output);
            string root = Path.Combine(output, "synthetic-wc");
            Directory.CreateDirectory(Path.Combine(root, ".svn"));
            File.WriteAllText(Path.Combine(root, ".svn", "wc.db"), "Synthetische Testmarkierung, keine SQLite-Datenbank.");
            Directory.CreateDirectory(Path.Combine(root, "first"));
            Directory.CreateDirectory(Path.Combine(root, "second"));
            Config.Init(Path.Combine(output, "fixture.ini"));
            Config.SVNpath = Config.TortoiseSVNpath = executable;
            CheckConfiguration(root);
            checks += await CoordinatorTests.RunAsync(output, executable);
            NotificationContextTests.Run(output, Assert);
            await CheckCoordinationAsync(root);
            await CheckRecoveryAsync(root);
            await CheckProcessesAsync(root);
            await CheckApplicationUpdateStopAsync(root);
            await CheckEntryPointsAsync(root);
            await CheckLiteralTargetsAsync(root);
            if (args.Length == 3 && args[1] == "--svn-directory") await CheckRealSvnAsync(output, args[2]);
            await CheckApplicationInstallationAsync(root);
            Console.WriteLine($"SVN-Prüfungen: {checks} erfolgreich.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void CheckConfiguration(string root)
    {
        Assert(!new SvnItem().AutoUpdate && !new SvnItem(root, PathType.Directory).AutoUpdate, "Neue Einträge benötigen Opt-in");
        string old = $"{root}|-1|-1|False|0";
        Assert(!SvnItem.Deserialize(old).AutoUpdate, "Alte Konfiguration ohne Feld bleibt ausgeschaltet");
        Assert(SvnItem.Deserialize(old + "|False").AutoUpdate, "Ausdrücklich gespeichertes Einschalten bleibt erhalten");
        Assert(!SvnItem.Deserialize(old + "|True").AutoUpdate, "Ausdrücklich gespeichertes Ausschalten bleibt erhalten");
        var item = new SvnItem(root, PathType.Directory) { AutoUpdate = true };
        Config.SaveSvnFolders(new BindingList<SvnItem> { item });
        Assert(Config.ReadSvnFolders().Single().AutoUpdate, "INI speichert aktiviertes Auto-Update");
        item.AutoUpdate = false;
        Config.SaveSvnFolders(new BindingList<SvnItem> { item });
        Assert(!Config.ReadSvnFolders().Single().AutoUpdate, "INI speichert ausgeschaltetes Auto-Update");
        item.DeferOperation();
        Assert(!item.RetryReady && SvnItem.FindNextStatusUpdateTimeMs(new BindingList<SvnItem> { item }, true) >= 14000, "Belegte Koordination erzeugt keine enge Wiederholungsschleife");
    }

    private static async Task CheckCoordinationAsync(string root)
    {
        string first = Path.Combine(root, "first");
        string second = Path.Combine(root, "second");
        using (var claim = WorkingCopyLease.TryAcquire(first, "status", out string foundRoot, out _))
        {
            Assert(claim is not null && foundRoot.Equals(root, StringComparison.OrdinalIgnoreCase), "Unterordner identifiziert Arbeitskopiewurzel");
            Assert(WorkingCopyLease.TryAcquire(second.ToUpperInvariant() + "\\..\\second\\", "update", out _, out string reason) is null && reason == "coordination-busy", "Zweiter Unterordner, Schreibweise und Normalisierung teilen Reservierung");
        }
        using (var claim = WorkingCopyLease.TryAcquire(second, "status", out _, out _)) Assert(claim is not null, "Freigabe erlaubt nächsten Vorgang");
        string nested = Path.Combine(first, "external");
        Directory.CreateDirectory(Path.Combine(nested, ".svn"));
        File.WriteAllText(Path.Combine(nested, ".svn", "wc.db"), "Synthetischer External");
        using (var claim = WorkingCopyLease.TryAcquire(nested, "update", out string foundRoot, out _))
        {
            Assert(claim is not null && foundRoot.Equals(nested, StringComparison.OrdinalIgnoreCase), "External hat eigene Arbeitskopiewurzel");
            Assert(WorkingCopyLease.TryAcquire(second, "status", out _, out _) is null, "Eingebettete Arbeitskopie reserviert zusätzlich äußere Wurzel");
        }
        string lockPath = Path.Combine(root, ".svn", WorkingCopyLease.FileName);
        File.WriteAllText(lockPath, "foreign-owner");
        Assert(WorkingCopyLease.TryAcquire(root, "status", out _, out _) is null && File.ReadAllText(lockPath) == "foreign-owner", "Fremde Reservierung wird weder gelöscht noch überschrieben");
        File.Delete(lockPath); // Ausschließlich die von diesem Test angelegte Datei.
        Directory.CreateDirectory(lockPath);
        Assert(WorkingCopyLease.TryAcquire(root, "status", out _, out _) is null, "Nicht beschreibbarer Koordinationspfad verhindert Start");
        Directory.Delete(lockPath);
        using var holder = Start("--hold", root, "600");
        Assert(await holder.StandardOutput.ReadLineAsync() == "acquired", "Separater Prozess hält Reservierung");
        Assert(WorkingCopyLease.TryAcquire(second, "status", out _, out _) is null, "Prozessübergreifende Belegung erkannt");
        await holder.WaitForExitAsync();
        Assert(!File.Exists(lockPath), "Separater Prozess gibt Reservierung frei");
    }

    /// <summary>Erzeugt einen echten Besitzerabsturz. Der eigene SVN-Testprozess darf dabei bewusst weiterlaufen.</summary>
    private static async Task<Process?> CrashOwnerAsync(string root, string state)
    {
        using var holder = Start("--crash-holder", root, state, executable);
        string? line = await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert(line is not null, "Isolierter Besitzer hat den Reservierungszustand geschrieben");
        Process? child = state == "reserved" ? null : Process.GetProcessById(int.Parse(line!));
        holder.Kill(); // Nur der Testbesitzer. Sein bekannter Kindprozess bleibt für die Gegenprobe am Leben.
        await holder.WaitForExitAsync();
        await WaitAsync(() =>
        {
            try
            {
                foreach (string workingRoot in WorkingCopyLease.FindRoots(root))
                {
                    using var stream = new FileStream(Path.Combine(workingRoot, ".svn", WorkingCopyLease.FileName), FileMode.Open, FileAccess.Read, FileShare.None);
                }
                return true;
            }
            catch (IOException) { return false; }
        });
        return child;
    }

    /// <summary>Prüft Wiederherstellung nach Absturz, alte Dateien, PID-Wiederverwendung, konkurrierende Starts und aktive Prozessreste.</summary>
    private static async Task CheckRecoveryAsync(string root)
    {
        string lockPath = Path.Combine(root, ".svn", WorkingCopyLease.FileName);
        string nested = Path.Combine(root, "first", "external");
        string nestedLock = Path.Combine(nested, ".svn", WorkingCopyLease.FileName);
        var logs = new ConcurrentQueue<string>();
        var runner = new SvnOperationRunner(logs.Enqueue);
        using (var live = WorkingCopyLease.TryAcquire(nested, "status", out _, out _))
        {
            runner.RecoverReservations(new[] { nested, root });
            Assert(File.Exists(lockPath) && File.Exists(nestedLock), "Startbereinigung erhält Reservierungen einer lebenden Instanz");
            Assert(WorkingCopyLease.TryAcquire(root, "update", out _, out _) is null, "Startbereinigung öffnet keine Lücke vor einem verzögerten Prozessstart");
        }
        using (await CrashOwnerAsync(nested, "reserved")) { }
        Assert(File.Exists(lockPath) && File.Exists(nestedLock), "Gegenprobe reproduziert zurückgebliebene Reservierungen nach echtem Prozessabbruch");
        string fixture = File.ReadAllLines(lockPath).Last(line => line.Length > 0);
        runner.RecoverReservations(new[] { nested, root, Path.Combine(root, "second") });
        Assert(!File.Exists(lockPath) && !File.Exists(nestedLock), "Anwendungsstart bereinigt äußere Wurzel und External ohne doppelte Bearbeitung");
        Assert(logs.Count(line => line.Contains("\"phase\":\"recover\"")) == 2, "Wiederherstellung protokolliert genau die beiden freigegebenen Arbeitskopien");

        using (var child = await CrashOwnerAsync(root, "started"))
        {
            try
            {
                Assert(!child!.HasExited && !WorkingCopyLease.TryRecover(root, out string busy) && busy.EndsWith("process-active"),
                    "Überlebender SVN-Prozess hält seine Arbeitskopie trotz Besitzerabsturz reserviert");
                Assert(WorkingCopyLease.TryAcquire(root, "update", out _, out _) is null, "Kein konkurrierendes Update während des überlebenden Prozesses");
            }
            finally { if (!child!.HasExited) child.Kill(); await child.WaitForExitAsync(); }
            var retry = runner.TryStart(root, "status", executable, new[] { "--child", "0", "0" }, true);
            Assert(retry is not null && (await retry).Success && !File.Exists(lockPath), "Spätere Prüfung übernimmt die Reservierung nach dem tatsächlichen Kindprozessende automatisch");
        }

        using (var child = await CrashOwnerAsync(root, "starting"))
        {
            try
            {
                Assert(!WorkingCopyLease.TryRecover(root, out string busy) && busy.EndsWith("possible-svn-process-active"),
                    "Absturz im Prozessstartfenster prüft auch Programme ohne gespeicherte Kindprozess-PID");
            }
            finally { if (!child!.HasExited) child.Kill(); await child.WaitForExitAsync(); }
            var examinedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Assert(WorkingCopyLease.TryRecover(root, out _, name => { examinedNames.Add(name); return false; }),
                "Unbestätigter Start wird bei leerem Windows-Prozessbestand automatisch freigegeben");
            Assert(examinedNames.Contains("svn") && examinedNames.Contains("TortoiseProc") && examinedNames.Contains(Path.GetFileNameWithoutExtension(executable)),
                "Die umfassendere Prozessprüfung erfasst SVN, TortoiseSVN und konfigurierte Programmnamen");
        }

        void WriteFixture(Action<JsonObject> modify)
        {
            var value = JsonNode.Parse(fixture)!.AsObject();
            modify(value);
            File.WriteAllText(lockPath, value.ToJsonString());
        }
        using var current = Process.GetCurrentProcess();
        long currentStart = current.StartTime.ToUniversalTime().Ticks;
        WriteFixture(value => { value["ownerPid"] = current.Id; value["ownerStartTicks"] = currentStart; });
        Assert(!WorkingCopyLease.TryRecover(root, out string activeOwner) && activeOwner.EndsWith("owner-active"), "Auch ohne offenes Handle bleibt ein nachweislich lebender Besitzer geschützt");
        File.Delete(lockPath); // Ausschließlich die vom Test erzeugte Metadaten-Fixture.
        WriteFixture(value => { value["ownerPid"] = current.Id; value["ownerStartTicks"] = currentStart - 1; });
        Assert(WorkingCopyLease.TryRecover(root, out _), "Wiederverwendete Besitzer-PID blockiert die Freigabe nicht");
        WriteFixture(value => { value["state"] = "started"; value["processId"] = current.Id; value["processStartTicks"] = currentStart - 1; });
        Assert(WorkingCopyLease.TryRecover(root, out _), "Wiederverwendete Kindprozess-PID wird anhand ihrer Startzeit unterschieden");
        File.WriteAllText(lockPath, fixture + "\n{\"protocol\":2,\"state\":\"starting");
        Assert(WorkingCopyLease.TryRecover(root, out _), "Abgebrochener Journaleintrag zerstört den vorherigen nachweisbaren Reservierungszustand nicht");
        WriteFixture(value => value["ownerHost"] = "different-windows-host");
        Assert(!WorkingCopyLease.TryRecover(root, out string host) && host.EndsWith("other-host") && File.Exists(lockPath), "Reservierung eines anderen Rechners wird nicht über lokale PIDs freigegeben");
        File.Delete(lockPath);
        WriteFixture(value =>
        {
            value["protocol"] = 1;
            foreach (string field in new[] { "ownerHost", "ownerStartTicks", "state", "processId", "processStartTicks", "executableName" }) value.Remove(field);
        });
        Assert(!WorkingCopyLease.TryRecover(root, out string legacyBusy, _ => true) && legacyBusy.EndsWith("possible-svn-process-active"),
            "Alte Reservierung bleibt während möglicherweise beteiligter SVN-Prozesse belegt");
        Assert(WorkingCopyLease.TryRecover(root, out _, _ => false) && !File.Exists(lockPath), "Alte Notifier-Reservierungen werden ebenfalls automatisch freigegeben");
        WriteFixture(value => { value["protocol"] = 1; value["ownerPid"] = current.Id; value["startedUtc"] = DateTimeOffset.UtcNow; });
        Assert(!WorkingCopyLease.TryRecover(root, out _, _ => false), "Alte Reservierung eines lebenden Besitzers wird erhalten");
        File.Delete(lockPath);
        WriteFixture(value => { value["protocol"] = 1; value["ownerPid"] = current.Id; value["startedUtc"] = current.StartTime.ToUniversalTime().AddSeconds(-1); });
        Assert(WorkingCopyLease.TryRecover(root, out _, _ => false), "Alte Reservierung erkennt eine erst danach wiederverwendete Besitzer-PID");
        foreach (string interrupted in new[] { "", "{", "{\"protocol\":2", "{\"protocol\":1,\"token\":\"", "{\"protocol\":2,\"token\":\"" })
        {
            File.WriteAllText(lockPath, interrupted);
            Assert(!WorkingCopyLease.TryRecover(root, out _, _ => true) && File.Exists(lockPath), "Leere und unterbrochene Notifier-Dateien bleiben bei möglicherweise laufendem SVN vorläufig belegt");
            Assert(WorkingCopyLease.TryRecover(root, out _, _ => false) && !File.Exists(lockPath), "Auch leere und unterbrochene eigene Reservierungsdateien werden automatisch freigegeben");
        }
        File.WriteAllText(lockPath, "{\"protocol\":2,\"ownerHost\":\"different-windows-host\",\"ownerPid\":");
        Assert(!WorkingCopyLease.TryRecover(root, out string partialHost, _ => false) && partialHost.EndsWith("other-host"), "Auch ein unterbrochener Eintrag mit erkennbar fremder Rechnerkennung bleibt geschützt");
        File.Delete(lockPath);
        File.WriteAllText(lockPath, "{unvollständige-fremde-Datei");
        Assert(!WorkingCopyLease.TryRecover(root, out _) && File.ReadAllText(lockPath) == "{unvollständige-fremde-Datei", "Unlesbare oder fremde Dateien werden nicht als eigene Reservierung gelöscht");
        File.Delete(lockPath);
        WriteFixture(_ => { });
        using (var blocked = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert(!WorkingCopyLease.TryRecover(root, out _), "Belegtes Dateihandle verhindert konkurrierende Wiederherstellung");
        File.SetAttributes(lockPath, FileAttributes.ReadOnly);
        Assert(!WorkingCopyLease.TryRecover(root, out _) && File.Exists(lockPath), "Fehlgeschlagene Löschmarkierung wird nicht als Freigabe ausgegeben");
        File.SetAttributes(lockPath, FileAttributes.Normal);
        Assert(WorkingCopyLease.TryRecover(root, out _), "Freigabe wird nach Wegfall des Dateifehlers erneut versucht");

        WriteFixture(_ => { });
        using var release = new ManualResetEventSlim();
        int attempts = 0;
        int winners = 0;
        var contenders = Enumerable.Range(0, 8).Select(contenderIndex => Task.Run(() =>
        {
            using var lease = WorkingCopyLease.TryAcquire(root, "status", out _, out _);
            if (lease is not null) Interlocked.Increment(ref winners);
            Interlocked.Increment(ref attempts);
            if (lease is not null) release.Wait();
        })).ToArray();
        try { await WaitAsync(() => Volatile.Read(ref attempts) == 8); Assert(winners == 1, "Gleichzeitige Wiederherstellung und Reservierung lässt genau einen Prozessbesitzer zu"); }
        finally { release.Set(); await Task.WhenAll(contenders); }
        Assert(!File.Exists(lockPath), "Gewinner gibt seine eigene Reservierung beim Beenden frei");
        Assert(File.ReadAllText(Path.Combine(root, ".svn", "wc.db")) == "Synthetische Testmarkierung, keine SQLite-Datenbank.", "Wiederherstellung verändert die SVN-Datenbank nicht");
    }

    private static async Task CheckProcessesAsync(string root)
    {
        var logs = new ConcurrentQueue<string>();
        var runner = new SvnOperationRunner(logs.Enqueue);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var delayed = new SvnOperationRunner(logs.Enqueue, info => { entered.Set(); release.Wait(); return Process.Start(info); });
        var first = delayed.TryStart(Path.Combine(root, "first"), "status", executable, new[] { "--child", "250", "0" }, true)!;
        Assert(entered.Wait(5000), "Prozessstart gezielt verzögert");
        var contenders = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run<Task<SvnProcessResult>?>(() => runner.TryStart(Path.Combine(root, "second"), "update", executable, new[] { "--child", "100", "0" }, false))));
        Assert(contenders.All(task => task is null), "Gleichzeitige Auslöser starten während reserviertem Prozessstart keinen zweiten Prozess");
        release.Set();
        Assert((await first).Success, "Verzögerter Prozess beendet sich erfolgreich");
        var noisy = await runner.TryStart(root, "status", executable, new[] { "--child", "0", "7", "noisy" }, true)!;
        Assert(noisy.ExitCode == 7 && noisy.Output.Length > 500000 && noisy.Error.Length > 500000, "Beide vollen Streams und Prozessfehler blockieren Freigabe nicht");
        var failed = await runner.TryStart(root, "update", Path.Combine(root, "missing.exe"), Array.Empty<string>(), false)!;
        Assert(!failed.Success && failed.ExitCode is null, "Startfehler wird erkannt");
        using (var claim = WorkingCopyLease.TryAcquire(root, "status", out _, out _)) Assert(claim is not null, "Startfehler gibt Reservierung frei");
        var invalid = await runner.TryStart(root, "update-dialog", executable, new[] { "/command:update", "/path:invalid\"path" }, false, tortoiseSyntax: true)!;
        Assert(!invalid.Success && invalid.ExitCode is null, "Ungültige Anführungszeichen verhindern den TortoiseSVN-Prozessstart");
        using (var claim = WorkingCopyLease.TryAcquire(root, "status", out _, out _)) Assert(claim is not null, "Ungültige Argumente geben die Reservierung frei");
        using var cancellation = new CancellationTokenSource();
        int begins = logs.Count(line => line.Contains("\"phase\":\"begin\""));
        var cancelled = runner.TryStart(root, "status", executable, new[] { "--child", "10000", "0" }, true, cancellation: cancellation.Token)!;
        await WaitAsync(() => logs.Count(line => line.Contains("\"phase\":\"begin\"")) > begins);
        cancellation.Cancel();
        Assert((await cancelled).Cancelled, "Abbruch wartet auf das Ende des Leseprozesses");
        var timed = await runner.TryStart(root, "status", executable, new[] { "--child", "10000", "0" }, true, 1)!;
        Assert(timed.Cancelled, "Zeitlimit beendet blockierenden Leseprozess");
        begins = logs.Count(line => line.Contains("\"phase\":\"begin\""));
        var write = runner.TryStart(root, "update", executable, new[] { "--child", "700", "0", "--password=test-secret-sentinel" }, false)!;
        await WaitAsync(() => logs.Count(line => line.Contains("\"phase\":\"begin\"")) > begins);
        var stopped = runner.StopAsync();
        Assert(!stopped.IsCompleted && runner.TryStart(root, "status", executable, Array.Empty<string>(), true) is null, "Programmende wartet auf Schreiben und lehnt neue Aufträge ab");
        Assert((await write).Success, "Programmende bricht schreibenden Prozess nicht gewaltsam ab");
        await stopped;
        using (var claim = WorkingCopyLease.TryAcquire(root, "status", out _, out _)) Assert(claim is not null, "Programmende gibt Reservierungen nach Prozessende frei");
        foreach (var line in logs)
        {
            using var value = JsonDocument.Parse(line);
            Assert(value.RootElement.TryGetProperty("workingCopy", out _) && value.RootElement.TryGetProperty("action", out _) && !line.Contains("--child") && !line.Contains("test-secret-sentinel"), "Diagnose enthält Metadaten ohne Befehlsargumente");
        }
        Assert(logs.Any(line => line.Contains("\"exitCode\":7")) && logs.Any(line => line.Contains("coordination-busy")), "Diagnose enthält Exit-Code und Überspringgrund");
        await delayed.StopAsync();
        var brokenDiagnostic = new SvnOperationRunner(line => { if (line.Contains("\"phase\":\"begin\"")) throw new IOException("Isolierter Diagnosefehler"); });
        var loggingFailure = brokenDiagnostic.TryStart(root, "update", executable, new[] { "--child", "600", "0" }, false)!;
        await Task.Delay(150);
        Assert(!loggingFailure.IsCompleted && WorkingCopyLease.TryAcquire(root, "status", out _, out _) is null, "Diagnosefehler nach Start gibt lebenden Prozess nicht vorzeitig frei");
        Assert(!(await loggingFailure).Success, "Diagnosefehler wird als Fehler zurückgegeben");
        using (var claim = WorkingCopyLease.TryAcquire(root, "status", out _, out _)) Assert(claim is not null, "Diagnosefehler gibt Reservierung nach Prozessende frei");
        await brokenDiagnostic.StopAsync();
    }

    /// <summary>Prüft den Installationsübergang mit eigenen Lese-, Schreib- und Dialogprozessen sowie Startverzögerungen.</summary>
    private static async Task CheckApplicationUpdateStopAsync(string root)
    {
        var logs = new ConcurrentQueue<string>();
        var runner = new SvnOperationRunner(logs.Enqueue);
        var operations = new List<Task<SvnProcessResult>>();
        var roots = new List<string>();
        var childFile = Path.Combine(Path.GetDirectoryName(root)!, "update-child.pid");
        var unrelatedInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "--child", "60000", "0" }) unrelatedInfo.ArgumentList.Add(argument);
        using var unrelated = Process.Start(unrelatedInfo)!;
        try
        {
            foreach (string action in new[] { "status", "update", "commit-dialog", "log" })
            {
                string workingCopy = Path.Combine(Path.GetDirectoryName(root)!, "application-update-" + action);
                Directory.CreateDirectory(Path.Combine(workingCopy, ".svn"));
                File.WriteAllText(Path.Combine(workingCopy, ".svn", "wc.db"), "Synthetische Testmarkierung.");
                roots.Add(workingCopy);
                var arguments = action == "update" ? new[] { "--child-tree", childFile } : new[] { "--child", "60000", "0" };
                operations.Add(runner.TryStart(workingCopy, action, executable, arguments, action == "status", captureOutput: action is "status" or "update")!);
            }
            await WaitAsync(() => logs.Count(line => line.Contains("\"phase\":\"begin\"")) == 4 && File.Exists(childFile));
            int childId = int.Parse(await File.ReadAllTextAsync(childFile));
            using var descendant = Process.GetProcessById(childId);
            var paused = runner.PauseForApplicationUpdateAsync();
            Assert(runner.TryStart(root, "status", executable, Array.Empty<string>(), true) is null, "Programmupdate sperrt neue Aufträge auf allen Arbeitskopien");
            await paused.WaitAsync(TimeSpan.FromSeconds(10));
            Assert(operations.All(operation => operation.IsCompleted && operation.Result.Cancelled && !operation.Result.Success), "Programmupdate beendet eigene Lese-, Schreib- und Dialogprozesse");
            Assert(descendant.HasExited, "Vor dem Installationsübergang ist auch der eigene SVN-Unterprozess beendet");
            Assert(!unrelated.HasExited, "Programmupdate beendet keine fremd gestarteten Prozesse");
            foreach (string workingCopy in roots)
            {
                using var claim = WorkingCopyLease.TryAcquire(workingCopy, "status", out _, out _);
                Assert(claim is not null, "Programmupdate gibt die Reservierung nach Prozessende frei");
            }
            Assert(runner.TryStart(root, "update", executable, Array.Empty<string>(), false) is null, "Prozessstarts bleiben bis zum Ende des Installationsübergangs gesperrt");
            Assert(logs.Count(line => line.Contains("\"phase\":\"end\"") && line.Contains("application-update")) == 4,
                "Diagnose kennzeichnet Prozessenden durch das Programmupdate");
            runner.ResumeAfterApplicationUpdate();
            Assert((await runner.TryStart(root, "status", executable, new[] { "--child", "0", "0" }, true)!).Success,
                "Nach einem Installationsfehler bleiben normale Statusprüfungen nutzbar");
            await runner.PauseForApplicationUpdateAsync();
            await runner.StopAsync();
            runner.ResumeAfterApplicationUpdate();
            Assert(runner.TryStart(root, "status", executable, Array.Empty<string>(), true) is null, "Wiederfreigabe hebt das normale Programmende nicht auf");
        }
        finally
        {
            if (!unrelated.HasExited) { unrelated.Kill(true); await unrelated.WaitForExitAsync(); }
            await runner.PauseForApplicationUpdateAsync();
        }

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var delayed = new SvnOperationRunner(logs.Enqueue, info => { entered.Set(); release.Wait(); return Process.Start(info); });
        var pending = delayed.TryStart(root, "update", executable, new[] { "--child", "60000", "0" }, false)!;
        Assert(entered.Wait(5000), "Programmupdate trifft gezielt auf einen verzögerten Prozessstart");
        try
        {
            var pausing = delayed.PauseForApplicationUpdateAsync(TimeSpan.FromMilliseconds(150));
            try { await pausing; throw new Exception("Der blockierte Prozessstart wurde nicht erkannt."); }
            catch (TimeoutException) { Assert(!pending.IsCompleted, "Fehlender Nachweis des Prozessendes bricht die Installationsvorbereitung ab"); }
            Assert(WorkingCopyLease.TryAcquire(root, "status", out _, out _) is null, "Zeitüberschreitung gibt eine noch aktive Reservierung nicht frei");
            delayed.ResumeAfterApplicationUpdate();
            release.Set();
            Assert((await pending.WaitAsync(TimeSpan.FromSeconds(10))).Cancelled, "Ein verspätet gestarteter Prozess bleibt trotz Wiederfreigabe zum Abbruch vorgemerkt");
            Assert((await delayed.TryStart(root, "status", executable, new[] { "--child", "0", "0" }, true)!).Success,
                "Ein neuer Auftrag nach fehlgeschlagenem Installationsübergang läuft normal");
        }
        finally { release.Set(); await delayed.StopAsync(); }

        using var diagnosticEntered = new ManualResetEventSlim();
        var diagnosticFailure = new SvnOperationRunner(line =>
        {
            if (line.Contains("\"phase\":\"begin\"")) { diagnosticEntered.Set(); throw new IOException("Isolierter Diagnosefehler."); }
        });
        var failed = diagnosticFailure.TryStart(root, "update", executable, new[] { "--child", "60000", "0" }, false)!;
        Assert(diagnosticEntered.Wait(5000), "Diagnosefehler nach Prozessstart gezielt ausgelöst");
        await diagnosticFailure.PauseForApplicationUpdateAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert(!(await failed).Success, "Programmupdate beendet den Prozess auch nach einem Diagnosefehler");
        using var finalClaim = WorkingCopyLease.TryAcquire(root, "status", out _, out _);
        Assert(finalClaim is not null, "Auch nach Diagnosefehler ist die Arbeitskopie nach Prozessende freigegeben");
    }

    /// <summary>Prüft den produktiven Installationsübergang einschließlich laufender Statusprüfung und Installerfehler.</summary>
    private static async Task CheckApplicationInstallationAsync(string root)
    {
        using (await CrashOwnerAsync(root, "reserved")) { }
        Config.CheckForNewVersion = false;
        Config.DoPauseAfterApplicationStartup = false;
        Config.SVNpath = Config.TortoiseSVNpath = executable;
        Config.SaveSvnFolders(new BindingList<SvnItem> { new(root, PathType.Directory) { Enabled = false } });
        using var coordinator = new ApplicationCoordinator(action => action());
        Assert(!File.Exists(Path.Combine(root, ".svn", WorkingCopyLease.FileName)),
            "Produktstart übernimmt verwaiste konfigurierte Reservierungen vor der ersten Statusprüfung");
        var runner = (SvnOperationRunner)typeof(SvnTools).GetField("runner", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        try
        {
            string statusRoot = Path.Combine(Path.GetDirectoryName(root)!, "installation-status");
            Directory.CreateDirectory(Path.Combine(statusRoot, ".svn"));
            File.WriteAllText(Path.Combine(statusRoot, ".svn", "wc.db"), "Synthetische Testmarkierung.");
            File.WriteAllText(Path.Combine(statusRoot, "hold-status"), "Isolierte blockierende Statusprüfung.");
            var item = new SvnItem(statusRoot, PathType.Directory) { AutoUpdate = false };
            coordinator.Folders.Add(item);
            var polling = coordinator.PollAsync();
            await WaitAsync(() => File.Exists(Path.Combine(statusRoot, "status.pid")));
            item.Enabled = false;
            var writing = runner.TryStart(root, "installation-write", executable, new[] { "--child", "60000", "0" }, false)!;
            int installerCalls = 0;
            Func<Task> installer = () =>
            {
                installerCalls++;
                Assert(!SvnTools.HasActiveOperations && writing.IsCompleted && writing.Result.Cancelled,
                    "Echter Updater beendet laufende SVN-Vorgänge vor dem Installeraufruf");
                Assert(polling.IsCompleted, "Echter Updater wartet auf das Ende der Hintergrundprüfung");
                Assert(runner.TryStart(root, "status", executable, Array.Empty<string>(), true) is null,
                    "Während des Installerstarts kann kein neuer SVN-Prozess starten");
                throw new IOException("Isolierter Installerfehler.");
            };
            try
            {
                await coordinator.PrepareInstallationAsync(installer);
                throw new Exception("Der Installerfehler wurde nicht gemeldet.");
            }
            catch (IOException error) when (error.Message == "Isolierter Installerfehler.")
            {
                Assert(installerCalls == 1, "Installerfehler wird weitergegeben und hebt den Installationszustand auf");
            }
            var retry = runner.TryStart(root, "status", executable, new[] { "--child", "0", "0" }, true);
            Assert(retry is not null, "Installerfehler gibt neue SVN-Aufträge wieder frei");
            Assert((await retry!).Success, "Nach echtem Installerfehler funktionieren weitere SVN-Prüfungen");
            await coordinator.PrepareInstallationAsync(() =>
            {
                Assert(!SvnTools.HasActiveOperations, "Auch ein erneuter Installationsversuch beginnt ohne laufende SVN-Prozesse");
                return Task.CompletedTask;
            });
            Assert(runner.TryStart(root, "update", executable, Array.Empty<string>(), false) is null,
                "Nach erfolgreicher Installerübergabe bleiben SVN-Aufträge bis zum Neustart gesperrt");
        }
        finally
        {
            await coordinator.StopAsync();
            SvnTools.ResumeAfterApplicationUpdate();
            Assert(!SvnTools.HasActiveOperations && !File.Exists(Path.Combine(root, ".svn", WorkingCopyLease.FileName)),
                "Produktiver Beenden-Pfad stoppt eigene Aufträge und hinterlässt keine Reservierung");
        }
    }

    private static async Task CheckEntryPointsAsync(string root)
    {
        var item = new SvnItem(Path.Combine(root, "first"), PathType.Directory);
        string spaced = Path.Combine(root, "Ordner mit Leerzeichen ä-ß");
        Directory.CreateDirectory(spaced);
        foreach (string path in new[] { root, spaced })
        {
            using var invalid = Start("/command:cleanup", "/path:" + path, "/nodlg");
            await invalid.WaitForExitAsync();
            Assert(invalid.ExitCode == 12, "Cleanup-Gegenprobe erkennt unterdrückte Auswahldialoge und zusätzliche Optionen");
        }
        using (var claim = WorkingCopyLease.TryAcquire(root, "other", out _, out _))
        {
            Assert(SvnTools.GetSvnFolderStatus(item) is null, "Belegte Statusprüfung wird ohne neuen Status übersprungen");
            Assert(!SvnTools.BeginUpdateSilently(item) && !SvnTools.Commit(item) && !SvnTools.Update(item, true), "Alle Update- und Commit-Einstiege beachten Belegung");
            Assert(!SvnTools.Cleanup(item), "Cleanup startet bei belegter Arbeitskopie keinen Prozess");
            Assert(SvnTools.GetRepositoryHeadRevision(item) == -1, "Revisionsabfrage startet bei Belegung nicht");
            SvnTools.OpenLogWindow(item.Path);
            SvnTools.OpenChangeLogWindow(item, true);
            Assert(!SvnTools.HasActiveOperations, "Auch Log-Einstiege starten bei Belegung keinen Prozess");
        }
        Assert(SvnTools.GetSvnFolderStatus(item) == SvnStatus.NeedUpdate, "Statusprüfung erkennt eingehende Änderungen weiterhin");
        Assert(SvnTools.GetRepositoryHeadRevision(item) == 2, "Revisionsabfrage funktioniert nach Freigabe");
        Assert(SvnTools.BeginUpdateSilently(item), "Explizit angeforderter stiller Update startet");
        await WaitAsync(() => !SvnTools.HasActiveOperations);
        Assert(SvnTools.TakeCompleted().Single().updateError == false, "Update-Ergebnis wird nach Prozessende übernommen");
        Assert(SvnTools.Commit(item), "Manueller Commit startet nach Freigabe");
        await WaitAsync(() => !SvnTools.HasActiveOperations);
        Assert(SvnTools.TakeCompleted().Length == 1, "Manueller Prozess wird ohne Statusprüfung freigegeben");
        foreach (string path in new[] { item.Path, spaced, spaced + Path.DirectorySeparatorChar })
        {
            var manual = new SvnItem(path, PathType.Directory);
            foreach (bool autoUpdate in new[] { false, true })
            {
                manual.AutoUpdate = autoUpdate;
                Assert(SvnTools.Update(manual, false), "Manuelles Update funktioniert unabhängig von Auto-Update und Leerzeichen im Pfad");
                Assert(manual.AutoUpdate == autoUpdate, "Manuelles Update verändert Auto-Update nicht");
                Assert(SvnTools.Cleanup(manual), "Cleanup öffnet unabhängig von Auto-Update den Auswahldialog");
                await WaitAsync(() => !SvnTools.HasActiveOperations);
                Assert(!SvnTools.TakeCompleted().Single().updateError && manual.AutoUpdate == autoUpdate,
                    "Cleanup erhält den Pfad und übergibt ausschließlich Dialog und Ziel ohne automatische Bereinigungsoptionen");
            }
            SvnTools.OpenLogWindow(path);
            await WaitAsync(() => !SvnTools.HasActiveOperations);
            Assert(!SvnTools.TakeCompleted().Single().updateError, "TortoiseSVN-Protokoll erhält den unveränderten Pfad");
            Assert(SvnTools.Commit(manual), "TortoiseSVN-Commit kann mit Leerzeichen gestartet werden");
            await WaitAsync(() => !SvnTools.HasActiveOperations);
            Assert(!SvnTools.TakeCompleted().Single().updateError, "TortoiseSVN-Commit erhält den unveränderten Pfad");
        }
    }

    /// <summary>Prüft die Zielübergabe an den CLI-Parser unabhängig von der weiterhin unveränderten Arbeitskopieidentität.</summary>
    private static async Task CheckLiteralTargetsAsync(string root)
    {
        string previousDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = root;
            foreach (string name in new[] { "literal@name", "literal@123", "literal@", "-r2" })
            {
                Directory.CreateDirectory(Path.Combine(root, name));
                var item = new SvnItem(name.StartsWith('-') ? name : Path.Combine(root, name), PathType.Directory);
                Assert(SvnTools.GetSvnFolderStatus(item) == SvnStatus.NeedUpdate, "Status übergibt Sonderzeichen und relative Pfade als literale Ziele");
                Assert(SvnTools.GetRepositoryHeadRevision(item) == 2 && SvnTools.GetRepositoryCommitedRevision(item) == 2,
                    "Beide Revisionsabfragen übergeben literale Ziele hinter den Optionen");
                Assert(SvnTools.BeginUpdateSilently(item), "Stilles Update mit literalem oder relativem Ziel startet");
                await WaitAsync(() => !SvnTools.HasActiveOperations);
                Assert(!SvnTools.TakeCompleted().Single().updateError, "Update erhält den absoluten Pfad mit leerer Peg-Revision");
            }
        }
        finally { Environment.CurrentDirectory = previousDirectory; }
    }

    private static async Task CheckRealSvnAsync(string output, string directory)
    {
        string svn = Path.Combine(directory, "svn.exe");
        string admin = Path.Combine(directory, "svnadmin.exe");
        string repository = Path.Combine(output, "repository");
        string working = Path.Combine(output, "working copy");
        string author = Path.Combine(output, "author");
        await RawAsync(admin, "create", repository);
        string url = new Uri(repository + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/');
        await RawAsync(svn, "checkout", url, author);
        Directory.CreateDirectory(Path.Combine(author, "first"));
        Directory.CreateDirectory(Path.Combine(author, "second"));
        File.WriteAllText(Path.Combine(author, "first", "fixture.txt"), "seed");
        await RawAsync(svn, "add", Path.Combine(author, "first"), Path.Combine(author, "second"));
        await RawAsync(svn, "commit", author, "-m", "Isolierte Testdaten");
        await RawAsync(svn, "checkout", url, working);
        File.AppendAllText(Path.Combine(author, "first", "fixture.txt"), "\nchanged");
        await RawAsync(svn, "commit", author, "-m", "Isolierte eingehende Änderung");
        Config.SVNpath = svn;
        Config.TortoiseSVNpath = Path.Combine(directory, "TortoiseProc.exe");
        var item = new SvnItem(Path.Combine(working, "first"), PathType.Directory);
        Assert(SvnTools.GetSvnFolderStatus(item) == SvnStatus.NeedUpdate && !item.AutoUpdate, "Echtes lokales SVN meldet Änderungen ohne automatisches Schreiben");
        using (var claim = WorkingCopyLease.TryAcquire(working, "other", out _, out _)) Assert(!SvnTools.BeginUpdateSilently(item), "Echtes SVN-Update startet bei belegter Arbeitskopie nicht");
        if (File.Exists(Config.TortoiseSVNpath))
        {
            Config.UpdateWindowAction = 0;
            var manual = Task.Run(() => SvnTools.Update(item, false));
            await CloseOwnTortoiseDialogAsync("update-dialog", working);
            Assert(await manual && !item.AutoUpdate && File.ReadAllText(Path.Combine(item.Path, "fixture.txt")).Contains("changed"),
                "Manuelles TortoiseSVN-Update mit ausgeschaltetem Auto-Update und Leerzeichen im Pfad übernimmt Änderung");
            File.AppendAllText(Path.Combine(author, "first", "fixture.txt"), "\nautomatic-update-test");
            await RawAsync(svn, "commit", author, "-m", "Weitere isolierte eingehende Änderung");
            Assert(SvnTools.GetSvnFolderStatus(item) == SvnStatus.NeedUpdate, "Weitere Änderung bleibt vor explizitem Auto-Update erkennbar");
        }
        item.AutoUpdate = true;
        Assert(SvnTools.BeginUpdateSilently(item), "Explizit aktiviertes Update im lokalen Test-Repository");
        await WaitAsync(() => !SvnTools.HasActiveOperations);
        Assert(!SvnTools.TakeCompleted().Single().updateError && SvnTools.GetSvnFolderStatus(item) == SvnStatus.UpToDate, "Echtes Update abgeschlossen und erneute Statusprüfung nutzbar");
        if (File.Exists(Config.TortoiseSVNpath))
        {
            SvnTools.OpenLogWindow(item.Path);
            await CloseOwnTortoiseDialogAsync("log", working);
            Assert(SvnTools.TakeCompleted().Length == 1, "Echter TortoiseSVN-Protokolldialog behält Reservierung bis zum Schließen");
            File.AppendAllText(Path.Combine(working, "first", "fixture.txt"), "\nlocal-dialog-test");
            Assert(SvnTools.Commit(item), "Echter TortoiseSVN-Commitdialog auf isolierter Arbeitskopie startet");
            await CloseOwnTortoiseDialogAsync("commit-dialog", working);
            Assert(SvnTools.TakeCompleted().Length == 1, "Abbrechen des echten Commitdialogs gibt Reservierung frei");
            Assert(SvnTools.Cleanup(item), "Echter Cleanup-Auswahldialog auf isolierter Arbeitskopie startet");
            await CloseOwnTortoiseDialogAsync("cleanup-dialog", working);
            Assert(SvnTools.TakeCompleted().Length == 1, "Abbrechen des echten Cleanupdialogs gibt Reservierung frei");
            foreach (string action in new[] { "log", "commit-dialog", "cleanup-dialog" })
            {
                if (action == "log") SvnTools.OpenLogWindow(item.Path);
                else if (action == "cleanup-dialog") Assert(SvnTools.Cleanup(item), "Echter Cleanupdialog startet für den isolierten Programmupdate-Test");
                else Assert(SvnTools.Commit(item), "Echter Commitdialog startet für den isolierten Programmupdate-Test");
                await CloseOwnTortoiseDialogAsync(action, working, applicationUpdate: true);
                Assert(SvnTools.TakeCompleted().Single().Completion!.Result.Cancelled,
                    "Programmupdate beendet den echten TortoiseSVN-Dialog als abgebrochenen Vorgang");
                Assert(SvnTools.GetSvnFolderStatus(item) == SvnStatus.UpToDate_Modified,
                    "Lokale Änderungen und Statusprüfung bleiben nach dem beendeten Testdialog erhalten");
            }
        }
        await CheckRealLiteralTargetsAsync(output, svn, url);
    }

    /// <summary>Gegenproben verwenden nur lokale Test-Arbeitskopien. Eine ähnlich benannte reservierte Arbeitskopie muss unverändert bleiben.</summary>
    private static async Task CheckRealLiteralTargetsAsync(string output, string svn, string url)
    {
        string previousDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = output;
            foreach (string name in new[] { "literal@name", "literal@123", "literal@", "-r2" })
            {
                string path = Path.Combine(output, name);
                await RawAsync(svn, "checkout", "-r", "1", url, path);
                var item = new SvnItem(name.StartsWith('-') ? name : path, PathType.Directory);
                Assert(SvnTools.GetSvnFolderStatus(item) == SvnStatus.NeedUpdate, "Echtes SVN erkennt Änderungen bei @ und relativen Optionsnamen");
                Assert(SvnTools.GetRepositoryHeadRevision(item) >= 2 && SvnTools.GetRepositoryCommitedRevision(item) == 1,
                    "Echte Revisionsabfragen treffen das unveränderte literale Ziel");
                Assert(SvnTools.BeginUpdateSilently(item), "Echtes Update mit @ oder relativem Minus-Pfad startet");
                await WaitAsync(() => !SvnTools.HasActiveOperations);
                Assert(!SvnTools.TakeCompleted().Single().updateError && SvnTools.GetSvnFolderStatus(item) == SvnStatus.UpToDate,
                    "Echtes Update und erneute Statusprüfung treffen dieselbe Arbeitskopie");
                if (File.Exists(Config.TortoiseSVNpath))
                {
                    var manual = Task.Run(() => SvnTools.Update(item, false));
                    await CloseOwnTortoiseDialogAsync("update-dialog", path);
                    Assert(await manual, "Manuelles TortoiseSVN-Update erhält auch @ und relative Minus-Pfade unverändert");
                }
            }
            string other = Path.Combine(output, "collision");
            string intended = other + "@";
            await RawAsync(svn, "checkout", "-r", "1", url, other);
            await RawAsync(svn, "checkout", "-r", "1", url, intended);
            var target = new SvnItem(intended, PathType.Directory);
            using (var own = WorkingCopyLease.TryAcquire(intended, "other", out _, out _))
                Assert(own is not null && !SvnTools.BeginUpdateSilently(target), "Reservierung des tatsächlichen @-Ziels verhindert einen zweiten Start");
            using (var own = WorkingCopyLease.TryAcquire(other, "other", out _, out _))
            {
                Assert(own is not null && SvnTools.BeginUpdateSilently(target), "@-Ziel ist von ähnlich benannter reservierter Arbeitskopie unabhängig");
                await WaitAsync(() => !SvnTools.HasActiveOperations);
                Assert(!SvnTools.TakeCompleted().Single().updateError, "Update des literalen @-Ziels wird erfolgreich beendet");
                Assert(!File.ReadAllText(Path.Combine(other, "first", "fixture.txt")).Contains("changed") &&
                    File.ReadAllText(Path.Combine(intended, "first", "fixture.txt")).Contains("changed"),
                    "Nur die beabsichtigte Arbeitskopie wird aktualisiert, die andere bleibt unverändert");
                Assert(File.Exists(Path.Combine(other, ".svn", WorkingCopyLease.FileName)) &&
                    !File.Exists(Path.Combine(intended, ".svn", WorkingCopyLease.FileName)), "Fremde Reservierung bleibt bestehen und eigene Reservierung ist freigegeben");
            }
        }
        finally { Environment.CurrentDirectory = previousDirectory; }
    }

    /// <summary>Schließt ausschließlich den anhand der eigenen aktuellen Diagnose identifizierten Testdialog, ohne Commit auszulösen.</summary>
    private static async Task CloseOwnTortoiseDialogAsync(string action, string root, bool applicationUpdate = false)
    {
        Process? own = null;
        string canonicalRoot = WorkingCopyLease.FindRoots(root)[0];
        await WaitAsync(() =>
        {
            using var stream = new FileStream(Config.logFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            foreach (string line in reader.ReadToEnd().Split('\n').Reverse())
            {
                int begin = line.IndexOf("{\"utc\":", StringComparison.Ordinal);
                if (begin < 0 || !line.Contains("\"action\":\"" + action + "\"")) continue;
                using var entry = JsonDocument.Parse(line[begin..]);
                if (!string.Equals(entry.RootElement.GetProperty("workingCopy").GetString(), canonicalRoot, StringComparison.OrdinalIgnoreCase)) continue;
                string phase = entry.RootElement.GetProperty("phase").GetString()!;
                if (phase == "end") return false;
                if (phase != "begin") continue;
                own?.Dispose();
                // Wiederholte Dialogtests können ältere Begin-Ereignisse enthalten, bevor der neue Worker protokolliert.
                try { own = Process.GetProcessById(entry.RootElement.GetProperty("processId").GetInt32()); }
                catch (ArgumentException) { own = null; continue; }
                // QueryFullProcessImageName funktioniert auch vom x86-Test aus für ein x64-TortoiseSVN.
                var imagePath = new System.Text.StringBuilder(32768);
                int imageLength = imagePath.Capacity;
                if (!QueryFullProcessImageNameW(own.SafeHandle, 0, imagePath, ref imageLength))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                if (own.StartTime.ToUniversalTime() > entry.RootElement.GetProperty("utc").GetDateTimeOffset().UtcDateTime
                    || !string.Equals(imagePath.ToString(), Config.TortoiseSVNpath, StringComparison.OrdinalIgnoreCase))
                { own.Dispose(); own = null; return false; }
                return own.MainWindowHandle != IntPtr.Zero;
            }
            return false;
        });
        using (own)
        {
            Assert(WorkingCopyLease.TryAcquire(root, "status", out _, out _) is null, "Echter TortoiseSVN-Dialog blockiert konkurrierende Aktion");
            if (applicationUpdate)
            {
                try
                {
                    await SvnTools.PauseForApplicationUpdateAsync();
                    Assert(own!.HasExited && !File.Exists(Path.Combine(root, ".svn", WorkingCopyLease.FileName)),
                        "Programmupdate beendet den echten eigenen TortoiseSVN-Dialog und gibt dessen Reservierung frei");
                }
                finally { SvnTools.ResumeAfterApplicationUpdate(); }
                return;
            }
            // TortoiseSVN verwendet im Protokoll einen eigenen Cancel/OK-Button statt des versteckten IDOK.
            // Im Commit- und Cleanupdialog ausschließlich Abbrechen verwenden.
            int buttonId = action == "log" ? 1383 : action == "update-dialog" ? 1 : 2;
            IntPtr button = IntPtr.Zero;
            await WaitAsync(() =>
            {
                own!.Refresh();
                button = GetDlgItem(own.MainWindowHandle, buttonId);
                // Der Fortschrittsdialog aktiviert OK zunächst vor dem Workerstart. Erst deaktiviertes Abbrechen zeigt dessen Ende an.
                return button != IntPtr.Zero && IsWindowEnabled(button) &&
                    (action != "update-dialog" || !IsWindowEnabled(GetDlgItem(own.MainWindowHandle, 2)));
            }, 90);
            if (action == "cleanup-dialog")
            {
                SetForegroundWindow(own!.MainWindowHandle);
                await Task.Delay(300);
                // Bildschirmaufnahme und native Fensterkoordinaten benötigen dieselbe DPI-Skalierung.
                IntPtr previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
                try
                {
                    Assert(GetWindowRect(own.MainWindowHandle, out var bounds), "Cleanup-Auswahldialog ist sichtbar erfassbar");
                    using var image = new System.Drawing.Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                    using var graphics = System.Drawing.Graphics.FromImage(image);
                    // Der Dialog wird auch auf einem nicht eingeblendeten Testdesktop direkt erfasst.
                    IntPtr context = graphics.GetHdc();
                    try { Assert(PrintWindow(own.MainWindowHandle, context, 2), "Cleanup-Dialog auf dem Testdesktop erfassbar"); }
                    finally { graphics.ReleaseHdc(context); }
                    image.Save(Path.Combine(Path.GetDirectoryName(root)!, "cleanup-dialog.png"));
                }
                finally { SetThreadDpiAwarenessContext(previousDpi); }
            }
            SendMessageTimeout(own!.MainWindowHandle, 0x0111, (IntPtr)buttonId, button, 2, 5000, out _);
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (SvnTools.HasActiveOperations && !own.HasExited && DateTime.UtcNow < deadline)
            {
                await Task.Delay(500);
                own.Refresh();
                if (!own.HasExited && own.MainWindowHandle != IntPtr.Zero)
                    SendMessageTimeout(own.MainWindowHandle, 0x0111, (IntPtr)buttonId, GetDlgItem(own.MainWindowHandle, buttonId), 2, 5000, out _);
            }
            await WaitAsync(() => !SvnTools.HasActiveOperations, 90);
            Assert(own.HasExited && !File.Exists(Path.Combine(root, ".svn", WorkingCopyLease.FileName)),
                "Eigener Testdialog beendet und Reservierung freigegeben");
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDlgItem(IntPtr window, int identifier);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowBounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out WindowBounds bounds);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr window, IntPtr context, uint flags);
    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(Microsoft.Win32.SafeHandles.SafeProcessHandle process,
        int flags, System.Text.StringBuilder name, ref int size);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCommandLineW();
    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);

    private static Process Start(params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }

    private static async Task RawAsync(string file, params string[] arguments)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException(await error);
        await output;
    }

    private static async Task WaitAsync(Func<bool> condition, int timeoutSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException(); await Task.Delay(10); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }
}
