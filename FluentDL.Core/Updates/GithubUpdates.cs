#nullable enable
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace FluentDL.Core.Updates;

public sealed record UpdateInstallation(string Name, string Publisher, Version Version, string Architecture);
public sealed record GithubUpdate(Version Version, string Tag, Uri Download, long Size, string Sha256);
public sealed record PreparedUpdate(string PackagePath, string Sha256, long Size, Version Version, string ArchivePath, string Tag);

public sealed class GithubUpdates(HttpClient http)
{
    public const string Repository = "https://github.com/DerekYang2/FluentDL";
    private const long MaxSize = 1024L * 1024 * 1024;

    public async Task<GithubUpdate?> CheckAsync(UpdateInstallation installation, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/DerekYang2/FluentDL/releases/latest");
        request.Headers.UserAgent.ParseAdd("FluentDL-Updater/1.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(token);
        return Parse(json, installation);
    }

    public static GithubUpdate? Parse(string json, UpdateInstallation installation)
    {
        if (installation.Architecture is not ("x64" or "arm64")) throw new NotSupportedException("This architecture has no FluentDL update package.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("The update is not a stable published release.");
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var text = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
        if (!Version.TryParse(text, out var version) || version.Revision < 0 || text != version.ToString()
            || new[] { version.Major, version.Minor, version.Build, version.Revision }.Any(value => value > 65535))
            throw new InvalidDataException("The release version is invalid.");
        if (version <= installation.Version) return null;
        var name = $"FluentDL_{version}_{installation.Architecture}.zip";
        var assets = root.GetProperty("assets").EnumerateArray().Where(asset => asset.GetProperty("name").GetString() == name).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("The release has no unique package for this installation.");
        var asset = assets[0];
        var digest = asset.GetProperty("digest").GetString() ?? "";
        if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 || !digest[7..].All(char.IsAsciiHexDigit))
            throw new InvalidDataException("GitHub did not provide a SHA-256 digest for this update.");
        var expected = new Uri($"{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{name}");
        if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var url)
            || url.AbsoluteUri != expected.AbsoluteUri) throw new InvalidDataException("The update download address is not the official release asset.");
        var size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaxSize) throw new InvalidDataException("The update package size is invalid.");
        return new(version, tag, url, size, digest[7..]);
    }

    public async Task<PreparedUpdate> DownloadAsync(GithubUpdate update, UpdateInstallation installation, string directory,
        IProgress<double>? progress, CancellationToken token)
    {
        if (Directory.Exists(directory)) throw new InvalidOperationException("Use a fresh directory for each update download.");
        Directory.CreateDirectory(directory);
        var zip = Path.Combine(directory, "download.zip");
        var package = Path.Combine(directory, "package.msix");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.Download);
            request.Headers.UserAgent.ParseAdd("FluentDL-Updater/1.0");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            var final = response.RequestMessage?.RequestUri ?? update.Download;
            if (final.Scheme != "https" || final.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("The download redirected outside GitHub's release hosts.");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long count = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    count += read;
                    if (count > update.Size || count > MaxSize) throw new InvalidDataException("The update download exceeds its declared size.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    progress?.Report((double)count / update.Size * 100);
                }
                if (count != update.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The update download failed SHA-256 verification.");
            }
            using (var archive = ZipFile.OpenRead(zip))
            {
                if (archive.Entries.Count > 32 || archive.Entries.Sum(entry => entry.Length) > MaxSize * 2)
                    throw new InvalidDataException("The release archive is too large.");
                foreach (var entry in archive.Entries)
                    if (entry.FullName.Contains('\\') || entry.FullName.StartsWith('/') || entry.FullName.Split('/').Any(part => part is "." or ".."))
                        throw new InvalidDataException("The release archive has an unsafe path.");
                var name = $"FluentDL_{update.Version}_{installation.Architecture}";
                var entries = archive.Entries.Where(entry => entry.FullName.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (entries.Length != 1 || entries[0].FullName != $"{name}/{name}.msix" || entries[0].Length > MaxSize)
                    throw new InvalidDataException("The release archive has an unexpected MSIX layout.");
                await using var input = entries[0].Open();
                await using var output = new FileStream(package, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long count = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    count += read;
                    if (count > entries[0].Length || count > MaxSize) throw new InvalidDataException("The extracted update exceeds its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                }
                if (count != entries[0].Length) throw new InvalidDataException("The extracted update is truncated.");
            }
            ValidatePackage(package, installation, update.Version);
            await using var file = File.OpenRead(package);
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
            return new(package, digest, file.Length, update.Version, zip, update.Tag);
        }
        catch
        {
            if (File.Exists(package)) File.Delete(package);
            if (File.Exists(zip)) File.Delete(zip);
            throw;
        }
    }

    public static void ValidatePackage(string path, UpdateInstallation installation, Version target)
    {
        if (target <= installation.Version) throw new InvalidDataException("The update must be newer than the installed app.");
        using var archive = ZipFile.OpenRead(path);
        var entries = archive.Entries.Where(entry => entry.FullName == "AppxManifest.xml").ToArray();
        if (entries.Length != 1 || entries[0].Length > 1024 * 1024) throw new InvalidDataException("Invalid update manifest.");
        using var stream = entries[0].Open();
        using var reader = XmlReader.Create(stream, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Empty update manifest.");
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var identity = root.Element(ns + "Identity") ?? throw new InvalidDataException("Missing update identity.");
        if ((string?)identity.Attribute("Name") != installation.Name || (string?)identity.Attribute("Publisher") != installation.Publisher
            || (string?)identity.Attribute("ProcessorArchitecture") != installation.Architecture
            || (string?)identity.Attribute("Version") != target.ToString()) throw new InvalidDataException("The update belongs to another package, version or architecture.");
        var application = root.Element(ns + "Applications")?.Elements(ns + "Application").SingleOrDefault();
        if ((string?)application?.Attribute("Id") != "App" || (string?)application?.Attribute("Executable") != "FluentDL.exe")
            throw new InvalidDataException("Unexpected update application.");
    }
}
