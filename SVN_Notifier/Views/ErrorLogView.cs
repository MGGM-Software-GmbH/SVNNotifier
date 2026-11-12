using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CHD.SVN_Notifier;

public sealed class ErrorLogView : UserControl
{
    internal event Action? ClearRequested;

    /// <summary>Leeren übernimmt die Änderung in den Fehlerpuffer und schließt den bestehenden Bedienablauf ab.</summary>
    internal ErrorLogView(Dictionary<string, string> errors)
    {
        var panel = new StackPanel { Spacing = 12, MinWidth = 260 };
        var text = new TextBox { Text = string.Concat(errors.Values), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 180, MaxHeight = 400 };
        ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        var clear = new Button { Content = Loc.ELF_Btn_Clear };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(clear, "ErrorLogClear");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(text, "ErrorLogText");
        clear.Click += (_, _) => { errors.Clear(); text.Text = ""; ClearRequested?.Invoke(); };
        panel.Children.Add(text); panel.Children.Add(clear); Content = panel;
    }
}
