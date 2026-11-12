using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CHD.SVN_Notifier;

public sealed class UpdateLogView : UserControl
{
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string file, IntPtr module, uint flags);

    /// <summary>Zeigt die gesicherte SVN-Ausgabe und bewahrt den Protokollzugang sowie den vorhandenen Hinweiston.</summary>
    internal UpdateLogView(SvnFolderProcess process)
    {
        if (!process.isUpdateCommand) throw new ArgumentException("Kein Aktualisierungsprotokoll.", nameof(process));
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("ms-appx:///ViewResources.xaml") });
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = process.folder.Path, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        var rows = new StackPanel { Spacing = 6 };
        var counts = new Dictionary<string, int>();
        bool conflict = false;
        foreach (string line in process.processOutput)
        {
            string action = line.Length >= 5 && line[1] == ' ' ? line[0] switch
            {
                'A' => Config.Language == 1 ? "Hinzugefügt" : "Added",
                'D' => Config.Language == 1 ? "Gelöscht" : "Deleted",
                'U' => Config.Language == 1 ? "Aktualisiert" : "Updated",
                'C' => Config.Language == 1 ? "Konflikt" : "Conflicted",
                'G' => Config.Language == 1 ? "Zusammengeführt" : "Merged", _ => ""
            } : "";
            if (action.Length > 0) counts[action] = counts.GetValueOrDefault(action) + 1;
            conflict |= line.StartsWith("C ", StringComparison.Ordinal);
            bool completed = line.StartsWith("Updated to revision ", StringComparison.Ordinal);
            string text = action.Length > 0 ? action + ": " + line[5..]
                : completed ? (Config.Language == 1 ? "Abgeschlossen: Revision " : "Completed: At revision ") + line[20..]
                : (Config.Language == 1 ? "Fehler: " : "Error: ") + line;
            var row = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            if (action.Length == 0 && !completed || line.StartsWith("C ", StringComparison.Ordinal)) row.Style = (Style)Resources["UpdateLogErrorStyle"];
            else if (completed || line.StartsWith("G ", StringComparison.Ordinal)) row.Style = (Style)Resources["UpdateLogSuccessStyle"];
            rows.Children.Add(row);
        }
        panel.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        panel.Children.Add(new TextBlock { Text = string.Join("   ", counts.Select(pair => pair.Key + ": " + pair.Value)), TextWrapping = TextWrapping.Wrap });
        var warning = new TextBlock { Text = conflict ? Config.Language == 1 ? "Mindestens eine Datei enthält Konflikte." : "One or more files are in a conflicted state." : "", TextWrapping = TextWrapping.Wrap, Visibility = conflict ? Visibility.Visible : Visibility.Collapsed };
        warning.Style = (Style)Resources["UpdateLogErrorStyle"];
        panel.Children.Add(warning);
        var showLog = new Button { Content = Loc.ULF_Btn_ShowLog, Visibility = counts.Count > 0 ? Visibility.Visible : Visibility.Collapsed };
        showLog.Click += async (_, _) =>
        {
            try { await Task.Run(() => SvnTools.OpenChangeLogWindow(process.folder, false)); }
            catch (Exception exception) { warning.Text = exception.Message; warning.Visibility = Visibility.Visible; }
        };
        panel.Children.Add(showLog); Content = panel;
        string sound = Path.Combine(Path.GetDirectoryName(Config.TortoiseSVNpath) ?? "", "TortoiseSVN_error.wav");
        if (File.Exists(sound)) PlaySound(sound, IntPtr.Zero, 0x22003);
    }
}
