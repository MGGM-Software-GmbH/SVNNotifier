namespace CHD.SVN_Notifier;

public partial class AboutForm
{
    /// <summary>Zeigt Versions-, Lizenz- und Herkunftsangaben mit allen vorhandenen Projektlinks.</summary>
    private void InitializeAppearance()
    {
        SuspendLayout();
        Text = Loc.AF_Title;
        HeaderPictureBox.Visible = false;
        BodyPanel.Dock = DockStyle.Fill;
        BodyPanel.AutoScroll = true;
        BodyPanel.Padding = new Padding(20, 12, 20, 12);
        var content = DialogLayout.Table(1);
        foreach (var control in new Control[] { TitleLabel, CopyrightLabel, CompanyLinkLabel, V3Label, V3LinkLabel, GitHubLabel, GitHubLinkLinkLabel, ProjectLabel, ProjectLinkLabel, DescriptionLabel, DevelopersLabel })
        {
            control.Font = Font;
            DialogLayout.Wide(content, control);
            if (control is Label label) label.TextAlign = ContentAlignment.MiddleLeft;
            control.MaximumSize = new Size(520, 0);
        }
        TitleLabel.Font = new Font(Font.FontFamily, Font.Size + 4, FontStyle.Bold);
        BodyPanel.Controls.Add(content);
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(16, 8, 16, 8), BackColor = ApplicationTheme.Canvas
        };
        OkButton.AutoSize = true;
        OkButton.MinimumSize = new Size(84, 28);
        OkButton.Padding = new Padding(8, 2, 8, 2);
        bar.Controls.Add(OkButton);
        Controls.Add(bar);
        bar.SendToBack();
        ClientSize = new Size(480, 380);
        ApplicationTheme.Apply(this, OkButton);
        Load += (sender, eventArgs) =>
        {
            foreach (var label in ApplicationTheme.Descendants(BodyPanel).OfType<Label>())
                label.MaximumSize = new Size(BodyPanel.ClientSize.Width - BodyPanel.Padding.Horizontal - 16, 0);
            DialogLayout.FitHeight(this, content, ClientSize.Height - BodyPanel.ClientSize.Height + BodyPanel.Padding.Vertical);
        };
        FormClosed += (sender, eventArgs) => TitleLabel.Font.Dispose();
        ResumeLayout(true);
    }
}
