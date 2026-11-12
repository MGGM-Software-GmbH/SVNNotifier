using Microsoft.UI.Xaml.Data;

namespace CHD.SVN_Notifier;

public sealed class StatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is SvnStatus status ? Text(status) : string.Empty;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new InvalidOperationException("Status wird ausschließlich vom Repository-Modell gesetzt.");

    internal static string Text(SvnStatus status) => Loc.SvnStatusText(status, true);
}

public sealed class StatusIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        SvnStatus.Error => "\uE783", SvnStatus.NeedUpdate => "\uE896", SvnStatus.NeedUpdate_Modified => "\uE70F",
        SvnStatus.UpToDate => "\uE73E", SvnStatus.UpToDate_Modified => "\uE70F", _ => "\uE9CE"
    };
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new InvalidOperationException("Statussymbol wird ausschließlich vom Repository-Modell gesetzt.");
}
