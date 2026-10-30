namespace CHD.SVN_Notifier;

public partial class PropertiesForm
{
    /// <summary>Erhält die Repository-Eigenschaften samt individueller Zeitsteuerung in einem skalierenden Layout.</summary>
    private void InitializeAppearance()
    {
        SuspendLayout();
        Padding = new Padding(16, 12, 16, 0);
        var content = DialogLayout.Table(1);
        DialogLayout.Wide(content, PathLabel);
        DialogLayout.Wide(content, PathTextBox);
        DialogLayout.Wide(content, EnableCheckBox);
        DialogLayout.Wide(content, StatusUpdateCheckBox);
        var intervals = DialogLayout.Intervals(
            new[] { StatusHoursLabel, StatusMinutesLabel, StatusSecondsLabel },
            new Control[] { FormIsActiveLabel, FormIsInactiveLabel },
            new[] { new[] { ActiveHoursUpDown, ActiveMinutesUpDown, ActiveSecondsUpDown }, new[] { InactiveHoursUpDown, InactiveMinutesUpDown, InactiveSecondsUpDown } },
            new[] { new[] { ActiveMinutesLabel, ActiveSecondsLabel }, new[] { InactiveMinutesLabel, InactiveSecondsLabel } });
        StatusUpdatePanel.Controls.Add(intervals);
        StatusUpdatePanel.AutoSize = true;
        StatusUpdatePanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        DialogLayout.Wide(content, StatusUpdatePanel);
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        body.Controls.Add(content);
        Controls.Add(body);
        DialogLayout.Buttons(this, OkButton, CloseButton);
        ClientSize = new Size(540, 300);
        MinimumSize = new Size(520, 300);
        ApplicationTheme.Apply(this, OkButton);
        Load += (sender, eventArgs) =>
        {
            DialogLayout.FitIntervals(this, intervals);
            DialogLayout.FitHeight(this, content, ClientSize.Height - body.ClientSize.Height);
        };
        ResumeLayout(true);
    }
}
