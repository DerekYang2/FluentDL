using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FluentDL.Core.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDL.Core.Tests;

[TestClass]
public class GithubUpdateTests
{
    private static readonly UpdateInstallation Installed = new("DerekYang2.FluentDL", "synthetic-publisher", new(1, 0, 0, 0), "x64");
    private static readonly Version Target = new(2, 0, 0, 0);
    private static JsonObject Release(byte[] bytes, string architecture = "x64") => new()
    {
        ["draft"] = false, ["prerelease"] = false, ["tag_name"] = Target.ToString(),
        ["assets"] = new JsonArray(new JsonObject
        {
            ["name"] = $"FluentDL_{Target}_{architecture}.zip", ["size"] = bytes.LongLength,
            ["digest"] = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            ["browser_download_url"] = $"{GithubUpdates.Repository}/releases/download/{Target}/FluentDL_{Target}_{architecture}.zip",
        }),
    };

    [TestMethod]
    [DataRow("x64")]
    [DataRow("arm64")]
    public void SelectsInstalledArchitectureAndIgnoresAnEqualOrOlderRelease(string architecture)
    {
        var json = Release([1], architecture).ToJsonString();
        Assert.IsNotNull(GithubUpdates.Parse(json, Installed with { Architecture = architecture }));
        Assert.IsNull(GithubUpdates.Parse(json, Installed with { Version = Target, Architecture = architecture }));
        Assert.IsNull(GithubUpdates.Parse(json, Installed with { Version = new(3, 0, 0, 0), Architecture = architecture }));
    }

    [TestMethod]
    [DataRow("digest")]
    [DataRow("url")]
    [DataRow("size")]
    [DataRow("duplicate")]
    [DataRow("prerelease")]
    [DataRow("draft")]
    [DataRow("version")]
    public void RejectsUntrustedOrIncompleteReleaseMetadata(string failure)
    {
        var json = Release([1]);
        var asset = json["assets"]![0]!;
        switch (failure)
        {
            case "digest": asset["digest"] = null; break;
            case "url": asset["browser_download_url"] = "https://example.test/update.zip"; break;
            case "size": asset["size"] = -1; break;
            case "duplicate": json["assets"]!.AsArray().Add(asset.DeepClone()); break;
            case "prerelease": json["prerelease"] = true; break;
            case "draft": json["draft"] = true; break;
            case "version": json["tag_name"] = "2.0"; break;
        }
        Assert.ThrowsException<InvalidDataException>(() => GithubUpdates.Parse(json.ToJsonString(), Installed));
    }

    [TestMethod]
    [DataRow("Name")]
    [DataRow("Publisher")]
    [DataRow("ProcessorArchitecture")]
    [DataRow("Version")]
    public void RejectsAnotherPackageIdentityOrVersion(string field)
    {
        using var files = new Fixture();
        var manifest = Manifest().Replace($"{field}=\"{Field(field)}\"", $"{field}=\"wrong\"");
        var package = Path.Combine(files.Root, "bad.msix");
        File.WriteAllBytes(package, Package(manifest));
        Assert.ThrowsException<InvalidDataException>(() => GithubUpdates.ValidatePackage(package, Installed, Target));
    }

    [TestMethod]
    public async Task DownloadsVerifiedBytesAndExtractsOnlyTheExpectedPackage()
    {
        using var files = new Fixture();
        var bytes = Archive(Package(Manifest()));
        using var http = new HttpClient(new Reply(bytes));
        var update = GithubUpdates.Parse(Release(bytes).ToJsonString(), Installed)!;
        var prepared = await new GithubUpdates(http).DownloadAsync(update, Installed, Path.Combine(files.Root, "download"), null, CancellationToken.None);
        Assert.AreEqual(Target, prepared.Version);
        Assert.IsTrue(File.Exists(prepared.PackagePath));
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(prepared.PackagePath))), prepared.Sha256);
        Assert.IsTrue(File.Exists(prepared.ArchivePath), "Keep the archive for independent elevated verification.");
        Assert.AreEqual(2, Directory.GetFiles(Path.GetDirectoryName(prepared.PackagePath)!).Length, "Keep only the package and verified archive, never extracted scripts.");
    }

    [TestMethod]
    [DataRow("hash")]
    [DataRow("truncated")]
    [DataRow("path")]
    [DataRow("identity")]
    public async Task InvalidDownloadsLeaveNoInstallablePackage(string failure)
    {
        using var files = new Fixture();
        var bytes = Archive(Package(failure == "identity" ? Manifest().Replace("synthetic-publisher", "other") : Manifest()), failure == "path");
        var update = GithubUpdates.Parse(Release(bytes).ToJsonString(), Installed)!;
        if (failure == "hash") update = update with { Sha256 = new string('0', 64) };
        if (failure == "truncated") bytes = bytes[..^1];
        using var http = new HttpClient(new Reply(bytes));
        var directory = Path.Combine(files.Root, "download");
        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => new GithubUpdates(http).DownloadAsync(update, Installed, directory, null, CancellationToken.None));
        Assert.IsFalse(File.Exists(Path.Combine(directory, "package.msix")));
    }

    [TestMethod]
    public async Task DoesNotOverwriteExistingDownloadDirectories()
    {
        using var files = new Fixture();
        var sentinel = Path.Combine(files.Root, "package.msix");
        File.WriteAllText(sentinel, "keep");
        using var http = new HttpClient(new Reply([1]));
        var update = GithubUpdates.Parse(Release([1]).ToJsonString(), Installed)!;
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => new GithubUpdates(http).DownloadAsync(update, Installed, files.Root, null, CancellationToken.None));
        Assert.AreEqual("keep", File.ReadAllText(sentinel));
    }

    private static string Field(string name) => name switch { "Name" => Installed.Name, "Publisher" => Installed.Publisher, "ProcessorArchitecture" => Installed.Architecture, _ => Target.ToString() };
    private static string Manifest() => $"<Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\"><Identity Name=\"{Installed.Name}\" Publisher=\"{Installed.Publisher}\" ProcessorArchitecture=\"{Installed.Architecture}\" Version=\"{Target}\"/><Applications><Application Id=\"App\" Executable=\"FluentDL.exe\"/></Applications></Package>";
    private static byte[] Package(string manifest)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(zip.CreateEntry("AppxManifest.xml").Open(), Encoding.UTF8)) writer.Write(manifest);
        return buffer.ToArray();
    }
    private static byte[] Archive(byte[] package, bool unsafePath = false)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            using (var stream = zip.CreateEntry($"FluentDL_{Target}_x64/FluentDL_{Target}_x64.msix").Open()) stream.Write(package);
            using var script = new StreamWriter(zip.CreateEntry(unsafePath ? "../escape.ps1" : "install.ps1").Open());
            script.Write("This must not be extracted or executed.");
        }
        return buffer.ToArray();
    }
    private sealed class Reply(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes), RequestMessage = request });
    }
    private sealed class Fixture : IDisposable
    {
        internal string Root = Path.Combine(Path.GetTempPath(), "opencode", "update-test-" + Guid.NewGuid().ToString("N"));
        internal Fixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
