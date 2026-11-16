using System.Diagnostics;
using CHD.SVN_Notifier;

internal static class TemporaryFileTests
{
    /// <summary>Prüft Bereinigung und Gegenfälle ausschließlich in eigenen, expliziten Testverzeichnissen.</summary>
    internal static async Task<int> RunAsync(string output)
    {
        int checks = 0;
        void Check(bool result, string message)
        {
            if (!result) throw new InvalidOperationException(message);
            checks++;
            Console.WriteLine("Dateiwartung: " + message);
        }
        string root = Path.Combine(output, "maintenance");
        string application = Path.Combine(root, "application");
        string temporary = Path.Combine(root, "temp");
        string bundles = Path.Combine(temporary, ".net");
        string notifications = Path.Combine(root, "notifications");
        foreach (string path in new[] { application, temporary, bundles, notifications }) Directory.CreateDirectory(path);
        string target = Path.Combine(application, "SVN_Notifier.exe");
        bool others = false;
        var diagnostics = new List<string>();
        using var maintenance = new TemporaryFileMaintenance(target, temporary, bundles, notifications,
            Path.Combine(bundles, "SVN_Notifier", "CURRENT12345"), diagnostics.Add, () => others);

        var (stage, lease) = TemporaryFileMaintenance.CreateStage(target);
        File.WriteAllText(Path.Combine(stage, "payload.exe"), "payload");
        maintenance.Sweep();
        Check(File.Exists(Path.Combine(stage, "payload.exe")), "Aktives Staging bleibt vor der ersten Löschung geschützt");
        lease.Dispose();
        maintenance.Sweep();
        Check(!Directory.Exists(stage), "Verwaistes Staging wird ohne zusätzliche Altersfrist entfernt");

        string legacy = Path.Combine(application, TemporaryFileMaintenance.StagePrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "package.zip"), "partial");
        others = true; maintenance.Sweep();
        Check(Directory.Exists(legacy), "Übergaben älterer Ausgaben bleiben bei weiteren Instanzen erhalten");
        others = false; maintenance.Sweep();
        Check(!Directory.Exists(legacy), "Verwaiste Übergaben älterer Ausgaben werden bereinigt");

        var (foreign, foreignLease) = TemporaryFileMaintenance.CreateStage(target);
        foreignLease.Dispose();
        File.WriteAllText(Path.Combine(foreign, "important.txt"), "preserve");
        maintenance.Sweep();
        Check(File.Exists(Path.Combine(foreign, "important.txt")) && diagnostics.Count > 0, "Unerwarteter Staging-Inhalt bleibt mit Diagnose erhalten");

