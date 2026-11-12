using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CHD.SVN_Notifier;

internal sealed class WindowService
{
    private readonly MainWindow owner;
    private readonly SemaphoreSlim dialogs = new(1);

    internal WindowService(MainWindow owner) => this.owner = owner;

    /// <summary>Serialisiert modale Ansichten und bindet Schrift, Theme und Besitzer an das Hauptfenster.</summary>
    internal async Task<ContentDialogResult> ShowAsync(string title, UIElement content, string primary = "", string close = "", Action<ContentDialog, ContentDialogButtonClickEventArgs>? apply = null)
    {
        await dialogs.WaitAsync();
        try
        {
            owner.ShowWindow();
            var dialog = new ContentDialog
            {
                Title = new TextBlock { Text = title, Tag = "Typography.Title", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                Content = content, PrimaryButtonText = primary.Replace("&", ""),
                CloseButtonText = close.Length == 0 ? Loc.PF_Btn_Cancel : close.Replace("&", ""),
                DefaultButton = primary.Length > 0 ? ContentDialogButton.Primary : ContentDialogButton.Close,
                XamlRoot = owner.RootElement.XamlRoot, RequestedTheme = owner.RootElement.ActualTheme,
                FontFamily = new FontFamily(Config.FontFamily), FontSize = Config.FontSize * 96d / 72d
            };
            // Die Inhaltsart bestimmt die Dialogbreite; kurze Dialoge erhalten keine breite leere Formularfläche.
            double preferredWidth = content switch
            {
                SettingsView => 720,
                RepositoryPropertiesView => 620,
                ErrorLogView => 680,
                UpdateLogView => 760,
                AboutView => 520,
                ApplicationUpdateView => 460,
                _ => 460
            };
            double width = Math.Max(280, Math.Min(preferredWidth * Math.Max(1, Config.FontSize / 12d), owner.RootElement.ActualWidth - 48));
            dialog.Resources["ContentDialogMinWidth"] = width;
            dialog.Resources["ContentDialogMaxWidth"] = width;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(dialog, content.GetType().Name + "Dialog");
            dialog.Resources["ContentDialogMaxHeight"] = Math.Max(240, owner.RootElement.ActualHeight - 32);
            var buttonStyle = new Style(typeof(Button));
            buttonStyle.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right));
            buttonStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 90d));
            buttonStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, Math.Max(220d, Config.FontSize * 96d / 72d * 10)));
            buttonStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, double.NaN));
            buttonStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, Math.Max(32d, Config.FontSize * 96d / 72d + 14)));
            dialog.PrimaryButtonStyle = dialog.CloseButtonStyle = buttonStyle;
            // Der DefaultButton-Zustand ersetzt den Primärstil durch AccentButtonStyle.
            // Die Akzentdarstellung erhält deshalb dieselben inhaltsbezogenen Grenzen.
            var accentStyle = new Style(typeof(Button)) { BasedOn = (Style)Application.Current.Resources["AccentButtonStyle"] };
            foreach (var setter in buttonStyle.Setters) accentStyle.Setters.Add(new Setter(((Setter)setter).Property, ((Setter)setter).Value));
            dialog.Resources["AccentButtonStyle"] = accentStyle;
            dialog.Loaded += (_, _) => { Typography.Apply(dialog); AlignCommands(dialog); };
            if (apply is not null) dialog.PrimaryButtonClick += (sender, args) => apply(sender, args);
            if (content is ErrorLogView errors) errors.ClearRequested += dialog.Hide;
            dialog.Closing += (_, args) => { if (content is ApplicationUpdateView update && !update.RequestClose()) args.Cancel = true; };
            return await dialog.ShowAsync();
        }
        finally { dialogs.Release(); }
    }

    internal Task<ContentDialogResult> ErrorAsync(string text) => ShowAsync("SVN Notifier", new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, close: Loc.ELF_Btn_Close);

    /// <summary>Hält die Dialogaktionen als kompakte Gruppe am rechten Rand.</summary>
    private static void AlignCommands(DependencyObject element)
    {
        if (element is Grid { Name: "CommandSpace" } commands) commands.HorizontalAlignment = HorizontalAlignment.Right;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            AlignCommands(VisualTreeHelper.GetChild(element, index));
    }
}
