using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using CHD;
using CHD.SVN_Notifier;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

internal static class Program
{
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr first, IntPtr second);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    private static int checks;
    private static string[] arguments = Array.Empty<string>();
    private static string output = "";
    private static int result = 1;
    private static readonly Dictionary<string, string[]> inventories = new();

    /// <summary>Startet echte WinUI-Ansichten mit eigener Konfiguration, ohne Tray oder Benachrichtigungen auf dem Benutzerdesktop.</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        if (args.Any(argument => argument.StartsWith("/command:cleanup", StringComparison.OrdinalIgnoreCase)))
        {
            string path = args.Single(argument => argument.StartsWith("/path:", StringComparison.OrdinalIgnoreCase))[6..];
            File.WriteAllText(Path.Combine(path, "cleanup-started"), "Isolierter Cleanupdialog.");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!File.Exists(Path.Combine(path, "cleanup-release")) && DateTime.UtcNow < deadline) Thread.Sleep(20);
            return File.Exists(Path.Combine(path, "cleanup-release")) ? 0 : 10;
        }
        if (args.Length > 0 && (args[0] is "info" or "status" || args[0].StartsWith("/command:", StringComparison.OrdinalIgnoreCase)))
        {
            string target = args[0].StartsWith("/command:", StringComparison.OrdinalIgnoreCase)
                ? args.Single(argument => argument.StartsWith("/path:", StringComparison.OrdinalIgnoreCase))[6..]
                : args[^1][..^1];
            if (!File.Exists(Path.Combine(target, "gui-svn-fixture"))) return 12;
            File.AppendAllText(Path.Combine(target, "gui-svn-calls.jsonl"), JsonSerializer.Serialize(args) + Environment.NewLine);
            if (args[0] == "info") Console.WriteLine("<info><entry revision='2'><url>file:///gui-fixture</url></entry></info>");
            if (args[0] == "status") Console.WriteLine("<status><target><entry path='fixture'><wc-status item='normal' props='none'><commit revision='2'/></wc-status></entry><against revision='2'/></target></status>");
            return 0;
        }
        arguments = args; output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        string fixture = Path.Combine(output, "fixture.ini");
        File.WriteAllText(fixture, "[Configuration]\nCheckForNewVersion=false\nHideOnStartup=false\nShowInTaskbar=true\n[Folders]\n");
        Config.Init(fixture);
        Config.SVNpath = Config.TortoiseSVNpath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        Config.CheckForNewVersion = Config.DoPauseAfterApplicationStartup = false;
        Config.FontFamily = "Segoe UI";
        Config.FontSize = args.Contains("--maximum-font") ? 24 : args.Contains("--large-font") ? 12 : 8.5f;
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY_PID", args.Contains("--negative-runtime-context") ? null : Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(initialization =>
        {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            try { _ = new GuiTests.TestApplication(); }
            catch (Exception exception) { RecordFailure(exception); Environment.Exit(1); }
        });
        return result;
    }

    internal static void RecordFailure(Exception exception)
    {
        File.AppendAllText(Path.Combine(output, "unhandled.log"), exception.ToString());
        foreach (System.Collections.DictionaryEntry entry in exception.Data)
            File.AppendAllText(Path.Combine(output, "unhandled.log"), Environment.NewLine + entry.Key + ": " + entry.Value);
        Console.Error.WriteLine(exception); result = 1;
    }

    internal static void RecordDiagnostic(string text) => File.AppendAllText(Path.Combine(output, "xaml-diagnostics.log"), text + Environment.NewLine);

    internal static async Task ExecuteAsync()
    {
        try
        {
            await RunAsync();
            File.WriteAllText(Path.Combine(output, "controls.json"), JsonSerializer.Serialize(inventories, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"GUI-Prüfungen: {checks} erfolgreich.");
            result = 0;
        }
        catch (Exception exception) { RecordFailure(exception); }
    }

    private static async Task RunAsync()
    {
        Assert(ApplicationUpdateInstaller.IsPortable, "Die gebündelte WinUI-Anwendungsassembly bleibt automatisch aktualisierbar");
        var main = new MainWindow(false);
        if (arguments.Contains("--dark")) main.RootElement.RequestedTheme = ElementTheme.Dark;
        main.Activate();
        await WaitAsync(() => main.RootElement.IsLoaded);
        if (arguments.Contains("--diagnose-error-log"))
        {
            foreach (int language in new[] { 1, 0 })
            {
                Config.Language = language;
                await CheckErrorLogAsync(main);
            }
            await main.ExitAsync();
            return;
        }
        await CheckWindowIconsAsync(main);
        if (arguments.Contains("--diagnose-status-icons")) { await main.ExitAsync(); return; }
        foreach (int language in new[] { 1, 0 })
        {
            Config.Language = language; Config.CheckForNewVersion = false;
            Config.SaveSvnFolders(new BindingList<SvnItem>());
            main.Coordinator.Folders.Clear();
            typeof(MainWindow).GetMethod("BuildCommands", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
            var folders = main.Coordinator.Folders;
            var statuses = new[] { SvnStatus.UpToDate, SvnStatus.NeedUpdate, SvnStatus.UpToDate_Modified, SvnStatus.NeedUpdate_Modified, SvnStatus.Error, SvnStatus.Unknown };
            foreach (var status in statuses)
                folders.Add(new SvnItem(Path.Combine(output, "Projekt mit langem Namen " + status, "Entwicklung und Dokumentation"), PathType.Directory) { Status = status, AutoUpdate = folders.Count % 2 == 0 });
            folders.Add(new SvnItem(Path.Combine(output, "Pausiertes Projekt"), PathType.Directory) { Enabled = false });
            main.Coordinator.Save(); await RenderAsync();
            await CaptureAsync(main.RootElement, $"main-initial-{language}");
            var list = Field<ListView>(main, "Repositories");
            if (arguments.Contains("--diagnose-keyboard"))
            {
                list.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, key) =>
                    Console.WriteLine($"Repository KeyDown: key={key.Key}; handled={key.Handled}; source={key.OriginalSource?.GetType().Name}")), true);
                await CheckAllowedDoubleClicksAsync(main, true);
                await main.ExitAsync();
                return;
            }
            if (arguments.Contains("--diagnose-checkboxes"))
            {
                // Trennt den tatsächlichen SDK-Zustand von der Aufnahme eines animierten Composition-Symbols.
                await Task.Delay(1000);
                foreach (var checkbox in Descendants(list).OfType<CheckBox>())
                {
                    var glyph = Descendants(checkbox).OfType<AnimatedIcon>().Single();
                    if (arguments.Contains("--negative-checkbox-state")) AnimatedIcon.SetState(glyph, "NormalIndeterminate");
                    string state = AnimatedIcon.GetState(glyph);
                    Console.WriteLine($"Checkbox {AutomationProperties.GetAutomationId(checkbox)}: IsChecked={checkbox.IsChecked}; AnimatedIcon.State={state}");
                    Assert(checkbox.IsChecked.HasValue && state == (checkbox.IsChecked.Value ? "NormalOn" : "NormalOff"),
                        "Checkbox-Symbolzustand entspricht dem tatsächlichen booleschen Wert");
                }
                await CaptureAsync(main.RootElement, $"checkbox-states-{language}");
                main.Close();
                return;
            }
            if (arguments.Contains("--negative-font-size"))
                foreach (var text in Descendants(list).OfType<TextBlock>().Where(text => text.Text == StatusTextConverter.Text(SvnStatus.UpToDate))) text.FontSize = 1;
            Assert(Descendants(list).OfType<TextBlock>().Any(text => text.Text == StatusTextConverter.Text(SvnStatus.UpToDate) && Math.Abs(text.FontSize - Config.FontSize * 96d / 72d) < 0.1), "Repository-Statustext verwendet die gespeicherte Schriftgröße");
            Assert(list.Items.Count == 7 && list.SelectionMode == ListViewSelectionMode.Single && list.AllowDrop, "Alle Repository-Spalten und Statuszeilen sowie Drag-and-drop bleiben vorhanden");
            var menus = Field<MenuBar>(main, "Menus");
            if (arguments.Contains("--negative-missing-command")) menus.Items[0].Items.RemoveAt(0);
            AssertCommands(menus.Items[0].Items, new[] { "AddFolder", "AddFile", "Remove", "ImportConfiguration", "ExportConfiguration", "Exit" }, "FileMenuItem");
            AssertCommands(menus.Items[1].Items, new[] { "UpdateAll", "Settings" }, "ToolsMenuItem");
            AssertCommands(menus.Items[2].Items, new[] { "CheckApplicationUpdates", "About" }, "HelpMenuItem");
            var toolbar = Field<CommandBar>(main, "Commands");
            AssertCommands(toolbar.PrimaryCommands.OfType<DependencyObject>(), new[] { "AddFolder", "AddFile", "Remove", "ChangeLog", "Update", "Commit", "Open", "Log", "Properties" }, "Toolbar");
            if (arguments.Contains("--negative-heading")) ((Grid)main.RootElement).Children.Add(new TextBlock { Text = "Repositories" });
            Assert(!Descendants(main.RootElement).OfType<TextBlock>().Any(text => text.Text == "Repositories"), "Repository-Liste verzichtet auf eine zusätzliche Überschrift");
            var addFolder = Find<AppBarButton>(toolbar, "AddFolder");
            if (arguments.Contains("--negative-command-alignment")) addFolder.RenderTransform = new TranslateTransform { X = 400 };
            Assert(addFolder.TransformToVisual(main.RootElement).TransformPoint(new Windows.Foundation.Point()).X < 80, "Häufige Listenaktion beginnt am linken Rand des Arbeitsbereichs");
            list.SelectedItem = folders[1]; await RenderAsync();
            Assert(Find<AppBarButton>(toolbar, "Update").IsEnabled, "Eingehende Änderungen können aktualisiert werden");
            list.SelectedItem = folders[0]; await RenderAsync();
            Assert(!Find<AppBarButton>(toolbar, "Update").IsEnabled, "Synchronisierte Repositories brauchen kein Update");
            var firstContainer = (ListViewItem)list.ContainerFromItem(folders[0]);
            if (arguments.Contains("--negative-row-density")) { firstContainer.Height = 80; await RenderAsync(); }
            double maximumRowHeight = Math.Max(34, Config.FontSize * 96d / 72d * 1.5 + 14);
            Assert(firstContainer.ActualHeight <= maximumRowHeight, "Repository-Zeilen bleiben bei der gespeicherten Schrift kompakt");
            var rowDivider = Descendants(firstContainer).OfType<Border>().Single(border => border.VerticalAlignment == VerticalAlignment.Bottom && Grid.GetColumnSpan(border) == 5);
            if (arguments.Contains("--negative-row-divider")) { rowDivider.Height = 8; await RenderAsync(); }
            Assert(rowDivider.ActualHeight == 1 && !rowDivider.IsHitTestVisible, "Dezente Repository-Trennlinie bleibt innerhalb der kompakten Zeile");
            var auto = Find<CheckBox>(firstContainer, "AutoUpdateToggle");
            var enabled = Find<CheckBox>(firstContainer, "MonitorToggle");
            Assert(auto.ActualHeight >= 28 && enabled.ActualHeight >= 28, "Kompakte Repository-Zeilen erhalten bedienbare Checkboxen");
            Assert(auto.ActualHeight <= firstContainer.ActualHeight && enabled.ActualHeight <= firstContainer.ActualHeight, "Checkboxen passen ohne vertikalen Überlauf in die Repository-Zeile");
            bool previous = folders[0].AutoUpdate;
            Toggle(auto); await RenderAsync();
            Assert(folders[0].AutoUpdate != previous, "Repository-Auto-Update lässt sich umschalten");
            Assert(Config.ReadSvnFolders().Single(item => item.Path == folders[0].Path).AutoUpdate == folders[0].AutoUpdate, "Repository-Auto-Update wird gespeichert");
            Toggle(auto); await RenderAsync();
            Assert(folders[0].AutoUpdate == previous, "Erneutes Umschalten stellt den Ausgangswert her");
            Toggle(enabled); await RenderAsync(); Assert(!folders[0].Enabled, "Repository-Prüfung lässt sich ausschalten");
            Assert(Descendants(firstContainer).OfType<TextBlock>().Any(text => text.Text == Loc.SvnStatusText(folders[0].Status, false)), "Deaktivierte Prüfung bleibt im gerenderten Statustext erkennbar");
            Toggle(enabled); await RenderAsync(); Assert(folders[0].Enabled, "Repository-Prüfung lässt sich einschalten");
            await CheckCleanupAsync(main, list, folders, language);
            await CheckDisabledDoubleClicksAsync(main, list, folders);
            list.SelectedItem = folders[1];
            await CaptureAsync(main.RootElement, $"main-{language}");
            main.AppWindow.Resize(new Windows.Graphics.SizeInt32(760, 500)); await RenderAsync();
            toolbar.IsOpen = true; await RenderAsync();
            Assert(toolbar.PrimaryCommands.OfType<AppBarButton>().All(button => button.IsLoaded && button.Visibility == Visibility.Visible), "Symbolleistenaktionen bleiben bei kleiner Fensterbreite im Überlauf erreichbar");
            toolbar.IsOpen = false;
            await CaptureAsync(main.RootElement, $"main-compact-{language}");
            main.AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 800)); await RenderAsync();

            var settings = new SettingsView(main.Handle);
            var tabs = Field<TabView>(settings, "Tabs");
            Assert(tabs.TabItems.Count == 3, "Alle Einstellungsbereiche bleiben vorhanden");
            await WithDialogAsync(main, Loc.SF_Title, settings, async dialog =>
            {
                for (int index = 0; index < 3; index++)
                {
                    tabs.SelectedIndex = index; await RenderAsync();
                    Assert(Descendants(settings).OfType<TextBox>().Where(field => field.IsLoaded).All(field => Math.Abs(field.FontSize - Config.FontSize * 96d / 72d) < 0.1), "Gerenderte Einstellungsfelder verwenden die gespeicherte Schriftgröße");
                    await CaptureAsync(dialog, $"settings-{index}-{language}");
                    CheckButtons(dialog);
                }
                tabs.SelectedIndex = 0; await RenderAsync();
                Find<CheckBox>(settings, nameof(Config.CheckForNewVersion)).IsChecked = true;
                Find<CheckBox>(settings, nameof(Config.ShowNotifications)).IsChecked = !Config.ShowNotifications;
            });
            Assert(!Config.CheckForNewVersion, "Abbrechen verwirft Einstellungsänderungen");
            Assert(Find<CheckBox>(settings, nameof(Config.ShowNotifications)).IsChecked != Config.ShowNotifications,
                "Abbrechen übernimmt den geänderten Benachrichtigungsschalter nicht");
            var save = new SettingsView(main.Handle);
            var svn = Find<TextBox>(save, nameof(Config.SVNpath)); string tool = svn.Text;
            svn.Text = Path.Combine(output, "missing-svn.exe");
            bool rejected = false; try { save.Apply(); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "Ungültige Programmpfade werden abgewiesen");
            svn.Text = tool;
            Find<CheckBox>(save, nameof(Config.CheckForNewVersion)).IsChecked = !arguments.Contains("--negative-save-option");
            int interval = Config.ShowBalloonInterval;
            Find<NumberBox>(save, nameof(Config.DefaultActiveStatusUpdateInterval)).Value = 23;
            Find<NumberBox>(save, nameof(Config.SvnCommandTimeout)).Value = 47;
            Find<ComboBox>(save, nameof(Config.ItemDoubleClickAction)).SelectedIndex = 4;
            save.Apply();
            Assert(Config.CheckForNewVersion && Config.ShowBalloonInterval == interval, "Programmprüfung und vorhandene Benachrichtigungsdauer werden gespeichert");
            Assert(Config.DefaultActiveStatusUpdateInterval == 23 && Config.SvnCommandTimeout == 47 && (int)Config.ItemDoubleClickAction == 4, "Prüfintervalle, Timeout und Doppelklickaktion werden gespeichert");
            Assert(Find<ComboBox>(save, nameof(Config.UpdateWindowAction)).Items.Count == 5, "Alle fünf Tortoise-Fensterabschlussaktionen bleiben erhalten");
            Config.CheckForNewVersion = false;
            CheckSettingsRoundtrip(main.Handle);
            var properties = new RepositoryPropertiesView(folders[0]);
            var custom = Field<CheckBox>(properties, "CustomInput");
            Toggle(custom);
            Assert(Field<NumberBox>(properties, "ActiveInput").IsEnabled, "Eigene Prüfintervalle aktivieren ihre Zeitfelder");
            Field<NumberBox>(properties, "ActiveInput").Value = 31; properties.Apply();
            Assert(folders[0].ActiveStatusUpdateInterval == 31, "Repository-Eigenschaften werden übernommen");
            await WithDialogAsync(main, Loc.PF_Title, properties, dialog => CaptureAsync(dialog, $"properties-{language}"));
            CheckRepositoryProperties(folders[0]);
            await CheckErrorLogAsync(main);
            var about = new AboutView();
            Assert(Descendants(about).OfType<HyperlinkButton>().Count() >= 4, "Alle Herkunfts- und Projektlinks bleiben vorhanden");
            await WithDialogAsync(main, Loc.AF_Title, about, dialog => CaptureAsync(dialog, $"about-{language}"));
            using var process = new Process();
            var update = new SvnFolderProcess(folders[0], process, true);
            update.processOutput.Add("U    C:\\Projekte\\Kundenportal\\README.md");
            update.processOutput.Add("A    C:\\Projekte\\Kundenportal\\Dokumente\\Anleitung.pdf");
            update.processOutput.Add("D    C:\\Projekte\\Kundenportal\\Alt.txt");
            update.processOutput.Add("C    C:\\Projekte\\Kundenportal\\Konflikt ä.txt");
            update.processOutput.Add("G    C:\\Projekte\\Kundenportal\\Zusammenführung ß.txt");
            update.processOutput.Add("svn: E170013: Isolierte Netzwerkfehlermeldung.");
            update.processOutput.Add("Updated to revision 123.");
            var log = new UpdateLogView(update);
            Assert(Descendants(log).OfType<TextBlock>().Any(text => text.Text.Contains("123")), "Updateprotokoll erhält Dateiaktionen und Ergebnis");
            foreach (string action in language == 1 ? new[] { "Aktualisiert", "Hinzugefügt", "Gelöscht", "Konflikt", "Zusammengeführt" } : new[] { "Updated", "Added", "Deleted", "Conflicted", "Merged" })
                Assert(Descendants(log).OfType<TextBlock>().Any(text => text.Text.Contains(action + ": 1")), "Updateprotokoll erhält den Aktionszähler: " + action);
            Assert(Descendants(log).OfType<TextBlock>().Any(text => text.Text.StartsWith(language == 1 ? "Fehler: svn:" : "Error: svn:")), "Updateprotokoll kennzeichnet Fehlerausgaben ausdrücklich");
            Assert(Descendants(log).OfType<TextBlock>().Any(text => text.Text == (language == 1 ? "Mindestens eine Datei enthält Konflikte." : "One or more files are in a conflicted state.")), "Updateprotokoll erhält die Konfliktwarnung");
            await WithDialogAsync(main, Loc.ULF_Title, log, dialog => CaptureAsync(dialog, $"update-log-{language}"));
            using var client = ApplicationUpdater.CreateClient();
            var release = new ApplicationRelease(new Version(3, 0, 0, 15), new Uri("https://github.com/MGGM-Software-GmbH/SVNNotifier/releases"), new Uri("https://example.invalid/package.zip"), new Uri("https://example.invalid/checksums.txt"), 100);
            var applicationUpdate = new ApplicationUpdateView(new ApplicationUpdater(client), release, _ => throw new InvalidOperationException("Kein Installer im Ansichtstest."));
            await WithDialogAsync(main, Loc.AppUpdate_Title, applicationUpdate, async dialog =>
            {
                Assert(Descendants(applicationUpdate).OfType<Button>().Any(button => Equals(button.Content, Loc.AppUpdate_Install)) && dialog.CloseButtonText.Length > 0, "Programmupdate behält Bestätigung und Abbruch");
                await CaptureAsync(dialog, $"application-update-{language}"); CheckButtons(dialog);
            });
            await CheckUpdateStatesAsync(main, release, language);
            await CheckVerifiedDownloadAsync(main, language);
            await CheckRepositoryDialogsAsync(main, language);
            await CheckAllowedDoubleClicksAsync(main);
        }
        await CheckSessionClosingAsync();
        await main.ExitAsync();
    }

    /// <summary>Prüft konfigurierte Zeilenaktionen per Doppelklickaufruf oder eigener Eingabenachricht über echte eigene Kindprozesse und deren Argumente.</summary>
    private static async Task CheckAllowedDoubleClicksAsync(MainWindow main, bool keyboard = false)
    {
        var originals = main.Coordinator.Folders.ToArray();
        string svn = Config.SVNpath, tortoise = Config.TortoiseSVNpath;
        bool changeLog = Config.ChangeLogBeforeUpdate;
        var action = Config.ItemDoubleClickAction;
        string root = Path.Combine(output, "Doppelklick Arbeitskopie ä " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".svn"));
        File.WriteAllText(Path.Combine(root, ".svn", "wc.db"), "Isolierte Testmarkierung.");
        File.WriteAllText(Path.Combine(root, "gui-svn-fixture"), "Isolierter GUI-Prozesstreiber.");
        string calls = Path.Combine(root, "gui-svn-calls.jsonl");
        var item = new SvnItem(root, PathType.Directory);
        string[][] ReadCalls() => File.Exists(calls) ? File.ReadAllLines(calls).Where(line => line.Length > 0).Select(line => JsonSerializer.Deserialize<string[]>(line)!).ToArray() : Array.Empty<string[]>();
        async Task<string[][]> ExecuteAsync(Config.Action selectedAction, SvnStatus status, string expected)
        {
            File.WriteAllText(calls, "");
            item.Status = status;
            Config.ItemDoubleClickAction = arguments.Contains("--negative-doubleclick-route") ? Config.Action.checkNow : selectedAction;
            if (keyboard)
            {
                var list = Field<ListView>(main, "Repositories");
                Assert(((ListViewItem)list.ContainerFromItem(item)).Focus(FocusState.Keyboard), "Repositoryzeile erhält den eigenen Tastaturfokus");
                IntPtr focus = GetFocus();
                Assert(focus != IntPtr.Zero && PostMessage(focus, 0x100, new IntPtr(0x0d), new IntPtr(1)), "Eingabenachricht wird ausschließlich an das eigene fokussierte Testfenster gesendet");
                PostMessage(focus, 0x101, new IntPtr(0x0d), new IntPtr(unchecked((int)0xc0000001)));
            }
            else await (Task)typeof(MainWindow).GetMethod("DoubleClickAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
            await WaitAsync(() => File.Exists(calls) && new FileInfo(calls).Length > 0, "Eigener Doppelklick-Prozesstreiber hat einen Auftrag erhalten");
            await WaitAsync(() => !SvnTools.HasActiveOperations && !main.Coordinator.IsBusy, "Eigene Doppelklick-Prozesse und Reservierungen sind beendet");
            // Der Prozess endet vor der UI-Fortsetzung, die den Status zurücksetzt und die Nachprüfung einreiht.
            if (keyboard && selectedAction == Config.Action.updateAction)
                await WaitAsync(() => item.Status == SvnStatus.Unknown, "Tastatur-Update hat auch seine UI-Fortsetzung beendet");
            Assert(ReadCalls().Any(command => command[0] == expected), "Doppelklick startet den erwarteten eigenen Prozess: " + selectedAction + "/" + status);
            Assert(!File.Exists(Path.Combine(root, ".svn", WorkingCopyLease.FileName)), "Doppelklick gibt die eigene Arbeitskopiereservierung frei");
            return ReadCalls();
        }
        try
        {
            Config.SVNpath = Config.TortoiseSVNpath = Environment.ProcessPath!;
            Config.ChangeLogBeforeUpdate = false;
            main.Coordinator.Folders.Clear(); main.Coordinator.Folders.Add(item); main.Coordinator.Save();
            Field<ListView>(main, "Repositories").SelectedItem = item; await RenderAsync();
            var fullLog = await ExecuteAsync(Config.Action.logAction, SvnStatus.UpToDate, "/command:log");
            Assert(fullLog.Single(command => command[0] == "/command:log").Contains("/path:" + root) &&
                !fullLog.Any(command => command.Any(argument => argument.StartsWith("/revend:", StringComparison.Ordinal))),
                "Synchronisierter Eintrag öffnet das vollständige Protokoll für sein eigenes Ziel");
            var changes = await ExecuteAsync(Config.Action.logAction, SvnStatus.NeedUpdate, "/command:log");
            Assert(changes.Any(command => command[0] == "info") && changes.Single(command => command[0] == "/command:log").Contains("/revend:2") && item.reviewedRevision == 2,
                "Eingehende Änderung bevorzugt das Änderungsprotokoll und merkt die geprüfte Revision");
            var update = await ExecuteAsync(Config.Action.updateAction, SvnStatus.NeedUpdate, "/command:update");
            Assert(update.Single(command => command[0] == "/command:update").Contains("/path:" + root) && !item.AutoUpdate,
                "Manuelles Doppelklick-Update erhält sein Ziel und lässt Auto-Update ausgeschaltet");
            var commit = await ExecuteAsync(Config.Action.commitAction, SvnStatus.UpToDate_Modified, "/command:commit");
            Assert(commit.Single(command => command[0] == "/command:commit").Contains("/path:" + root),
                "Commit-Doppelklick übergibt das ausgewählte eigene Ziel");
            await ExecuteAsync(Config.Action.checkNow, SvnStatus.Unknown, "status");
            await main.Coordinator.PollAsync();
            Assert(item.Status == SvnStatus.UpToDate && !item.AutoUpdate, "Jetzt prüfen liefert den synchronisierten Status ohne Auto-Update-Änderung");
        }
        finally
        {
            Config.SVNpath = svn; Config.TortoiseSVNpath = tortoise; Config.ChangeLogBeforeUpdate = changeLog; Config.ItemDoubleClickAction = action;
            main.Coordinator.Folders.Clear(); foreach (var original in originals) main.Coordinator.Folders.Add(original);
            main.Coordinator.Save();
        }
    }

    /// <summary>Eine statusabhängig deaktivierte Aktion darf auch über Doppelklick keinen SVN-Auftrag oder Fehler auslösen.</summary>
    private static async Task CheckDisabledDoubleClicksAsync(MainWindow main, ListView list, BindingList<SvnItem> folders)
    {
        var previousAction = Config.ItemDoubleClickAction;
        object previousSelection = list.SelectedItem;
        var cases = new[]
        {
            (SvnStatus.UpToDate, Config.Action.updateAction), (SvnStatus.UpToDate_Modified, Config.Action.updateAction),
            (SvnStatus.Unknown, Config.Action.updateAction), (SvnStatus.Error, Config.Action.updateAction),
            (SvnStatus.UpToDate, Config.Action.commitAction), (SvnStatus.NeedUpdate, Config.Action.commitAction),
            (SvnStatus.Unknown, Config.Action.commitAction), (SvnStatus.Error, Config.Action.commitAction),
            (SvnStatus.Unknown, Config.Action.logAction), (SvnStatus.Error, Config.Action.logAction),
            (SvnStatus.Unknown, Config.Action.openAction)
        };
        try
        {
            foreach (var (status, action) in cases)
            {
                var item = folders.First(folder => folder.Status == status);
                list.SelectedItem = item; Config.ItemDoubleClickAction = action; await RenderAsync();
                string before = item.Serialize();
                long logSize = File.Exists(Config.logFileName) ? new FileInfo(Config.logFileName).Length : 0;
                int errors = main.Coordinator.Errors.Count;
                if (arguments.Contains("--negative-doubleclick-gate"))
                    await Task.Run(() => SvnTools.Update(item, false));
                else
                    await (Task)typeof(MainWindow).GetMethod("DoubleClickAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
                await RenderAsync();
                long afterSize = File.Exists(Config.logFileName) ? new FileInfo(Config.logFileName).Length : 0;
                Assert(item.Serialize() == before && item.Status == status && errors == main.Coordinator.Errors.Count && logSize == afterSize,
                    "Deaktivierte Doppelklickaktion bleibt ohne SVN-Auftrag, Fehler oder Datenänderung: " + action + "/" + status);
            }
        }
        finally { Config.ItemDoubleClickAction = previousAction; list.SelectedItem = previousSelection; }
    }

    /// <summary>Übt Entfernen und Eigenschaften über die produktiven Befehle und tatsächlichen Dialogaktionen.</summary>
    private static async Task CheckRepositoryDialogsAsync(MainWindow main, int language)
    {
        var list = Field<ListView>(main, "Repositories");
        var item = main.Coordinator.Folders[0];
        list.SelectedItem = item;
        await RenderAsync();
        main.Coordinator.Save();
        string before = item.Serialize();
        async Task<ContentDialog> OpenAsync(string command)
        {
            if (command == "Settings")
            {
                var settings = Field<MenuBar>(main, "Menus").Items[1].Items.OfType<MenuFlyoutItem>()
                    .Single(control => AutomationProperties.GetAutomationId(control) == command);
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(settings)).Invoke();
            }
            else
            {
                var button = Field<CommandBar>(main, "Commands").PrimaryCommands.OfType<AppBarButton>()
                    .Single(control => AutomationProperties.GetAutomationId(control) == command);
                if (command == "Remove" && arguments.Contains("--negative-remove-binding"))
                {
                    var commands = Field<CommandBar>(main, "Commands").PrimaryCommands;
                    var detached = new AppBarButton { Label = button.Label };
                    AutomationProperties.SetAutomationId(detached, command);
                    commands[commands.IndexOf(button)] = detached;
                    button = detached; await RenderAsync();
                }
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            }
            ContentDialog? dialog = null;
            await WaitAsync(() =>
            {
                dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(main.RootElement.XamlRoot)
                    .Where(popup => popup.Child is not null).SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>().FirstOrDefault();
                return dialog?.IsLoaded == true;
            }, "Repository-Befehl öffnet den zugehörigen Dialog: " + command);
            return dialog!;
        }
        async Task CloseAsync(ContentDialog dialog, bool confirm)
        {
            string name = confirm ? "PrimaryButton" : "CloseButton";
            string text = confirm ? dialog.PrimaryButtonText : dialog.CloseButtonText;
            Invoke(Descendants(dialog).OfType<Button>().Single(button => button.Name == name && Equals(button.Content, text)));
            await WaitAsync(() => !dialog.IsLoaded, "Repository-Dialogaktion schließt den Dialog");
        }
        if (arguments.Contains("--shell-settings"))
        {
            bool originalCheck = Config.CheckForNewVersion, originalTaskbar = Config.ShowInTaskbar;
            byte[] originalIni = File.ReadAllBytes(Config.iniFileName);
            var settingsDialog = await OpenAsync("Settings");
            var settingsView = (SettingsView)settingsDialog.Content;
            Find<CheckBox>(settingsView, nameof(Config.CheckForNewVersion)).IsChecked = !originalCheck;
            Find<CheckBox>(settingsView, nameof(Config.ShowInTaskbar)).IsChecked = !originalTaskbar;
            if (arguments.Contains("--negative-settings-cancel")) settingsView.Apply();
            await CloseAsync(settingsDialog, false);
            Assert(Config.CheckForNewVersion == originalCheck && Config.ShowInTaskbar == originalTaskbar && File.ReadAllBytes(Config.iniFileName).SequenceEqual(originalIni),
                "Abbrechen im tatsächlichen Einstellungsdialog erhält Optionen und INI-Datei");
            settingsDialog = await OpenAsync("Settings");
            settingsView = (SettingsView)settingsDialog.Content;
            Find<CheckBox>(settingsView, nameof(Config.CheckForNewVersion)).IsChecked = !originalCheck;
            Find<CheckBox>(settingsView, nameof(Config.ShowInTaskbar)).IsChecked = !originalTaskbar;
            await CloseAsync(settingsDialog, true);
            await RenderAsync();
            if (arguments.Contains("--negative-settings-effect")) main.AppWindow.IsShownInSwitchers = originalTaskbar;
            var savedSettings = new CHD.IniFile(Config.iniFileName, true);
            Assert(Config.CheckForNewVersion == !originalCheck && Config.ShowInTaskbar == !originalTaskbar &&
                savedSettings.ReadBoolean("Settings", nameof(Config.ShowInTaskbar), originalTaskbar) == !originalTaskbar &&
                main.AppWindow.IsShownInSwitchers == !originalTaskbar,
                "Bestätigte Einstellungen werden gespeichert und auf das tatsächliche Taskleistenfenster angewendet");
            settingsDialog = await OpenAsync("Settings");
            settingsView = (SettingsView)settingsDialog.Content;
            Find<CheckBox>(settingsView, nameof(Config.CheckForNewVersion)).IsChecked = originalCheck;
            Find<CheckBox>(settingsView, nameof(Config.ShowInTaskbar)).IsChecked = originalTaskbar;
            await CloseAsync(settingsDialog, true);
        }
        var cancelled = await OpenAsync("Properties");
        var properties = (RepositoryPropertiesView)cancelled.Content;
        Field<TextBox>(properties, "PathInput").Text = "cancelled-properties-path";
        Field<CheckBox>(properties, "AutoUpdateInput").IsChecked = !item.AutoUpdate;
        if (arguments.Contains("--negative-properties-cancel")) properties.Apply();
        await CloseAsync(cancelled, false);
        Assert(item.Serialize() == before && Config.ReadSvnFolders().Single(folder => folder.Path == item.Path).Serialize() == before,
            "Abbrechen im tatsächlichen Eigenschaftendialog erhält Objekt und gespeicherten Eintrag");
        var removal = await OpenAsync("Remove");
        await CloseAsync(removal, false);
        Assert(main.Coordinator.Folders.Contains(item) && Config.ReadSvnFolders().Any(folder => folder.Path == item.Path),
            "Abbrechen der Entfernen-Rückfrage erhält den ausgewählten Eintrag");
        removal = await OpenAsync("Remove");
        await CaptureAsync(removal, $"remove-confirmation-{language}");
        await CloseAsync(removal, true);
        await WaitAsync(() => !main.Coordinator.Folders.Contains(item), "Bestätigtes Entfernen betrifft den ausgewählten Eintrag");
        Assert(!Config.ReadSvnFolders().Any(folder => folder.Path == item.Path) && main.Coordinator.Folders.Count == 6,
            "Bestätigtes Entfernen wird gespeichert und erhält die übrigen Einträge");
        list.SelectedItem = main.Coordinator.Folders[0]; await RenderAsync();
        var orphan = (SvnItem)list.SelectedItem;
        var opened = await OpenAsync("Properties");
        main.Coordinator.Folders.Remove(orphan); main.Coordinator.Save();
        string[] remaining = main.Coordinator.Folders.Select(folder => folder.Serialize()).ToArray();
        Field<TextBox>((RepositoryPropertiesView)opened.Content, "PathInput").Text = Path.Combine(output, "orphan-properties-path");
        if (arguments.Contains("--negative-orphan-properties")) main.Coordinator.Folders.Add(orphan);
        await CloseAsync(opened, true);
        Assert(main.Coordinator.Folders.Select(folder => folder.Serialize()).SequenceEqual(remaining) &&
            Config.ReadSvnFolders().Select(folder => folder.Serialize()).SequenceEqual(remaining),
            "Speichern nach zwischenzeitlichem Entfernen verändert weder fremde Einträge noch fügt es den entfernten Eintrag wieder hinzu");
    }

    /// <summary>Prüft Verwerfen, Eingabefehler und beide Intervallarten an einem isolierten Eintrag.</summary>
    private static void CheckRepositoryProperties(SvnItem source)
    {
        var item = new SvnItem(source.origPath, source.pathType);
        bool enabled = item.Enabled, autoUpdate = item.AutoUpdate;
        int active = item.ActiveStatusUpdateInterval, idle = item.IdleStatusUpdateInterval;
        var discarded = new RepositoryPropertiesView(item);
        Field<TextBox>(discarded, "PathInput").Text = "unapplied-path";
        Field<CheckBox>(discarded, "EnabledInput").IsChecked = !enabled;
        Field<CheckBox>(discarded, "AutoUpdateInput").IsChecked = !autoUpdate;
        Assert(item.origPath == source.origPath && item.Enabled == enabled && item.AutoUpdate == autoUpdate && item.ActiveStatusUpdateInterval == active && item.IdleStatusUpdateInterval == idle,
            "Verworfene Repository-Eigenschaften verändern den Eintrag nicht");
        var invalid = new RepositoryPropertiesView(item);
        Field<TextBox>(invalid, "PathInput").Text = "invalid|path";
        bool rejected = false;
        try { invalid.Apply(); } catch (InvalidDataException) { rejected = true; }
        Assert(rejected && item.origPath == source.origPath && item.Enabled == enabled && item.AutoUpdate == autoUpdate,
            "Ungültige Repository-Pfade werden vor jeder Übernahme abgewiesen");
        var custom = new RepositoryPropertiesView(item);
        Field<CheckBox>(custom, "CustomInput").IsChecked = true;
        Field<NumberBox>(custom, "ActiveInput").Value = 0;
        Field<NumberBox>(custom, "IdleInput").Value = 59;
        Field<CheckBox>(custom, "AutoUpdateInput").IsChecked = !autoUpdate;
        custom.Apply();
        var reopened = new RepositoryPropertiesView(item);
        Assert(item.ActiveStatusUpdateInterval == 0 && item.IdleStatusUpdateInterval == 59 && item.AutoUpdate == !autoUpdate &&
            Field<NumberBox>(reopened, "ActiveInput").Value == 0 && Field<NumberBox>(reopened, "IdleInput").Value == 59,
            "Eigene Repository-Intervalle einschließlich Null und Auto-Update bleiben beim Wiederöffnen erhalten");
        Field<CheckBox>(reopened, "CustomInput").IsChecked = false;
        reopened.Apply();
        var defaults = new RepositoryPropertiesView(item);
        Assert(item.ActiveStatusUpdateInterval == -1 && item.IdleStatusUpdateInterval == -1 &&
            Field<CheckBox>(defaults, "CustomInput").IsChecked == false &&
            Field<NumberBox>(defaults, "ActiveInput").Value == Config.DefaultActiveStatusUpdateInterval &&
            Field<NumberBox>(defaults, "IdleInput").Value == Config.DefaultIdleStatusUpdateInterval,
            "Globale Repository-Intervalle werden mit ihren gespeicherten Standardwerten wieder geöffnet");
    }

    /// <summary>Übt Fehler und Abbruch über die tatsächlichen Updatebuttons mit kontrollierten HTTP-Antworten aus.</summary>
    private static async Task CheckUpdateStatesAsync(MainWindow main, ApplicationRelease release, int language)
    {
        foreach (bool blocked in new[] { false, true })
        {
            using var handler = new UpdateStateHandler(blocked);
            using var client = new HttpClient(handler);
            bool installerCalled = false;
            var view = new ApplicationUpdateView(new ApplicationUpdater(client), release, _ => { installerCalled = true; return Task.CompletedTask; });
            await WithDialogAsync(main, Loc.AppUpdate_Title, view, async dialog =>
            {
                Invoke(Field<Button>(view, "install"));
                await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (blocked)
                {
                    Assert(Field<Button>(view, "cancel").Visibility == Visibility.Visible && !Field<Button>(view, "install").IsEnabled, "Laufender Download zeigt Abbrechen und sperrt einen zweiten Download");
                    Invoke(Field<Button>(view, "cancel"));
                    await WaitAsync(() => Field<TextBlock>(view, "state").Text == Loc.AppUpdate_Cancelled, "Abbruch des Updatebuttons beendet den tatsächlichen HTTP-Auftrag");
                }
                else
                {
                    await WaitAsync(() => Field<TextBlock>(view, "state").Text.Contains("503"), "HTTP-Fehler wird im tatsächlichen Programmupdatedialog angezeigt");
                }
                Assert(Field<Button>(view, "install").IsEnabled && Field<Button>(view, "cancel").Visibility == Visibility.Collapsed, "Nach Fehler oder Abbruch kann der Benutzer den Download erneut versuchen");
                Assert(!installerCalled, "Fehlerhafter oder abgebrochener Download ruft keinen Installer auf");
                await CaptureAsync(dialog, $"application-update-{(blocked ? "cancelled" : "error")}-{language}");
            });
        }
    }

    /// <summary>Bestätigt über den echten Button den geprüften Paketdownload und kontrolliert die Installerübergabe.</summary>
    private static async Task CheckVerifiedDownloadAsync(MainWindow main, int language)
    {
        int index = Array.IndexOf(arguments, "--update-package");
        Assert(index >= 0 && index + 1 < arguments.Length, "GUI-Prüfung erhält das tatsächlich gebaute Produktpaket");
        string packagePath = arguments[index + 1];
        byte[] archive = await File.ReadAllBytesAsync(packagePath);
        using var handler = new ValidPackageHandler(archive, Path.GetFileName(packagePath));
        using var client = new HttpClient(handler);
        var release = new ApplicationRelease(ApplicationUpdater.CurrentVersion,
            new Uri("https://example.invalid/release"), new Uri("https://example.invalid/" + Path.GetFileName(packagePath)),
            new Uri("https://example.invalid/SHA256SUMS.txt"), archive.LongLength);
        PreparedApplicationUpdate? received = null;
        var continueInstall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new ApplicationUpdateView(new ApplicationUpdater(client), release, prepared =>
        {
            received = prepared;
            return continueInstall.Task;
        });
        try
        {
            await WithDialogAsync(main, Loc.AppUpdate_Title, view, async dialog =>
            {
                Invoke(Field<Button>(view, "install"));
                await WaitAsync(() => received is not null, "Bestätigter Download erreicht mit geprüftem Paket die Installerübergabe");
                Assert(received!.Version == release.Version && File.Exists(Path.Combine(received.DirectoryPath, "payload.exe")), "Installerübergabe erhält die geprüfte Produkt-EXE und ihre Version");
                Assert(!view.RequestClose() && !Field<Button>(view, "cancel").IsEnabled && !Field<Button>(view, "install").IsEnabled,
                    "Während der Installerübergabe bleiben Schließen, Abbruch und erneuter Download gesperrt");
                await CaptureAsync(dialog, $"application-update-installing-{language}");
                continueInstall.TrySetResult();
                await WaitAsync(() => Field<Button>(view, "install").IsEnabled, "Bestätigte Übergabe beendet den Downloadzustand");
            });
        }
        finally
        {
            continueInstall.TrySetResult();
            if (received is not null) ApplicationUpdater.DeleteStage(received.DirectoryPath, Environment.ProcessPath!);
        }
    }

    /// <summary>Liefert unveränderte gebaute Paketbytes und ihre passende Prüfsumme über den HTTP-Testtransport.</summary>
    private sealed class ValidPackageHandler(byte[] archive, string packageName) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpContent content = request.RequestUri!.AbsolutePath.EndsWith("SHA256SUMS.txt", StringComparison.Ordinal)
                ? new StringContent(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archive)) + "  " + packageName)
                : new ByteArrayContent(archive);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }

    /// <summary>Simuliert einen konkreten HTTP-Fehler oder wartet gezielt auf den Abbruch des UI-Auftrags.</summary>
    private sealed class UpdateStateHandler(bool blocked) : HttpMessageHandler
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            if (blocked) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable) { Content = new StringContent("Isolierte HTTP-Fehlerfixture.") };
        }
    }

    /// <summary>Sendet Schließnachrichten nur an ein eigenes Fenster; es wird keine Windows-Abmeldung ausgelöst.</summary>
    private static async Task CheckSessionClosingAsync()
    {
        int[] originalSize = Config.ReadMainFormSize();
        Config.SaveMainFormSize(50000, 50000);
        var window = new MainWindow(false);
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        try
        {
            window.Activate(); await RenderAsync();
            var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(window.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
            Assert(window.AppWindow.Size.Width <= area.Width && window.AppWindow.Size.Height <= area.Height, "Gespeicherte übergroße Fenstermaße werden auf den aktuellen Arbeitsbereich begrenzt");
            window.SetSessionEnding(true);
            Assert(window.Coordinator.Suspended, "Sitzungsbeendigung hält neue Hintergrundprüfungen an");
            window.SetSessionEnding(false);
            Assert(!window.Coordinator.Suspended, "Abgebrochene Sitzungsbeendigung gibt die Hintergrundprüfung wieder frei");
            Assert(PostMessage(window.Handle, 0x10, IntPtr.Zero, IntPtr.Zero), "Schließnachricht wird an das eigene Testfenster zugestellt");
            await RenderAsync();
            Assert(!closed && !IsWindowVisible(window.Handle), "Nach abgebrochener Sitzungsbeendigung blendet Schließen das eigene Fenster nur aus");
            window.ShowWindow();
            window.SetSessionEnding(!arguments.Contains("--negative-session-end"));
            Assert(PostMessage(window.Handle, 0x10, IntPtr.Zero, IntPtr.Zero), "Bestätigtes Sitzungsschließen wird an das eigene Testfenster zugestellt");
            for (int attempt = 0; attempt < 20 && !closed; attempt++) await RenderAsync();
            Assert(closed, "Bestätigte Sitzungsbeendigung schließt das Fenster statt es nur auszublenden");
        }
        finally
        {
            if (!closed) await window.ExitAsync();
            Config.SaveMainFormSize(originalSize[0], originalSize[1]);
        }
    }

    /// <summary>Ändert sämtliche sichtbaren Optionen und lädt sie aus der geschriebenen INI erneut.</summary>
    private static void CheckSettingsRoundtrip(IntPtr owner)
    {
        string clientDirectory = Path.Combine(output, "settings-clients-" + Config.Language);
        Directory.CreateDirectory(clientDirectory);
        string svnClient = Path.Combine(clientDirectory, "svn ä.exe");
        string tortoiseClient = Path.Combine(clientDirectory, "Tortoise ß.exe");
        File.Copy(Config.SVNpath, svnClient);
        File.Copy(Config.TortoiseSVNpath, tortoiseClient);
        var expected = new Dictionary<string, object>
        {
            [nameof(Config.SVNpath)] = svnClient,
            [nameof(Config.TortoiseSVNpath)] = tortoiseClient,
            [nameof(Config.ItemDoubleClickAction)] = Config.Action.commitAction,
            [nameof(Config.HideOnStartup)] = !Config.HideOnStartup,
            [nameof(Config.ShowInTaskbar)] = !Config.ShowInTaskbar,
            [nameof(Config.CheckForNewVersion)] = !Config.CheckForNewVersion,
            [nameof(Config.ShowNotifications)] = !Config.ShowNotifications,
            [nameof(Config.Language)] = 1 - Config.Language,
            [nameof(Config.FontFamily)] = "Arial",
            [nameof(Config.FontSize)] = 17.5f,
            [nameof(Config.DefaultActiveStatusUpdateInterval)] = 41,
            [nameof(Config.DefaultIdleStatusUpdateInterval)] = 91,
            [nameof(Config.SvnCommandTimeout)] = 0,
            [nameof(Config.DoPauseAfterApplicationStartup)] = !Config.DoPauseAfterApplicationStartup,
            [nameof(Config.PauseAfterApplicationStartupInterval)] = 37,
            [nameof(Config.DoPauseAfterWindowsResume)] = !Config.DoPauseAfterWindowsResume,
            [nameof(Config.PauseAfterWindowsResumeInterval)] = 53,
            [nameof(Config.ChangeLogBeforeUpdate)] = Config.IsTortoiseVersion_1_5_orHigher() && !Config.ChangeLogBeforeUpdate,
            [nameof(Config.UpdateAllSilently)] = !Config.UpdateAllSilently,
            [nameof(Config.UpdateWindowAction)] = (Config.UpdateWindowAction + 1) % 5
        };
        var original = expected.Keys.ToDictionary(name => name, name => typeof(Config).GetField(name)!.GetValue(null));
        int duration = Config.ShowBalloonInterval;
        string configuration = Config.iniFileName;
        var draft = new SettingsView(owner);
        try
        {
            foreach (var pair in expected)
            {
                if (pair.Value is bool enabled) Find<CheckBox>(draft, pair.Key).IsChecked = enabled;
                else if (pair.Value is string text) Find<TextBox>(draft, pair.Key).Text = text;
                else if (pair.Key is nameof(Config.Language) or nameof(Config.ItemDoubleClickAction) or nameof(Config.UpdateWindowAction))
                    Find<ComboBox>(draft, pair.Key).SelectedIndex = Convert.ToInt32(pair.Value);
                else Find<NumberBox>(draft, pair.Key).Value = Convert.ToDouble(pair.Value);
            }
            draft.Apply();
            if (arguments.Contains("--negative-notification-option"))
                new IniFile(configuration, false).Write("Settings", nameof(Config.ShowNotifications), (bool)original[nameof(Config.ShowNotifications)]!);
            if (arguments.Contains("--negative-roundtrip"))
                new IniFile(configuration, false).Write("Settings", "DefaultIdleStatusUpdateInterval", 7);
            Config.Init(configuration);
            foreach (var pair in expected)
                Assert(Equals(typeof(Config).GetField(pair.Key)!.GetValue(null), pair.Value), "Einstellung bleibt nach erneutem INI-Laden erhalten: " + pair.Key);
            Assert(Config.ShowBalloonInterval == duration, "Erneutes INI-Laden erhält den nicht mehr angebotenen Sekundenwert");
            var reopened = new SettingsView(owner);
            foreach (var pair in expected)
            {
                object actual;
                if (pair.Value is bool) actual = Find<CheckBox>(reopened, pair.Key).IsChecked == true;
                else if (pair.Value is string) actual = Find<TextBox>(reopened, pair.Key).Text;
                else if (pair.Key is nameof(Config.Language) or nameof(Config.ItemDoubleClickAction) or nameof(Config.UpdateWindowAction))
                    actual = Convert.ChangeType(Find<ComboBox>(reopened, pair.Key).SelectedIndex, pair.Value is Enum ? typeof(int) : pair.Value.GetType());
                else actual = Convert.ChangeType(Find<NumberBox>(reopened, pair.Key).Value, pair.Value.GetType());
                Assert(Equals(actual, pair.Value is Enum ? Convert.ToInt32(pair.Value) : pair.Value), "Erneut geöffnete Einstellungen zeigen den gespeicherten Wert: " + pair.Key);
            }
        }
        finally
        {
            foreach (var pair in original) typeof(Config).GetField(pair.Key)!.SetValue(null, pair.Value);
            Config.ShowBalloonInterval = duration;
            Config.SaveSettings();
        }
    }

    /// <summary>Prüft die Statuspriorität am tatsächlichen Fenster-HICON und am gemeinsam verwendeten Tray-Symbol.</summary>
    private static async Task CheckWindowIconsAsync(MainWindow main)
    {
        var icons = Field<StatusIcons>(main, "statusIcons");
        var resources = Field<Dictionary<string, IntPtr>>(icons, "icons");
        var cases = new (string Name, SvnStatus[] Statuses, bool Enabled, string Resource)[]
        {
            ("empty", [], true, "Unknown"),
            ("green", [SvnStatus.UpToDate], true, "UpToDate"),
            ("modified", [SvnStatus.UpToDate_Modified], true, "UpToDate"),
            ("yellow", [SvnStatus.NeedUpdate], true, "NeedUpdate"),
            ("yellow-modified", [SvnStatus.NeedUpdate_Modified], true, "NeedUpdate"),
            ("gray", [SvnStatus.Unknown], true, "Unknown"),
            ("red", [SvnStatus.Error], true, "Error"),
            ("unknown-priority", [SvnStatus.UpToDate, SvnStatus.Unknown], true, "Unknown"),
            ("incoming-priority", [SvnStatus.UpToDate, SvnStatus.Unknown, SvnStatus.NeedUpdate], true, "NeedUpdate"),
            ("error-priority", [SvnStatus.UpToDate, SvnStatus.NeedUpdate_Modified, SvnStatus.Unknown, SvnStatus.Error], true, "Error"),
            ("disabled-error", [SvnStatus.Error], false, "Error"),
            ("green-again", [SvnStatus.UpToDate], true, "UpToDate"),
            ("empty-again", [], true, "Unknown")
        };
        foreach (var sample in cases)
        {
            main.Coordinator.Folders.Clear();
            foreach (SvnStatus status in sample.Statuses)
                main.Coordinator.Folders.Add(new SvnItem(Path.Combine(output, "Icon " + status), PathType.Directory) { Status = status, Enabled = sample.Enabled });
            main.Coordinator.Save(); await RenderAsync();
            if (arguments.Contains("--negative-window-icon"))
                main.AppWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(resources["Unknown"]));
            IntPtr actual = WindowIconProbe.SendMessage(main.Handle, 0x7f, new IntPtr(1), IntPtr.Zero);
            byte[] pixels = WindowIconProbe.Pixels(actual);
            Assert(pixels.SequenceEqual(WindowIconProbe.Pixels(resources[sample.Resource])), "Natives Fenstersymbol folgt dem Gesamtstatus: " + sample.Name);
            Assert(icons.Current == resources[sample.Resource], "Tray und Taskleiste verwenden dasselbe Statussymbol: " + sample.Name);
            if (arguments.Contains("--capture") || arguments.Contains("--diagnose-status-icons"))
            {
                using var stream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 32, 32, 96, 96, pixels);
                await encoder.FlushAsync(); stream.Seek(0);
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)stream.Size);
                var png = new byte[(int)stream.Size]; reader.ReadBytes(png);
                File.WriteAllBytes(Path.Combine(output, "window-icon-" + sample.Name + ".png"), png);
            }
        }
        main.Coordinator.Folders.Clear(); main.Coordinator.Save();
    }

    /// <summary>Übt den tatsächlich gebundenen Cleanupbefehl an einer isolierten Arbeitskopie aus.</summary>
    private static async Task CheckCleanupAsync(MainWindow main, ListView list, BindingList<SvnItem> folders, int language)
    {
        list.SelectedItem = null;
        var menu = main.CreateRepositoryMenu();
        if (arguments.Contains("--negative-cleanup-command")) menu.Items.Remove(menu.Items.Single(item => AutomationProperties.GetAutomationId(item) == "Cleanup"));
        AssertCommands(menu.Items, new[] { "CheckNow", "ChangeLog", "Update", "Commit", "Cleanup", "Open", "Log", "Properties" }, "ItemMenu");
        var cleanup = menu.Items.OfType<MenuFlyoutItem>().Single(item => AutomationProperties.GetAutomationId(item) == "Cleanup");
        Assert(cleanup.Text == "Cleanup" && !cleanup.IsEnabled, "Cleanup benötigt einen ausgewählten Eintrag und behält seine Beschriftung");
        foreach (var folder in folders)
        {
            list.SelectedItem = folder; await RenderAsync();
            Assert(cleanup.IsEnabled, "Cleanup ist auch bei Fehlern, unbekanntem Status und pausierter Prüfung verfügbar");
        }
        string target = Path.Combine(output, $"Cleanup Arbeitskopie ä-ß {language}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(target, ".svn"));
        File.WriteAllText(Path.Combine(target, ".svn", "wc.db"), "Isolierte Testmarkierung.");
        string previousPath = folders[4].Path, previousTool = Config.TortoiseSVNpath;
        try
        {
            folders[4].Path = target; list.SelectedItem = folders[4]; Config.TortoiseSVNpath = Environment.ProcessPath!;
            if (arguments.Contains("--negative-cleanup-click"))
            {
                var broken = new MenuFlyoutItem { Text = cleanup.Text }; menu.Items.Remove(cleanup); menu.Items.Add(broken); cleanup = broken;
            }
            menu.ShowAt(list); await RenderAsync();
            if (arguments.Contains("--capture") || arguments.Contains("--capture-cleanup"))
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(main.RootElement.XamlRoot)) if (popup.Child is FrameworkElement visual) await CaptureAsync(visual, $"repository-menu-{language}", true);
            ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(cleanup)).Invoke();
            await WaitAsync(() => File.Exists(Path.Combine(target, "cleanup-started")), "Cleanup-Menüklick startet den Dialog");
            Assert(File.Exists(Path.Combine(target, ".svn", WorkingCopyLease.FileName)), "Der geöffnete Cleanupdialog reserviert die Arbeitskopie");
        }
        finally
        {
            menu.Hide(); File.WriteAllText(Path.Combine(target, "cleanup-release"), "Dialog schließen.");
            await WaitAsync(() => !File.Exists(Path.Combine(target, ".svn", WorkingCopyLease.FileName)));
            folders[4].Path = previousPath; Config.TortoiseSVNpath = previousTool;
        }
        Assert(!File.Exists(Path.Combine(target, ".svn", WorkingCopyLease.FileName)), "Dialogende gibt die Arbeitskopie frei");
    }

    private static async Task WithDialogAsync(MainWindow main, string title, UIElement content, Func<ContentDialog, Task> inspect)
    {
        var service = new WindowService(main);
        bool editable = content is SettingsView or RepositoryPropertiesView;
        var showing = service.ShowAsync(title, content, editable ? Loc.PF_Btn_OK : "", editable ? Loc.PF_Btn_Cancel : Loc.ELF_Btn_Close);
        ContentDialog? dialog = null;
        await WaitAsync(() =>
        {
            dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(main.RootElement.XamlRoot)
                .Where(popup => popup.Child is not null).SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>().FirstOrDefault();
            return dialog?.IsLoaded == true;
        });
        try { await RenderAsync(); await inspect(dialog!); CheckButtons(dialog!); }
        finally { dialog!.Hide(); await showing; }
    }

    /// <summary>Prüft Zeitstempel, Pfade und Ursachen mehrerer Fehler statt nur einer einzeiligen Testmeldung.</summary>
    private static async Task CheckErrorLogAsync(MainWindow main)
    {
        var errorBuffer = new Dictionary<string, string>
        {
            ["first"] = "[13.11.2026 14:20:57]\r\nD:\\Testprojekte\\Änderungen:\r\n" + Loc.Svn_TimedOut(arguments.Contains("--one-second-timeout") ? 1 : 60) + "\r\n\r\n",
            ["second"] = "[13.11.2026 14:21:58]\r\nD:\\Testprojekte\\Projekt 2:\r\n" + Loc.Svn_ProcessError(1) + "\r\n\r\n",
            ["third"] = "[13.11.2026 14:22:59]\r\nD:\\Testprojekte\\Projekt 3:\r\n" + Loc.Svn_Cancelled + "\r\n\r\n"
        };
        string expected = string.Concat(errorBuffer.Values);
        var errors = new ErrorLogView(errorBuffer);
        await WithDialogAsync(main, Loc.ELF_Title, errors, async dialog =>
        {
            var text = Find<TextBox>(errors, "ErrorLogText");
            if (arguments.Contains("--negative-error-log")) text.Text = expected.Split('\r')[0];
            static string Normalize(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
            Assert(Normalize(text.Text) == Normalize(expected), "Mehrzeiliges Fehlerprotokoll zeigt sämtliche Zeitstempel, Pfade und Ursachen");
            Assert(text.AcceptsReturn && text.IsReadOnly, "Fehlerprotokoll bleibt mehrzeilig, auswählbar und schreibgeschützt");
            await CaptureAsync(dialog, $"errors-{Config.Language}");
            Invoke(Find<Button>(errors, "ErrorLogClear"));
            await RenderAsync();
            Assert(text.Text.Length == 0, "Fehlerprotokoll kann geleert werden");
            Assert(errorBuffer.Count == 0, "Leeren entfernt auch den gemeinsamen Fehlerpuffer");
        });
    }

    private static void CheckButtons(DependencyObject root)
    {
        var buttons = Descendants(root).OfType<Button>().Where(button => button.IsLoaded && button.Visibility == Visibility.Visible).ToArray();
        if (arguments.Contains("--negative-button-size") && buttons.Length > 0) { buttons[0].Height = 220; buttons[0].UpdateLayout(); }
        foreach (var button in buttons) Assert(button.ActualHeight <= button.FontSize * 1.6 + 36, "Schaltflächenhöhe folgt dem Schriftbedarf");
        foreach (var button in buttons.Where(button => button.Name is "PrimaryButton" or "CloseButton"))
            Assert(button.ActualWidth <= Math.Max(220, Config.FontSize * 96d / 72d * 10) + 2, "Dialogaktionen bleiben inhaltsbezogen begrenzt");
    }

    private static void AssertCommands(IEnumerable<DependencyObject> commands, string[] expected, string name)
    {
        var found = commands.Select(AutomationProperties.GetAutomationId).ToArray();
        Assert(expected.All(found.Contains), "Befehle erhalten: " + name);
        inventories[name] = found;
    }

    private static void Toggle(CheckBox checkbox) => ((IToggleProvider)new CheckBoxAutomationPeer(checkbox)).Toggle();
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
    private static T Field<T>(object owner, string name) => (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(owner) ?? throw new Exception("Feld fehlt: " + name));
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Descendants(root).OfType<T>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id) ?? throw new Exception("Bedienelement fehlt: " + id);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var visited = new HashSet<DependencyObject>();
        var queue = new Queue<DependencyObject>(); queue.Enqueue(root);
        while (queue.TryDequeue(out var current))
        {
            if (!visited.Add(current)) continue;
            yield return current;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++) queue.Enqueue(VisualTreeHelper.GetChild(current, index));
            if (current is Panel panel) foreach (var child in panel.Children) queue.Enqueue(child);
            if (current is ContentControl { Content: DependencyObject content }) queue.Enqueue(content);
            if (current is UserControl { Content: DependencyObject userContent }) queue.Enqueue(userContent);
            if (current is TabView tabs) foreach (DependencyObject item in tabs.TabItems) queue.Enqueue(item);
            if (current is CommandBar bar)
            {
                foreach (var command in bar.PrimaryCommands.Concat(bar.SecondaryCommands)) queue.Enqueue((DependencyObject)command);
            }
            if (current is ScrollViewer { Content: DependencyObject scrollContent }) queue.Enqueue(scrollContent);
        }
    }

    private static void Assert(bool condition, string text)
    {
        if (!condition) throw new Exception(text);
        checks++; Console.WriteLine("OK: " + text);
    }

    private static async Task WaitAsync(Func<bool> condition, string message = "Fensterzustand wird erreicht")
    {
        var timeout = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < timeout) await Task.Delay(25);
        Assert(condition(), message);
    }

    private static async Task RenderAsync() => await Task.Delay(150);

    /// <summary>Erfasst ausschließlich die gerenderte XAML-Ansicht; der Benutzerdesktop wird weder kopiert noch umgeschaltet.</summary>
    private static async Task CaptureAsync(FrameworkElement visual, string name, bool force = false)
    {
        if (!force && !arguments.Contains("--capture")) return;
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(visual);
        Assert(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, "Gerenderte Ansicht besitzt Bildinhalt: " + name);
        var pixels = await bitmap.GetPixelsAsync();
        byte[] bytes = new byte[pixels.Length];
        using (var reader = DataReader.FromBuffer(pixels)) reader.ReadBytes(bytes);
        var folder = await StorageFolder.GetFolderFromPathAsync(output);
        var file = await folder.CreateFileAsync(name + ".png", CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
}
