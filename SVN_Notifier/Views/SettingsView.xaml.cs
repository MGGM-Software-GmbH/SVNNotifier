using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace CHD.SVN_Notifier;

public sealed partial class SettingsView : UserControl
{
    private readonly IntPtr owner;
    private readonly Dictionary<string, Func<object>> values = new();

    internal SettingsView(IntPtr owner)
    {
        this.owner = owner;
        InitializeComponent();
        var general = AddTab(Loc.SF_Tab_General);
        var clients = Section(general, "Subversion");
        PathField(clients, nameof(Config.SVNpath), Loc.SF_Label_SvnPath);
        PathField(clients, nameof(Config.TortoiseSVNpath), Loc.SF_Label_TortoisePath);
        var behavior = Section(general, Config.Language == 1 ? "Bedienung und Start" : "Behavior and startup");
        Choice(behavior, nameof(Config.ItemDoubleClickAction), Loc.SF_Label_DblClick,
            new[] { Loc.SF_DblClick_Open, Loc.SF_DblClick_Log, Loc.SF_DblClick_Update, Loc.SF_DblClick_Commit, Loc.SF_DblClick_Check });
        var notifications = new Button { Content = Config.Language == 1 ? "Windows-Benachrichtigungseinstellungen" : "Windows notification settings" };
        Toggle(behavior, nameof(Config.ShowNotifications), Loc.SF_Chk_ShowNotifications);
        notifications.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true }); }
            catch (Exception exception) { ShowError(exception.Message); }
        };
        behavior.Children.Add(notifications);
        Toggle(behavior, nameof(Config.HideOnStartup), Loc.SF_Chk_HideOnStart);
        Toggle(behavior, nameof(Config.ShowInTaskbar), Loc.SF_Chk_ShowTaskbar);
        Toggle(behavior, nameof(Config.CheckForNewVersion), Loc.SF_Chk_CheckVersion);
        var typography = Section(general, Config.Language == 1 ? "Darstellung" : "Appearance");
        Choice(typography, nameof(Config.Language), Loc.SF_Label_Language, new[] { "English", "Deutsch" });
        Text(typography, nameof(Config.FontFamily), Loc.SF_Label_Font);
        Number(typography, nameof(Config.FontSize), Loc.SF_Label_FontSize, 6, 72, false);
        var status = AddTab(Loc.SF_Tab_Status);
        Number(status, nameof(Config.DefaultActiveStatusUpdateInterval), Seconds(Loc.SF_Label_Active), 0, 359999);
        Number(status, nameof(Config.DefaultIdleStatusUpdateInterval), Seconds(Loc.SF_Label_Inactive), 0, 359999);
        Number(status, nameof(Config.SvnCommandTimeout), Seconds(Loc.SF_Label_SvnTimeout), 0, 86400);
        var startup = Toggle(status, nameof(Config.DoPauseAfterApplicationStartup), Loc.SF_Chk_Startup);
        var startupTime = Number(status, nameof(Config.PauseAfterApplicationStartupInterval), Seconds(Loc.SF_Chk_Startup), 0, 359999);
        startupTime.IsEnabled = startup.IsChecked == true; startup.Click += (_, _) => startupTime.IsEnabled = startup.IsChecked == true;
        var resume = Toggle(status, nameof(Config.DoPauseAfterWindowsResume), Loc.SF_Chk_Resume);
        var resumeTime = Number(status, nameof(Config.PauseAfterWindowsResumeInterval), Seconds(Loc.SF_Chk_Resume), 0, 359999);
        resumeTime.IsEnabled = resume.IsChecked == true; resume.Click += (_, _) => resumeTime.IsEnabled = resume.IsChecked == true;
        var update = AddTab(Loc.SF_Tab_Update);
        var changelog = Toggle(update, nameof(Config.ChangeLogBeforeUpdate), Loc.SF_Chk_ChangeLog);
        changelog.IsEnabled = Config.IsTortoiseVersion_1_5_orHigher();
        Toggle(update, nameof(Config.UpdateAllSilently), Loc.SF_Chk_SilentUpdate);
        Choice(update, nameof(Config.UpdateWindowAction), Loc.SF_Label_DialogAction,
            Config.Language == 1 ? new[] { "Nicht schließen", "Ohne Fehler schließen", "Ohne Fehler und Konflikte schließen", "Ohne Fehler, Konflikte und Zusammenführungen schließen", "Bei lokalen Vorgängen ohne Fehler, Konflikte und Zusammenführungen schließen" }
            : new[] { "Do not close", "Close if no errors", "Close if no errors or conflicts", "Close if no errors, conflicts or merges", "Close for local operations if no errors, conflicts or merges" });
    }

    private static string Seconds(string label) => label.TrimEnd(':') + " (" + Loc.SF_Label_Seconds + ")";

    /// <summary>Fasst zusammengehörige Optionen zusammen, ohne zusätzliche Bedienseiten einzuführen.</summary>
    private static StackPanel Section(StackPanel parent, string title)
    {
        var fields = new StackPanel { Spacing = 12 };
        fields.Children.Add(new TextBlock { Text = title, Tag = "Typography.Section", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var border = new Border { Child = fields };
        border.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("ms-appx:///ViewResources.xaml") });
        border.Style = (Style)border.Resources["SettingsSectionStyle"];
        parent.Children.Add(border);
        return fields;
    }
    private static FieldInfo Field(string name) => typeof(Config).GetField(name, BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("Unbekannte Einstellung: " + name);

    private StackPanel AddTab(string title)
    {
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(4, 12, 12, 12) };
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 520 };
        scroll.Loaded += (_, _) =>
        {
            Typography.Apply(scroll);
            scroll.MaxHeight = Math.Max(120, Math.Min(520, scroll.XamlRoot.Size.Height - 300));
        };
        Tabs.TabItems.Add(new TabViewItem { Header = title, IsClosable = false, Content = scroll });
        return panel;
    }

    private TextBox Text(StackPanel panel, string name, string label)
    {
        var field = new TextBox { Header = label, Text = (string)Field(name).GetValue(null)!, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(field, name);
        panel.Children.Add(field); values[name] = () => field.Text; return field;
    }

    private void PathField(StackPanel panel, string name, string label)
    {
        var path = Text(panel, name, label);
        panel.Children.Remove(path);
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var browse = new Button { Content = Loc.SF_Btn_Browse, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumn(browse, 1);
        row.Children.Add(path); row.Children.Add(browse); panel.Children.Add(row);
        browse.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".exe"); WinRT.Interop.InitializeWithWindow.Initialize(picker, owner);
                var file = await picker.PickSingleFileAsync(); if (file is not null) path.Text = file.Path;
            }
            catch (Exception exception) { ShowError(exception.Message); }
        };
    }

    private CheckBox Toggle(StackPanel panel, string name, string label)
    {
        var field = new CheckBox { Content = label, IsChecked = (bool)Field(name).GetValue(null)! };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(field, name);
        panel.Children.Add(field); values[name] = () => field.IsChecked == true; return field;
    }

    private NumberBox Number(StackPanel panel, string name, string label, double minimum, double maximum, bool integer = true)
    {
        var field = new NumberBox { Header = label, Value = Convert.ToDouble(Field(name).GetValue(null)), Minimum = minimum, Maximum = maximum, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(field, name);
        panel.Children.Add(field);
        values[name] = () =>
        {
            if (!double.IsFinite(field.Value) || field.Value < minimum || field.Value > maximum || (integer && field.Value != Math.Truncate(field.Value))) throw new InvalidDataException(label);
            return Convert.ChangeType(field.Value, Field(name).FieldType);
        };
        return field;
    }

    private void Choice(StackPanel panel, string name, string label, string[] choices)
    {
        var field = new ComboBox { Header = label, ItemsSource = choices, SelectedIndex = Convert.ToInt32(Field(name).GetValue(null)), HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(field, name);
        panel.Children.Add(field);
        values[name] = () =>
        {
            if (field.SelectedIndex < 0) throw new InvalidDataException(label);
            return Field(name).FieldType.IsEnum ? Enum.ToObject(Field(name).FieldType, field.SelectedIndex) : field.SelectedIndex;
        };
    }

    /// <summary>Übernimmt den Entwurf erst nach erfolgreicher Prüfung; Abbrechen verändert Config nicht.</summary>
    internal void Apply()
    {
        var draft = values.ToDictionary(pair => pair.Key, pair => pair.Value());
        if (!File.Exists((string)draft[nameof(Config.SVNpath)]) || !File.Exists((string)draft[nameof(Config.TortoiseSVNpath)]))
            throw new InvalidDataException(Config.Language == 1 ? "Bitte gültige Programmdateien für SVN und TortoiseSVN auswählen." : "Select valid SVN and TortoiseSVN program files.");
        if (string.IsNullOrWhiteSpace((string)draft[nameof(Config.FontFamily)])) throw new InvalidDataException(Loc.SF_Label_Font);
        var original = draft.Keys.ToDictionary(name => name, name => Field(name).GetValue(null));
        try
        {
            foreach (var pair in draft) Field(pair.Key).SetValue(null, pair.Value);
            Config.SaveSettings();
        }
        catch { foreach (var pair in original) Field(pair.Key).SetValue(null, pair.Value); throw; }
    }

    internal void ShowError(string message) { ValidationError.Text = message; ValidationError.Visibility = Visibility.Visible; }
}
