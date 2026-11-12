using System.Reflection;
using CHD.SVN_Notifier;

internal static class ConfigurationTransferTests
{
    /// <summary>Prüft Import und Export einschließlich fehlerhafter Eingaben, Dateisperre und unverändertem aktivem Zustand.</summary>
    internal static void Run(string root, Action<bool, string> assert)
    {
        var fields = typeof(Config).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => !field.IsLiteral && !field.IsInitOnly).ToDictionary(field => field, field => field.GetValue(null));
        string directory = Path.Combine(root, "configuration-transfer");
        Directory.CreateDirectory(directory);
        string active = Path.Combine(directory, "active.ini");
        string repository = Path.Combine(directory, "Projekt ä");
        try
        {
            File.WriteAllText(active, "[Settings]\nVersion=1.9\nLanguage=1\nDefaultActiveStatusUpdateInterval=17\n[Folders]\nFolder1=" + repository + "|21|34|False|0|True\n");
            Config.Init(active);
            Config.DefaultActiveStatusUpdateInterval = 713;
            byte[] original = File.ReadAllBytes(active);
            void Reject(string source, Type expected)
            {
                Exception? failure = null;
                try { Config.Import(source); } catch (Exception exception) { failure = exception; }
                assert(failure is not null && expected.IsInstanceOfType(failure), "Ungültiger Import meldet seinen konkreten Fehler: " + Path.GetFileName(source) + "; erhalten: " + failure?.GetType().Name + ": " + failure?.Message);
                assert(File.ReadAllBytes(active).SequenceEqual(original) && Config.iniFileName == active &&
                    Config.DefaultActiveStatusUpdateInterval == 713 && Config.Language == 1 && Config.ReadSvnFolders().Single().Path == repository,
                    "Abgewiesener Import erhält Datei, nicht gespeicherte Optionen, INI-Cache und Repository-Zuordnung");
                assert(!Directory.EnumerateFiles(directory, ".svnnotifier-import-*.ini").Any(), "Abgewiesener Import hinterlässt keine Importdatei");
            }
            Reject(Path.Combine(directory, "missing.ini"), typeof(FileNotFoundException));
            string future = Path.Combine(directory, "future.ini");
            File.WriteAllText(future, "[Settings]\nVersion=999.0\n");
            Reject(future, typeof(ApplicationException));
            string malformed = Path.Combine(directory, "malformed.ini");
            File.WriteAllText(malformed, "[Settings]\nVersion=1.9\nLanguage=0\n[Folders]\nFolder1=" + repository + "|1|2|False|invalid|True\n");
            Reject(malformed, typeof(FormatException));
            string valid = Path.Combine(directory, "valid.ini");
            string sourceText = "[Settings]\nVersion=1.8\nLanguage=0\nDefaultActiveStatusUpdateInterval=31\nShowBalloonInterval=23000\n[Folders]\nFolder1=" + repository + "|21|34|False|0\n";
            File.WriteAllText(valid, sourceText);
            using (var locked = new FileStream(active, FileMode.Open, FileAccess.Read, FileShare.Read))
                Reject(valid, typeof(UnauthorizedAccessException));
            Config.Import(active);
            assert(File.ReadAllBytes(active).SequenceEqual(original) && Config.DefaultActiveStatusUpdateInterval == 713,
                "Import der aktiven Datei verändert weder Datei noch laufende Optionen");
            Config.Import(valid);
            assert(Config.iniFileName == active && Config.Language == 0 && Config.DefaultActiveStatusUpdateInterval == 31 &&
                Config.ShowBalloonInterval == 23000 && !Config.ReadSvnFolders().Single().AutoUpdate,
                "Gültiger Import übernimmt migrierte Optionen und Repository-Zuordnungen in den aktiven Kontext");
            assert(File.ReadAllText(valid) == sourceText, "Import und Migration verändern die ausgewählte Quelldatei nicht");
            string exported = Path.Combine(directory, "exported.ini");
            File.WriteAllText(exported, "Isoliertes vorhandenes Exportziel.");
            Config.Export(exported);
            assert(File.ReadAllBytes(exported).SequenceEqual(File.ReadAllBytes(active)), "Export erhält sämtliche gespeicherten INI-Daten bytegenau");
            bool exportRejected = false;
            try { Config.Export(directory); } catch (IOException) { exportRejected = true; }
            assert(exportRejected && Config.iniFileName == active && File.ReadAllBytes(exported).SequenceEqual(File.ReadAllBytes(active)),
                "Nicht beschreibbares Exportziel meldet einen Fehler und erhält aktive Konfiguration sowie vorhandenen Export");
            Config.DefaultActiveStatusUpdateInterval = 19; Config.SaveSettings();
            assert(File.ReadAllText(active).Contains("DefaultActiveStatusUpdateInterval=19", StringComparison.Ordinal) &&
                !Directory.EnumerateFiles(directory, ".svnnotifier-import-*.ini").Any(),
                "Speichern nach Import verwendet die aktive Datei und keinen temporären INI-Cache");
        }
        finally
        {
            foreach (var field in fields) field.Key.SetValue(null, field.Value);
        }
    }
}
