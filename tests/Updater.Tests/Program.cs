using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using CHD.SVN_Notifier;

internal static class Program
{
    private static int passed;

    /// <summary>Führt Regressionstests oder den isolierten Installer-Testprozess aus.</summary>
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--apply-update")
            {
                ApplicationUpdateInstaller.Apply(args[1]);
                return 0;
            }
            if (args.Length == 2 && args[0] is "--install-payload" or "--install-payload-tampered" or "--install-payload-invalid")
            {
                var directory = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, ApplicationUpdater.StagePrefix + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var payload = Path.Combine(directory, "payload.exe");
                File.Copy(args[1], payload);
                var version = Version.Parse(FileVersionInfo.GetVersionInfo(payload).FileVersion!);
                var payloadHash = await ApplicationUpdater.HashAsync(payload, CancellationToken.None);
                if (args[0] == "--install-payload-invalid")
                    await File.WriteAllBytesAsync(payload, new byte[100]);
                await ApplicationUpdateInstaller.StartAsync(new PreparedApplicationUpdate(directory,
                    payloadHash, version), 0);
                if (args[0] == "--install-payload-tampered")
                    await File.WriteAllTextAsync(payload, "Tampered test fixture");
                return 0;
            }
            if (args.Length != 2)
                throw new ArgumentException("Expected runtime and release ZIP path.");
            await RunAsync(args[0], Path.GetFullPath(args[1]));
            Console.WriteLine($"Updater-Regressionstests: {passed} erfolgreich.");
            return 0;
        }
        catch (Exception exception)
        {
            if (args.Length == 2 && args[0] == "--apply-update")
                await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(args[1]))!, "installer-error.txt"), exception.ToString());
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    /// <summary>Prüft Versionsentscheidungen, Paketintegrität und die Begrenzung aller Schreibziele.</summary>
    private static async Task RunAsync(string runtime, string archivePath)
    {
        var archive = await File.ReadAllBytesAsync(archivePath);
        using var zip = ZipFile.OpenRead(archivePath);
        await using var executable = zip.GetEntry(ApplicationUpdater.ExecutableName)!.Open();
        using var buffer = new MemoryStream();
        await executable.CopyToAsync(buffer);
        var payload = buffer.ToArray();
        var root = Path.Combine(Path.GetTempPath(), "svnnotifier-updater-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, ApplicationUpdater.ExecutableName);
        await File.WriteAllBytesAsync(target, payload);
        var version = Version.Parse(FileVersionInfo.GetVersionInfo(target).FileVersion!);
        var older = new Version(version.Major, version.Minor, version.Build, version.Revision - 1);
        var newer = new Version(version.Major, version.Minor, version.Build, version.Revision + 1);
        try
        {
            var valid = new FixtureHandler(version, runtime, archive);
            using var client = new HttpClient(valid);
            var updater = new ApplicationUpdater(client);
            Check((await updater.CheckAsync(older, runtime, CancellationToken.None))?.Version == version, "Neue Version erkannt");
            Check(await updater.CheckAsync(version, runtime, CancellationToken.None) is null, "Gleiche Version übersprungen");
            Check(await updater.CheckAsync(newer, runtime, CancellationToken.None) is null, "Kein Downgrade");
            await RejectAsync(() => CheckFixtureAsync(new FixtureHandler(version, runtime, archive) { Draft = true }, older, runtime), "Entwurf abgelehnt");
            await RejectAsync(() => CheckFixtureAsync(new FixtureHandler(version, runtime, archive) { Prerelease = true }, older, runtime), "Vorabversion abgelehnt");
            await RejectAsync(() => CheckFixtureAsync(new FixtureHandler(version, runtime, archive) { Tag = "vinvalid" }, older, runtime), "Ungültiger Tag abgelehnt");
            await RejectAsync(() => CheckFixtureAsync(new FixtureHandler(version, runtime, archive) { ForeignUrl = true }, older, runtime), "Fremde Downloadadresse abgelehnt");
            await RejectAsync(() => CheckFixtureAsync(new FixtureHandler(version, runtime, archive) { MissingPackage = true }, older, runtime), "Fehlendes Architekturpaket abgelehnt");
            await RejectAsync(() => CheckFixtureAsync(new FixtureHandler(version, runtime, archive) { MetadataError = true }, older, runtime), "Netzwerkfehler ist kein aktueller Versionsstand");
            await RejectAsync(() => CheckFixtureAsync(new FixtureHandler(version, runtime, archive) { SizeDifference = int.MaxValue }, older, runtime), "Übermäßige Paketgröße abgelehnt");
            await RejectAsync(() => CheckFixtureAsync(valid, older, "win-arm64"), "Nicht unterstützte Architektur abgelehnt");
            var release = (await updater.CheckAsync(older, runtime, CancellationToken.None))!;
            var prepared = await updater.PrepareAsync(release, runtime, target, null, CancellationToken.None);
            Check(File.ReadAllBytes(Path.Combine(prepared.DirectoryPath, "payload.exe")).SequenceEqual(payload), "Gültiges Paket extrahiert");
            ApplicationUpdater.DeleteStage(prepared.DirectoryPath, target);
            Check(!Directory.Exists(prepared.DirectoryPath), "Eigenes Staging entfernt");
            await RejectPackageAsync(new FixtureHandler(version, runtime, archive) { WrongChecksum = true }, older, runtime, target, "Falsche Prüfsumme abgelehnt");
            await RejectPackageAsync(new FixtureHandler(version, runtime, archive) { DuplicateChecksum = true }, older, runtime, target, "Doppelte Prüfsumme abgelehnt");
            await RejectPackageAsync(new FixtureHandler(version, runtime, archive) { SizeDifference = 1 }, older, runtime, target, "Unvollständiger Download abgelehnt");
            await RejectPackageAsync(new FixtureHandler(version, runtime, archive) { SizeDifference = -1 }, older, runtime, target, "Zu großer Download abgelehnt");
            await RejectPackageAsync(new FixtureHandler(version, runtime, MakeZip(payload, "../SVN_Notifier.exe")), older, runtime, target, "ZIP-Pfadmanipulation abgelehnt");
            await RejectPackageAsync(new FixtureHandler(version, runtime, MakeZip(payload, ApplicationUpdater.ExecutableName, true)), older, runtime, target, "Doppelte ZIP-Datei abgelehnt");
            await RejectPackageAsync(new FixtureHandler(version, runtime, MakeZip(new byte[100], ApplicationUpdater.ExecutableName)), older, runtime, target, "Ungültige EXE abgelehnt");
            var wrongArchitecture = payload.ToArray();
            var peOffset = BitConverter.ToInt32(wrongArchitecture, 0x3c);
            BitConverter.GetBytes((ushort)(runtime == "win-x64" ? 0x14c : 0x8664)).CopyTo(wrongArchitecture, peOffset + 4);
            await RejectPackageAsync(new FixtureHandler(version, runtime, MakeZip(wrongArchitecture, ApplicationUpdater.ExecutableName)), older, runtime, target, "Falsche PE-Architektur abgelehnt");
            await RejectPackageAsync(new FixtureHandler(newer, runtime, archive), older, runtime, target, "Abweichende EXE-Version abgelehnt");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                await RejectAsync(() => updater.PrepareAsync(release, runtime, target, null, cancelled.Token), "Downloadabbruch erkannt");
            }
            using (var cancelled = new CancellationTokenSource())
            using (var cancellationClient = new HttpClient(new FixtureHandler(version, runtime, archive) { CancelDownload = cancelled }))
            {
                await RejectAsync(() => new ApplicationUpdater(cancellationClient).PrepareAsync(release, runtime, target, null, cancelled.Token), "Abbruch während des Paketstreams erkannt");
            }
            Check(File.ReadAllBytes(target).SequenceEqual(payload), "Installierte EXE bei allen Fehlern unverändert");
            Check(Directory.GetDirectories(root).Length == 0, "Fehlerhafte Downloads hinterlassen kein Staging");
            var protectedDirectory = Path.Combine(root, "user-data");
            Directory.CreateDirectory(protectedDirectory);
            await File.WriteAllTextAsync(Path.Combine(protectedDirectory, "important.txt"), "preserve");
            await RejectAsync(() => Task.Run(() => ApplicationUpdater.DeleteStage(protectedDirectory, target)), "Fremdes Verzeichnis nicht gelöscht");
            Check(File.Exists(Path.Combine(protectedDirectory, "important.txt")), "Benutzerdatei erhalten");
            File.Delete(Path.Combine(protectedDirectory, "important.txt"));
            Directory.Delete(protectedDirectory);
            using var liveClient = ApplicationUpdater.CreateClient();
            var liveRelease = await new ApplicationUpdater(liveClient).CheckAsync(new Version(9999, 0, 0, 0), runtime, CancellationToken.None);
            Check(liveRelease is null, "Öffentliche GitHub-Releaseseite gelesen");
            var liveUpdate = await new ApplicationUpdater(liveClient).CheckAsync(new Version(0, 0, 0, 0), runtime, CancellationToken.None);
            Check(liveUpdate is not null && liveUpdate.PackageSize > 0, "Öffentliches Architekturpaket per HEAD geprüft");
        }
        finally
        {
            File.Delete(target);
            Directory.Delete(root);
        }
    }

    /// <summary>Prüft die Metadaten über denselben HTTP-Pfad wie die Anwendung.</summary>
    private static async Task CheckFixtureAsync(FixtureHandler fixture, Version current, string runtime)
    {
        using var client = new HttpClient(fixture);
        await new ApplicationUpdater(client).CheckAsync(current, runtime, CancellationToken.None);
    }

    /// <summary>Prüft einen vollständigen fehlerhaften Download und dessen Aufräumen.</summary>
    private static async Task RejectPackageAsync(FixtureHandler fixture, Version current, string runtime, string target, string description)
    {
        using var client = new HttpClient(fixture);
        var updater = new ApplicationUpdater(client);
        var release = (await updater.CheckAsync(current, runtime, CancellationToken.None))!;
        await RejectAsync(() => updater.PrepareAsync(release, runtime, target, null, CancellationToken.None), description);
    }

    /// <summary>Erstellt ausschließlich isolierte ZIP-Testdaten.</summary>
    private static byte[] MakeZip(byte[] payload, string executableName, bool duplicate = false)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var name in new[] { executableName, "README.md", "LICENSE" })
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(name == executableName ? payload : Encoding.ASCII.GetBytes("fixture"));
            }
            if (duplicate)
                zip.CreateEntry(executableName);
        }
        return output.ToArray();
    }

    /// <summary>Ein Fehlerfall muss ausdrücklich scheitern, statt einen Erfolg zu melden.</summary>
    private static async Task RejectAsync(Func<Task> action, string description)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or HttpRequestException or OperationCanceledException or PlatformNotSupportedException)
        {
            Check(true, description);
            return;
        }
        throw new InvalidOperationException("Gegenprobe wurde akzeptiert: " + description);
    }

    /// <summary>Zählt nur tatsächlich erfüllte Prüfkriterien.</summary>
    private static void Check(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        passed++;
        Console.WriteLine("OK: " + description);
    }
}

