using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using CHD;
using CHD.SVN_Notifier;

internal static class ReliabilityTests
{
    /// <summary>Prüft die fünf Fehlerursachen mit isolierten Dateien und tatsächlich gestarteten, kontrollierten Testprozessen.</summary>
    internal static async Task<int> RunAsync(string output)
    {
        int checks = 0;
        void Check(bool result, string description)
        {
            if (!result) throw new InvalidOperationException(description);
            checks++;
            Console.WriteLine("Zuverlässigkeit: " + description);
        }
        var fields = typeof(Config).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => !field.IsLiteral && !field.IsInitOnly).ToDictionary(field => field, field => field.GetValue(null));
        string root = Path.Combine(output, "reliability");
        Directory.CreateDirectory(root);
        try
        {
            string iniPath = Path.Combine(root, "concurrent.ini");
            File.WriteAllText(iniPath, "[Settings]\nFont=old\nWidth=100\n");
            var first = new IniFile(iniPath, true);
            var second = new IniFile(iniPath, true);
            first.Write("Settings", "Font", "new");
            second.Write("Settings", "Width", 200);
            second.Write("Settings", "Width", 201);
            var merged = new IniFile(iniPath, true);
            Check(merged.ReadString("Settings", "Font") == "new" && merged.ReadInteger("Settings", "Width") == 201,
                "Getrennte INI-Instanzen erhalten auch bei wiederholtem Speichern fremde Änderungen.");
            byte[] before = File.ReadAllBytes(iniPath);
            try { first.Update(file => { file.DeleteSection("Settings"); throw new InvalidDataException("Isolierter Abbruch."); }); }
            catch (InvalidDataException) { }
            Check(File.ReadAllBytes(iniPath).SequenceEqual(before), "Abgebrochene INI-Transaktion erhält die bisherige Datei.");
            first.Update(file => { file.DeleteSection("Folders"); file.Write("Folders", "Folder1", "new"); file.Write("Folders", "Folder2", "second"); });
            Check(new IniFile(iniPath, true).GetKeys("Folders").Length == 2 && !Directory.EnumerateFiles(root, "*.tmp").Any(),
                "Zusammengehörige INI-Werte werden ohne temporäre Restdatei gespeichert.");
            using (var writerA = StartWriter(iniPath, "WriterA"))
            using (var writerB = StartWriter(iniPath, "WriterB"))
            {
                await Task.WhenAll(writerA.WaitForExitAsync(), writerB.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(15));
                merged = new IniFile(iniPath, true);
                Check(writerA.ExitCode == 0 && writerB.ExitCode == 0 && merged.GetKeys("WriterA").Length == 30 && merged.GetKeys("WriterB").Length == 30,
                    "Zwei gleichzeitige Schreiberprozesse erhalten sämtliche 60 unabhängigen INI-Änderungen.");
            }

            Config.Init(Path.Combine(root, "active.ini"));
            Config.SVNpath = Config.TortoiseSVNpath = Environment.ProcessPath!;
            Config.DoPauseAfterApplicationStartup = false;
            var foreignConfiguration = new IniFile(Config.iniFileName, true);
            foreignConfiguration.Write("Settings", "FontFamily", "ForeignFont");
            Config.SaveSettings(); Config.SaveSettings();
            Check(new IniFile(Config.iniFileName, true).ReadString("Settings", "FontFamily") == "ForeignFont",
                "Unveränderte, zuvor nicht gespeicherte Standardoptionen überschreiben keine fremden Einstellungen.");
            Config.SaveSvnFolders(new BindingList<SvnItem> { new(root, PathType.Directory) });
            foreignConfiguration = new IniFile(Config.iniFileName, true);
            foreignConfiguration.Write("Folders", "Folder2", new SvnItem(Path.Combine(root, "foreign"), PathType.Directory).Serialize());
            Config.SaveSvnFolders(Config.ReadSvnFolders());
            Check(new IniFile(Config.iniFileName, true).GetKeys("Folders").Length == 2,
                "Unverändertes Speichern der Repository-Liste erhält fremde Ergänzungen.");
            Config.Language = 1;
            string activeConfiguration = Config.iniFileName;
            byte[] activeBytes = File.ReadAllBytes(activeConfiguration);
            string malformedImport = Path.Combine(root, "large-invalid.ini");
            File.WriteAllText(malformedImport, "[Settings]\nVersion=1.9\nLanguage=0\n[Folders]\n" +
                string.Join("\n", Enumerable.Range(1, 3000).Select(index => "Folder" + index + "=" + root + "|1|2|False|0|True")) +
                "\nFolder3001=" + root + "|1|2|False|invalid|True\n");
            using (var observed = new ManualResetEventSlim())
            using (var done = new ManualResetEventSlim())
            {
                bool temporaryContextSeen = false;
                Task observer = Task.Run(() =>
                {
                    observed.Set();
                    while (!done.IsSet)
                    {
                        if (Config.iniFileName != activeConfiguration || Config.Language != 1) temporaryContextSeen = true;
                        Thread.SpinWait(20);
                    }
                });
                observed.Wait();
                bool rejected = false;
                try { Config.Import(malformedImport); } catch (FormatException) { rejected = true; }
                finally { done.Set(); await observer; }
                Check(rejected && !temporaryContextSeen && File.ReadAllBytes(activeConfiguration).SequenceEqual(activeBytes),
                    "Importvalidierung veröffentlicht auch für parallele Leser keine vorübergehenden Einstellungen.");
            }
            foreach (bool cancelRead in new[] { false, true })
            {
                var old = CreateItem(Path.Combine(root, "path-old-" + cancelRead));
                var replacement = CreateItem(Path.Combine(root, "path-new-" + cancelRead));
                old.AutoUpdate = true;
                Config.SaveSvnFolders(new BindingList<SvnItem> { old });
                using var coordinator = new ApplicationCoordinator(action => action());
                var item = coordinator.Folders.Single();
                item.RepositoryUrl = "file:///old"; item.reviewedRevision = 123; item.updateRevision = 123;
                string oldHold = Path.Combine(old.Path, "hold-status"), newHold = Path.Combine(replacement.Path, "hold-status");
                File.WriteAllText(oldHold, "Angehaltene alte Prüfung."); File.WriteAllText(newHold, "Angehaltene neue Prüfung.");
                Task poll = coordinator.PollAsync();
                try
                {
                    await WaitForFileAsync(Path.Combine(old.Path, "status.pid.ready"));
                    if (cancelRead) coordinator.EnsurePathCanChange(item);
                    item.Path = replacement.Path; item.origPath = replacement.Path;
                    Check(item.RepositoryUrl is null && item.reviewedRevision == 0 && item.updateRevision == 0 && item.Status == SvnStatus.Unknown,
                        "Pfadwechsel verwirft Status, Repository-URL und Revisionsstände: " + cancelRead);
                    File.Delete(oldHold);
                    await WaitForFileAsync(Path.Combine(replacement.Path, "status.pid.ready"));
                    Check(item.Status == SvnStatus.Unknown && !Commands(replacement.Path).Contains("update"),
                        "Altes Prüfergebnis startet kein Update am neuen Ziel vor dessen eigener Prüfung: " + cancelRead);
                    File.Delete(newHold);
                    await poll.WaitAsync(TimeSpan.FromSeconds(10));
                    await SvnTools.WaitForTrackedOperationsAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    SvnTools.TakeCompleted();
                    Check(Commands(replacement.Path).Contains("update"), "Eigenes Prüfergebnis des neuen Pfads erlaubt dessen Auto-Update: " + cancelRead);
                }
                finally
                {
                    File.Delete(oldHold); File.Delete(newHold);
                    coordinator.Suspended = true;
                    await poll.WaitAsync(TimeSpan.FromSeconds(10));
                }
            }

            var invalid = new SvnItem(Path.Combine(root, "missing"), PathType.Directory) { Enabled = false };
            var malformed = new SvnItem("invalid\0path", PathType.Directory);
            Check(SvnTools.GetSvnFolderStatus(malformed) == SvnStatus.Error && !malformed.RetryReady,
                "Syntaktisch ungültiger Pfad meldet einen Fehler statt die Prüfrunde mit einer Exception abzubrechen.");
            var independent = CreateItem(Path.Combine(root, "independent")); independent.AutoUpdate = true;
            Config.SaveSvnFolders(new BindingList<SvnItem> { invalid, independent });
            using (var coordinator = new ApplicationCoordinator(action => action()))
            {
                invalid = coordinator.Folders.Single(item => !item.Enabled);
                coordinator.CheckNow(invalid);
                await coordinator.PollAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await SvnTools.WaitForTrackedOperationsAsync().WaitAsync(TimeSpan.FromSeconds(10)); SvnTools.TakeCompleted();
                Check(invalid.Status == SvnStatus.Error && Commands(independent.Path).Contains("update"),
                    "Fehlende Arbeitskopie beendet den manuellen Auftrag mit Fehler und blockiert kein unabhängiges Auto-Update.");
            }

            var writeTarget = CreateItem(Path.Combine(root, "import-write"));
            Config.SaveSvnFolders(new BindingList<SvnItem> { writeTarget });
            using (var coordinator = new ApplicationCoordinator(action => action()))
            {
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task manual = coordinator.RunManualAsync(() => release.Task);
                bool imported = false;
                Task import = coordinator.ChangeConfigurationAsync(() => { imported = true; return Task.CompletedTask; });
                try
                {
                    Check(!import.IsCompleted && !imported,
                        "Konfigurationswechsel wartet auch ohne parallelen Schreibprozess auf die manuelle Aktion.");
                }
                finally { release.TrySetResult(); await Task.WhenAll(manual, import).WaitAsync(TimeSpan.FromSeconds(10)); }
                Check(imported, "Nach der manuellen Aktion wird der wartende Konfigurationswechsel ausgeführt.");
            }
            using (var coordinator = new ApplicationCoordinator(action => action()))
            {
                var item = coordinator.Folders.Single();
                string hold = Path.Combine(item.Path, "hold-update"); File.WriteAllText(hold, "Angehaltenes Update.");
                Check(SvnTools.BeginPreparedUpdateSilently(item), "Isolierter Schreibvorgang für Importsperre startet.");
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task manual = Task.CompletedTask, import = Task.CompletedTask;
                try
                {
                    await WaitForFileAsync(Path.Combine(item.Path, "update.pid.ready"));
                    bool pathRejected = false;
                    try { coordinator.EnsurePathCanChange(item); } catch (InvalidOperationException) { pathRejected = true; }
                    Check(pathRejected, "Pfadwechsel während eines eigenen Schreibvorgangs wird abgewiesen.");
                    manual = coordinator.RunManualAsync(() => release.Task);
                    bool imported = false;
                    import = coordinator.ChangeConfigurationAsync(() => { imported = true; return Task.CompletedTask; });
                    Check(!import.IsCompleted && !imported, "Konfigurationswechsel wartet auf eine laufende manuelle Aktion.");
                    bool rejected = false;
                    try { await coordinator.RunManualAsync(() => Task.CompletedTask); } catch (InvalidOperationException) { rejected = true; }
                    Check(rejected, "Während des Imports werden neue manuelle SVN-Aufträge sichtbar abgewiesen.");
                    release.TrySetResult(); await manual;
                    Check(await Task.WhenAny(import, Task.Delay(TimeSpan.FromSeconds(1))) != import && !imported,
                        "Konfigurationswechsel wartet zusätzlich auf bereits gestartete Schreibprozesse.");
                    File.Delete(hold); await import.WaitAsync(TimeSpan.FromSeconds(10));
                    Check(imported && !coordinator.Suspended, "Konfigurationswechsel läuft nach dem Prozessende und gibt die Verarbeitung frei.");
                    bool failed = false;
                    try { await coordinator.ChangeConfigurationAsync(() => throw new InvalidDataException("Isolierter Importfehler.")); }
                    catch (InvalidDataException) { failed = true; }
                    Check(failed && !coordinator.Suspended, "Auch ein fehlgeschlagener Import gibt die Verarbeitung frei.");
                }
                finally { release.TrySetResult(); File.Delete(hold); await Task.WhenAll(manual, import).WaitAsync(TimeSpan.FromSeconds(10)); }
            }

            string contexts = Path.Combine(root, "notifications"); var errors = new List<Exception>();
            var store = new NotificationContextStore(contexts, errors.Add);
            var request = new NotificationRequest(NotificationKind.IncomingChanges, new[] { root }, "Test", "Isolierte Meldung.");
            string configuration = Path.Combine(root, "notifications.ini");
            var valid = store.Create(request, Environment.ProcessPath!, configuration);
            store.Create(request, Environment.ProcessPath!, Path.Combine(root, "other.ini"));
            var expired = store.Create(request, Environment.ProcessPath!, configuration);
            File.SetLastWriteTimeUtc(Path.Combine(contexts, expired.Id + ".json"), DateTime.UtcNow.AddDays(-8));
            string broken = Path.Combine(contexts, Guid.NewGuid().ToString("N") + ".json"); File.WriteAllText(broken, "{");
            Check(store.ReadConfiguration(configuration).Single().Id == valid.Id && errors.Count == 1,
                "Beschädigter Kontext blockiert gültige Meldungen nicht und erzeugt eine Diagnose.");
            Check(!File.Exists(broken) && Directory.EnumerateFiles(contexts, "*.invalid").Count() == 1 && store.Read(valid.Id).Paths.Single() == root,
                "Beschädigter Kontext wird isoliert; gültiges Klickziel bleibt erhalten.");
            Check(store.ReadConfiguration(configuration).Single().Id == valid.Id && errors.Count == 1 && !File.Exists(Path.Combine(contexts, expired.Id + ".json")),
                "Abgelaufene Kontexte werden entfernt; isolierter Defekt wird nicht wiederholt gemeldet.");
            Check(!Directory.EnumerateFiles(contexts, "*.tmp").Any(), "Atomar gespeicherte Benachrichtigungen hinterlassen keine temporäre Datei.");
            string concurrentContexts = Path.Combine(root, "concurrent-notifications"); Directory.CreateDirectory(concurrentContexts);
            var concurrentStore = new NotificationContextStore(concurrentContexts, errors.Add);
            using (var observed = new ManualResetEventSlim())
            using (var done = new ManualResetEventSlim())
            {
                Exception? incomplete = null;
                Task reader = Task.Run(() =>
                {
                    observed.Set();
                    while (!done.IsSet)
                        foreach (string file in Directory.EnumerateFiles(concurrentContexts, "*.json"))
                            try { concurrentStore.Read(Path.GetFileNameWithoutExtension(file)); }
                            catch (Exception exception) { incomplete = exception; }
                });
                observed.Wait();
                try
                {
                    var largeRequest = request with { Paths = Enumerable.Repeat(root, 4000).ToArray() };
                    for (int index = 0; index < 8; index++) concurrentStore.Create(largeRequest, Environment.ProcessPath!, configuration);
                }
                finally { done.Set(); await reader; }
                Check(incomplete is null && concurrentStore.ReadConfiguration(configuration).Count() == 8,
                    "Parallele Kontextleser sehen ausschließlich vollständig geschriebene Benachrichtigungen. " + incomplete?.ToString());
            }
            return checks;
        }
        finally { foreach (var field in fields) field.Key.SetValue(null, field.Value); }
    }

    /// <summary>Startet einen unabhängigen INI-Schreiberprozess zur Prüfung der dateibezogenen Sperre.</summary>
    private static Process StartWriter(string path, string section)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "--ini-writer", path, section }) info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }

    /// <summary>Erzeugt ausschließlich eine synthetische Arbeitskopie für kontrollierte Testprozesse.</summary>
    private static SvnItem CreateItem(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, ".svn"));
        File.WriteAllText(Path.Combine(path, ".svn", "wc.db"), "Isolierte synthetische Arbeitskopie.");
        File.WriteAllText(Path.Combine(path, "record-commands"), "Isolierter Befehlsnachweis.");
        return new(path, PathType.Directory);
    }

    private static string[] Commands(string path) => File.Exists(Path.Combine(path, "command-calls")) ? File.ReadAllLines(Path.Combine(path, "command-calls")) : Array.Empty<string>();

    /// <summary>Begrenzt das Warten auf einen atomar veröffentlichten Bereitschaftsmarker des Testprozesses.</summary>
    private static async Task WaitForFileAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(path)) await Task.Delay(20, timeout.Token);
    }
}
