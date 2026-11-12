using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CHD.SVN_Notifier;

public sealed class AboutView : UserControl
{
    internal AboutView()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "SVN Notifier " + ApplicationUpdater.CurrentVersion, Tag = "Typography.Title", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Copyright (C) 2007-2011 CHD\nv1/v2: Vlad Rudenko, A. Vaschenko, N. Nahimov | v3: MGGM-Software", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = Loc.AF_Description, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new HyperlinkButton { Content = "www.chd.lv", NavigateUri = new Uri("http://www.chd.lv"), HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(new HyperlinkButton { Content = "MGGM-Software GmbH", NavigateUri = new Uri("https://github.com/MGGM-Software-GmbH/SVNNotifier"), HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(new HyperlinkButton { Content = "JocysCom/SVNNotifier", NavigateUri = new Uri("https://github.com/JocysCom/SVNNotifier"), HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(new HyperlinkButton { Content = "svnnotifier.tigris.org", NavigateUri = new Uri("http://svnnotifier.tigris.org"), HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(new HyperlinkButton { Content = "GNU General Public License v3", NavigateUri = new Uri("https://www.gnu.org/licenses/gpl-3.0.html"), HorizontalAlignment = HorizontalAlignment.Left });
        Content = panel;
    }
}
