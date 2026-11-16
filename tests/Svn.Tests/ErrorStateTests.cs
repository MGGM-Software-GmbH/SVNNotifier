using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using CHD.SVN_Notifier;

internal static class ErrorStateTests
{
    /// <summary>Prüft Diagnoseausfälle, ungültige Intervalle und fehlgeschlagene manuelle Dialoge mit eigenen Dateien und Prozessen.</summary>
    internal static async Task<int> RunAsync(string output, string? only = null)
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
            Console.WriteLine("Fehlerzustand: " + message);
        }
        var fields = typeof(Config).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => !field.IsLiteral && !field.IsInitOnly).ToDictionary(field => field, field => field.GetValue(null));
        string root = Path.Combine(output, "error-states");
        Directory.CreateDirectory(Path.Combine(root, ".svn"));
        File.WriteAllText(Path.Combine(root, ".svn", "wc.db"), "Isolierte Arbeitskopiemarkierung.");
        try
        {
            if (only is null or "diagnostic")
            foreach (string mode in new[] { "timeout", "cancel", "stop", "completed", "end" })
            {
                Process? child = null;
                var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var runner = new SvnOperationRunner(message =>
                {
                    bool begin = message.Contains("\"phase\":\"begin\"");
                    if (begin) began.TrySetResult();
                    if ((mode != "end" && begin) || (mode == "end" && message.Contains("\"phase\":\"end\"")))
                        throw new IOException("Isolierter Diagnosefehler.");
                }, info =>
                {
                    Process started = Process.Start(info)!;
                    child = Process.GetProcessById(started.Id);
                    _ = child.Handle;
                    return started;
                });
                using var cancellation = new CancellationTokenSource();
                Task<SvnProcessResult> completion = runner.TryStart(root, "status", Environment.ProcessPath!,
                    new[] { "--child", mode is "completed" or "end" ? "10" : "30000", "0" }, true,
                    mode == "timeout" ? 1 : 0, cancellation.Token)!;
                try
                {
                    await began.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    if (mode == "cancel") cancellation.Cancel();
                    Task? stopping = mode == "stop" ? runner.StopAsync() : null;
                    SvnProcessResult result = await completion.WaitAsync(TimeSpan.FromSeconds(8));
                    if (stopping is not null) await stopping.WaitAsync(TimeSpan.FromSeconds(2));
                    Check(!result.Success && result.Error.Contains("SVN-Diagnosefehler: IOException"), mode + ": Diagnosefehler wird im Ergebnis gemeldet.");
                    Check(child!.HasExited, mode + ": Eigener Prozess ist vor Freigabe beendet.");
                    Check(result.TimedOut == (mode == "timeout") && result.Cancelled == (mode is "timeout" or "cancel" or "stop"), mode + ": Timeout und Abbruch bleiben wirksam und unterscheidbar.");
                    using var claim = WorkingCopyLease.TryAcquire(root, "probe", out _, out _);
                    Check(claim is not null, mode + ": Reservierung ist nach Prozessende frei.");
                }
                finally
                {
                    if (child is not null)
                    {
                        if (!child.HasExited) child.Kill(true);
                        await child.WaitForExitAsync(); child.Dispose();
                    }
                    await runner.StopAsync();
                }
            }
            string active = Path.Combine(root, "active.ini"), imported = Path.Combine(root, "import.ini");
            File.WriteAllText(active, "[Settings]\nDefaultActiveStatusUpdateInterval=5\n[Folders]\n");
            Config.Init(active);
            if (only is null or "intervals")
            {
            foreach (string name in new[] { "DefaultActiveStatusUpdateInterval", "DefaultIdleStatusUpdateInterval", "PauseAfterApplicationStartupInterval", "PauseAfterWindowsResumeInterval" })
            {
                foreach (int invalid in new[] { int.MinValue, -1, 360000, int.MaxValue })
                {
                    byte[] before = File.ReadAllBytes(active);
                    File.WriteAllText(imported, $"[Settings]\n{name}={invalid}\n[Folders]\n");
                    bool rejected = false;
                    try { Config.Import(imported); } catch (FormatException) { rejected = true; }
                    Check(rejected && File.ReadAllBytes(active).SequenceEqual(before) && Config.iniFileName == active,
                        name + ": Ungültiger Import bleibt ohne Übernahme: " + invalid);
                }
                foreach (int valid in new[] { 0, 359999 })
                {
                    File.WriteAllText(imported, $"[Settings]\n{name}={valid}\n[Folders]\n");
                    Config.Import(imported);
                    Check((int)typeof(Config).GetField(name)!.GetValue(null)! == valid, name + ": Intervallgrenze wird übernommen: " + valid);
                }
            }
            foreach (bool activeInterval in new[] { true, false })
            {
                foreach (int invalid in new[] { int.MinValue, -2, 360000, int.MaxValue })
                {
                    byte[] before = File.ReadAllBytes(active);
                    string interval = invalid.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    File.WriteAllText(imported, "[Settings]\n[Folders]\nFolder1=" + root + "|" + (activeInterval ? interval + "|-1" : "-1|" + interval) + "|False|0|True\n");
                    bool rejected = false;
                    try { Config.Import(imported); } catch (FormatException) { rejected = true; }
                    Check(rejected && File.ReadAllBytes(active).SequenceEqual(before), "Ungültiges Repository-Intervall wird vor Übernahme abgewiesen: " + activeInterval + "/" + invalid);
                }
            }
            foreach (string intervals in new[] { "-1|-1", "0|0", "359999|359999", "0|-1", "-1|0" })
            {
                var item = SvnItem.Deserialize(root + "|" + intervals + "|False|0|True");
                Check(item.GetInterval(true) >= 0 && item.GetInterval(false) >= 0 && item.NextCheckTime(true) >= DateTime.MinValue && item.NextCheckTime(false) >= DateTime.MinValue,
                    "Gültige Repository-Intervalle und getrennte Standardwerte bleiben planbar: " + intervals);
            }
            }
            if (only is null or "manual")
            {
            Config.Init(active);
            Config.DoPauseAfterApplicationStartup = false;
            Config.TortoiseSVNpath = Path.Combine(root, "missing-tortoise.exe");
            var folder = new SvnItem(root, PathType.Directory) { Enabled = false };
            Config.SaveSvnFolders(new BindingList<SvnItem> { folder });
            using var coordinator = new ApplicationCoordinator(action => action());
            folder = coordinator.Folders.Single();
            int dialogs = 0;
            coordinator.UpdateLogRequested += process =>
            {
                Check(process.updateError && process.processOutput.Cast<string>().Any(line => line.Contains("SVN-Prozessfehler")), "Manueller Dialogfehler enthält die Prozessdiagnose.");
                dialogs++;
            };
            foreach (string action in new[] { "commit", "cleanup", "log" })
            {
                bool accepted = action == "commit" ? SvnTools.Commit(folder) : action == "cleanup" ? SvnTools.Cleanup(folder) : OpenLog();
                bool OpenLog() { SvnTools.OpenLogWindow(root); return true; }
                Check(accepted, action + ": Auftrag wird zunächst angenommen.");
                await SvnTools.WaitForTrackedOperationsAsync();
                int before = dialogs;
                await coordinator.PollAsync();
                Check(dialogs == before + 1 && SvnTools.TakeCompleted().Length == 0, action + ": Fehler wird genau einmal angezeigt und Auftrag entnommen.");
                await coordinator.PollAsync();
                Check(dialogs == before + 1, action + ": Keine doppelte Fehlermeldung.");
            }
            }
        }
        finally
        {
            foreach (var pair in fields) pair.Key.SetValue(null, pair.Value);
        }
        return checks;
    }
}
