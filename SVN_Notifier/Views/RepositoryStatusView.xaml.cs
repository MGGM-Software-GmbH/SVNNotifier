using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CHD.SVN_Notifier;

/// <summary>Bindet Statusfarben als Theme-Ressourcen, damit Kontrast- und Themewechsel die Zeile mitnehmen.</summary>
public sealed partial class RepositoryStatusView : UserControl
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(nameof(Status), typeof(SvnStatus), typeof(RepositoryStatusView),
        new PropertyMetadata(SvnStatus.Unknown, (owner, _) => ((RepositoryStatusView)owner).UpdateState()));
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(RepositoryStatusView), new PropertyMetadata(""));

    public SvnStatus Status { get => (SvnStatus)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public RepositoryStatusView()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateState();
    }

    private void UpdateState()
    {
        if (!IsLoaded) return;
        Icon.Glyph = (string)new StatusIconConverter().Convert(Status, typeof(string), "", "");
        string state = Status switch
        {
            SvnStatus.Error => "Error",
            SvnStatus.NeedUpdate or SvnStatus.NeedUpdate_Modified => "Incoming",
            SvnStatus.UpToDate => "Synchronized",
            SvnStatus.UpToDate_Modified => "Modified",
            _ => "Unknown"
        };
        VisualStateManager.GoToState(this, state, false);
    }
}
