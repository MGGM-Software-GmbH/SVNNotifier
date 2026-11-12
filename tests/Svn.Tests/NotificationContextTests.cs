using CHD.SVN_Notifier;

internal static class NotificationContextTests
{
    /// <summary>Prüft stabile Klickziele, Konfigurationstrennung und die Ablehnung fremder Payloads ohne Windows-Shell.</summary>
    internal static void Run(string root, Action<bool, string> assert)
    {
        var store = new NotificationContextStore(Path.Combine(root, "notification-contexts"));
        string firstPath = Path.Combine(root, "Projekt A");
        string secondPath = Path.Combine(root, "Projekt B");
        string firstConfig = Path.Combine(root, "first.ini");
        string secondConfig = Path.Combine(root, "second.ini");
        var first = store.Create(new(NotificationKind.IncomingChanges, new[] { firstPath }, "A", "Änderungen"), Environment.ProcessPath!, firstConfig);
        var second = store.Create(new(NotificationKind.AutoUpdateCompleted, new[] { secondPath }, "B", "Aktualisiert"), Environment.ProcessPath!, secondConfig);
        assert(first.Id != second.Id && store.Read(first.Id).Paths.Single() == firstPath, "Ältere Meldung behält ihr eigenes Repositoryziel");
        assert(store.Read(second.Id).Paths.Single() == secondPath, "Zweite Meldung erhält unabhängiges Repositoryziel");
        assert(store.ReadConfiguration(firstConfig).Single().Id == first.Id, "Benachrichtigungen verschiedener INI-Dateien bleiben getrennt");
        assert(NotificationContextStore.IsOriginRunning(first), "Lebender Ursprungsprozess wird erkannt");
        assert(!NotificationContextStore.IsOriginRunning(first with { ProcessStartTicks = 1 }), "Wiederverwendete Prozess-ID ersetzt nicht den Ursprung");
        foreach (string invalid in new[] { "../first.ini", "", "context=" + first.Id, firstPath, new string('a', 32) })
        {
            bool rejected = false;
            try { store.Read(invalid); }
            catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException) { rejected = true; }
            assert(rejected, "Fremde oder unbekannte Benachrichtigungspayload wird abgewiesen: " + invalid);
        }
        assert(NotificationContextStore.Identity(firstConfig) != NotificationContextStore.Identity(secondConfig), "Historiengruppen verschiedener Konfigurationen bleiben getrennt");
        CheckPortableMove(root, assert);
    }

    /// <summary>Neue Meldungen nach einem Ordnerwechsel verwenden ausschließlich den neuen portablen Kontext.</summary>
    private static void CheckPortableMove(string root, Action<bool, string> assert)
    {
        string originalDirectory = Path.Combine(root, "Portable Ursprung ä");
        string movedDirectory = Path.Combine(root, "Portable Ziel ß");
        Directory.CreateDirectory(originalDirectory);
        string originalExecutable = Path.Combine(originalDirectory, "SVN_Notifier.exe");
        string originalConfiguration = Path.Combine(originalDirectory, "SVN_Notifier.ini");
        File.WriteAllText(originalExecutable, "Isolierte Kontextfixture, keine ausführbare Datei");
        File.WriteAllText(originalConfiguration, "[Main]\n");
        string contextRoot = Path.Combine(root, "portable-contexts");
        var originalStore = new NotificationContextStore(Path.Combine(contextRoot, NotificationContextStore.Identity(originalExecutable)));
        var request = new NotificationRequest(NotificationKind.IncomingChanges, new[] { root }, "Projekt", "Änderungen");
        var original = originalStore.Create(request, originalExecutable, originalConfiguration);
        Directory.Move(originalDirectory, movedDirectory);
        string movedExecutable = Path.Combine(movedDirectory, "SVN_Notifier.exe");
        string movedConfiguration = Path.Combine(movedDirectory, "SVN_Notifier.ini");
        var movedStore = new NotificationContextStore(Path.Combine(contextRoot, NotificationContextStore.Identity(movedExecutable)));
        var moved = movedStore.Create(request, movedExecutable, movedConfiguration);
        var restored = movedStore.Read(moved.Id);
        assert(restored.Executable == movedExecutable && File.Exists(restored.Executable), "Neue Meldung verwendet nach Ordnerwechsel die vorhandene neue EXE");
        assert(restored.Configuration == movedConfiguration && File.Exists(restored.Configuration), "Neue Meldung verwendet nach Ordnerwechsel die neue portable INI");
        assert(!File.Exists(originalStore.Read(original.Id).Executable), "Alte Meldung verweist nach Ordnerwechsel ausdrücklich auf den ungültigen alten Pfad");
        bool rejected = false;
        try { movedStore.Read(original.Id); }
        catch (FileNotFoundException) { rejected = true; }
        assert(rejected, "Alte Meldungs-ID wird nicht als neuer portabler Kontext interpretiert");
    }
}
