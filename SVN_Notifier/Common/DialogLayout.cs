namespace CHD.SVN_Notifier;

/// <summary>Ordnet vorhandene Dialogfelder nach ihrem Inhalt und erhält deren Ereignisse und Tab-Reihenfolge.</summary>
internal static class DialogLayout
{
    internal static TableLayoutPanel Table(int columns = 2)
    {
        var table = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top, ColumnCount = columns, Margin = Padding.Empty
        };
        for (int index = 0; index < columns; index++)
            table.ColumnStyles.Add(new ColumnStyle(index == columns - 1 ? SizeType.Percent : SizeType.AutoSize, index == columns - 1 ? 100 : 0));
        return table;
    }

    internal static void Row(TableLayoutPanel table, Control label, Control editor)
    {
        int row = table.RowCount++;
        Prepare(label);
        Prepare(editor);
        label.Anchor = AnchorStyles.Left;
        editor.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        table.Controls.Add(label, 0, row);
        table.Controls.Add(editor, 1, row);
        label.TabIndex = row * 2;
        editor.TabIndex = row * 2 + 1;
    }

    internal static void Wide(TableLayoutPanel table, Control control)
    {
        int row = table.RowCount++;
        Prepare(control);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.TabIndex = row * 2;
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, table.ColumnCount);
    }

    internal static void Prepare(Control control)
    {
        control.Dock = DockStyle.None;
        control.Margin = new Padding(0, 5, 12, 5);
        if (control is Label or CheckBox) control.AutoSize = true;
    }

    internal static void Group(GroupBox group, TableLayoutPanel content)
    {
        group.SuspendLayout();
        group.Controls.Clear();
        group.AutoSize = true;
        group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        group.Dock = DockStyle.Top;
        group.Padding = new Padding(12, 22, 12, 10);
        group.Margin = new Padding(0, 0, 0, 14);
        group.Controls.Add(content);
        group.ResumeLayout(true);
    }

    /// <summary>Verwendet für die Zeitfelder eigene Spalten statt Zahlen in ausgesparten Labeltext einzublenden.</summary>
    internal static TableLayoutPanel Intervals(Label[] headers, Control[] labels, NumericUpDown[][] values, Label[][] separators)
    {
        var table = Table(6);
        table.ColumnStyles.Clear();
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int index = 1; index < 6; index++) table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.RowCount = 3;
        for (int index = 0; index < headers.Length; index++)
        {
            Prepare(headers[index]);
            headers[index].Anchor = AnchorStyles.Left;
            headers[index].ForeColor = ApplicationTheme.Muted;
            table.Controls.Add(headers[index], index * 2 + 1, 0);
        }
        for (int row = 0; row < labels.Length; row++)
        {
            Prepare(labels[row]);
            labels[row].Anchor = AnchorStyles.Left;
            table.Controls.Add(labels[row], 0, row + 1);
            for (int column = 0; column < 3; column++)
            {
                var editor = values[row][column];
                Prepare(editor);
                editor.Width = 62;
                editor.TabIndex = row * 3 + column;
                editor.Anchor = AnchorStyles.Left;
                table.Controls.Add(editor, column * 2 + 1, row + 1);
                if (column == 2) continue;
                var separator = separators[row][column];
                Prepare(separator);
                separator.Anchor = AnchorStyles.None;
                separator.Margin = new Padding(0, 5, 8, 5);
                table.Controls.Add(separator, column * 2 + 2, row + 1);
            }
        }
        return table;
    }

    /// <summary>Behält dieselben OK-, Abbruch- und Zusatzaktionen in einer gemeinsamen unteren Leiste.</summary>
    internal static void Buttons(Form form, Button primary, Button cancel, Button? secondary = null)
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 3,
            Name = "DialogButtonBar", Padding = new Padding(16, 8, 16, 8), BackColor = ApplicationTheme.Canvas
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var buttons = secondary is null ? new[] { primary, cancel } : new[] { secondary, primary, cancel };
        foreach (var button in buttons)
        {
            button.Dock = DockStyle.None;
            button.Anchor = AnchorStyles.Right;
            button.AutoSize = true;
            button.MinimumSize = new Size(84, 28);
            button.Padding = new Padding(8, 2, 8, 2);
            button.Margin = new Padding(8, 0, 0, 0);
        }
        if (secondary is not null) { secondary.Anchor = AnchorStyles.Left; bar.Controls.Add(secondary, 0, 0); }
        bar.Controls.Add(primary, 1, 0);
        bar.Controls.Add(cancel, 2, 0);
        form.Controls.Add(bar);
        bar.SendToBack();
    }

    /// <summary>Vergrößert Dialoge nur um den tatsächlichen Platzbedarf der Zeitfelder und begrenzt sie auf den Bildschirm.</summary>
    internal static void FitIntervals(Form form, params TableLayoutPanel[] tables)
    {
        int width = form.ClientSize.Width;
        foreach (var table in tables)
        {
            int columnsWidth = 0;
            for (int column = 0; column < table.ColumnCount; column++)
            {
                columnsWidth += table.Controls.Cast<Control>().Where(control => table.GetColumn(control) == column)
                    .Select(control => control.GetPreferredSize(Size.Empty).Width + control.Margin.Horizontal).DefaultIfEmpty(0).Max();
            }
            width = Math.Max(width, columnsWidth + form.ClientSize.Width - table.ClientSize.Width);
        }
        var available = Screen.FromControl(form).WorkingArea;
        form.ClientSize = new Size(Math.Min(width, available.Width - 64), form.ClientSize.Height);
        form.PerformLayout();
    }

    /// <summary>Hält den Inhalt bei normaler Schrift sichtbar und lässt bei großem Inhalt Platz für Scrollleisten.</summary>
    internal static void FitHeight(Form form, Control content, int chromeHeight)
    {
        var available = Screen.FromControl(form).WorkingArea;
        int height = Math.Max(form.ClientSize.Height, content.GetPreferredSize(Size.Empty).Height + chromeHeight);
        int maximumHeight = Math.Min(available.Height - 100, 800 * form.DeviceDpi / 96);
        form.ClientSize = new Size(form.ClientSize.Width, Math.Min(height, maximumHeight));
    }
}
