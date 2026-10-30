using System.Net.Http;

namespace CHD.SVN_Notifier;

public partial class MainForm
{
    private readonly HttpClient applicationUpdateClient = ApplicationUpdater.CreateClient();
    private readonly System.Windows.Forms.Timer applicationUpdateTimer = new() { Interval = 12 * 60 * 60 * 1000 };
    private readonly CancellationTokenSource applicationUpdateCancellation = new();
    private ToolStripMenuItem checkApplicationUpdatesMenuItem = null!;
    private bool checkingApplicationUpdate;
    private bool applicationInstallationInProgress;
    private Version? announcedApplicationVersion;

    /// <summary>Verbindet die manuelle Prüfung und die abschaltbare regelmäßige Programmprüfung.</summary>
    private void InitializeApplicationUpdates()
    {
        checkApplicationUpdatesMenuItem = new ToolStripMenuItem(Loc.Menu_CheckUpdates) { Name = "CheckApplicationUpdatesMenuItem" };
        checkApplicationUpdatesMenuItem.Click += async (sender, eventArgs) => await CheckApplicationUpdateAsync(true);
        HelpMenuItem.DropDownItems.Insert(0, checkApplicationUpdatesMenuItem);
        applicationUpdateTimer.Tick += async (sender, eventArgs) => await CheckApplicationUpdateAsync(false);
        Shown += async (sender, eventArgs) => await CheckApplicationUpdateAsync(false);
        applicationUpdateTimer.Start();
    }

    /// <summary>Hält die Oberfläche bei der Netzwerkprüfung bedienbar und trennt Fehler von fehlenden Updates.</summary>
    private async Task CheckApplicationUpdateAsync(bool manual)
    {
        if (checkingApplicationUpdate || (!manual && !Config.CheckForNewVersion))
            return;
        checkingApplicationUpdate = true;
        checkApplicationUpdatesMenuItem.Enabled = false;
        var previousStatus = StatusLabel.Text;
        if (manual)
            StatusLabel.Text = Loc.AppUpdate_Checking;
        try
        {
            var updater = new ApplicationUpdater(applicationUpdateClient);
            var release = await updater.CheckAsync(ApplicationUpdater.CurrentVersion, ApplicationUpdater.RuntimeIdentifier, applicationUpdateCancellation.Token);
            if (IsDisposed || applicationUpdateCancellation.IsCancellationRequested)
                return;
            if (release is null)
            {
                if (manual)
                    MessageBox.Show(this, Loc.Msg_LatestVersion, Loc.AppUpdate_Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!manual && announcedApplicationVersion == release.Version)
                return;
            announcedApplicationVersion = release.Version;
            using var dialog = new ApplicationUpdateForm(updater, release, InstallApplicationUpdateAsync);
            dialog.ShowDialog(this);
        }
        catch (OperationCanceledException) when (applicationUpdateCancellation.IsCancellationRequested)
        {
            // Beim Beenden wird keine neue Meldung mehr geöffnet.
        }
        catch (Exception exception)
        {
            Config.WriteLog("ApplicationUpdate", exception.ToString());
            if (!IsDisposed)
            {
                if (manual)
                    MessageBox.Show(this, Loc.AppUpdate_CheckError(exception.Message), Loc.AppUpdate_Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                else
                    TrayNotifyIcon.ShowBalloonTip(5000, Loc.AppUpdate_Title, Loc.Msg_CantCheckVersion, ToolTipIcon.Warning);
            }
        }
        finally
        {
            checkingApplicationUpdate = false;
            if (!IsDisposed)
            {
                checkApplicationUpdatesMenuItem.Enabled = true;
                if (StatusLabel.Text == Loc.AppUpdate_Checking)
                    StatusLabel.Text = previousStatus;
            }
        }
    }

    /// <summary>Beendet die Anwendung erst nach der Bereitschaft des Installers und ohne laufende SVN-Vorgänge.</summary>
    private async Task InstallApplicationUpdateAsync(PreparedApplicationUpdate update)
    {
        lock (this)
        {
            if (statusThread is not null || SvnTools.HasActiveOperations || !updateNotInProgress.WaitOne(0))
                throw new InvalidOperationException(Loc.AppUpdate_RepositoryBusy);
            StatusUpdateTimer.Stop();
            PauseTimer.Stop();
            applicationInstallationInProgress = true;
        }
        try
        {
            Config.SaveSettings();
            Config.SaveSvnFolders(folders);
            await ApplicationUpdateInstaller.StartAsync(update, Config.Language);
            sessionEndInProgress = true;
            Application.Exit();
        }
        catch
        {
            applicationInstallationInProgress = false;
            StartTimer();
            throw;
        }
    }

    /// <summary>Stoppt Prüfungen und gibt Netzwerkressourcen beim Beenden frei.</summary>
    private void StopApplicationUpdates()
    {
        applicationUpdateTimer.Dispose();
        applicationUpdateCancellation.Cancel();
        applicationUpdateClient.Dispose();
    }

    /// <summary>Protokolliert ein fehlgeschlagenes Aufräumen, ohne den Neustart zu verhindern.</summary>
    private static async Task CleanupApplicationUpdateAsync(string directory, int helperId, long helperStartTicks)
    {
        try
        {
            await ApplicationUpdateInstaller.CleanupAsync(directory, helperId, helperStartTicks);
        }
        catch (Exception exception)
        {
            Config.WriteLog("ApplicationUpdateCleanup", exception.ToString());
        }
    }

    /// <summary>Vermeidet doppelte Fehlermeldungen, solange die ursprüngliche Anwendung den Installer noch überwacht.</summary>
    private static bool IsOriginalUpdateProcessRunning(ApplicationUpdatePlan plan)
    {
        try
        {
            using var parent = System.Diagnostics.Process.GetProcessById(plan.ParentId);
            return !parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
