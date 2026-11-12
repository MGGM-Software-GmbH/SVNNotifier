using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;

namespace CHD.SVN_Notifier;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<SvnItem> items = new();
    private readonly Dictionary<string, List<Microsoft.UI.Xaml.DependencyObject>> commandControls = new();
    private readonly WindowService windows;
    private readonly ApplicationCoordinator coordinator;
    private readonly DispatcherTimer pollTimer = new() { Interval = TimeSpan.FromMilliseconds(333) };
    private readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromHours(12) };
    private readonly CancellationTokenSource cancellation = new();
    private readonly HttpClient updateClient = ApplicationUpdater.CreateClient();
    private readonly bool shellIntegration;
    private TrayService? tray;
    private NotificationService? notifications;
    private bool exiting, checkingUpdate, sessionEnding, initializing = true;
    private Version? announcedVersion;
    internal FrameworkElement RootElement => Root;
    internal ApplicationCoordinator Coordinator => coordinator;
    internal IntPtr Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);
    private SvnItem? Selected => Repositories.SelectedItem as SvnItem;

    public MainWindow() : this(true) { }

    /// <summary>Der Testhost prüft Ansichten ohne Meldungen auf dem Benutzerdesktop; der Produktstart verwendet Shellintegration.</summary>
    internal MainWindow(bool shellIntegration)
    {
        this.shellIntegration = shellIntegration;
        InitializeComponent();
        Title = "SVN Notifier";
        windows = new WindowService(this);
        coordinator = new ApplicationCoordinator(action =>
        {
            if (!DispatcherQueue.TryEnqueue(() => action())) throw new InvalidOperationException("Der Oberflächenthread ist nicht verfügbar.");
        });
        coordinator.Changed += RefreshRepositories;
        coordinator.StatusChanged += text => Status.Text = text;
        coordinator.UpdateLogRequested += process => _ = RunAsync(() => windows.ShowAsync(Loc.ULF_Title, new UpdateLogView(process), close: Loc.ULF_Btn_OK));
        Repositories.ItemsSource = items;
        BuildCommands();
        ApplyFonts();
        RefreshRepositories();
        int[] size = Config.ReadMainFormSize();
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Clamp(size[0] > 0 ? size[0] : 1040, Math.Min(760, workArea.Width), workArea.Width);
        int height = Math.Clamp(size[1] > 0 ? size[1] : 560, Math.Min(360, workArea.Height), workArea.Height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(workArea.X + (workArea.Width - width) / 2, workArea.Y + (workArea.Height - height) / 2, width, height));
        AppWindow.Closing += (_, args) => { if (!exiting && !sessionEnding) { args.Cancel = true; AppWindow.Hide(); } };
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange && !initializing && AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
                Config.SaveMainFormSize(AppWindow.Size.Width, AppWindow.Size.Height);
        };
        Activated += (_, args) => coordinator.Active = args.WindowActivationState != WindowActivationState.Deactivated;
        Root.KeyDown += (_, args) => { if (args.Key == VirtualKey.Escape) { AppWindow.Hide(); args.Handled = true; } };
        Root.Loaded += async (_, _) => { ApplyFonts(); await RunAsync(InitializeAsync); };
        Commands.Loaded += (_, _) => AlignRepositoryCommands(Commands);
        pollTimer.Tick += async (_, _) => await RunAsync(() => coordinator.PollAsync());
        updateTimer.Tick += async (_, _) => await RunAsync(() => CheckApplicationUpdateAsync(false));
        Closed += (_, _) => { cancellation.Cancel(); tray?.Dispose(); updateClient.Dispose(); coordinator.Dispose(); };
    }

    private async Task InitializeAsync()
    {
        if (!initializing) return;
        initializing = false;
        if (!shellIntegration) return;
        if (!Config.IsSettingsOK()) await ShowSettingsAsync();
        if (!Config.IsSettingsOK()) { await ExitAsync(); return; }
        tray = new TrayService(Handle);
        tray.ShowRequested += ShowWindow;
        tray.UpdateAllRequested += () => _ = RunAsync(coordinator.UpdateAllAsync);
        tray.ExitRequested += () => _ = RunAsync(ExitAsync);
        tray.SessionEnding += SetSessionEnding;
        tray.Suspended += suspended => { if (suspended) coordinator.Suspended = true; else coordinator.Resume(); };
        notifications = NotificationService.Current ?? throw new InvalidOperationException("Benachrichtigungsregistrierung fehlt.");
        notifications.Failure += message => _ = RunAsync(() => windows.ErrorAsync(message));
        coordinator.NotificationRequested += notifications.Show;
        notifications.Attach(DispatcherQueue, ActivateNotificationAsync);
        AppWindow.IsShownInSwitchers = Config.ShowInTaskbar;
        RefreshRepositories();
        pollTimer.Start();
        updateTimer.Start();
        if (Config.HideOnStartup) AppWindow.Hide();
        await CheckApplicationUpdateAsync(false);
    }

    internal void ShowWindow()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.Restore();
        Activate();
    }

    /// <summary>Eine bestätigte Windows-Abmeldung darf nicht durch das gewöhnliche Ausblenden verhindert werden.</summary>
    internal void SetSessionEnding(bool ending)
    {
        sessionEnding = ending;
        coordinator.Suspended = ending;
    }

    private async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Config.WriteLog("Application", exception.ToString());
            if (!exiting) await windows.ErrorAsync(exception.Message);
        }
    }

    private static string Label(string value) => value.Replace("&", "");
    private static string MenuKey(string value)
    {
        int marker = value.IndexOf('&');
        return marker >= 0 && marker + 1 < value.Length ? value.Substring(marker + 1, 1) : string.Empty;
    }

    /// <summary>Übernimmt die konfigurierbare Schrift auch für programmgesteuert erzeugte Befehle.</summary>
    private void ApplyFonts()
    {
        Typography.Apply(Root);
        SizeRepositoryColumns(Headers);
        foreach (var item in items)
            if (Repositories.ContainerFromItem(item) is ListViewItem { ContentTemplateRoot: Grid row }) SizeRepositoryColumns(row);
        var family = new FontFamily(Config.FontFamily);
        double size = Config.FontSize * 96d / 72d;
        foreach (var control in new Control[] { Menus, Commands, Repositories, ErrorsButton })
        { control.FontFamily = family; control.FontSize = size; }
        foreach (var text in new[] { MonitorHeader, StatusHeader, PathHeader, AutoHeader, EmptyState, Status })
        { text.FontFamily = family; text.FontSize = size; }
    }

    private void RepositoryRow_Loaded(object sender, RoutedEventArgs args)
    {
        var row = (Grid)sender; Typography.Apply(row); SizeRepositoryColumns(row);
    }

    /// <summary>Die Aktionsleiste beginnt direkt am Listenrand, ohne den leeren Inhaltsbereich der SDK-Vorlage.</summary>
    private static void AlignRepositoryCommands(DependencyObject element)
    {
        if (element is ItemsControl { Name: "PrimaryItemsControl" } commands && VisualTreeHelper.GetParent(commands) is Grid host && host.ColumnDefinitions.Count == 2)
        {
            host.ColumnDefinitions[0].Width = new GridLength(0);
            host.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            commands.HorizontalAlignment = HorizontalAlignment.Left;
        }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++) AlignRepositoryCommands(VisualTreeHelper.GetChild(element, index));
    }

    /// <summary>Gibt Status und Spaltenköpfen auch mit großer Benutzerschrift Platz; der Pfad nutzt den verbleibenden Raum.</summary>
    private static void SizeRepositoryColumns(Grid row)
    {
        double size = Config.FontSize * 96d / 72d;
        row.ColumnDefinitions[0].Width = new GridLength(Math.Max(64, size * 3.3));
        row.ColumnDefinitions[2].Width = new GridLength(Math.Max(120, size * 7));
        row.ColumnDefinitions[4].Width = new GridLength(Math.Max(92, size * 6.4));
    }

    private void BuildCommands()
    {
        commandControls.Clear(); Menus.Items.Clear(); Commands.PrimaryCommands.Clear(); Commands.SecondaryCommands.Clear();
        RepositoryTitle.Text = "Repositories";
        var file = new MenuBarItem { Title = Label(Loc.Menu_File), AccessKey = MenuKey(Loc.Menu_File) };
        AddMenu(file.Items, "AddFolder", Loc.Menu_AddFolder, AddFolderAsync);
        AddMenu(file.Items, "AddFile", Loc.Menu_AddFile, AddFileAsync);
        AddMenu(file.Items, "Remove", Loc.Menu_Remove, RemoveAsync);
        AddMenu(file.Items, "ImportConfiguration", Loc.Menu_ImportConfig, ImportAsync);
        AddMenu(file.Items, "ExportConfiguration", Loc.Menu_ExportConfig, ExportAsync);
        file.Items.Add(new MenuFlyoutSeparator()); AddMenu(file.Items, "Exit", Loc.Menu_Exit, ExitAsync);
        var tools = new MenuBarItem { Title = Label(Loc.Menu_Tools), AccessKey = MenuKey(Loc.Menu_Tools) };
        AddMenu(tools.Items, "UpdateAll", Loc.Menu_UpdateAll, coordinator.UpdateAllAsync);
        AddMenu(tools.Items, "Settings", Loc.Menu_Settings, ShowSettingsAsync);
        var help = new MenuBarItem { Title = Label(Loc.Menu_Help), AccessKey = MenuKey(Loc.Menu_Help) };
        AddMenu(help.Items, "CheckApplicationUpdates", Loc.Menu_CheckUpdates, () => CheckApplicationUpdateAsync(true));
        AddMenu(help.Items, "About", Loc.Menu_About, ShowAboutAsync);
        foreach (var menu in new[] { file, tools, help })
        {
            menu.FontFamily = new FontFamily(Config.FontFamily); menu.FontSize = Config.FontSize * 96d / 72d;
            menu.Loaded += (_, _) => Typography.Apply(menu);
        }
        Menus.Items.Add(file); Menus.Items.Add(tools); Menus.Items.Add(help);
        AddButton("AddFolder", Loc.Btn_AddFolder, Symbol.OpenLocal, AddFolderAsync);
        AddButton("AddFile", Loc.Btn_AddFile, Symbol.Document, AddFileAsync);
        AddButton("Remove", Loc.Btn_Remove, Symbol.Delete, RemoveAsync);
        Commands.PrimaryCommands.Add(new AppBarSeparator());
        AddButton("ChangeLog", Loc.Btn_ChangeLog, Symbol.View, () => SelectedActionAsync("ChangeLog"));
        AddButton("Update", Loc.Btn_Update, Symbol.Download, () => SelectedActionAsync("Update"));
        AddButton("Commit", Loc.Btn_Commit, Symbol.Upload, () => SelectedActionAsync("Commit"));
        AddButton("Open", Loc.Btn_Open, Symbol.OpenFile, () => SelectedActionAsync("Open"));
        AddButton("Log", Loc.Btn_Log, Symbol.List, () => SelectedActionAsync("Log"));
        AddButton("Properties", Loc.Btn_Properties, Symbol.Setting, ShowPropertiesAsync);
        MonitorHeader.Text = Loc.Col_Enabled; StatusHeader.Text = Loc.Col_Status; PathHeader.Text = Loc.Col_Path; AutoHeader.Text = Loc.Col_AutoUpdate;
        EmptyState.Text = Config.Language == 1 ? "Noch keine Repositories. Ordner oder Datei hinzufügen." : "No repositories yet. Add a folder or file.";
        UpdateCommands();
    }

    private void Track(string name, Microsoft.UI.Xaml.DependencyObject control)
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(control, name);
        if (!commandControls.TryGetValue(name, out var controls)) commandControls[name] = controls = new();
        controls.Add(control);
    }

    private void AddButton(string name, string text, Symbol symbol, Func<Task> action)
    {
        var button = new AppBarButton { Label = Label(text), Icon = new SymbolIcon(symbol) };
        if (name == "AddFolder")
        {
            button.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("ms-appx:///ViewResources.xaml") });
            button.CornerRadius = new CornerRadius(4);
            button.Margin = new Thickness(0, 0, 8, 0);
        }
        button.Loaded += (_, _) => Typography.Apply(button);
        Track(name, button); button.Click += async (_, _) => await RunAsync(action); Commands.PrimaryCommands.Add(button);
    }

    private void AddMenu(IList<MenuFlyoutItemBase> menu, string name, string text, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = Label(text), AccessKey = MenuKey(text), FontFamily = new FontFamily(Config.FontFamily), FontSize = Config.FontSize * 96d / 72d };
        Track(name, item); item.Click += async (_, _) => await RunAsync(action); menu.Add(item);
    }

    private void RefreshRepositories()
    {
        var selected = Selected;
        if (!items.SequenceEqual(coordinator.Folders))
        {
            items.Clear(); foreach (var item in coordinator.Folders) items.Add(item);
            if (selected is not null && items.Contains(selected)) Repositories.SelectedItem = selected;
        }
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ErrorsButton.Content = (Config.Language == 1 ? "Fehler" : "Errors") + " (" + coordinator.Errors.Count + ")";
        ErrorsButton.IsEnabled = coordinator.Errors.Count > 0;
        tray?.Update(items);
        notifications?.Reconcile(items);
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        Title = "SVN Notifier " + ApplicationUpdater.CurrentVersion + (Selected?.RepositoryUrl is { Length: > 0 } repository ? " - " + repository : "");
        foreach (var pair in commandControls)
        {
            bool enabled = pair.Key switch
            {
                "Remove" or "Cleanup" or "Properties" or "CheckNow" or "Open" or "Update" or "ChangeLog" or "Commit" or "Log" => CanExecuteSelectedAction(pair.Key),
                "UpdateAll" => !Config.ChangeLogBeforeUpdate && items.Any(ApplicationCoordinator.NeedsUpdate),
                "CheckApplicationUpdates" => !checkingUpdate,
                _ => true
            };
            foreach (var control in pair.Value) if (control is Control element) element.IsEnabled = enabled;
        }
    }

    private async Task AddFolderAsync()
    {
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) coordinator.Add(folder.Path, PathType.Directory);
    }

    private async Task AddFileAsync()
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        var file = await picker.PickSingleFileAsync();
        if (file is not null) coordinator.Add(file.Path, PathType.File);
    }

    private async Task RemoveAsync()
    {
        if (Selected is not { } item) return;
        if (await windows.ShowAsync("SVN Notifier", new TextBlock { Text = Loc.Msg_ConfirmRemove(item.Path), TextWrapping = TextWrapping.Wrap }, Loc.PF_Btn_OK) != ContentDialogResult.Primary) return;
        coordinator.Folders.Remove(item); coordinator.Save();
    }

    private async Task SelectedActionAsync(string action)
    {
        if (Selected is not { } item || !CanExecuteSelectedAction(action)) return;
        switch (action)
        {
            case "Update": await coordinator.UpdateAsync(item); break;
            case "CheckNow": coordinator.CheckNow(item); break;
            case "Commit": await Task.Run(() => SvnTools.Commit(item)); break;
            case "Cleanup": await Task.Run(() => SvnTools.Cleanup(item)); break;
            case "ChangeLog": await Task.Run(() => SvnTools.OpenChangeLogWindow(item, true)); break;
            case "Log": await Task.Run(() => SvnTools.OpenLogWindow(item.Path)); break;
            case "Properties": await ShowPropertiesAsync(); break;
            case "Open": Process.Start(new ProcessStartInfo(File.Exists(item.Path) ? Path.GetDirectoryName(item.Path)! : item.Path) { UseShellExecute = true }); break;
        }
    }

    /// <summary>Menü, Tastatur und Doppelklick beachten dieselben Statusvoraussetzungen des ausgewählten Eintrags.</summary>
    private bool CanExecuteSelectedAction(string action) => action switch
    {
        "Remove" or "Cleanup" or "Properties" or "CheckNow" => Selected is not null,
        "Open" => Selected is not null && (Directory.Exists(Selected.Path) || File.Exists(Selected.Path)),
        "Update" or "ChangeLog" => Selected is not null && ApplicationCoordinator.NeedsUpdate(Selected),
        "Commit" => Selected?.Status is SvnStatus.NeedUpdate_Modified or SvnStatus.UpToDate_Modified,
        "Log" => Selected is not null && Selected.Status is not (SvnStatus.Unknown or SvnStatus.Error),
        _ => false
    };

    private Task DoubleClickAsync() => SelectedActionAsync(Config.ItemDoubleClickAction switch
    {
        Config.Action.logAction => Selected is not null && ApplicationCoordinator.NeedsUpdate(Selected) ? "ChangeLog" : "Log",
        Config.Action.updateAction => "Update", Config.Action.commitAction => "Commit", Config.Action.checkNow => "CheckNow", _ => "Open"
    });

    private async Task ShowSettingsAsync()
    {
        var view = new SettingsView(Handle);
        await windows.ShowAsync(Loc.SF_Title, view, Loc.SF_Btn_OK, Loc.SF_Btn_Cancel, (_, args) =>
        {
            try { view.Apply(); }
            catch (Exception exception) { args.Cancel = true; view.ShowError(exception.Message); }
        });
        BuildCommands(); ApplyFonts();
        var selected = Selected; Repositories.ItemsSource = null; Repositories.ItemsSource = items; Repositories.SelectedItem = selected;
        AppWindow.IsShownInSwitchers = Config.ShowInTaskbar;
        RefreshRepositories();
    }

    private async Task ShowPropertiesAsync()
    {
        if (Selected is not { } item) return;
        var view = new RepositoryPropertiesView(item);
        if (await windows.ShowAsync(Loc.PF_Title, view, Loc.PF_Btn_OK, Loc.PF_Btn_Cancel, (_, args) =>
        {
            try { view.Apply(); }
            catch (Exception exception) { args.Cancel = true; view.ShowError(exception.Message); }
        }) == ContentDialogResult.Primary) coordinator.Save();
    }

    private async Task ShowAboutAsync() => await windows.ShowAsync(Label(Loc.Menu_About), new AboutView(), close: Loc.ELF_Btn_Close);

    private async void ErrorsButton_Click(object sender, RoutedEventArgs args)
    {
        await RunAsync(async () => { var view = new ErrorLogView(coordinator.Errors); await windows.ShowAsync(Loc.ELF_Title, view, close: Loc.ELF_Btn_Close); RefreshRepositories(); });
    }

    private async Task ImportAsync()
    {
        var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".ini"); WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        var file = await picker.PickSingleFileAsync(); if (file is null) return;
        if (items.Count > 0 && await windows.ShowAsync("SVN Notifier", new TextBlock { Text = Config.Language == 1 ? "Aktuelle Einstellungen durch die ausgewählte Konfiguration ersetzen?" : "Replace current settings with the selected configuration?", TextWrapping = TextWrapping.Wrap }, Loc.PF_Btn_OK) != ContentDialogResult.Primary) return;
        bool previouslySuspended = coordinator.Suspended;
        pollTimer.Stop(); coordinator.Suspended = true;
        try
        {
            // Erst die laufende Prüfung beenden, bevor der globale INI-Kontext ersetzt wird.
            await coordinator.PollAsync();
            Config.Import(file.Path);
            coordinator.Folders.Clear(); foreach (var item in Config.ReadSvnFolders().OrderBy(item => item.Path, WindowsPathComparer.Instance)) coordinator.Folders.Add(item);
            coordinator.Save(); BuildCommands(); ApplyFonts();
            AppWindow.IsShownInSwitchers = Config.ShowInTaskbar;
            if (!Config.IsSettingsOK())
            {
                await ShowSettingsAsync();
                if (!Config.IsSettingsOK()) await ExitAsync();
            }
        }
        finally
        {
            coordinator.Suspended = previouslySuspended;
            if (!exiting) pollTimer.Start();
        }
    }

    private async Task ExportAsync()
    {
        var picker = new FileSavePicker { SuggestedFileName = "SVN_Notifier" }; picker.FileTypeChoices.Add("INI", new List<string> { ".ini" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle); var file = await picker.PickSaveFileAsync();
        if (file is not null) Config.Export(file.Path);
    }

    private async Task CheckApplicationUpdateAsync(bool manual)
    {
        if (checkingUpdate || exiting || (!manual && !Config.CheckForNewVersion)) return;
        checkingUpdate = true; UpdateCommands();
        try
        {
            var updater = new ApplicationUpdater(updateClient);
            var release = await updater.CheckAsync(ApplicationUpdater.CurrentVersion, ApplicationUpdater.RuntimeIdentifier, cancellation.Token);
            if (release is null) { if (manual) await windows.ErrorAsync(Loc.Msg_LatestVersion); return; }
            if (!manual && announcedVersion == release.Version) return;
            announcedVersion = release.Version;
            var view = new ApplicationUpdateView(updater, release, async update =>
            {
                await coordinator.PrepareInstallationAsync(() => ApplicationUpdateInstaller.StartAsync(update, Config.Language));
                await ExitAsync();
            });
            await windows.ShowAsync(Loc.AppUpdate_Title, view, close: Loc.AppUpdate_Later);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Config.WriteLog("ApplicationUpdate", exception.ToString());
            if (manual) await windows.ErrorAsync(Loc.AppUpdate_CheckError(exception.Message));
            else notifications?.Show(new(NotificationKind.ApplicationUpdateCheckFailed, Array.Empty<string>(), Loc.AppUpdate_Title, Loc.Msg_CantCheckVersion));
        }
        finally { checkingUpdate = false; UpdateCommands(); }
    }

    private async Task ActivateNotificationAsync(NotificationContext context)
    {
        ShowWindow();
        var item = items.FirstOrDefault(item => context.Paths.Contains(item.Path, StringComparer.OrdinalIgnoreCase));
        if (context.Paths.Count > 0 && item is null) { await windows.ErrorAsync(Config.Language == 1 ? "Der gemeldete Repository-Eintrag wurde entfernt." : "The notified repository entry was removed."); return; }
        if (item is not null) { Repositories.SelectedItem = item; Repositories.ScrollIntoView(item); }
    }

    internal async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true; pollTimer.Stop(); updateTimer.Stop(); cancellation.Cancel();
        Status.Text = Loc.Status_WaitingForOperations;
        await coordinator.StopAsync();
        Config.SaveSettings(); coordinator.Save();
        tray?.Dispose();
        Close();
    }

    private void RepositoryToggle_Click(object sender, RoutedEventArgs args) => coordinator.Save();
    private void Repositories_SelectionChanged(object sender, SelectionChangedEventArgs args) => UpdateCommands();
    private async void Repositories_DoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        for (DependencyObject? source = args.OriginalSource as DependencyObject; source is not null && source != Repositories; source = VisualTreeHelper.GetParent(source))
            if (source is CheckBox) return;
        if (args.OriginalSource is FrameworkElement { DataContext: SvnItem } && args.OriginalSource is not CheckBox) await RunAsync(DoubleClickAsync);
    }
    private async void Repositories_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Delete) { args.Handled = true; await RunAsync(RemoveAsync); }
        if (args.Key == VirtualKey.Insert) { args.Handled = true; await RunAsync(AddFolderAsync); }
        if (args.Key == VirtualKey.Enter) { args.Handled = true; await RunAsync(DoubleClickAsync); }
    }
    private void Repositories_RightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (args.OriginalSource is FrameworkElement { DataContext: SvnItem item }) Repositories.SelectedItem = item;
        var menu = CreateRepositoryMenu();
        menu.ShowAt(Repositories, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = args.GetPosition(Repositories) }); args.Handled = true;
    }

    internal MenuFlyout CreateRepositoryMenu()
    {
        var menu = new MenuFlyout();
        foreach (var command in new[] { ("CheckNow", Loc.Ctx_CheckNow), ("ChangeLog", Loc.Ctx_ChangeLog), ("Update", Loc.Ctx_Update), ("Commit", Loc.Ctx_Commit), ("Cleanup", Loc.Ctx_Cleanup), ("Open", Loc.Ctx_Open), ("Log", Loc.Ctx_Log), ("Properties", Loc.Ctx_Properties) })
        {
            string action = command.Item1; AddMenu(menu.Items, action, command.Item2, () => SelectedActionAsync(action));
        }
        menu.Closed += (_, _) =>
        {
            foreach (var controls in commandControls.Values) controls.RemoveAll(control => menu.Items.Contains(control as MenuFlyoutItemBase));
        };
        UpdateCommands(); return menu;
    }
    private void Repositories_DragOver(object sender, DragEventArgs args) => args.AcceptedOperation = args.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None;
    private async void Repositories_Drop(object sender, DragEventArgs args)
    {
        await RunAsync(async () => { if (!args.DataView.Contains(StandardDataFormats.StorageItems)) return; foreach (var item in await args.DataView.GetStorageItemsAsync()) coordinator.Add(item.Path, item.IsOfType(Windows.Storage.StorageItemTypes.Folder) ? PathType.Directory : PathType.File); });
    }
}
