using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CHD.SVN_Notifier;

public sealed class ApplicationUpdateView : UserControl
{
    private readonly TextBlock state = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
    private readonly Button install = new() { Content = Loc.AppUpdate_Install };
    private readonly Button cancel = new() { Content = Loc.AppUpdate_Cancel, Visibility = Visibility.Collapsed };
    private CancellationTokenSource? cancellation;
    private bool installing;

    /// <summary>Download bleibt abbrechbar; die bestätigte Übergabe wartet auf eigene SVN-Prozesse.</summary>
    internal ApplicationUpdateView(ApplicationUpdater updater, ApplicationRelease release, Func<PreparedApplicationUpdate, Task> apply)
    {
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = Loc.AppUpdate_Current(ApplicationUpdater.CurrentVersion.ToString()), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = Loc.AppUpdate_Available(release.Version.ToString()), TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new HyperlinkButton { Content = Loc.AppUpdate_Notes, NavigateUri = release.PageUrl, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(state); panel.Children.Add(progress);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(install); buttons.Children.Add(cancel); panel.Children.Add(buttons); Content = panel;
        cancel.Click += (_, _) => cancellation?.Cancel();
        install.Click += async (_, _) =>
        {
            if (!ApplicationUpdateInstaller.IsPortable) { state.Text = Loc.AppUpdate_PortableRequired; return; }
            install.IsEnabled = false; cancel.Visibility = progress.Visibility = Visibility.Visible;
            cancellation = new CancellationTokenSource();
            PreparedApplicationUpdate? prepared = null;
            bool handedOver = false;
            try
            {
                state.Text = Loc.AppUpdate_Downloading;
                prepared = await updater.PrepareAsync(release, ApplicationUpdater.RuntimeIdentifier, Environment.ProcessPath!, new Progress<int>(value => progress.Value = value), cancellation.Token);
                installing = true; cancel.IsEnabled = false; state.Text = Loc.AppUpdate_Installing;
                await apply(prepared);
                handedOver = true;
            }
            catch (OperationCanceledException) { state.Text = Loc.AppUpdate_Cancelled; }
            catch (Exception exception) { state.Text = Loc.AppUpdate_Error(exception.Message); }
            finally
            {
                if (prepared is not null && !handedOver) ApplicationUpdater.DeleteStage(prepared.DirectoryPath, Environment.ProcessPath!);
                installing = false; cancellation.Dispose(); cancellation = null;
                install.IsEnabled = cancel.IsEnabled = true; cancel.Visibility = progress.Visibility = Visibility.Collapsed;
            }
        };
    }

    internal bool RequestClose()
    {
        if (installing) return false;
        if (cancellation is null) return true;
        cancellation.Cancel(); return false;
    }
}
