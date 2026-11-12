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
            coordinator.Folders.Clear();
            string numberedRoot = Path.Combine(root, "Änderungen und Dokumentation");
            string directoryTen = Path.Combine(numberedRoot, "Projekt 10");
            string directoryTwo = Path.Combine(numberedRoot, "Projekt 2");
            string document = Path.Combine(numberedRoot, "Projekt 2", "Datei ä.txt");
            Directory.CreateDirectory(directoryTen);
            Directory.CreateDirectory(directoryTwo);
            File.WriteAllText(document, "Isolierter Datei-Eintrag.");
            coordinator.Add(directoryTen, PathType.Directory);
            coordinator.Add(directoryTwo, PathType.Directory);
            coordinator.Add(document, PathType.File);
            Check(coordinator.Folders.IndexOf(coordinator.Folders.Single(folder => folder.Path == directoryTwo)) <
                coordinator.Folders.IndexOf(coordinator.Folders.Single(folder => folder.Path == directoryTen)),
                "Natürliche Pfadsortierung stellt Projekt 2 vor Projekt 10.");
            Check(coordinator.Folders.Single(folder => folder.Path == document).pathType == PathType.File &&
                Config.ReadSvnFolders().Single(folder => folder.Path == document).pathType == PathType.File,
                "Datei- und Ordnereinträge behalten ihren Typ beim Speichern.");
            Check(coordinator.Folders.All(folder => folder.VisiblePath.StartsWith("…", StringComparison.Ordinal)) &&
                coordinator.Folders.Select(folder => folder.VisiblePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3,
                "Gemeinsame Unicode-Pfade werden gekürzt und bleiben sichtbar unterscheidbar.");
            coordinator.Add(document.ToUpperInvariant(), PathType.File);
            Check(coordinator.Folders.Count == 3, "Erneutes Hinzufügen desselben Windows-Pfades erzeugt keinen doppelten Eintrag.");
            var removed = coordinator.Folders.Single(folder => folder.Path == document);
            coordinator.Folders.Remove(removed);
            coordinator.Save();
            using var reopened = new ApplicationCoordinator(action => action());
            Check(reopened.Folders.Count == 2 && reopened.Folders.All(folder => folder.Path != document),
                "Ein entfernter Eintrag bleibt nach erneutem Laden entfernt.");
            string longRoot = Path.Combine(root, new string('a', 90), new string('b', 90), "Arbeitskopie mit langem Pfad");
            Directory.CreateDirectory(Path.Combine(longRoot, ".svn"));
            File.WriteAllText(Path.Combine(longRoot, ".svn", "wc.db"), "Isolierte Testmarkierung.");
            Check(longRoot.Length > 260, "Die lange Arbeitskopie überschreitet tatsächlich die Win32-Pfadgrenze.");
            using (var longLease = WorkingCopyLease.TryAcquire(longRoot, "long-path-test", out _, out string reason))
            {
                Check(longLease is not null, "Lange Arbeitskopie kann reserviert werden: " + reason);
                Check(WorkingCopyLease.TryAcquire(longRoot, "concurrent-long-path-test", out _, out _) is null,
                    "Auch lange Arbeitskopien bleiben gegen gleichzeitige eigene Aktionen geschützt.");
            }
            using (var acquiredAgain = WorkingCopyLease.TryAcquire(longRoot, "long-path-reopen", out _, out _))
                Check(acquiredAgain is not null, "Lange Arbeitskopie wird nach Freigabe wieder reservierbar.");
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
