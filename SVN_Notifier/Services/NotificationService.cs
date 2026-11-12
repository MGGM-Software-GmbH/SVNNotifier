using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace CHD.SVN_Notifier;

/// <summary>Registriert nur den Produktstart und leitet Klicks vor dem Start eines weiteren Prüfers zum Ursprung.</summary>
internal sealed class NotificationService : IDisposable
{
    internal static NotificationService? Current { get; private set; }
    private readonly AppNotificationManager manager = AppNotificationManager.Default;
    private readonly NotificationContextStore store;
    private readonly CancellationTokenSource cancellation = new();
    private readonly HashSet<string> delivered = new(StringComparer.Ordinal);
    private readonly HashSet<string> removed = new(StringComparer.Ordinal);
    private readonly Queue<string> pending = new();
    private readonly object gate = new();
    private DispatcherQueue? dispatcher;
    private Func<NotificationContext, Task>? activate;
    private string? configuration;
    private Task? listener;
    private NotificationContext? initial;
    private Exception? startupFailure;
    internal bool Redirected { get; private set; }
    internal string? InitialConfiguration => initial?.Configuration;

    private NotificationService()
    {
        if (!AppNotificationManager.IsSupported()) throw new PlatformNotSupportedException("Windows-App-Benachrichtigungen sind in diesem Prozess nicht verfügbar.");
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MGGM", "SVNNotifier", "Notifications", NotificationContextStore.Identity(Environment.ProcessPath!));
        store = new NotificationContextStore(directory);
        manager.NotificationInvoked += OnInvoked;
        try { manager.Register(); }
        catch { manager.NotificationInvoked -= OnInvoked; throw; }
    }

