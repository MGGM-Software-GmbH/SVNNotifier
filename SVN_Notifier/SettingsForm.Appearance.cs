namespace CHD.SVN_Notifier;

public partial class SettingsForm
{
    /// <summary>Ordnet alle Einstellungen mit denselben Feldern und Handlern in skalierende Zeilen.</summary>
    private void InitializeAppearance()
    {
        SuspendLayout();
        Padding = new Padding(12, 12, 12, 0);
        MainTabControl.Dock = DockStyle.Fill;
        MainTabControl.Padding = new Point(18, 8);
        foreach (TabPage page in MainTabControl.TabPages)
        {
            page.Padding = new Padding(16);
            page.AutoScroll = true;
        }
        var paths = DialogLayout.Table();
        paths.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100);
        paths.ColumnStyles[1] = new ColumnStyle(SizeType.AutoSize);
        DialogLayout.Wide(paths, SvnPathLabel);
        DialogLayout.Row(paths, SvnPathTextBox, SvnBrowseButton);
        SvnPathTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        DialogLayout.Wide(paths, TortoisePathLabel);
        DialogLayout.Row(paths, TortoisePathTextBox, TortoiseBrowseButton);
        TortoisePathTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        SvnBrowseButton.AutoSize = TortoiseBrowseButton.AutoSize = true;
        SvnBrowseButton.MinimumSize = TortoiseBrowseButton.MinimumSize = new Size(100, 28);
        DialogLayout.Group(LocationsGroupBox, paths);

        var options = DialogLayout.Table();
        DialogLayout.Row(options, DoubleClickLabel, DoubleClickComboBox);
        var balloon = DialogLayout.Table();
        balloon.ColumnStyles[0] = new ColumnStyle(SizeType.AutoSize);
        balloon.ColumnStyles[1] = new ColumnStyle(SizeType.Percent, 100);
        DialogLayout.Row(balloon, HideSystemTrayUpDown, new Label { AutoSize = true, Text = Loc.SF_Label_Seconds });
        HideSystemTrayUpDown.Width = 70;
        HideSystemTrayUpDown.Anchor = AnchorStyles.Left;
        DialogLayout.Row(options, HideSystemTrayLabel, balloon);
        DialogLayout.Wide(options, HideOnStartupCheckBox);
        DialogLayout.Wide(options, ShowInTaskbarCheckBox);
        DialogLayout.Row(options, LanguageLabel, LanguageComboBox);
        DialogLayout.Row(options, FontFamilyLabel, FontFamilyComboBox);
        DialogLayout.Row(options, FontSizeLabel, FontSizeNumericUpDown);
        FontSizeNumericUpDown.Anchor = AnchorStyles.Left;
        FontSizeNumericUpDown.Width = 70;
        DialogLayout.Wide(options, checkApplicationVersionCheckBox);
        DialogLayout.Group(OtherOptionsGroupBox, options);
        var general = DialogLayout.Table(1);
        DialogLayout.Wide(general, LocationsGroupBox);
        DialogLayout.Wide(general, OtherOptionsGroupBox);
        GeneralTabPage.Controls.Add(general);

        var statusTimes = DialogLayout.Intervals(
            new[] { StatusHoursLabel, StatusMinutesLabel, StatusSecondsLabel },
            new Control[] { ActiveLabel, InactiveLabel },
            new[] { new[] { ActiveHoursUpDown, ActiveMinutesUpDown, ActiveSecondsUpDown }, new[] { InactiveHoursUpDown, InactiveMinutesUpDown, InactiveSecondsUpDown } },
            new[] { new[] { ActiveMinutesLabel, ActiveSecondsLabel }, new[] { InactiveMinutesLabel, InactiveSecondsLabel } });
        DialogLayout.Group(StatusGroupBox, statusTimes);
        var pauseTimes = DialogLayout.Intervals(
            new[] { PauseHoursLabel, PauseMinutesLabel, PauseSecondsLabel },
            new Control[] { StartupLabel, ResumeLabel },
            new[] { new[] { StartupHoursUpDown, StartupMinutesUpDown, StartupSecondsUpDown }, new[] { ResumeHoursUpDown, ResumeMinutesUpDown, ResumeSecondsUpDown } },
            new[] { new[] { StartupMinutesLabel, StartupSecondsLanbel }, new[] { ResumeMinutesLabel, ResumeSecondsLabel } });
        DialogLayout.Group(PauseGroupBox, pauseTimes);
        var status = DialogLayout.Table(1);
        DialogLayout.Wide(status, StatusGroupBox);
        DialogLayout.Wide(status, PauseGroupBox);
        var timeout = DialogLayout.Table(3);
        DialogLayout.Row(timeout, TimeoutLabel, TimeoutUpDown);
        TimeoutUpDown.Width = 70;
        TimeoutUpDown.Anchor = AnchorStyles.Left;
        DialogLayout.Prepare(TimeoutSecondsLabel);
        TimeoutSecondsLabel.Anchor = AnchorStyles.Left;
        timeout.Controls.Add(TimeoutSecondsLabel, 2, 0);
        DialogLayout.Wide(status, timeout);
        StatusTabPage.Controls.Add(status);
        var update = DialogLayout.Table(1);
        DialogLayout.Wide(update, DialogActionLabel);
        DialogLayout.Wide(update, DialogActiionDropDown);
        DialogLayout.Wide(update, ChangeLogBeforeUpdateCheckBox);
        DialogLayout.Wide(update, RequiresTortoiseLabel);
        DialogLayout.Wide(update, SilentlUpdateCheckBox);
        UpdateTabPage.Controls.Add(update);
        DialogLayout.Buttons(this, OkButton, CloseButton);
        ClientSize = new Size(560, 640);
        MinimumSize = new Size(550, 540);
        ApplicationTheme.Apply(this, OkButton);
        Load += (sender, eventArgs) =>
        {
            DialogLayout.FitIntervals(this, statusTimes, pauseTimes);
            DialogLayout.FitHeight(this, general, ClientSize.Height - GeneralTabPage.ClientSize.Height + GeneralTabPage.Padding.Vertical);
        };
        ResumeLayout(true);
    }
}
