using System.Reflection;
using CHD;
using CHD.SVN_Notifier;

internal static class ConfigurationMigrationTests
{
    /// <summary>Prüft jede gespeicherte INI-Generation mit eigenen Dateien, ohne Benutzerkonfigurationen anzufassen.</summary>
    internal static void Run(string root, Action<bool, string> assert)
    {
        string configuration = Config.iniFileName;
        var fields = typeof(Config).GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => !field.IsLiteral).ToArray();
        var original = fields.ToDictionary(field => field, field => field.GetValue(null));
        string directory = Path.Combine(root, "configuration-migration");
        Directory.CreateDirectory(directory);
        try
        {
            string target = Path.Combine(directory, "Projekt ä");
            var cases = new[]
            {
                (Name: "unversioned-path", Version: "", Folder: target, Balloon: "ShowBallonInterval=17000", Expected: 17000),
                (Name: "unversioned-intervals", Version: "", Folder: target + ",21,34", Balloon: "ShowBallonInterval=17000", Expected: 17000),
                (Name: "unversioned-enabled", Version: "", Folder: target + ",21,34,True", Balloon: "ShowBallonInterval=17000", Expected: 17000),
                (Name: "format-1.5", Version: "1.5", Folder: target + ",21,34,False,0", Balloon: "ShowBallonInterval=17000", Expected: 17000),
                (Name: "format-1.7", Version: "1.7", Folder: target + "|21|34|False|0", Balloon: "ShowBallonInterval=17000", Expected: 17000),
                (Name: "format-1.8-zero", Version: "1.8", Folder: target + "|21|34|False|0", Balloon: "ShowBalloonInterval=0", Expected: 10000),
                (Name: "format-1.8-value", Version: "1.8", Folder: target + "|21|34|False|0", Balloon: "ShowBalloonInterval=23000", Expected: 23000),
                (Name: "format-1.9-zero", Version: "1.9", Folder: target + "|21|34|False|0|False", Balloon: "ShowBalloonInterval=0", Expected: 0)
            };
            foreach (var sample in cases)
            {
                string file = Path.Combine(directory, sample.Name + ".ini");
                File.WriteAllText(file, "[Settings]\nVersion=" + sample.Version + "\n" + sample.Balloon + "\n[Folders]\nFolder1=" + sample.Folder + "\n");
                Config.Init(file);
                var item = Config.ReadSvnFolders().Single();
                assert(item.Path == target, "Migration erhält das Repositoryziel: " + sample.Name);
                assert(Config.ShowBalloonInterval == sample.Expected, "Migration erhält die vereinbarte Dauersemantik: " + sample.Name);
                assert(new IniFile(file, false).ReadString("Settings", "Version") == "1.9", "Migration schreibt das aktuelle INI-Format: " + sample.Name);
                assert(item.AutoUpdate == (sample.Version == "1.9"), "Migration erhält Auto-Update-Opt-in: " + sample.Name);
                if (sample.Name == "unversioned-enabled") assert(!item.Enabled && item.ActiveStatusUpdateInterval == 21 && item.IdleStatusUpdateInterval == 34, "Migration erhält deaktivierte Prüfung und eigene Intervalle");
                if (sample.Version is "" or "1.5" or "1.7") assert(!File.ReadAllText(file).Contains("ShowBallonInterval="), "Migration entfernt den alten Schreibfehler: " + sample.Name);
            }
            foreach (string folder in new[] { target + ",21", target + ",21,34,False,0,True" })
            {
                string file = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".ini");
                File.WriteAllText(file, "[Settings]\n[Folders]\nFolder1=" + folder + "\n");
                bool rejected = false;
                try { Config.Init(file); }
                catch (ApplicationException) { rejected = true; }
                assert(rejected, "Ungültige alte Feldanzahl wird bei der Migration abgewiesen");
            }
            foreach (bool bundledClientExists in new[] { false, true })
            {
                string clients = Path.Combine(directory, bundledClientExists ? "clients-present" : "clients-absent");
                Directory.CreateDirectory(clients);
                string tortoise = Path.Combine(clients, "TortoiseProc.exe");
                string bundled = Path.Combine(clients, "svn.exe");
                string custom = Path.Combine(clients, "custom-svn.exe");
                File.WriteAllText(tortoise, "Isolierte Pfadfixture, nicht ausführbar.");
                if (bundledClientExists) File.WriteAllText(bundled, "Isolierte Pfadfixture, nicht ausführbar.");
                string file = Path.Combine(clients, "migration.ini");
                File.WriteAllText(file, "[Settings]\nVersion=1.7\nSVN_path=" + custom + "\nTortoiseSVN_path=" + tortoise + "\nShowBallonInterval=19000\n");
                Config.Init(file);
                assert(Config.SVNpath == (bundledClientExists ? bundled : custom), "Clientmigration berücksichtigt die tatsächliche Verfügbarkeit von Tortoise-SVN: " + bundledClientExists);
                assert(Config.ShowBalloonInterval == 19000, "Clientmigration erhält die gespeicherte Benachrichtigungsdauer");
            }
            string monitoredFile = Path.Combine(directory, "Überwachte Datei ß.txt");
            File.WriteAllText(monitoredFile, "Isolierte Typfixture.");
            string legacyFile = Path.Combine(directory, "unversioned-file.ini");
            File.WriteAllText(legacyFile, "[Settings]\n[Folders]\nFolder1=" + monitoredFile + "\n");
            Config.Init(legacyFile);
            var migratedFile = Config.ReadSvnFolders().Single();
            assert(migratedFile.pathType == PathType.File && migratedFile.Path == monitoredFile, "Unversionierte Dateikonfiguration behält den ermittelten Dateityp");
            assert(migratedFile.Enabled && !migratedFile.AutoUpdate && migratedFile.ActiveStatusUpdateInterval == -1 && migratedFile.IdleStatusUpdateInterval == -1, "Unversionierte Dateikonfiguration erhält Aktivierung und Intervallvererbung ohne Auto-Update");
            string future = Path.Combine(directory, "unknown-format.ini");
            File.WriteAllText(future, "[Settings]\nVersion=99.0\n");
            bool unknownRejected = false;
            try { Config.Init(future); }
            catch (ApplicationException) { unknownRejected = true; }
            assert(unknownRejected, "Unbekannte INI-Version wird ausdrücklich abgewiesen");
        }
        finally
        {
            Config.Init(configuration);
            foreach (var pair in original) pair.Key.SetValue(null, pair.Value);
        }
    }
}
