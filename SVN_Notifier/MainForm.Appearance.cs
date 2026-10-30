namespace CHD.SVN_Notifier;

public partial class MainForm
{
    private readonly Dictionary<UiIcon, Image> appearanceImages = new();

    /// <summary>Modernisiert die vorhandenen Bedienelemente; Aktionen und Ereignisbindungen bleiben erhalten.</summary>
    private void InitializeAppearance()
    {
        ApplicationTheme.Apply(this);
        MaximizeBox = true;
        MinimumSize = new Size(760, 300);
        MainMenu.Padding = new Padding(8, 4, 8, 4);
        MenuToolStrip.GripStyle = ToolStripGripStyle.Hidden;
        MenuToolStrip.Padding = new Padding(8, 6, 8, 6);
        MainStatusStrip.Padding = new Padding(12, 4, 12, 4);
        StatusLabel.ForeColor = ApplicationTheme.Muted;
        ItemListView.ColumnHeadersVisible = true;
        ItemListView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        ItemListView.ColumnHeadersDefaultCellStyle.BackColor = ApplicationTheme.Canvas;
        ItemListView.ColumnHeadersDefaultCellStyle.ForeColor = ApplicationTheme.Muted;
        ItemListView.ColumnHeadersDefaultCellStyle.SelectionBackColor = ApplicationTheme.Canvas;
        ItemListView.ColumnHeadersDefaultCellStyle.SelectionForeColor = ApplicationTheme.Muted;
        ItemListView.DefaultCellStyle.BackColor = ApplicationTheme.Surface;
        ItemListView.DefaultCellStyle.ForeColor = ApplicationTheme.Text;
        ItemListView.DefaultCellStyle.SelectionBackColor = ApplicationTheme.Selection;
        ItemListView.DefaultCellStyle.SelectionForeColor = SystemInformation.HighContrast ? SystemColors.HighlightText : ApplicationTheme.Text;
        ItemListView.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        ItemListView.GridColor = ApplicationTheme.Border;
        ColumnEnabled.HeaderText = Loc.Col_Enabled;
        ColumnEnabled.ToolTipText = Loc.Col_Enabled;
        ColumnAutoUpdate.HeaderText = Loc.Col_AutoUpdate;
        ColumnAutoUpdate.ToolTipText = Loc.Tooltip_AutoUpdate;
        ColumnAutoUpdate.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        PathColumn.MinimumWidth = 180;
        ImageColumn.DefaultCellStyle.Padding = Padding.Empty;
        ImageColumn.ImageLayout = DataGridViewImageCellLayout.Normal;
        foreach (var column in new DataGridViewColumn[] { ColumnEnabled, ImageColumn, StatusColumn, ColumnAutoUpdate })
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
        foreach (var strip in new ToolStrip[] { MainMenu, MenuToolStrip, MainStatusStrip, ItemMenu, TrayMenu })
        {
            strip.Renderer = new ApplicationTheme.StripRenderer();
            strip.ForeColor = ApplicationTheme.Text;
        }
        foreach (ToolStripItem item in MenuToolStrip.Items)
        {
            item.Margin = new Padding(1, 0, 1, 0);
            item.Padding = item is ToolStripSeparator ? new Padding(3, 0, 3, 0) : new Padding(6, 5, 6, 5);
            item.ImageScaling = ToolStripItemImageScaling.None;
            item.Overflow = ToolStripItemOverflow.AsNeeded;
            item.AccessibleName = item.Text;
        }
        AddFolderButton.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        AddFolderButton.Overflow = ToolStripItemOverflow.Never;
        AddFileButton.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        RemoteButton.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        // Das native Überlaufmenü zeigt auch die seltener verwendeten Aktionen mit lesbarer Beschriftung.
        var overflowLayout = (FlowLayoutSettings)MenuToolStrip.OverflowButton.DropDown.LayoutSettings;
        overflowLayout.FlowDirection = FlowDirection.TopDown;
        overflowLayout.WrapContents = false;
        RefreshAppearanceImages();
        DpiChanged += (sender, eventArgs) => RefreshAppearanceImages();
        FormClosed += (sender, eventArgs) =>
        {
            foreach (var image in appearanceImages.Values) image.Dispose();
            appearanceImages.Clear();
        };
    }