/// <summary>Ersetzt nur den Transport; Parser, Download und Paketprüfung bleiben Produktcode.</summary>
internal sealed class FixtureHandler(Version version, string runtime, byte[] archive) : HttpMessageHandler
{
    internal bool Draft { get; init; }
    internal bool Prerelease { get; init; }
    internal string? Tag { get; init; }
    internal bool ForeignUrl { get; init; }
    internal bool MissingPackage { get; init; }
    internal bool MetadataError { get; init; }
    internal bool WrongChecksum { get; init; }
    internal bool DuplicateChecksum { get; init; }
    internal int SizeDifference { get; init; }
    internal CancellationTokenSource? CancelDownload { get; init; }

    /// <summary>Liefert kontrollierte Release-Metadaten sowie Paket- und Prüfsummenantworten.</summary>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var packageName = $"SVNNotifier-{version}-{runtime}.zip";
        var prefix = $"https://github.com/{ApplicationUpdater.Repository}/releases/";
        if (request.RequestUri!.AbsolutePath.EndsWith("/releases/latest"))
        {
            if (MetadataError)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var tag = Draft ? "draft" : Prerelease ? $"v{version}-preview.1" : Tag ?? "v" + version;
            var url = ForeignUrl ? "https://example.com/releases/tag/v" + version : prefix + "tag/" + tag;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = new HttpRequestMessage(HttpMethod.Get, url) });
        }
        if (request.Method == HttpMethod.Head)
        {
            var response = new HttpResponseMessage(MissingPackage ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentLength = archive.LongLength + SizeDifference;
            return Task.FromResult(response);
        }
        if (request.RequestUri.AbsolutePath.EndsWith("SHA256SUMS.txt"))
        {
            var hash = WrongChecksum ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(archive));
            var line = $"{hash}  {packageName}\r\n";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(line + (DuplicateChecksum ? line : "")) });
        }
        HttpContent content = CancelDownload is null ? new ByteArrayContent(archive) : new StreamContent(new CancelOnReadStream(archive, CancelDownload));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}

/// <summary>Löst den Benutzerabbruch erst beim Lesen des eigentlichen Paketstreams aus.</summary>
internal sealed class CancelOnReadStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellation.Cancel();
        return base.ReadAsync(buffer, cancellationToken);
    }
}
