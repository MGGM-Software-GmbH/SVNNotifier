using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CHD.SVN_Notifier;

internal static class Typography
{
    /// <summary>Überträgt die gespeicherte Benutzerschrift auch über WinUI-Templategrenzen hinweg.</summary>
    internal static void Apply(DependencyObject root)
    {
        var family = new FontFamily(Config.FontFamily);
        double size = Config.FontSize * 96d / 72d;
        if (root is Control control) { control.FontFamily = family; control.FontSize = size; }
        if (root is TextBlock text && !text.FontFamily.Source.Contains("Icons", StringComparison.OrdinalIgnoreCase)
            && !text.FontFamily.Source.Contains("MDL2", StringComparison.OrdinalIgnoreCase))
        {
            text.FontFamily = family;
            text.FontSize = (text.Tag as string) switch
            {
                "Typography.Title" => Math.Max(20, size * 1.35),
                "Typography.Section" => Math.Max(14, size * 1.15),
                _ => size
            };
        }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            Apply(VisualTreeHelper.GetChild(root, index));
    }
}
