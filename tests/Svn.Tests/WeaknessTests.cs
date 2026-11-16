using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using CHD;
using CHD.SVN_Notifier;

internal static class WeaknessTests
{
    /// <summary>Prüft Fehlerübergabe, begrenzte Konfigurationssperren, SVN-Zeitlimits und variable Repository-Pfade mit eigenen Dateien.</summary>
    internal static async Task<int> RunAsync(string output, string? only = null)
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
            Console.WriteLine("Schwachstelle: " + message);
        }
        var fields = typeof(Config).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => !field.IsLiteral && !field.IsInitOnly).ToDictionary(field => field, field => field.GetValue(null));
        string root = Path.Combine(output, "weaknesses"), active = Path.Combine(root, "active.ini"), imported = Path.Combine(root, "import.ini");
        Directory.CreateDirectory(Path.Combine(root, ".svn"));
        File.WriteAllText(Path.Combine(root, ".svn", "wc.db"), "Isolierte Arbeitskopiemarkierung.");
        try
        {
            Config.Init(active);
            if (only is null or "timeouts")
            {
                Config.SvnCommandTimeout = 17;
                foreach (string invalid in new[] { "-1", "86401", int.MaxValue.ToString(), "2147483648", "invalid", "" })
                {
                    byte[] original = File.ReadAllBytes(active);
                    File.WriteAllText(imported, "[Settings]\nSvnCommandTimeout=" + invalid + "\n");
                    bool rejected = false;
                    try { Config.Import(imported); } catch (FormatException) { rejected = true; }
                    Check(rejected && Config.SvnCommandTimeout == 17 && File.ReadAllBytes(active).SequenceEqual(original),
                        "Ungültiges SVN-Zeitlimit erhält aktive Datei und ungespeicherte Optionen: " + invalid);
                    rejected = false;
                    try { Config.Init(imported); } catch (FormatException) { rejected = true; }
                    Check(rejected && Config.SvnCommandTimeout == 17, "Programmstart weist ungültiges SVN-Zeitlimit ab: " + invalid);
                    Config.Init(active); Config.SvnCommandTimeout = 17;
                }
                foreach (int valid in new[] { 0, 1, 60, Config.MaximumSvnCommandTimeout })
                {
                    File.WriteAllText(imported, "[Settings]\nSvnCommandTimeout=" + valid + "\n");
                    Config.Import(imported);
                    Check(Config.SvnCommandTimeout == valid, "Gültiges SVN-Zeitlimit bleibt erhalten: " + valid);
                    var runner = new SvnOperationRunner(_ => { });
                    var result = await runner.TryStart(root, "timeout-boundary", Environment.ProcessPath!, new[] { "--child", "20", "0" }, true, valid)!;
                    Check(result.Success, "Akzeptiertes SVN-Zeitlimit erlaubt einen echten Prozessstart: " + valid);
                    await runner.StopAsync();
                }
                File.WriteAllText(imported, "[Settings]\n"); Config.Import(imported);
                Check(Config.SvnCommandTimeout == 60, "Fehlendes SVN-Zeitlimit verwendet den Standardwert");
            }
            if (only is null or "paths")
            {
                string variableRoot = "SVNNOTIFIER_TEST_ROOT_" + Guid.NewGuid().ToString("N");
                string variableChild = "SVNNOTIFIER_TEST_CHILD_" + Guid.NewGuid().ToString("N");
                Environment.SetEnvironmentVariable(variableRoot, root);
                Environment.SetEnvironmentVariable(variableChild, "child");
                try
                {
                    string first = "%" + variableRoot + "%", second = "%" + variableChild + "%";
                    foreach (var pair in new[]
                    {
                        (root, root), (first + "\\repo", root + "\\repo"),
                        (first + "\\" + second, root + "\\child"),
                        (second + "-" + second, "child-child"), (second + second, "childchild"),
                        ("%UNKNOWN_" + variableRoot + "%\\" + second, "%UNKNOWN_" + variableRoot + "%\\child"),
                        ("100%\\" + second, "100%\\child"), ("%%", "%%")
                    })
                    {
                        var item = new SvnItem(pair.Item1, PathType.Directory);
                        Check(item.Path == pair.Item2, "Pfadvariablen werden unabhängig aufgelöst: " + pair.Item1);
                        Check(item.origPath == pair.Item1 && SvnItem.Deserialize(item.Serialize()).Path == pair.Item2,
                            "Speichern und Laden erhält variablen Originalpfad");
                    }
                }
                finally { Environment.SetEnvironmentVariable(variableRoot, null); Environment.SetEnvironmentVariable(variableChild, null); }
            }
            if (only is null or "locks")
            {
                foreach (string operation in new[] { "save", "import", "read", "short-lock" })
                {
                    Config.Init(active); Config.ShowNotifications = !Config.ShowNotifications;
                    byte[] original = File.ReadAllBytes(active);
                    bool desired = Config.ShowNotifications;
                    File.WriteAllText(imported, "[Settings]\nSvnCommandTimeout=42\n");
                    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                    foreach (string argument in new[] { "--ini-holder", active, operation == "short-lock" ? "400" : "7000" }) start.ArgumentList.Add(argument);
                    using var holder = Process.Start(start)!;
                    try
                    {
                        Check(await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)) == "READY", operation + ": Eigener Sperrhalter ist aktiv");
                        var timer = Stopwatch.StartNew();
                        bool rejected = false;
                        try
                        {
                            if (operation == "import") Config.Import(imported);
                            else if (operation == "read") _ = new IniFile(active, true);
                            else Config.SaveSettings();
                        }
                        catch (IOException exception) { rejected = exception.Message == Loc.Config_LockTimeout; }
                        Check(operation == "short-lock" ? !rejected : rejected && timer.Elapsed < TimeSpan.FromSeconds(5),
                            operation + ": Kurze Sperre wird abgewartet, dauerhafte Sperre begrenzt und verständlich gemeldet");
                        if (operation != "short-lock")
                            Check(File.ReadAllBytes(active).SequenceEqual(original) && Config.ShowNotifications == desired,
                                operation + ": Sperrfehler erhält Datei und ungespeicherte Optionen");
                    }
                    finally
                    {
                        if (!holder.HasExited) holder.Kill(true);
                        await holder.WaitForExitAsync();
                    }
                    Config.SaveSettings();
                    Check(new IniFile(active, true).ReadBoolean("Settings", "ShowNotifications", !desired) == desired,
                        operation + ": Wiederholung nach Freigabe speichert den erhaltenen Entwurf");
                    Check(!Directory.EnumerateFiles(root, ".svnnotifier-import-*.ini").Any(), operation + ": Keine verwaiste Importdatei");
                }
            }
            if (only is null or "transition-errors")
            {
                Config.Init(active); Config.DoPauseAfterApplicationStartup = false;
                Config.TortoiseSVNpath = Path.Combine(root, "missing.exe");
                using var coordinator = new ApplicationCoordinator(action => action());
                coordinator.Folders.Clear();
                var folder = new SvnItem(root, PathType.Directory) { Enabled = false };
                int events = 0;
                coordinator.UpdateLogRequested += process =>
                {
                    Check(process.updateError && process.processOutput.Cast<string>().Any(line => line.Contains("SVN-Prozessfehler") || line.Contains("SVN-Diagnosefehler")), "Übergang meldet die ursprüngliche Prozessursache");
                    events++;
                };
                foreach (string action in new[] { "commit", "cleanup", "log" })
                foreach (string transition in new[] { "poll", "import", "failed-import", "installer" })
                {
                    int previous = events;
                    if (action == "commit") Check(SvnTools.Commit(folder), "Commit-Testauftrag angenommen");
                    else if (action == "cleanup") Check(SvnTools.Cleanup(folder), "Cleanup-Testauftrag angenommen");
                    else SvnTools.OpenLogWindow(root);
                    await SvnTools.WaitForTrackedOperationsAsync();
                    bool changed = false;
                    Task Change()
                    {
                        Check(events == previous + 1, "Prozessfehler wird vor dem Konfigurations- oder Installationswechsel gemeldet");
                        changed = true;
                        if (transition is "failed-import" or "installer") throw new IOException("Isolierter abgewiesener Wechsel.");
                        return Task.CompletedTask;
                    }
                    if (transition == "poll") await coordinator.PollAsync();
                    else
                    {
                        try
                        {
                            if (transition == "installer") await coordinator.PrepareInstallationAsync(Change);
                            else await coordinator.ChangeConfigurationAsync(Change);
                        }
                        catch (IOException) when (transition is "failed-import" or "installer") { }
                        Check(changed && !coordinator.Suspended, "Nach dem Wechsel ist der Coordinator wieder freigegeben");
                    }
                    await coordinator.PollAsync();
                    Check(events == previous + 1 && SvnTools.TakeCompleted().Length == 0, action + "/" + transition + ": Fehler genau einmal gemeldet");
                }
                foreach (string diagnostic in new[] { "", "SVN-Diagnosefehler: IOException" })
                {
                    int previous = events;
                    lock (SvnTools.svnFolderProcesses.SyncRoot)
                        SvnTools.svnFolderProcesses.Add(new SvnFolderProcess(folder, new Process(), false)
                        {
                            IsInteractive = true,
                            Completion = Task.FromResult(new SvnProcessResult(-1, "", diagnostic, true))
                        });
                    try
                    {
                        await coordinator.PrepareInstallationAsync(() =>
                        {
                            Check(events == previous + (diagnostic.Length > 0 ? 1 : 0), "Absichtlicher Installationsabbruch bleibt still; zusätzlicher Diagnosefehler bleibt sichtbar");
                            throw new IOException("Isoliert abgewiesener Installer.");
                        });
                    }
                    catch (IOException) { }
                }
            }
            if (only is null or "dialog-exits")
            {
                foreach (bool interactive in new[] { false, true })
                foreach (int? exitCode in new int?[] { 0, -1, 1, unchecked((int)0xC0000005), null })
                foreach (bool cancelled in new[] { false, true })
                {
                    var folder = new SvnItem(root, PathType.Directory);
                    using var process = new Process();
                    var tracked = new SvnFolderProcess(folder, process, false)
                    {
                        IsInteractive = interactive,
                        Completion = Task.FromResult(new SvnProcessResult(exitCode, "", "", cancelled))
                    };
                    SvnTools.ReadProcessOutput(tracked);
                    bool expectedError = cancelled || (exitCode != 0 && !(interactive && exitCode == -1));
                    Check(tracked.updateError == expectedError, $"Dialog- und Konsolenende bleiben unterscheidbar: interaktiv={interactive}, Exit={exitCode}, Abbruch={cancelled}");
                    Check(!expectedError || tracked.processOutput.Count > 0, "Ein tatsächlicher Prozessfehler besitzt immer eine sichtbare Diagnose");
                }
                foreach (string diagnostic in new[] { "SVN-Prozessfehler: Win32Exception", "SVN-Diagnosefehler: IOException" })
                {
                    using var process = new Process();
                    var tracked = new SvnFolderProcess(new SvnItem(root, PathType.Directory), process, false)
                    {
                        IsInteractive = true,
                        Completion = Task.FromResult(new SvnProcessResult(-1, "", diagnostic, false))
                    };
                    SvnTools.ReadProcessOutput(tracked);
                    Check(tracked.updateError && tracked.processOutput.Contains(diagnostic), "Echter Start- oder Diagnosefehler wird trotz Dialog-Rückgabecode angezeigt");
                }
            }
        }
        finally { foreach (var pair in fields) pair.Key.SetValue(null, pair.Value); }
        return checks;
    }
}
