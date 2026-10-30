using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text.Json;
using CHD.SVN_Notifier;

internal static class Program
{
    private static int checks;
    private static readonly Dictionary<string, string[]> inventories = new();
    private static bool oversizedButtonCounterexample;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    /// <summary>Prüft echte WinForms-Dialoge mit isolierter Konfiguration und optionalen sichtbaren Aufnahmen.</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        oversizedButtonCounterexample = args.Contains("--negative-button-size");
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "svnnotifier-gui-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(output);
        var fixture = Path.Combine(output, "fixture.ini");
        File.WriteAllText(fixture, "[Settings]\nVersion=1.9\nCheckForNewVersion=false\nShowInTaskbar=true\n[Folders]\n");
        Config.Init(fixture);
        Config.SVNpath = Config.TortoiseSVNpath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        Config.CheckForNewVersion = false;
        Application.SetDefaultFont(new Font("Segoe UI", args.Contains("--maximum-font") ? 24 : args.Contains("--large-font") ? 12 : 8.5f));
        try
        {
            foreach (int language in new[] { 1, 0 })
            {
                Config.Language = language;
                Config.CheckForNewVersion = false;
                Config.SaveSvnFolders(new BindingList<SvnItem>());
                using var main = new MainForm();
                Field(main, "applicationInstallationInProgress").SetValue(main, true);
                var folders = (BindingList<SvnItem>)Field(main, "folders").GetValue(main)!;
                var statuses = new[] { SvnStatus.UpToDate, SvnStatus.NeedUpdate, SvnStatus.UpToDate_Modified, SvnStatus.NeedUpdate_Modified, SvnStatus.Error, SvnStatus.Unknown };
                for (int index = 0; index < statuses.Length; index++)
                    folders.Add(new SvnItem($@"C:\Projekte\{new[] { "Kundenportal", "Office", "Abrechnung", "Schnittstellen", "Archiv", "Testumgebung" }[index]}", PathType.Directory)
                    { Status = statuses[index], AutoUpdate = index % 2 == 0 });
                folders.Add(new SvnItem(@"C:\Projekte\Pausiertes Projekt", PathType.Directory) { Enabled = false, AutoUpdate = false });
                foreach (var folder in folders) folder.VisiblePath = folder.Path;
                Show(main);
                var grid = Control<DataGridView>(main, "ItemListView");
                if (args.Contains("--negative-missing-command"))
                    ((ToolStripMenuItem)Field(main, "FileMenuItem").GetValue(main)!).DropDownItems.RemoveByKey("AddFolderMenuItem");
                Assert(grid.Columns.Count == 6 && grid.Rows.Count == 7, "Alle Repository-Spalten und Statuszeilen sind vorhanden");
                Assert(grid.AllowDrop && grid.MultiSelect == false && main.KeyPreview, "Drag-and-drop und Tastaturbedienung bleiben aktiv");
                AssertMenu(main, "FileMenuItem", new[] { "AddFolderMenuItem", "AddFileMenuItem", "RemoveMenuItem", "ImportConfigurationMenuItem", "ExportConfigurationMenuItem", "ExitMenuItem" });
                AssertMenu(main, "ToolsMenuItem", new[] { "UpdateAllMenuItem", "SettingsMenuItem" });
                AssertMenu(main, "HelpMenuItem", new[] { "CheckApplicationUpdatesMenuItem", "AboutMenuItem" });
                AssertMenu(main, "ItemMenu", new[] { "CheckNowMenuItem", "ChangeLogMenuItem", "UpdateMenuItem", "CommitMenuItem", "OpenMenuItem", "LogMenuItem", "PropertiesMenuItem" });
                AssertMenu(main, "TrayMenu", new[] { "TrayShowProgramMenuItem", "TrayUpdateAllMenuItem", "TrayExitMenuItem" });
                var toolbar = Control<ToolStrip>(main, "MenuToolStrip");
                Assert(new[] { "AddFolderButton", "AddFileButton", "RemoteButton", "ChangeLogButton", "UpdateButton", "CommitButton", "OpenButton", "LogButton", "PropertiesButton" }
                    .All(name => toolbar.Items.Cast<ToolStripItem>().Any(item => item.Name == name)), "Alle Symbolleistenaktionen bleiben erreichbar");
                grid.CurrentCell = grid.Rows[1].Cells["PathColumn"];
                Assert(((ToolStripButton)Field(main, "UpdateButton").GetValue(main)!).Enabled, "Eingehende Änderungen können aktualisiert werden");
                grid.CurrentCell = grid.Rows[0].Cells["PathColumn"];
                Assert(!((ToolStripButton)Field(main, "UpdateButton").GetValue(main)!).Enabled, "Synchronisierte Repositories brauchen kein Update");
                bool previousAutoUpdate = folders[0].AutoUpdate;
                Assert(grid.Columns["ColumnAutoUpdate"] is DataGridViewCheckBoxColumn
                    && (bool)grid.Rows[0].Cells["ColumnAutoUpdate"].Value == folders[0].AutoUpdate
                    && (bool)grid.Rows[1].Cells["ColumnAutoUpdate"].Value == folders[1].AutoUpdate,
                    "Auto-Update hat sichtbare aktivierte und deaktivierte Checkboxen");
                ClickCell(grid, "ColumnAutoUpdate");
                Assert(folders[0].AutoUpdate != previousAutoUpdate, "Repository-Auto-Update lässt sich umschalten");
                Assert(Config.ReadSvnFolders().Single(folder => folder.Path == folders[0].Path).AutoUpdate == folders[0].AutoUpdate, "Repository-Auto-Update wird gespeichert");
                ClickCell(grid, "ColumnAutoUpdate");
                grid.CurrentCell = grid.Rows[0].Cells["ColumnAutoUpdate"];
                typeof(DataGridView).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(grid, new object[] { new KeyEventArgs(Keys.Space) });
                Assert(folders[0].AutoUpdate != previousAutoUpdate, "Auto-Update lässt sich per Leertaste umschalten");
                typeof(DataGridView).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(grid, new object[] { new KeyEventArgs(Keys.Space) });
                ClickCell(grid, "ColumnEnabled");
                Assert(!folders[0].Enabled, "Repository-Prüfung lässt sich ausschalten");
                ClickCell(grid, "ColumnEnabled");
                Assert(folders[0].Enabled, "Repository-Prüfung lässt sich einschalten");
                var tray = (NotifyIcon)Field(main, "TrayNotifyIcon").GetValue(main)!;
                bool trayVisible = tray.Visible;
                tray.Visible = false;
                typeof(MainForm).GetMethod("UpdateListView", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(main, new object[] { folders[5], SvnStatus.NeedUpdate, DateTime.Now });
                Assert(folders[5].Status == SvnStatus.NeedUpdate
                    && (string?)Field(main, "firstBalloonPath").GetValue(main) == folders[5].VisiblePath,
                    "Eingehende Änderungen erzeugen bei ausgeschaltetem Auto-Update weiterhin Benachrichtigungen");
                tray.Visible = trayVisible;
                folders[5].Status = SvnStatus.Unknown;
                ((BindingList<SvnItem>)Field(main, "newNonUpdatedFolders").GetValue(main)!).Clear();
                grid.CurrentCell = grid.Rows[1].Cells["PathColumn"];
                main.Text = $"SVN Notifier {typeof(MainForm).Assembly.GetName().Version}";
                Capture(main, output, $"main-{language}", args);
                main.Size = main.MinimumSize;
                Pump();
                Assert(toolbar.Items.OfType<ToolStripButton>().All(item => item.Placement != ToolStripItemPlacement.None), "Symbolleistenaktionen bleiben auch bei kleiner Fensterbreite erreichbar");
                Capture(main, output, $"main-compact-{language}", args);
                if (args.Contains("--capture"))
                {
                    toolbar.OverflowButton.ShowDropDown();
                    Pump();
                    Capture(toolbar.OverflowButton.DropDown, output, $"toolbar-overflow-{language}", args);
                    toolbar.OverflowButton.HideDropDown();
                    var fileMenu = (ToolStripMenuItem)Field(main, "FileMenuItem").GetValue(main)!;
                    fileMenu.ShowDropDown();
                    Pump();
                    Capture(fileMenu.DropDown, output, $"file-menu-{language}", args);
                    fileMenu.HideDropDown();
                    var itemMenu = (ContextMenuStrip)Field(main, "ItemMenu").GetValue(main)!;
                    itemMenu.Show(main, new Point(180, 100));
                    Pump();
                    Capture(itemMenu, output, $"repository-menu-{language}", args);
                    itemMenu.Close();
                    var trayMenu = (ContextMenuStrip)Field(main, "TrayMenu").GetValue(main)!;
                    trayMenu.Show(main, new Point(180, 100));
                    Pump();
                    Capture(trayMenu, output, $"tray-menu-{language}", args);
                    trayMenu.Close();
                }
                main.Hide();

                using var settings = new SettingsForm();
                Show(settings);
                var tabs = Control<TabControl>(settings, "MainTabControl");
                Assert(tabs.TabPages.Count == 3, "Alle Einstellungsbereiche bleiben vorhanden");
                for (int tab = 0; tab < tabs.TabPages.Count; tab++)
                {
                    tabs.SelectedIndex = tab;
                    Pump();
                    Capture(settings, output, $"settings-{tab}-{language}", args);
                }
                tabs.SelectedIndex = 0;
                var automaticCheck = Control<CheckBox>(settings, "checkApplicationVersionCheckBox");
                automaticCheck.Focus();
                Pump();
                var checkBounds = automaticCheck.RectangleToScreen(automaticCheck.ClientRectangle);
                var pageBounds = tabs.SelectedTab!.RectangleToScreen(tabs.SelectedTab.ClientRectangle);
                Assert(checkBounds.Top >= pageBounds.Top && checkBounds.Bottom <= pageBounds.Bottom, "Auch das letzte Einstellungsfeld ist per Tastatur erreichbar");
                automaticCheck.Checked = true;
                Control<Button>(settings, "CloseButton").PerformClick();
                Assert(!Config.CheckForNewVersion, "Abbrechen verwirft Einstellungsänderungen");
                using (var saveSettings = new SettingsForm())
                {
                    Show(saveSettings);
                    Control<TextBox>(saveSettings, "SvnPathTextBox").Text = Path.Combine(output, "missing-svn.exe");
                    Assert(!Control<Button>(saveSettings, "OkButton").Enabled, "Ungültige Programmpfade werden abgewiesen");
                    Control<TextBox>(saveSettings, "SvnPathTextBox").Text = Config.SVNpath;
                    Assert(Control<Button>(saveSettings, "OkButton").Enabled, "Gültige Programmpfade erlauben Speichern");
                    Control<CheckBox>(saveSettings, "checkApplicationVersionCheckBox").Checked = !args.Contains("--negative-save-option");
                    Control<NumericUpDown>(saveSettings, "HideSystemTrayUpDown").Value = 17;
                    Control<NumericUpDown>(saveSettings, "ActiveSecondsUpDown").Value = 23;
                    Control<NumericUpDown>(saveSettings, "TimeoutUpDown").Value = 47;
                    Control<ComboBox>(saveSettings, "DoubleClickComboBox").SelectedIndex = 4;
                    Control<Button>(saveSettings, "OkButton").PerformClick();
                    Assert(Config.CheckForNewVersion && Config.ShowBalloonInterval == 17000, "Programmprüfung und Benachrichtigungsdauer werden gespeichert");
                    Assert(Config.DefaultActiveStatusUpdateInterval % 60 == 23 && Config.SvnCommandTimeout == 47 && (int)Config.ItemDoubleClickAction == 4, "Prüfintervalle, Timeout und Doppelklickaktion werden gespeichert");
                    Config.CheckForNewVersion = false;
                }
                using var properties = new PropertiesForm(folders[0]);
                Show(properties);
                Capture(properties, output, $"properties-{language}", args);
                Control<CheckBox>(properties, "StatusUpdateCheckBox").Checked = true;
                Assert(Control<Panel>(properties, "StatusUpdatePanel").Enabled, "Eigene Prüfintervalle aktivieren ihre Zeitfelder");
                Control<NumericUpDown>(properties, "ActiveSecondsUpDown").Value = 31;
                Control<Button>(properties, "OkButton").PerformClick();
                Assert(folders[0].ActiveStatusUpdateInterval % 60 == 31, "Repository-Eigenschaften werden übernommen");
                using var errors = new ErrorLogForm(new() { ["fixture"] = "30.10.2026 14:20\r\nTestmeldung: Repository ist nicht erreichbar.\r\n" });
                Show(errors);
                Capture(errors, output, $"errors-{language}", args);
                Field(errors, "ButtonsTimer").GetValue(errors)!.GetType().GetProperty("Enabled")!.SetValue(Field(errors, "ButtonsTimer").GetValue(errors), false);
                Control<Button>(errors, "ClearButton").Enabled = true;
                Control<Button>(errors, "ClearButton").PerformClick();
                Assert(Control<TextBox>(errors, "LogTextBox").Text.Length == 0, "Fehlerprotokoll kann geleert werden");
                errors.Hide();
                using var about = new AboutForm();
                Show(about);
                Control<Label>(about, "TitleLabel").Text = $"SVN Notifier {typeof(MainForm).Assembly.GetName().Version}";
                Pump();
                Capture(about, output, $"about-{language}", args);
                Assert(new[] { "CompanyLinkLabel", "V3LinkLabel", "GitHubLinkLinkLabel", "ProjectLinkLabel" }.All(name => Control<LinkLabel>(about, name).Visible), "Alle Herkunfts- und Projektlinks bleiben vorhanden");
                about.Hide();
                using var process = new Process();
                var updateProcess = new SvnFolderProcess(folders[0], process, true);
                updateProcess.processOutput.Add("U    C:\\Projekte\\Kundenportal\\README.md");
                updateProcess.processOutput.Add("A    C:\\Projekte\\Kundenportal\\Dokumente\\Anleitung.pdf");
                updateProcess.processOutput.Add("Updated to revision 123.");
                using var log = new UpdateLogForm(updateProcess);
                Show(log);
                Capture(log, output, $"update-log-{language}", args);
                Assert(Control<ListView>(log, "LogListView").Items.Count == 3, "Updateprotokoll erhält Dateiaktionen und Ergebnis");
                log.Hide();
                using var applicationUpdate = CreateApplicationUpdate();
                Show(applicationUpdate);
                Capture(applicationUpdate, output, $"application-update-{language}", args);
                Assert(applicationUpdate.AcceptButton is Button && applicationUpdate.CancelButton is Button, "Programmupdate behält Bestätigung und Abbruch");
                applicationUpdate.Hide();
            }
            File.WriteAllText(Path.Combine(output, "controls.json"), JsonSerializer.Serialize(inventories, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"GUI-Prüfungen: {checks} erfolgreich.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    /// <summary>Übt die gebundenen Klickereignisse aus, ohne einen SVN-Prozess oder Benutzerdaten zu verändern.</summary>
    private static void ClickCell(DataGridView grid, string column)
    {
        var targetColumn = grid.Columns[column] ?? throw new Exception($"Spalte fehlt: {column}");
        typeof(DataGridView).GetMethod("OnCellClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(grid, new object[] { new DataGridViewCellEventArgs(targetColumn.Index, 0) });
        Pump();
    }

    /// <summary>Erzeugt nur die Updateansicht; diese Testansicht lädt und installiert kein Paket.</summary>
    private static Form CreateApplicationUpdate()
    {
        var assembly = typeof(MainForm).Assembly;
        var updaterType = assembly.GetType("CHD.SVN_Notifier.ApplicationUpdater")!;
        var releaseType = assembly.GetType("CHD.SVN_Notifier.ApplicationRelease")!;
        var preparedType = assembly.GetType("CHD.SVN_Notifier.PreparedApplicationUpdate")!;
        var formType = assembly.GetType("CHD.SVN_Notifier.ApplicationUpdateForm")!;
        var updater = Activator.CreateInstance(updaterType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { new HttpClient() }, null)!;
        var currentVersion = assembly.GetName().Version!;
        var nextVersion = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build, currentVersion.Revision + 1);
        var release = Activator.CreateInstance(releaseType, new object[] { nextVersion, new Uri($"https://github.com/MGGM-Software-GmbH/SVNNotifier/releases/tag/v{nextVersion}"), new Uri("https://example.com/package.zip"), new Uri("https://example.com/SHA256SUMS.txt"), 100L })!;
        var callbackType = typeof(Func<,>).MakeGenericType(preparedType, typeof(Task));
        var callback = Expression.Lambda(callbackType, Expression.Constant(Task.CompletedTask), Expression.Parameter(preparedType)).Compile();
        return (Form)Activator.CreateInstance(formType, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { updater, release, callback }, null)!;
    }

    private static void AssertMenu(Form form, string field, string[] names)
    {
        var value = field == "CheckApplicationUpdatesMenuItem" ? null : Field(form, field).GetValue(form);
        var items = value is ToolStripMenuItem menu ? menu.DropDownItems : ((ToolStrip)value!).Items;
        Assert(names.All(name => items.Cast<ToolStripItem>().Any(item => item.Name == name)), $"Befehle erhalten: {field}");
    }

    private static FieldInfo Field(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? throw new Exception($"Feld fehlt: {name}");
    private static TControl Control<TControl>(object instance, string name) where TControl : Control => (TControl)Field(instance, name).GetValue(instance)!;
    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }

    private static void Show(Form form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(30, 30);
        form.Show();
        form.TopMost = true;
        SetForegroundWindow(form.Handle);
        Pump();
        foreach (var button in Descendants(form).OfType<Button>())
        {
            if (!button.Visible) continue;
            if (oversizedButtonCounterexample)
            {
                form.Hide();
                button.MinimumSize = new Size(button.MinimumSize.Width, 160);
                button.Parent!.PerformLayout();
                oversizedButtonCounterexample = false;
            }
            Assert(button.Height <= button.Font.Height + 24 * form.DeviceDpi / 96, $"Buttonhöhe überschreitet den Schriftbedarf: {form.Name}/{button.Name}");
        }
        Assert(form.Height <= Math.Min(Screen.FromControl(form).WorkingArea.Height - 32, 860 * form.DeviceDpi / 96), $"Dialoghöhe überschreitet die Arbeitsfläche: {form.Name}");
        Console.WriteLine($"Fenster: {form.Name}, {form.Width}x{form.Height}, DPI={form.DeviceDpi}");
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Pump()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(350);
        do { Application.DoEvents(); Thread.Sleep(10); } while (DateTime.UtcNow < deadline);
    }

    private static void Capture(Control form, string output, string name, string[] args)
    {
        inventories[name] = Descendants(form).Select(control => control.Name).Where(controlName => controlName.Length > 0).Order().ToArray();
        if (!args.Contains("--capture")) return;
        using var image = new Bitmap(form.Width, form.Height);
        using var graphics = Graphics.FromImage(image);
        graphics.CopyFromScreen(form is Form ? form.Location : form.PointToScreen(Point.Empty), Point.Empty, image.Size);
        image.Save(Path.Combine(output, name + ".png"));
        Console.WriteLine($"Aufnahme: {name}, {form.Width}x{form.Height}");
    }
}
