using System.ComponentModel;
using CHD.SVN_Notifier;

internal static class CoordinatorTests
{
    /// <summary>Prüft den UI-freien Ablauf mit demselben isolierten SVN-Prozesstreiber wie die Koordinationstests.</summary>
    internal static async Task<int> RunAsync(string output, string executable)
    {
        string previousConfig = Config.iniFileName;
        string previousSvn = Config.SVNpath, previousTortoise = Config.TortoiseSVNpath;
        string root = Path.Combine(output, "coordinator-wc");
        Directory.CreateDirectory(Path.Combine(root, ".svn"));
        File.WriteAllText(Path.Combine(root, ".svn", "wc.db"), "Isolierte synthetische Arbeitskopie.");
        int checks = 0;
        void Check(bool result, string description)
        {
            if (!result) throw new InvalidOperationException(description);
            checks++;
        }
        try
        {
            Config.Init(Path.Combine(output, "coordinator.ini"));
            Config.SVNpath = Config.TortoiseSVNpath = executable;
            Config.DoPauseAfterApplicationStartup = false;
            Config.SaveSvnFolders(new BindingList<SvnItem> { new(root, PathType.Directory) });
            using var coordinator = new ApplicationCoordinator(action => action());
            var item = coordinator.Folders.Single();
            var notifications = new List<NotificationRequest>();
            coordinator.NotificationRequested += notifications.Add;
            Task polling = coordinator.PollAsync();
            Check(ReferenceEquals(polling, coordinator.PollAsync()), "Zwei Prüfauslöser verwenden denselben laufenden Auftrag.");
            await polling;
            Check(item.Status == SvnStatus.NeedUpdate && !item.AutoUpdate, "Eingehende Änderungen lassen Auto-Update ausgeschaltet.");
            Check(notifications.Count == 1 && notifications[0].Paths.SequenceEqual(new[] { root }), "Benachrichtigung enthält das tatsächliche Repository-Ziel.");
            await coordinator.PollAsync();
            Check(notifications.Count == 1, "Unveränderter Status erzeugt keine erneute Änderungsmeldung.");
            Check(WorkingCopyLease.TryAcquire(root, "status", out _, out _) is { } released && Release(released), "Prüfung gibt ihre Reservierung frei.");
            using (var occupied = WorkingCopyLease.TryAcquire(root, "external-test", out _, out _))
            {
                item.Status = SvnStatus.Unknown;
                coordinator.CheckNow(item);
                await coordinator.PollAsync();
                Check(item.Status == SvnStatus.Unknown && !item.RetryReady && notifications.Count == 1,
                    "Belegte Arbeitskopie wird ohne Erfolgsmeldung zurückgestellt.");
            }
            bool failed = false;
            try { await coordinator.PrepareInstallationAsync(() => throw new IOException("Isolierter Installerstartfehler")); }
            catch (IOException) { failed = true; }
            Check(failed && !coordinator.IsBusy, "Fehlgeschlagener Installerstart gibt den Coordinator wieder frei.");
            coordinator.Add(Path.Combine(root, "second"), PathType.Directory);
            Check(coordinator.Folders.Count == 2 && Config.ReadSvnFolders().Count == 2,
                "Hinzufügen speichert dieselben Repository-Objekte über den bestehenden INI-Vertrag.");
            return checks;
        }
        finally
        {
            Config.Init(previousConfig);
            Config.SVNpath = previousSvn;
            Config.TortoiseSVNpath = previousTortoise;
        }
    }

    private static bool Release(IDisposable reservation) { reservation.Dispose(); return true; }
}
