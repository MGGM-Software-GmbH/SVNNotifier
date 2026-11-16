using System.ComponentModel;
using CHD.SVN_Notifier;

internal static class ManualActionTests
{
    /// <summary>Prüft alle manuellen SVN-Befehle vor automatischen Statusprüfungen und Auto-Update-Vorbereitungen.</summary>
    internal static async Task<int> RunAsync(string output)
    {
        int checks = 0;
        int previousTimeout = Config.SvnCommandTimeout;
        bool previousChangeLog = Config.ChangeLogBeforeUpdate, previousSilent = Config.UpdateAllSilently;
        var previousFolders = Config.ReadSvnFolders();
        void Check(bool result, string description)
        {
            if (!result) throw new InvalidOperationException(description);
            checks++;
            Console.WriteLine("Manuelle Priorität: " + description);
        }
        try
        {
            Config.SvnCommandTimeout = 0;
            Config.ChangeLogBeforeUpdate = false;
            foreach (string command in new[] { "Update", "Commit", "Cleanup", "Log", "ChangeLog", "UpdateLog", "UpdateAll", "UpdateAllSilent" })
            foreach (bool prepareAutoUpdate in new[] { false, true })
            {
                string root = Path.Combine(output, "manual-" + command + "-" + prepareAutoUpdate);
                var automatic = CreateItem(Path.Combine(root, "A"));
                var remaining = CreateItem(Path.Combine(root, "B"));
                var target = CreateItem(Path.Combine(automatic.Path, "Unterordner"), false);
                target.Enabled = false;
                Config.SaveSvnFolders(new BindingList<SvnItem> { automatic, remaining, target });
                using var coordinator = new ApplicationCoordinator(action => action());
                automatic = coordinator.Folders.Single(item => item.Path == automatic.Path);
                remaining = coordinator.Folders.Single(item => item.Path == remaining.Path);
                target = coordinator.Folders.Single(item => item.Path == target.Path);
                automatic.RepositoryUrl = "file:///fixture";
                automatic.AutoUpdate = prepareAutoUpdate;
                target.Status = SvnStatus.NeedUpdate_Modified;
                string stage = prepareAutoUpdate ? "info" : "status";
                string hold = Path.Combine(automatic.Path, "hold-" + stage);
                File.WriteAllText(hold, "Angehaltene automatische Leseprüfung ohne Zeitlimit.");
                Task action = Task.CompletedTask;
                try
                {
                    Task polling = coordinator.PollAsync();
                    await WaitForFileAsync(Path.Combine(automatic.Path, stage + ".pid"));
                    // Alle aktualisieren soll hier nur das manuelle Ziel auswählen.
                    if (command.StartsWith("UpdateAll", StringComparison.Ordinal)) automatic.Status = SvnStatus.UpToDate;
                    Config.UpdateAllSilently = command == "UpdateAllSilent";
                    action = command switch
                    {
                        "Update" => coordinator.UpdateAsync(target),
                        "UpdateAll" or "UpdateAllSilent" => coordinator.UpdateAllAsync(),
                        _ => coordinator.RunManualAsync(target, () => Task.Run(() =>
                        {
                            switch (command)
                            {
                                case "Commit": Check(SvnTools.Commit(target), "Commit wird gestartet."); break;
                                case "Cleanup": Check(SvnTools.Cleanup(target), "Cleanup wird gestartet."); break;
                                case "Log": SvnTools.OpenLogWindow(target.Path); break;
                                case "ChangeLog": SvnTools.OpenChangeLogWindow(target, true); break;
                                case "UpdateLog": SvnTools.OpenChangeLogWindow(target, false); break;
                            }
                        }))
                    };
                    await action.WaitAsync(TimeSpan.FromSeconds(5));
                    await SvnTools.WaitForWorkingCopyAsync(target).WaitAsync(TimeSpan.FromSeconds(5));
                    string expected = command switch
                    {
                        "Update" or "UpdateAll" => "/command:update", "UpdateAllSilent" => "update",
                        "Commit" => "/command:commit", "Cleanup" => "/command:cleanup", _ => "/command:log"
                    };
                    Check(Calls(target).Contains(expected), "Manueller Befehl startet trotz angehaltener Automatik: " + command + "/" + stage);
                    Check(remaining.Status == SvnStatus.Unknown && !Calls(automatic).Contains("update"),
                        "Weitere automatische Prüfungen und Auto-Updates erhalten keinen Vorrang: " + command + "/" + stage);
                    Check(coordinator.Errors.Count == 0 && target.RetryReady,
                        "Gemeinsame Arbeitskopie ist vor dem manuellen Start freigegeben: " + command + "/" + stage);
                    await polling;
                }
                finally
                {
                    File.Delete(hold);
                    automatic.AutoUpdate = false;
                    await coordinator.PollAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    await action.WaitAsync(TimeSpan.FromSeconds(10));
                    await SvnTools.WaitForWorkingCopyAsync(automatic).WaitAsync(TimeSpan.FromSeconds(10));
                }
            }

            var writing = CreateItem(Path.Combine(output, "manual-waits-for-writing"));
            Config.SaveSvnFolders(new BindingList<SvnItem> { writing });
            using (var coordinator = new ApplicationCoordinator(action => action()))
            {
                writing = coordinator.Folders.Single();
                writing.Status = SvnStatus.NeedUpdate;
                string hold = Path.Combine(writing.Path, "hold-update");
                File.WriteAllText(hold, "Nicht unterbrechbarer eigener Schreibvorgang.");
                try
                {
                    Check(SvnTools.BeginUpdateSilently(writing), "Gegenprobe startet einen eigenen Schreibvorgang.");
                    await WaitForFileAsync(Path.Combine(writing.Path, "update.pid"));
                    Task manual = coordinator.RunManualAsync(writing, () => Task.Run(() => SvnTools.Commit(writing)));
                    await coordinator.PollAsync();
                    Check(!manual.IsCompleted && !Calls(writing).Contains("/command:commit"),
                        "Manueller Commit wartet auf den laufenden Schreibvorgang, ohne ihn zu beenden.");
                    File.Delete(hold);
                    await manual.WaitAsync(TimeSpan.FromSeconds(5));
                    await SvnTools.WaitForWorkingCopyAsync(writing).WaitAsync(TimeSpan.FromSeconds(5));
                    Check(Calls(writing).Contains("/command:commit"), "Nach dem Schreibende startet der vorgemerkte manuelle Commit.");
                }
                finally
                {
                    File.Delete(hold);
                    await SvnTools.WaitForWorkingCopyAsync(writing).WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
            return checks;
        }
        finally
        {
            Config.SvnCommandTimeout = previousTimeout;
            Config.ChangeLogBeforeUpdate = previousChangeLog;
            Config.UpdateAllSilently = previousSilent;
            Config.SaveSvnFolders(previousFolders);
        }
    }

    /// <summary>Erzeugt protokollierbare synthetische Arbeitskopien einschließlich gemeinsamer Unterordner.</summary>
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

    /// <summary>Liest die vom echten Testprozess protokollierten Befehle der isolierten Arbeitskopie.</summary>
    private static string[] Calls(SvnItem item) => File.Exists(Path.Combine(item.Path, "command-calls"))
        ? File.ReadAllLines(Path.Combine(item.Path, "command-calls")) : Array.Empty<string>();

    /// <summary>Wartet begrenzt auf den tatsächlich gestarteten Testprozess.</summary>
    private static async Task WaitForFileAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(path) || new FileInfo(path).Length == 0) await Task.Delay(20, timeout.Token);
    }
}
