using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
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
        if (args[0] is "status" or "info" or "update" || args[0].StartsWith("/command:"))
        {
            if (args[0].StartsWith("/command:"))
            {
                // TortoiseSVN wertet die rohe Befehlszeile aus, nicht die vom .NET-Argumentparser aufbereitete Liste.
                var path = System.Text.RegularExpressions.Regex.Match(Marshal.PtrToStringUni(GetCommandLineW())!, "(?:^| )/path:(?:\"(?<path>[^\"]*)\"|(?<path>[^ ]*))");
                if (!path.Success || !Directory.Exists(path.Groups["path"].Value)) return 9;
            }
            if (args[0] == "status") Console.WriteLine("<status><target><entry path='fixture'><wc-status item='normal' props='none'><commit revision='1'/></wc-status><repos-status item='modified'/></entry><against revision='2'/></target></status>");
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
            await CheckCoordinationAsync(root);
            await CheckProcessesAsync(root);
            await CheckEntryPointsAsync(root);
            if (args.Length == 3 && args[1] == "--svn-directory") await CheckRealSvnAsync(output, args[2]);
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

    private static async Task CheckEntryPointsAsync(string root)
    {
        var item = new SvnItem(Path.Combine(root, "first"), PathType.Directory);
        using (var claim = WorkingCopyLease.TryAcquire(root, "other", out _, out _))
        {
            Assert(SvnTools.GetSvnFolderStatus(item) is null, "Belegte Statusprüfung wird ohne neuen Status übersprungen");
            Assert(!SvnTools.BeginUpdateSilently(item) && !SvnTools.Commit(item) && !SvnTools.Update(item, true), "Alle Update- und Commit-Einstiege beachten Belegung");
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
        string spaced = Path.Combine(root, "Ordner mit Leerzeichen ä-ß");
        Directory.CreateDirectory(spaced);
        foreach (string path in new[] { item.Path, spaced, spaced + Path.DirectorySeparatorChar })
        {
            var manual = new SvnItem(path, PathType.Directory);
            foreach (bool autoUpdate in new[] { false, true })
            {
                manual.AutoUpdate = autoUpdate;
                Assert(SvnTools.Update(manual, false), "Manuelles Update funktioniert unabhängig von Auto-Update und Leerzeichen im Pfad");
                Assert(manual.AutoUpdate == autoUpdate, "Manuelles Update verändert Auto-Update nicht");
            }
            SvnTools.OpenLogWindow(path);
            await WaitAsync(() => !SvnTools.HasActiveOperations);
            Assert(!SvnTools.TakeCompleted().Single().updateError, "TortoiseSVN-Protokoll erhält den unveränderten Pfad");
            Assert(SvnTools.Commit(manual), "TortoiseSVN-Commit kann mit Leerzeichen gestartet werden");
            await WaitAsync(() => !SvnTools.HasActiveOperations);
            Assert(!SvnTools.TakeCompleted().Single().updateError, "TortoiseSVN-Commit erhält den unveränderten Pfad");
        }
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
        }
    }

    /// <summary>Schließt ausschließlich den anhand der eigenen aktuellen Diagnose identifizierten Testdialog, ohne Commit auszulösen.</summary>
    private static async Task CloseOwnTortoiseDialogAsync(string action, string root)
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
                if (entry.RootElement.GetProperty("phase").GetString() != "begin") continue;
                if (!string.Equals(entry.RootElement.GetProperty("workingCopy").GetString(), canonicalRoot, StringComparison.OrdinalIgnoreCase)) continue;
                own?.Dispose();
                own = Process.GetProcessById(entry.RootElement.GetProperty("processId").GetInt32());
                return own.MainWindowHandle != IntPtr.Zero;
            }
            return false;
        });
        using (own)
        {
            Assert(WorkingCopyLease.TryAcquire(root, "status", out _, out _) is null, "Echter TortoiseSVN-Dialog blockiert konkurrierende Aktion");
            // TortoiseSVN verwendet im Protokoll einen eigenen Cancel/OK-Button statt des versteckten IDOK.
            // Im Commitdialog ausschließlich Abbrechen verwenden.
            int buttonId = action == "log" ? 1383 : action == "update-dialog" ? 1 : 2;
            IntPtr button = IntPtr.Zero;
            await WaitAsync(() =>
            {
                own!.Refresh();
                button = GetDlgItem(own.MainWindowHandle, buttonId);
                return button != IntPtr.Zero && IsWindowEnabled(button);
            }, 90);
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
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCommandLineW();
    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);

    private static Process Start(params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }

    private static async Task RawAsync(string file, params string[] arguments)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
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
