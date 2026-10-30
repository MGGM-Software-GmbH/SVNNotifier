using System.Drawing.Drawing2D;

namespace CHD.SVN_Notifier;

internal enum UiIcon
{
    Folder, AddFolder, AddFile, Remove, Update, UpdateAll, Commit, Log, Changes,
    Settings, Properties, Import, Export, Check, Info, Close, Window, Error,
    Unknown, Current, Modified, Incoming, IncomingModified
}

/// <summary>Zeichnet die Anwendungssymbole unabhängig von installierten Symbolschriften und Bildpaketen.</summary>
internal static class UiIcons
{
    internal static Bitmap Create(UiIcon icon, int size, Color color)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.ScaleTransform(size / 24f, size / 24f);
        using var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(color);
        using var surface = new SolidBrush(ApplicationTheme.Surface);

        void Lines(params PointF[] points) => graphics.DrawLines(pen, points);
        void Line(float left, float top, float right, float bottom) => graphics.DrawLine(pen, left, top, right, bottom);
        void Plus() { Line(18, 12, 18, 20); Line(14, 16, 22, 16); }
        void Arrow(bool down) { Line(12, down ? 5 : 19, 12, down ? 19 : 5); Lines(new(7, down ? 14 : 10), new(12, down ? 19 : 5), new(17, down ? 14 : 10)); }

        switch (icon)
        {
            case UiIcon.Folder:
            case UiIcon.AddFolder:
                Lines(new(3, 18), new(3, 6), new(9, 6), new(11, 8), new(20, 8), new(20, 10));
                Lines(new(3, 18), new(6, 11), new(21, 11), new(19, 18), new(3, 18));
                if (icon == UiIcon.AddFolder) { graphics.FillRectangle(surface, 14, 12, 9, 10); Plus(); }
                break;
            case UiIcon.AddFile:
            case UiIcon.Properties:
            case UiIcon.Log:
            case UiIcon.Changes:
                Lines(new(5, 20), new(5, 4), new(14, 4), new(19, 9), new(19, 20), new(5, 20));
                Lines(new(14, 4), new(14, 9), new(19, 9));
                if (icon == UiIcon.AddFile) { graphics.FillRectangle(surface, 13, 12, 10, 10); Plus(); }
                else { Line(8, 12, 15, 12); Line(8, 16, icon == UiIcon.Changes ? 12 : 15, 16); }
                break;
            case UiIcon.Update:
            case UiIcon.UpdateAll:
                Arrow(true);
                Line(5, 21, 19, 21);
                if (icon == UiIcon.UpdateAll) { Line(3, 7, 3, 15); Line(21, 7, 21, 15); }
                break;
            case UiIcon.Commit:
                Arrow(false); Line(5, 21, 19, 21);
                break;
            case UiIcon.Import:
            case UiIcon.Export:
                Lines(new(9, 4), new(4, 4), new(4, 20), new(9, 20));
                Line(9, 12, 21, 12);
                var arrowTip = icon == UiIcon.Import ? 9 : 21;
                var arrowBase = icon == UiIcon.Import ? 13 : 17;
                Lines(new(arrowBase, 8), new(arrowTip, 12), new(arrowBase, 16));
                break;
            case UiIcon.Settings:
                for (var index = 0; index < 8; index++)
                {
                    var angle = index * Math.PI / 4;
                    Line(12 + (float)Math.Cos(angle) * 7, 12 + (float)Math.Sin(angle) * 7,
                        12 + (float)Math.Cos(angle) * 10, 12 + (float)Math.Sin(angle) * 10);
                }
                graphics.DrawEllipse(pen, 5, 5, 14, 14); graphics.DrawEllipse(pen, 9, 9, 6, 6);
                break;
            case UiIcon.Check:
                graphics.DrawArc(pen, 4, 4, 16, 16, 45, 285);
                Lines(new(15, 3), new(20, 4), new(19, 9));
                break;
            case UiIcon.Window:
                graphics.DrawRectangle(pen, 3, 5, 18, 15); Line(3, 9, 21, 9);
                break;
            case UiIcon.Close:
                Line(6, 6, 18, 18); Line(18, 6, 6, 18);
                break;
            case UiIcon.Remove:
                Line(5, 7, 19, 7); Line(9, 4, 15, 4);
                Lines(new(7, 7), new(8, 20), new(16, 20), new(17, 7));
                Line(10, 10, 10, 17); Line(14, 10, 14, 17);
                break;
            default:
                graphics.DrawEllipse(pen, 3, 3, 18, 18);
                if (icon == UiIcon.Current)
                    Lines(new(7, 12), new(10, 15), new(17, 8));
                else if (icon is UiIcon.Incoming or UiIcon.IncomingModified)
                { Line(12, 6, 12, 17); Lines(new(8, 13), new(12, 17), new(16, 13)); }
                else if (icon == UiIcon.Modified)
                    Lines(new(8, 16), new(9, 12), new(15, 6), new(18, 9), new(12, 15), new(8, 16));
                else if (icon == UiIcon.Error)
                { Line(12, 7, 12, 12); graphics.FillEllipse(fill, 11, 15, 2, 2); }
                else if (icon == UiIcon.Unknown)
                { graphics.DrawArc(pen, 9, 7, 6, 6, 180, 250); Line(12, 12, 12, 14); Line(12, 17, 12, 17.1f); }
                else
                { Line(12, 11, 12, 17); Line(12, 7, 12, 7.1f); }
                if (icon == UiIcon.IncomingModified)
                { using var brush = new SolidBrush(ApplicationTheme.Warning); graphics.FillEllipse(brush, 16, 15, 7, 7); }
                break;
        }
        return bitmap;
    }
}
