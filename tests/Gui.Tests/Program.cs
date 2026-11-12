using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
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
        var main = new MainWindow(false);
        if (arguments.Contains("--dark")) main.RootElement.RequestedTheme = ElementTheme.Dark;
        main.Activate();
        await WaitAsync(() => main.RootElement.IsLoaded);
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
            list.SelectedItem = folders[1]; await RenderAsync();
            Assert(Find<AppBarButton>(toolbar, "Update").IsEnabled, "Eingehende Änderungen können aktualisiert werden");
            list.SelectedItem = folders[0]; await RenderAsync();
            Assert(!Find<AppBarButton>(toolbar, "Update").IsEnabled, "Synchronisierte Repositories brauchen kein Update");
            var firstContainer = (ListViewItem)list.ContainerFromItem(folders[0]);
            var auto = Find<CheckBox>(firstContainer, "AutoUpdateToggle");
            var enabled = Find<CheckBox>(firstContainer, "MonitorToggle");
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
            });
            Assert(!Config.CheckForNewVersion, "Abbrechen verwirft Einstellungsänderungen");
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
            var properties = new RepositoryPropertiesView(folders[0]);
            var custom = Field<CheckBox>(properties, "CustomInput");
            Toggle(custom);
            Assert(Field<NumberBox>(properties, "ActiveInput").IsEnabled, "Eigene Prüfintervalle aktivieren ihre Zeitfelder");
            Field<NumberBox>(properties, "ActiveInput").Value = 31; properties.Apply();
            Assert(folders[0].ActiveStatusUpdateInterval == 31, "Repository-Eigenschaften werden übernommen");
            await WithDialogAsync(main, Loc.PF_Title, properties, dialog => CaptureAsync(dialog, $"properties-{language}"));
            var errorBuffer = new Dictionary<string, string> { ["fixture"] = "Testmeldung: Repository ist nicht erreichbar.\r\n" };
            var errors = new ErrorLogView(errorBuffer);
            await WithDialogAsync(main, Loc.ELF_Title, errors, async dialog =>
            {
                await CaptureAsync(dialog, $"errors-{language}");
                Invoke(Find<Button>(errors, "ErrorLogClear"));
                await RenderAsync();
                Assert(Find<TextBox>(errors, "ErrorLogText").Text.Length == 0, "Fehlerprotokoll kann geleert werden");
                Assert(errorBuffer.Count == 0, "Leeren entfernt auch den gemeinsamen Fehlerpuffer");
            });
            var about = new AboutView();
            Assert(Descendants(about).OfType<HyperlinkButton>().Count() >= 4, "Alle Herkunfts- und Projektlinks bleiben vorhanden");
            await WithDialogAsync(main, Loc.AF_Title, about, dialog => CaptureAsync(dialog, $"about-{language}"));
            using var process = new Process();
            var update = new SvnFolderProcess(folders[0], process, true);
            update.processOutput.Add("U    C:\\Projekte\\Kundenportal\\README.md");
            update.processOutput.Add("A    C:\\Projekte\\Kundenportal\\Dokumente\\Anleitung.pdf");
            update.processOutput.Add("Updated to revision 123.");
            var log = new UpdateLogView(update);
            Assert(Descendants(log).OfType<TextBlock>().Any(text => text.Text.Contains("123")), "Updateprotokoll erhält Dateiaktionen und Ergebnis");
            await WithDialogAsync(main, Loc.ULF_Title, log, dialog => CaptureAsync(dialog, $"update-log-{language}"));
            using var client = ApplicationUpdater.CreateClient();
            var release = new ApplicationRelease(new Version(3, 0, 0, 14), new Uri("https://github.com/MGGM-Software-GmbH/SVNNotifier/releases"), new Uri("https://example.invalid/package.zip"), new Uri("https://example.invalid/checksums.txt"), 100);
            var applicationUpdate = new ApplicationUpdateView(new ApplicationUpdater(client), release, _ => throw new InvalidOperationException("Kein Installer im Ansichtstest."));
            await WithDialogAsync(main, Loc.AppUpdate_Title, applicationUpdate, async dialog =>
            {
                Assert(Descendants(applicationUpdate).OfType<Button>().Any(button => Equals(button.Content, Loc.AppUpdate_Install)) && dialog.CloseButtonText.Length > 0, "Programmupdate behält Bestätigung und Abbruch");
                await CaptureAsync(dialog, $"application-update-{language}"); CheckButtons(dialog);
            });
        }
        await main.ExitAsync();
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
