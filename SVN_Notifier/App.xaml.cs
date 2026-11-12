using Microsoft.UI.Xaml;

namespace CHD.SVN_Notifier;

public partial class App : Application
{
    private readonly string[] arguments;
    internal MainWindow? MainWindow { get; private set; }

    internal App(string[] arguments)
    {
        this.arguments = arguments;
        UnhandledException += (_, eventArgs) =>
        {
            string details = eventArgs.Exception.ToString();
            foreach (System.Collections.DictionaryEntry entry in eventArgs.Exception.Data)
                details += Environment.NewLine + entry.Key + ": " + entry.Value;
            Config.WriteLog("UnhandledException", details);
        };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