    /// <summary>Aktivierung und Weiterleitung sind vor Konfiguration, Coordinator und XAML entschieden.</summary>
    internal static NotificationService Prepare(string[] arguments)
    {
        var service = new NotificationService();
        try
        {
            string? id = arguments is ["--notification", var token] ? token : null;
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activation.Kind == ExtendedActivationKind.AppNotification)
                id = ParseId(((AppNotificationActivatedEventArgs)activation.Data).Argument);
            if (id is not null)
            {
                var context = service.store.Read(id);
                service.Redirected = service.ForwardAsync(context).GetAwaiter().GetResult();
                if (!service.Redirected)
                {
                    if (!File.Exists(context.Configuration) || !Directory.Exists(context.WorkingDirectory))
                        throw new FileNotFoundException("Der ursprüngliche Konfigurationskontext ist nicht mehr verfügbar.");
                    Environment.CurrentDirectory = context.WorkingDirectory;
                    service.initial = context;
                }
                lock (service.gate) service.delivered.Add(id);
            }
            Current = service;
            return service;
        }
        catch { service.Dispose(); throw; }
    }

    private static string ParseId(string argument)
    {
        const string prefix = "context=";
        if (!argument.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(argument[prefix.Length..], "N", out _))
            throw new InvalidDataException("Ungültiges Benachrichtigungsziel.");
        return argument[prefix.Length..];
    }

    internal void Attach(DispatcherQueue dispatcher, Func<NotificationContext, Task> activate)
    {
        this.dispatcher = dispatcher; this.activate = activate; configuration = Path.GetFullPath(Config.iniFileName);
        using var process = Process.GetCurrentProcess();
        listener = ListenAsync(NotificationContextStore.PipeName(process.Id, process.StartTime.ToUniversalTime().Ticks));
        if (initial is not null) Dispatch(initial);
        string[] queued;
        lock (gate) { queued = pending.ToArray(); pending.Clear(); }
        lock (gate) delivered.Clear();
        foreach (string id in queued.Distinct().Where(id => id != initial?.Id)) _ = HandleAsync(id);
        if (startupFailure is not null) Report(startupFailure);
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        try
        {
            string id = ParseId(args.Argument);
            lock (gate)
            {
                if (dispatcher is null) { pending.Enqueue(id); return; }
            }
            _ = HandleAsync(id);
        }
        catch (Exception exception) { Report(exception); }
    }

    private async Task HandleAsync(string id)
    {
        try
        {
            lock (gate) if (!delivered.Add(id)) return;
            var context = store.Read(id);
            if (await ForwardAsync(context)) return;
            if (!string.Equals(context.Configuration, configuration, StringComparison.OrdinalIgnoreCase))
            {
                StartContext(context); return;
            }
            Dispatch(context);
        }
        catch (Exception exception) { Report(exception); }
        finally { lock (gate) delivered.Remove(id); }
    }

    private async Task<bool> ForwardAsync(NotificationContext context)
    {
        using var current = Process.GetCurrentProcess();
        if (context.ProcessId == current.Id && context.ProcessStartTicks == current.StartTime.ToUniversalTime().Ticks) return false;
        if (!NotificationContextStore.IsOriginRunning(context)) return false;
        await using var pipe = new NamedPipeClientStream(".", NotificationContextStore.PipeName(context.ProcessId, context.ProcessStartTicks), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pipe.ConnectAsync(timeout.Token);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await writer.WriteLineAsync(context.Id.AsMemory(), timeout.Token);
        if (await reader.ReadLineAsync(timeout.Token) != "OK") throw new IOException("Die Benachrichtigungsaktivierung wurde nicht bestätigt.");
        return true;
    }

    private async Task ListenAsync(string name)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation.Token);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                // Keine frei wählbaren Pfade oder Befehle über die Pipe entgegennehmen.
                var token = new char[33];
                int count = await reader.ReadBlockAsync(token.AsMemory(), timeout.Token);
                if (count != 33 || token[32] != '\n') throw new InvalidDataException("Ungültige Aktivierungsnachricht.");
                var context = store.Read(new string(token, 0, 32));
                if (!string.Equals(context.Configuration, configuration, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Benachrichtigung gehört zu einer anderen Konfiguration.");
                Dispatch(context);
                await writer.WriteLineAsync("OK".AsMemory(), timeout.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { break; }
            catch (Exception exception) { Report(exception); }
        }
    }

    private static void StartContext(NotificationContext context)
    {
        if (!File.Exists(context.Executable) || !Directory.Exists(context.WorkingDirectory)) throw new FileNotFoundException("Der ursprüngliche portable Programmordner ist nicht mehr verfügbar.");
        var start = new ProcessStartInfo(context.Executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = context.WorkingDirectory };
        start.ArgumentList.Add("--notification"); start.ArgumentList.Add(context.Id);
        using var process = Process.Start(start) ?? throw new IOException("Der Benachrichtigungskontext konnte nicht geöffnet werden.");
    }

    private void Dispatch(NotificationContext context)
    {
        if (dispatcher is null || !dispatcher.TryEnqueue(async () =>
        {
            try { await activate!(context); }
            catch (Exception exception) { Report(exception); }
        })) throw new InvalidOperationException("Der Oberflächenthread ist nicht verfügbar.");
    }

    internal void Show(NotificationRequest request)
    {
        var context = store.Create(request, Environment.ProcessPath!, Config.iniFileName);
        var notification = new AppNotificationBuilder().AddArgument("context", context.Id).AddText(request.Title).AddText(request.Text).BuildNotification();
        notification.Tag = context.Id; notification.Group = NotificationContextStore.Identity(context.Configuration);
        if (manager.Setting != AppNotificationSetting.Enabled)
            Config.WriteLog("Notifications", "Windows-Benachrichtigungen sind deaktiviert: " + manager.Setting);
        manager.Show(notification);
        if (notification.Id == 0) throw new IOException("Windows hat die Benachrichtigung nicht angenommen.");
    }

    internal async void Reconcile(IEnumerable<SvnItem> folders)
    {
        try
        {
            var snapshot = folders.ToArray();
            foreach (var context in store.ReadConfiguration(Config.iniFileName))
            {
                if (context.Kind != NotificationKind.IncomingChanges || removed.Contains(context.Id)) continue;
                if (context.Paths.Any(path => snapshot.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase) && item.Status is not (SvnStatus.UpToDate or SvnStatus.UpToDate_Modified)))) continue;
                await manager.RemoveByTagAndGroupAsync(context.Id, NotificationContextStore.Identity(context.Configuration));
                removed.Add(context.Id);
            }
        }
        catch (Exception exception) { Report(exception); }
    }

    private void Report(Exception exception)
    {
        if (configuration is null) { startupFailure = exception; return; }
        Config.WriteLog("Notifications", exception.ToString());
        if (dispatcher is not null) dispatcher.TryEnqueue(() => Failure?.Invoke(exception.Message));
    }

    internal event Action<string>? Failure;

    public void Dispose()
    {
        if (ReferenceEquals(Current, this)) Current = null;
        cancellation.Cancel();
        manager.NotificationInvoked -= OnInvoked;
        manager.Unregister();
    }
}
