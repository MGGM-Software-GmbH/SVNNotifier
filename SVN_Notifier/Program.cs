using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CHD.SVN_Notifier;

internal static class Program
{
    internal static NotificationService? Notifications { get; private set; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string title, uint flags);

    /// <summary>Trennt Installer und normalen Start vor der Initialisierung von XAML und Benachrichtigungen.</summary>
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--apply-update")
        {
            ApplicationUpdatePlan? plan = null;
            try
            {
                plan = JsonSerializer.Deserialize<ApplicationUpdatePlan>(File.ReadAllText(args[1]));
                Config.Language = plan?.Language ?? 0;
                ApplicationUpdateInstaller.Apply(args[1]);
                return 0;
            }
            catch (Exception exception)
            {
                if (plan is null || !OriginalRunning(plan)) MessageBox(IntPtr.Zero, Loc.AppUpdate_Error(exception.Message), Loc.AppUpdate_Title, 0x10);
                return 1;
            }
        }
        if (args is ["start"])
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Environment.ProcessPath)! });
            return 0;
        }
        try
        {
            if (new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("Bitte SVN Notifier ohne Administratorrechte starten. Windows-App-Benachrichtigungen unterstützen keine erhöhten Prozesse.");
            // Die Einzel-EXE entpackt native SDK-Dateien neben die verwalteten Assemblies.
            Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
            Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY_PID", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Notifications = NotificationService.Prepare(args);
            if (Notifications.Redirected) return 0;
            const string name = "SVN_Notifier.ini";
            string config = Notifications.InitialConfiguration ?? (File.Exists(name) ? Path.GetFullPath(name) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name));
            Config.Init(config);
            if (args.Length == 4 && args[0] == "--update-cleanup" && int.TryParse(args[2], out int helper) && long.TryParse(args[3], out long began))
                _ = CleanupAsync(args[1], helper, began);
            StartXaml(args);
            return 0;
        }
        catch (Exception exception)
        {
            string message = exception.Message;
            if (!string.IsNullOrEmpty(Config.logFileName))
            {
                try { Config.WriteLog("FatalError", exception.ToString()); }
                catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { message += Environment.NewLine + logError.Message; }
            }
            MessageBox(IntPtr.Zero, message, "SVN Notifier", 0x10);
            return 1;
        }
        finally
        {
            // Auch ein Fehler im Nachrichtenloop darf eigene Reservierungen nicht ohne Prozessende zurücklassen.
            SvnTools.StopAsync().GetAwaiter().GetResult();
            Notifications?.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartXaml(string[] args)
    {
        Microsoft.UI.Xaml.Application.Start(initialization =>
        {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            try { _ = new App(args); }
            catch (Exception exception)
            {
                string details = exception.ToString();
                foreach (System.Collections.DictionaryEntry entry in exception.Data)
                    details += Environment.NewLine + entry.Key + ": " + entry.Value;
                Config.WriteLog("Startup", details);
                throw;
            }
        });
    }

    private static bool OriginalRunning(ApplicationUpdatePlan plan)
    {
        try { using var process = Process.GetProcessById(plan.ParentId); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks; }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private static async Task CleanupAsync(string directory, int helper, long began)
    {
        try { await ApplicationUpdateInstaller.CleanupAsync(directory, helper, began); }
        catch (Exception exception) { Config.WriteLog("ApplicationUpdateCleanup", exception.ToString()); }
    }
}
