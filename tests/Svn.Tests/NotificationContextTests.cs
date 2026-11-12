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
    }
}
