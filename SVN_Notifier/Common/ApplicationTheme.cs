namespace CHD.SVN_Notifier;

/// <summary>Gemeinsame Darstellung der vorhandenen WinForms-Steuerelemente ohne zusätzliche UI-Abhängigkeiten.</summary>
internal static class ApplicationTheme
{
    internal static Color Surface => SystemInformation.HighContrast ? SystemColors.Window : Color.White;
    internal static Color Canvas => SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(246, 248, 251);
    internal static Color Text => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(31, 41, 55);
    internal static Color Muted => SystemInformation.HighContrast ? SystemColors.GrayText : Color.FromArgb(97, 109, 128);
    internal static Color Border => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(220, 226, 234);
    internal static Color Accent => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(24, 94, 179);
    internal static Color Selection => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(226, 237, 252);
    internal static Color Success => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(29, 122, 78);
    internal static Color Warning => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(156, 94, 15);
    internal static Color Danger => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(184, 48, 57);

    /// <summary>Belässt native Tastatur- und Fokusdarstellung sowie die vom Benutzer gewählte Schrift.</summary>
    internal static void Apply(Form form, Button? primary = null)
    {
        // Abstände und Fenstermaße folgen der Monitorauflösung. Größere Schrift vergrößert nur den benötigten Inhalt.
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.BackColor = Surface;
        form.ForeColor = Text;
        foreach (var control in Descendants(form))
        {
            if (control is Button button)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = Selection;
                button.FlatAppearance.MouseDownBackColor = Selection;
                button.BackColor = Surface;
                button.ForeColor = Text;
                if (button == primary)
                {
                    button.BackColor = Accent;
                    button.ForeColor = SystemColors.HighlightText;
                    button.FlatAppearance.BorderColor = Accent;
                    button.FlatAppearance.MouseOverBackColor = ControlPaint.Dark(Accent, .1f);
                    button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(Accent, .2f);
                }
                void RefreshEnabledColors(object? sender, EventArgs eventArgs)
                {
                    button.BackColor = button == primary && button.Enabled ? Accent : Surface;
                    button.ForeColor = !button.Enabled ? SystemColors.GrayText : button == primary ? SystemColors.HighlightText : Text;
                    button.FlatAppearance.BorderColor = button == primary && button.Enabled ? Accent : Border;
                }
                button.EnabledChanged += RefreshEnabledColors;
                RefreshEnabledColors(button, EventArgs.Empty);
            }
            else if (control is LinkLabel link)
            {
                link.LinkColor = Accent;
                link.ActiveLinkColor = Accent;
                link.VisitedLinkColor = Accent;
            }
            else if (control is TextBox textBox)
            {
                textBox.BorderStyle = BorderStyle.FixedSingle;
                if (textBox.ReadOnly) textBox.BackColor = Surface;
            }
            else if (control is ListView list)
            {
                list.BorderStyle = BorderStyle.None;
                list.FullRowSelect = true;
                list.BackColor = Surface;
                list.ForeColor = Text;
            }
            else if (control is TabPage page)
            {
                page.UseVisualStyleBackColor = false;
                page.BackColor = Surface;
            }
        }
        form.Load += (sender, eventArgs) =>
        {
            foreach (var editor in Descendants(form).OfType<NumericUpDown>())
            {
                var text = editor.Maximum.ToString("F" + editor.DecimalPlaces);
                editor.Width = Math.Max(60 * form.DeviceDpi / 96, TextRenderer.MeasureText(text, editor.Font).Width + 24 * form.DeviceDpi / 96);
            }
        };
    }

    /// <summary>Erlaubt gemeinsamen Stil und Tests auch für verschachtelte Dialoginhalte.</summary>
    internal static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    /// <summary>Erzeugt flache Menüs mit nativer Tastaturbedienung und sichtbaren Auswahlzuständen.</summary>
    internal sealed class StripRenderer : ToolStripProfessionalRenderer
    {
        internal StripRenderer() : base(new StripColors()) { RoundedEdges = false; }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs eventArgs)
        {
            if (eventArgs.ToolStrip is ToolStripDropDown)
                base.OnRenderToolStripBorder(eventArgs);
        }
    }

    private sealed class StripColors : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Surface;
        public override Color ToolStripGradientMiddle => Surface;
        public override Color ToolStripGradientEnd => Surface;
        public override Color MenuStripGradientBegin => Surface;
        public override Color MenuStripGradientEnd => Surface;
        public override Color StatusStripGradientBegin => Canvas;
        public override Color StatusStripGradientEnd => Canvas;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuItemSelected => Selection;
        public override Color MenuItemSelectedGradientBegin => Selection;
        public override Color MenuItemSelectedGradientEnd => Selection;
        public override Color MenuItemPressedGradientBegin => Selection;
        public override Color MenuItemPressedGradientMiddle => Selection;
        public override Color MenuItemPressedGradientEnd => Selection;
        public override Color ButtonSelectedGradientBegin => Selection;
        public override Color ButtonSelectedGradientMiddle => Selection;
        public override Color ButtonSelectedGradientEnd => Selection;
        public override Color ButtonPressedGradientBegin => Selection;
        public override Color ButtonPressedGradientMiddle => Selection;
        public override Color ButtonPressedGradientEnd => Selection;
        public override Color ButtonSelectedBorder => Border;
        public override Color MenuItemBorder => Border;
        public override Color MenuBorder => Border;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
    }
}