    /// <summary>Rendert scharfe Symbole passend zum aktuellen Monitor statt kleine Bitmaps hochzuskalieren.</summary>
    private void RefreshAppearanceImages()
    {
        var previousImages = appearanceImages.Values.ToArray();
        appearanceImages.Clear();
        int size = (int)Math.Round(20 * DeviceDpi / 96f);
        void Assign(UiIcon icon, params ToolStripItem[] items)
        {
            foreach (var item in items)
            {
                item.Image = AppearanceIcon(icon);
                item.ImageScaling = ToolStripItemImageScaling.None;
            }
        }
        Assign(UiIcon.AddFolder, AddFolderButton, AddFolderMenuItem);
        Assign(UiIcon.AddFile, AddFileButton, AddFileMenuItem);
        Assign(UiIcon.Remove, RemoteButton, RemoveMenuItem);
        Assign(UiIcon.Properties, PropertiesButton, PropertiesMenuItem);
        Assign(UiIcon.Log, LogButton, LogMenuItem);
        Assign(UiIcon.Folder, OpenButton, OpenMenuItem);
        Assign(UiIcon.Commit, CommitButton, CommitMenuItem);
        Assign(UiIcon.Update, UpdateButton, UpdateMenuItem);
        Assign(UiIcon.Changes, ChangeLogButton, ChangeLogMenuItem);
        Assign(UiIcon.UpdateAll, UpdateAllMenuItem, TrayUpdateAllMenuItem);
        Assign(UiIcon.Check, CheckNowMenuItem, checkApplicationUpdatesMenuItem);
        Assign(UiIcon.Settings, SettingsMenuItem);
        Assign(UiIcon.Import, ImportConfigurationMenuItem);
        Assign(UiIcon.Export, ExportConfigurationMenuItem);
        Assign(UiIcon.Info, AboutMenuItem);
        Assign(UiIcon.Close, ExitMenuItem, TrayExitMenuItem);
        Assign(UiIcon.Window, TrayShowProgramMenuItem);
        Assign(UiIcon.Error, WarningStatusLabel);
        foreach (var icon in new[] { UiIcon.Unknown, UiIcon.Current, UiIcon.Incoming, UiIcon.Modified, UiIcon.IncomingModified, UiIcon.Error })
            AppearanceIcon(icon);
        int rowHeight = Math.Max(size + 14, ItemListView.Font.Height + 16);
        ItemListView.RowTemplate.Height = rowHeight;
        foreach (DataGridViewRow row in ItemListView.Rows) row.Height = rowHeight;
        ItemListView.ColumnHeadersHeight = Math.Max(size + 14, ItemListView.Font.Height + 16);
        foreach (var image in previousImages) image.Dispose();
        ItemListView.Invalidate();
    }

    /// <summary>Teilt fertig gezeichnete Symbole zwischen Zellen und Menüs und vermeidet GDI-Objekte je Zeichenvorgang.</summary>
    private Image AppearanceIcon(UiIcon icon)
    {
        if (!appearanceImages.TryGetValue(icon, out var image))
        {
            var color = icon switch
            {
                UiIcon.Current => ApplicationTheme.Success,
                UiIcon.Incoming or UiIcon.IncomingModified or UiIcon.Modified => ApplicationTheme.Warning,
                UiIcon.Error => ApplicationTheme.Danger,
                UiIcon.Unknown => ApplicationTheme.Muted,
                _ => ApplicationTheme.Accent
            };
            image = UiIcons.Create(icon, (int)Math.Round(20 * DeviceDpi / 96f), color);
            appearanceImages.Add(icon, image);
        }
        return image;
    }

    /// <summary>Verbindet Statusfarbe und Symbol, ohne lokale Änderungen oder Fehler gleichzusetzen.</summary>
    private Image StatusImage(SvnItem item)
    {
        var icon = !item.Enabled ? UiIcon.Unknown : item.Status switch
        {
            SvnStatus.Error => UiIcon.Error,
            SvnStatus.UpToDate => UiIcon.Current,
            SvnStatus.UpToDate_Modified => UiIcon.Modified,
            SvnStatus.NeedUpdate => UiIcon.Incoming,
            SvnStatus.NeedUpdate_Modified => UiIcon.IncomingModified,
            _ => UiIcon.Unknown
        };
        return AppearanceIcon(icon);
    }
}