        string crossProcess = Path.Combine(application, TemporaryFileMaintenance.StagePrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(crossProcess);
        File.WriteAllText(Path.Combine(crossProcess, "payload.exe"), "preserve");
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("--maintenance-lease"); info.ArgumentList.Add(Path.Combine(crossProcess, TemporaryFileMaintenance.StageLock));
        using (var child = Process.Start(info)!)
        {
            try
            {
                Check(await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)) == "READY", "Separater Prozess hält die Staging-Reservierung");
                maintenance.Sweep();
                Check(File.Exists(Path.Combine(crossProcess, "payload.exe")), "Reservierung eines anderen Prozesses schützt alle Übergabedateien");
            }
            finally { if (!child.HasExited) child.Kill(); await child.WaitForExitAsync(); }
        }
        maintenance.Sweep();
        Check(!Directory.Exists(crossProcess), "Prozessabsturz gibt die automatische Bereinigung frei");

        string current = Path.Combine(bundles, "SVN_Notifier", "CURRENT12345");
        string stale = Path.Combine(bundles, "SVN_Notifier", "STALE1234567");
        string busy = Path.Combine(bundles, "SVN_Notifier", "BUSY12345678");
        string foreignCache = Path.Combine(bundles, "other-product", "STALE1234567");
        string foreignInstaller = Path.Combine(bundles, "installer", "FOREIGN12345");
        foreach (string path in new[] { current, stale, busy, foreignCache, foreignInstaller }) { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "library.dll"), "fixture"); }
        using (var cache = TemporaryFileMaintenance.OpenLease(Path.Combine(busy, ".svnnotifier-cache.lock")))
        {
            maintenance.Sweep();
            Check(!Directory.Exists(stale), "Alter eigener .NET-Cache wird entfernt");
            Check(File.Exists(Path.Combine(current, "library.dll")), "Aktuelles Entpackverzeichnis bleibt erhalten");
            Check(File.Exists(Path.Combine(busy, "library.dll")), "Aktive Cache-Reservierung verhindert eine teilweise Löschung");
            Check(Directory.Exists(foreignCache), "Entpackverzeichnisse anderer Anwendungen bleiben erhalten");
            Check(Directory.Exists(foreignInstaller), "Generische Installer-Caches benötigen einen Herkunftsnachweis des Notifiers");
        }
        maintenance.Sweep();
        Check(!Directory.Exists(busy), "Nicht mehr verwendeter Cache wird im nächsten Lauf entfernt");
        if (OperatingSystem.IsWindows())
        {
            string loaded = Path.Combine(bundles, "SVN_Notifier", "LOADED123456");
            Directory.CreateDirectory(loaded);
            string sentinel = Path.Combine(loaded, "aaa-preserve.txt");
            File.WriteAllText(sentinel, "preserve");
            string library = Path.Combine(loaded, "native-probe.dll");
            File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "version.dll"), library);
            nint module = System.Runtime.InteropServices.NativeLibrary.Load(library);
            try
            {
                maintenance.Sweep();
                Check(File.Exists(sentinel) && File.Exists(library), "Geladene native Bibliothek schützt einen alten Cache ohne Markierungsdatei vor jeder Teillöschung");
            }
            finally { System.Runtime.InteropServices.NativeLibrary.Free(module); }
            maintenance.Sweep();
            Check(!Directory.Exists(loaded), "Nach Entladen der Bibliothek wird der alte Cache entfernt");
        }
        Directory.CreateDirectory(stale);
        others = true; maintenance.Sweep();
        Check(Directory.Exists(stale), "Weitere laufende Instanzen schützen auch ältere Cache-Formate");
        TemporaryFileMaintenance.OpenLease(Path.Combine(stale, ".svnnotifier-cache.lock")).Dispose();
        maintenance.Sweep();
        Check(!Directory.Exists(stale), "Markierte, verwaiste Caches werden auch bei weiteren laufenden Instanzen entfernt");
        others = false;

        string protectedDirectory = Path.Combine(root, "user-data");
        Directory.CreateDirectory(protectedDirectory);
        File.WriteAllText(Path.Combine(protectedDirectory, "important.txt"), "preserve");
        string linkedCache = Path.Combine(bundles, "SVN_Notifier", "LINK12345678");
        if (OperatingSystem.IsWindows())
        {
            var linkInfo = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "/c", "mklink", "/J", linkedCache, protectedDirectory }) linkInfo.ArgumentList.Add(argument);
            using var linkProcess = Process.Start(linkInfo)!;
            await linkProcess.WaitForExitAsync();
            Check(linkProcess.ExitCode == 0, "Eigene Junction-Gegenprobe wurde angelegt");
        }
        else Directory.CreateSymbolicLink(linkedCache, protectedDirectory);
        maintenance.Sweep();
        Check(File.Exists(Path.Combine(protectedDirectory, "important.txt")), "Verzeichnisverknüpfung führt nicht zur Löschung fremder Dateien");
        Directory.Delete(linkedCache);

        string configuration = Path.Combine(application, "fixture.ini");
        string abandonedIni = configuration + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(abandonedIni, "partial");
        File.WriteAllText(configuration + ".user.tmp", "preserve");
        maintenance.Configure(configuration);
        Check(!File.Exists(abandonedIni), "Verwaiste atomare INI-Schreibdatei wird beim Konfigurationsstart entfernt");
        Check(File.Exists(configuration + ".user.tmp"), "Nicht zum INI-Schema passende Dateien bleiben erhalten");
        string legacyImport = Path.Combine(application, ".svnnotifier-import-" + Guid.NewGuid().ToString("N") + ".ini");
        File.WriteAllText(legacyImport, "fixture"); File.SetLastWriteTimeUtc(legacyImport, DateTime.UtcNow.AddDays(-2));
        others = true; maintenance.Sweep();
        Check(File.Exists(legacyImport), "Alte Importdateien ohne Besitzerzuordnung bleiben bei weiteren Instanzen erhalten");
        others = false; maintenance.Sweep();
        Check(!File.Exists(legacyImport), "Verwaiste Importdateien werden nach Ende weiterer Instanzen bereinigt");

        using var process = Process.GetCurrentProcess();
        string liveLog = Path.Combine(temporary, $"SVNNotifier-startup-{process.Id}-{process.StartTime.ToUniversalTime().Ticks}.log");
        string deadLog = Path.Combine(temporary, "SVNNotifier-startup-2147483647-1.log");
        string youngLog = Path.Combine(temporary, "SVNNotifier-startup-2147483647-2.log");
        foreach (string file in new[] { liveLog, deadLog, youngLog }) File.WriteAllText(file, "fixture");
        File.SetLastWriteTimeUtc(liveLog, DateTime.UtcNow.AddDays(-8)); File.SetLastWriteTimeUtc(deadLog, DateTime.UtcNow.AddDays(-8));
        maintenance.Sweep();
        Check(File.Exists(liveLog), "Startfehlerprotokoll eines laufenden Prozesses bleibt erhalten");
        Check(!File.Exists(deadLog) && File.Exists(youngLog), "Alte Startfehlerprotokolle werden nach sieben Tagen entfernt, aktuelle bleiben erhalten");

        string contexts = Path.Combine(notifications, new string('A', 24)); Directory.CreateDirectory(contexts);
        string expired = Path.Combine(contexts, Guid.NewGuid().ToString("N") + ".json.tmp");
        string unrelated = Path.Combine(contexts, "important.json");
        foreach (string file in new[] { expired, unrelated }) { File.WriteAllText(file, "fixture"); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-8)); }
        maintenance.Sweep();
        Check(!File.Exists(expired), "Alte Kontexte verschobener Programmordner werden ebenfalls entfernt");
        Check(File.Exists(unrelated), "Fremde Dateien im Kontextordner werden nicht pauschal gelöscht");
        var startupStage = TemporaryFileMaintenance.CreateStage(target); startupStage.Lease.Dispose();
        maintenance.Start(TimeSpan.FromMilliseconds(50));
        async Task WaitRemoved(string directory)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Directory.Exists(directory) && DateTime.UtcNow < deadline) await Task.Delay(10);
        }
        try
        {
            await WaitRemoved(startupStage.Directory);
            Check(!Directory.Exists(startupStage.Directory), "Hintergrundwartung bereinigt bereits bei ihrem Start");
            var runtimeStage = TemporaryFileMaintenance.CreateStage(target); runtimeStage.Lease.Dispose();
            await WaitRemoved(runtimeStage.Directory);
            Check(!Directory.Exists(runtimeStage.Directory), "Periodische Wartung entfernt erst während der Laufzeit verwaiste Dateien");
        }
        finally { maintenance.Dispose(); await maintenance.Completion; }
        return checks;
    }
}
