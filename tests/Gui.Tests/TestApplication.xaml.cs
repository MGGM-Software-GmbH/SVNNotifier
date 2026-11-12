using Microsoft.UI.Xaml;

namespace GuiTests;

public sealed partial class TestApplication : Application
{
    public TestApplication()
    {
        DebugSettings.IsXamlResourceReferenceTracingEnabled = true;
        DebugSettings.XamlResourceReferenceFailed += (_, args) => Program.RecordDiagnostic(args.Message);
        DebugSettings.BindingFailed += (_, args) => Program.RecordDiagnostic(args.Message);
        InitializeComponent();
        UnhandledException += (_, args) => Program.RecordFailure(args.Exception);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Ein Hostfenster hält den XAML-Loop zwischen den beiden Sprachläufen am Leben.
        var lifetime = new Window { Content = new Microsoft.UI.Xaml.Controls.Grid() };
        lifetime.Activate(); lifetime.AppWindow.Hide();
        try { await Program.ExecuteAsync(); }
        finally { lifetime.Close(); Exit(); }
    }
}
