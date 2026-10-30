using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CHD.SVN_Notifier;

internal sealed record ApplicationRelease(Version Version, Uri PageUrl, Uri PackageUrl, Uri ChecksumUrl, long PackageSize);
internal sealed record PreparedApplicationUpdate(string DirectoryPath, string ExecutableHash, Version Version);

/// <summary>Prüft stabile GitHub-Releases und bereitet ausschließlich das passende Anwendungspaket vor.</summary>
internal sealed class ApplicationUpdater
{
    internal const string Repository = "MGGM-Software-GmbH/SVNNotifier";
    internal const string ExecutableName = "SVN_Notifier.exe";
    internal const string StagePrefix = ".svnnotifier-update-";
    private const long MaximumPackageSize = 256L * 1024 * 1024;
    private readonly HttpClient client;

    internal ApplicationUpdater(HttpClient client)
    {
        this.client = client;
    }

    /// <summary>Legt HTTP-Zeitlimit und die von GitHub verlangte Clientkennung fest.</summary>
    internal static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SVNNotifier/" + CurrentVersion);
        return client;
    }

    internal static Version CurrentVersion => typeof(ApplicationUpdater).Assembly.GetName().Version
        ?? throw new InvalidOperationException("Application version is missing.");

    internal static string RuntimeIdentifier => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.X86 => "win-x86",
        _ => throw new PlatformNotSupportedException("No update package is available for this process architecture.")
    };

    /// <summary>Liefert nur neuere stabile Versionen; Netzwerkfehler werden nicht als aktueller Stand behandelt.</summary>
    internal async Task<ApplicationRelease?> CheckAsync(Version currentVersion, string runtime, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://github.com/{Repository}/releases/latest");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        var baseUrl = $"https://github.com/{Repository}/releases/";
        var pageUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? "";
        if (!pageUrl.StartsWith(baseUrl + "tag/", StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected latest-release redirect.");
        var tag = pageUrl[(baseUrl.Length + 4)..];
        if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+\.\d+$") || !Version.TryParse(tag[1..], out var version))
            throw new InvalidDataException("The latest release is not a stable application version.");
        if (version <= currentVersion)
            return null;
        if (runtime is not ("win-x64" or "win-x86"))
            throw new PlatformNotSupportedException("Unsupported update architecture.");
        var packageName = $"SVNNotifier-{version}-{runtime}.zip";
        var packageUrl = new Uri(baseUrl + $"download/{tag}/{packageName}");
        using var packageRequest = new HttpRequestMessage(HttpMethod.Head, packageUrl);
        using var packageResponse = await client.SendAsync(packageRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        packageResponse.EnsureSuccessStatusCode();
        var packageSize = packageResponse.Content.Headers.ContentLength ?? 0;
        if (packageSize <= 0 || packageSize > MaximumPackageSize)
            throw new InvalidDataException("Invalid update package size.");
        return new ApplicationRelease(version,
            new Uri(pageUrl), packageUrl, new Uri(baseUrl + $"download/{tag}/SHA256SUMS.txt"),
            packageSize);
    }

    /// <summary>Begrenzt kleine Metadatenantworten auch bei fehlender Content-Length.</summary>
    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int limit, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > limit)
                throw new InvalidDataException("Update metadata exceeds its size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        return output.ToArray();
    }

    /// <summary>Prüft Prüfsumme, ZIP-Inhalt, PE-Architektur und Dateiversion vor jeder Installation.</summary>
    internal async Task<PreparedApplicationUpdate> PrepareAsync(ApplicationRelease release, string runtime, string targetPath,
        IProgress<int>? progress, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        cancellationToken = timeout.Token;
        using var checksumResponse = await client.GetAsync(release.ChecksumUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        checksumResponse.EnsureSuccessStatusCode();
        var checksumText = System.Text.Encoding.ASCII.GetString(await ReadLimitedAsync(checksumResponse, 65536, cancellationToken));
        var packageName = Path.GetFileName(release.PackageUrl.AbsolutePath);
        var matches = checksumText.Split('\n').Select(line => Regex.Match(line.TrimEnd('\r'), @"^([a-fA-F0-9]{64}) [ *](\S+)$"))
            .Where(match => match.Success && match.Groups[2].Value == packageName).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("A unique package checksum is required.");
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(targetPath))!, StagePrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var archivePath = Path.Combine(directory, "package.zip");
            using var response = await client.GetAsync(release.PackageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += count;
                    if (total > release.PackageSize || total > MaximumPackageSize)
                        throw new InvalidDataException("The download exceeds the expected package size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    progress?.Report((int)(total * 100 / release.PackageSize));
                }
                if (total != release.PackageSize)
                    throw new InvalidDataException("The update download is incomplete.");
            }
            if (!string.Equals(await HashAsync(archivePath, cancellationToken), matches[0].Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The package SHA-256 checksum does not match.");
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                var names = archive.Entries.Select(entry => entry.FullName).ToArray();
                if (names.Length != 3 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3 ||
                    !names.Order().SequenceEqual(new[] { "LICENSE", "README.md", ExecutableName }.Order()))
                    throw new InvalidDataException("The update ZIP contains unexpected or missing files.");
                var executable = archive.GetEntry(ExecutableName)!;
                if (executable.Length < 64 || executable.Length > MaximumPackageSize)
                    throw new InvalidDataException("Invalid uncompressed executable size.");
                await using var source = executable.Open();
                await using var destination = new FileStream(Path.Combine(directory, "payload.exe"), FileMode.CreateNew);
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += count;
                    if (total > executable.Length || total > MaximumPackageSize)
                        throw new InvalidDataException("The extracted executable exceeds its size limit.");
                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
                if (total != executable.Length)
                    throw new InvalidDataException("The extracted executable is incomplete.");
            }
            ValidateExecutable(Path.Combine(directory, "payload.exe"), release.Version, runtime);
            return new PreparedApplicationUpdate(directory, await HashAsync(Path.Combine(directory, "payload.exe"), cancellationToken), release.Version);
        }
        catch
        {
            DeleteStage(directory, targetPath);
            throw;
        }
    }

    /// <summary>Verhindert den Austausch durch ein Paket für die falsche Architektur oder Version.</summary>
    internal static void ValidateExecutable(string path, Version version, string runtime)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadUInt16() != 0x5a4d)
            throw new InvalidDataException("The payload is not a Windows executable.");
        reader.BaseStream.Position = 0x3c;
        var offset = reader.ReadInt32();
        if (offset < 64 || offset > reader.BaseStream.Length - 6)
            throw new InvalidDataException("Invalid PE header.");
        reader.BaseStream.Position = offset;
        if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != (runtime == "win-x64" ? 0x8664 : 0x14c))
            throw new InvalidDataException("The executable architecture does not match.");
        var information = FileVersionInfo.GetVersionInfo(path);
        if (information.FileVersion != version.ToString() || information.ProductVersion != version.ToString())
            throw new InvalidDataException("The executable version does not match the release.");
    }

    /// <summary>Berechnet den Hash erneut, damit auch Änderungen zwischen Download und Installation auffallen.</summary>
    internal static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
    }

    /// <summary>Entfernt nur eigene Stagingdateien direkt neben der betroffenen Anwendung.</summary>
    internal static void DeleteStage(string directory, string targetPath)
    {
        directory = Path.GetFullPath(directory);
        var name = Path.GetFileName(directory);
        if (Path.GetDirectoryName(directory) != Path.GetDirectoryName(Path.GetFullPath(targetPath)) ||
            !name.StartsWith(StagePrefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[StagePrefix.Length..], "N", out _) ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Invalid application update directory.");
        var allowedNames = new[] { "package.zip", "payload.exe", "installer.exe", "previous.exe", "update.json", "installer-error.txt" };
        var files = Directory.GetFiles(directory);
        if (Directory.GetDirectories(directory).Length != 0 || files.Any(file =>
            !allowedNames.Contains(Path.GetFileName(file), StringComparer.Ordinal) ||
            (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0))
            throw new InvalidDataException("Unexpected content in the application update directory.");
        foreach (var file in files)
            File.Delete(file);
        Directory.Delete(directory);
    }
}
