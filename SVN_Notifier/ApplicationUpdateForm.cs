using System.Diagnostics;

namespace CHD.SVN_Notifier;

/// <summary>Zeigt die angebotene Programmversion und führt den bestätigten Download mit Neustart aus.</summary>
internal sealed class ApplicationUpdateForm : Form
{
    private readonly ApplicationUpdater updater;
    private readonly ApplicationRelease release;
    private readonly Func<PreparedApplicationUpdate, Task> install;
    private readonly Button installButton = new() { AutoSize = true, Name = "InstallApplicationUpdateButton" };
    private readonly Button cancelButton = new() { AutoSize = true, Name = "CancelApplicationUpdateButton" };
    private readonly Label statusLabel = new() { AutoSize = true, Visible = false, Name = "ApplicationUpdateStatusLabel" };
    private readonly ProgressBar progressBar = new() { Dock = DockStyle.Fill, Visible = false, Name = "ApplicationUpdateProgressBar" };
    private CancellationTokenSource? downloadCancellation;
    private bool closingToInstall;

    internal ApplicationUpdateForm(ApplicationUpdater updater, ApplicationRelease release, Func<PreparedApplicationUpdate, Task> install)
    {
        this.updater = updater;
        this.release = release;
        this.install = install;
        Name = "ApplicationUpdateForm";
        Text = Loc.AppUpdate_Title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(510, 0);

        var layout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1 };
        layout.Controls.Add(new Label { AutoSize = true, Text = Loc.AppUpdate_Current(ApplicationUpdater.CurrentVersion.ToString()), Margin = new Padding(0, 0, 0, 8) });
        layout.Controls.Add(new Label { AutoSize = true, Text = Loc.AppUpdate_Available(release.Version.ToString()), Margin = new Padding(0, 0, 0, 8) });
        var notes = new LinkLabel { AutoSize = true, Text = Loc.AppUpdate_Notes, Margin = new Padding(0, 0, 0, 16) };
        notes.LinkClicked += (sender, eventArgs) => OpenReleaseNotes();
        layout.Controls.Add(notes);
        statusLabel.Margin = new Padding(0, 0, 0, 6);
        layout.Controls.Add(statusLabel);
        progressBar.Margin = new Padding(0, 0, 0, 16);
        progressBar.Height = 16;
        layout.Controls.Add(progressBar);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
        cancelButton.Text = Loc.AppUpdate_Later;
        cancelButton.MinimumSize = new Size(85, 28);
        cancelButton.Margin = new Padding(8, 0, 0, 0);
        cancelButton.Click += (sender, eventArgs) => CancelOrClose();
        installButton.Text = Loc.AppUpdate_Install;
        installButton.MinimumSize = new Size(195, 28);
        installButton.Margin = Padding.Empty;
        installButton.Click += async (sender, eventArgs) => await DownloadAndInstallAsync();
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(installButton);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = installButton;
        CancelButton = cancelButton;
        ApplicationTheme.Apply(this, installButton);
        FormClosing += (sender, eventArgs) =>
        {
            if (downloadCancellation is not null && !closingToInstall)
            {
                downloadCancellation.Cancel();
                eventArgs.Cancel = true;
            }
        };
    }

    /// <summary>Öffnet ausschließlich die bereits validierte GitHub-Release-Seite.</summary>
    private void OpenReleaseNotes()
    {
        try
        {
            Process.Start(new ProcessStartInfo(release.PageUrl.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, Loc.AppUpdate_Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Erlaubt Abbruch während des Downloads; während des Austauschs übernimmt der Installer.</summary>
    private void CancelOrClose()
    {
        if (downloadCancellation is null)
            Close();
        else
            downloadCancellation.Cancel();
    }

    /// <summary>Installiert erst nach erfolgreicher Paketprüfung und meldet Fehler ohne Versionswechsel.</summary>
    private async Task DownloadAndInstallAsync()
    {
        if (!ApplicationUpdateInstaller.IsPortable)
        {
            MessageBox.Show(this, Loc.AppUpdate_PortableRequired, Loc.AppUpdate_Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        installButton.Enabled = false;
        cancelButton.Text = Loc.AppUpdate_Cancel;
        statusLabel.Text = Loc.AppUpdate_Downloading;
        statusLabel.Visible = true;
        progressBar.Visible = true;
        downloadCancellation = new CancellationTokenSource();
        PreparedApplicationUpdate? prepared = null;
        try
        {
            var progress = new Progress<int>(value =>
            {
                progressBar.Value = value;
                if (value == 100)
                    statusLabel.Text = Loc.AppUpdate_Verifying;
            });
            prepared = await updater.PrepareAsync(release, ApplicationUpdater.RuntimeIdentifier, Environment.ProcessPath!, progress, downloadCancellation.Token);
            downloadCancellation.Token.ThrowIfCancellationRequested();
            statusLabel.Text = Loc.AppUpdate_Installing;
            cancelButton.Enabled = false;
            ControlBox = false;
            closingToInstall = true;
            await install(prepared);
            prepared = null;
        }
        catch (OperationCanceledException) when (downloadCancellation.IsCancellationRequested)
        {
            statusLabel.Text = Loc.AppUpdate_Cancelled;
        }
        catch (Exception exception)
        {
            statusLabel.Text = Loc.AppUpdate_NotInstalled;
            MessageBox.Show(this, Loc.AppUpdate_Error(exception.Message), Loc.AppUpdate_Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (prepared is not null)
                ApplicationUpdater.DeleteStage(prepared.DirectoryPath, Environment.ProcessPath!);
            downloadCancellation.Dispose();
            downloadCancellation = null;
            closingToInstall = false;
            if (!IsDisposed)
            {
                installButton.Enabled = true;
                cancelButton.Enabled = true;
                cancelButton.Text = Loc.AppUpdate_Later;
                progressBar.Value = 0;
                progressBar.Visible = false;
                ControlBox = true;
            }
        }
    }
}
