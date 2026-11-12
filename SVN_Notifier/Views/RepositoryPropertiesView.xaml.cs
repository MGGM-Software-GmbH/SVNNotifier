using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CHD.SVN_Notifier;

public sealed partial class RepositoryPropertiesView : UserControl
{
    private readonly SvnItem item;
    internal RepositoryPropertiesView(SvnItem item)
    {
        this.item = item; InitializeComponent();
        PathInput.Header = Loc.PF_Label_Path; PathInput.Text = item.origPath;
        EnabledInput.Content = Loc.PF_Chk_Enable; EnabledInput.IsChecked = item.Enabled;
        AutoUpdateInput.Content = Loc.Col_AutoUpdate; AutoUpdateInput.IsChecked = item.AutoUpdate;
        CustomInput.Content = Loc.PF_Chk_CustomInterval; CustomInput.IsChecked = item.ActiveStatusUpdateInterval >= 0;
        ActiveInput.Header = Loc.PF_Label_Active.TrimEnd(':') + " (" + Loc.PF_Label_Seconds + ")";
        IdleInput.Header = Loc.PF_Label_Inactive.TrimEnd(':') + " (" + Loc.PF_Label_Seconds + ")";
        ActiveInput.Value = item.ActiveStatusUpdateInterval < 0 ? Config.DefaultActiveStatusUpdateInterval : item.ActiveStatusUpdateInterval;
        IdleInput.Value = item.IdleStatusUpdateInterval < 0 ? Config.DefaultIdleStatusUpdateInterval : item.IdleStatusUpdateInterval;
        UpdateEnabled(this, new RoutedEventArgs());
    }

    private void UpdateEnabled(object sender, RoutedEventArgs args)
    {
        CustomInput.IsEnabled = EnabledInput.IsChecked == true;
        ActiveInput.IsEnabled = IdleInput.IsEnabled = EnabledInput.IsChecked == true && CustomInput.IsChecked == true;
    }

    internal void Apply()
    {
        if (string.IsNullOrWhiteSpace(PathInput.Text) || PathInput.Text.Contains('|')) throw new InvalidDataException(Loc.PF_Label_Path);
        if (!double.IsFinite(ActiveInput.Value) || !double.IsFinite(IdleInput.Value) || ActiveInput.Value != Math.Truncate(ActiveInput.Value) || IdleInput.Value != Math.Truncate(IdleInput.Value)) throw new InvalidDataException(Loc.PF_Chk_CustomInterval);
        item.Path = new SvnItem(PathInput.Text, item.pathType).Path; item.origPath = PathInput.Text;
        item.Enabled = EnabledInput.IsChecked == true; item.AutoUpdate = AutoUpdateInput.IsChecked == true;
        item.ActiveStatusUpdateInterval = CustomInput.IsChecked == true ? (int)ActiveInput.Value : -1;
        item.IdleStatusUpdateInterval = CustomInput.IsChecked == true ? (int)IdleInput.Value : -1;
    }

    internal void ShowError(string message) { ValidationError.Text = message; ValidationError.Visibility = Visibility.Visible; }
}
