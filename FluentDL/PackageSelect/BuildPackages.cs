// Builds FluentDL's Store MSIX files and sideload release ZIPs for x64 and ARM64.
// Run from the FluentDL project folder with the .NET 10 SDK or later:
//   dotnet run --file .\PackageSelect\BuildPackages.cs -- --version 3.8.0.0
// See https://github.com/DerekYang2/FluentDL/wiki/Packaging for every option.

using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

return Packager.Run(args);

static class Packager
{
    static readonly string[] Channels = ["Store", "Sideload"];
    static readonly string[] Architectures = ["x64", "arm64"];
    static readonly string[] Installers = ["install_fluentdl.cmd", "install_fluentdl.ps1"];
    static readonly XNamespace PackageNs = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    static readonly string ScriptDir = AppContext.GetData("EntryPointFileDirectoryPath") as string
        ?? Path.GetDirectoryName(SourcePath())!;
    static readonly string ProjectDir = Path.GetDirectoryName(ScriptDir)!;
    static volatile bool cancelled;

    const string Help = """
        Builds FluentDL's Store MSIX files and sideload release ZIPs.

        Usage: dotnet run --file .\PackageSelect\BuildPackages.cs -- --version <version> [options]

          --version <a.b.c.d>       Package version, for example 3.8.0.0. Required.
          --channel <name>          All (default), Store or Sideload.
          --architecture <name>     All (default), x64 or arm64.
          --output <folder>         Artifact root. Default: FLUENTDL_PACKAGE_OUTPUT, or artifacts in the project folder.
          --sandbox-share <folder>  Existing folder that receives unzipped sideload builds. Default: FLUENTDL_SANDBOX_SHARE.
          --no-sandbox              Skip the sandbox copy, even when FLUENTDL_SANDBOX_SHARE is set.
          --msbuild <MSBuild.exe>   Build with this MSBuild instead of 'dotnet msbuild'. Default: FLUENTDL_MSBUILD.
          --overwrite               Replace output files from an earlier run. Other files are left alone.
          -h, --help                Show this help.
        """;

    static string SourcePath([CallerFilePath] string path = "") => path;

    public static int Run(string[] args)
    {
        Console.CancelKeyPress += (_, e) =>
        {
            // MSBuild gets the same Ctrl+C and stops; the build loop then cleans up and exits.
            e.Cancel = true;
            cancelled = true;
        };
        try
        {
            var options = Parse(args);
            if (options is null)
            {
                Console.WriteLine(Help);
                return 0;
            }
            Build(options);
            Console.WriteLine();
            Console.WriteLine("Packaging complete.");
            return 0;
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine($"ERROR: {e.Message}");
            return 2;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Packaging cancelled.");
            return 130;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                       or InvalidOperationException or XmlException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"ERROR: {e.Message}");
            return 1;
        }
    }

    sealed record Options(
        string Version, string[] Channels, string[] Architectures, string Output, string? Sandbox, string? MSBuild, bool Overwrite);

    sealed class UsageException(string message) : Exception(message);

    static Options? Parse(string[] args)
    {
        string? version = null, output = null, sandbox = null, msbuild = null;
        string channel = "All", architecture = "All";
        bool noSandbox = false, overwrite = false;

        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i];
            string? inline = null;
            if (name.StartsWith("--") && name.IndexOf('=') is var equals and > 0)
            {
                inline = name[(equals + 1)..];
                name = name[..equals];
            }

            string Value() => inline ?? (i + 1 < args.Length ? args[++i] : throw new UsageException($"{name} needs a value."));

            switch (name)
            {
                case "-h" or "--help": return null;
                case "--version": version = Value(); break;
                case "--channel": channel = Value(); break;
                case "--architecture": architecture = Value(); break;
                case "--output": output = Value(); break;
                case "--sandbox-share": sandbox = Value(); break;
                case "--no-sandbox": noSandbox = true; break;
                case "--msbuild": msbuild = Value(); break;
                case "--overwrite": overwrite = true; break;
                default: throw new UsageException($"Unknown argument {args[i]}. Use --help to list the options.");
            }
        }

        if (version is null) throw new UsageException("--version is required, for example --version 3.8.0.0.");
        CheckVersion(version);
        if (sandbox is not null && noSandbox) throw new UsageException("Use --sandbox-share or --no-sandbox, not both.");

        return new Options(
            version,
            Choose(channel, Channels, "--channel"),
            Choose(architecture, Architectures, "--architecture"),
            Path.GetFullPath(output ?? EnvironmentPath("FLUENTDL_PACKAGE_OUTPUT") ?? Path.Combine(ProjectDir, "artifacts")),
            noSandbox ? null : (sandbox ?? EnvironmentPath("FLUENTDL_SANDBOX_SHARE")) is { } share ? Path.GetFullPath(share) : null,
            msbuild ?? EnvironmentPath("FLUENTDL_MSBUILD"),
            overwrite);
    }

    static string[] Choose(string value, string[] allowed, string option)
    {
        if (value.Equals("All", StringComparison.OrdinalIgnoreCase)) return allowed;
        var match = allowed.FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase));
        return match is not null
            ? [match]
            : throw new UsageException($"{option} must be All, {string.Join(" or ", allowed)}; got {value}.");
    }

    static void CheckVersion(string version)
    {
        if (!Regex.IsMatch(version, @"^(0|[1-9][0-9]*)(\.(0|[1-9][0-9]*)){3}$"))
            throw new UsageException("Version must have four numeric parts, for example 3.8.0.0.");
        var parts = version.Split('.').Select(part => int.TryParse(part, out var number) ? number : int.MaxValue).ToArray();
        if (parts[0] == 0 || parts.Any(part => part > 65535))
            throw new UsageException("Version parts must be 0 to 65535, with a major version above 0.");
    }

    static string? EnvironmentPath(string name)
    {
        var value = Environment.GetEnvironmentVariable(name)?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        value = Environment.ExpandEnvironmentVariables(value);
        if (value == "~" || value.StartsWith("~\\") || value.StartsWith("~/"))
            value = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + value[1..];
        return value;
    }

    static void Build(Options options)
    {
        if (options.Channels.Contains("Store") && !options.Version.EndsWith(".0"))
            throw new UsageException("Store versions must end in .0, because the Store reserves the last number.");
        if (options.Sandbox is not null && options.Channels.Contains("Sideload") && !Directory.Exists(options.Sandbox))
            throw new DirectoryNotFoundException($"Sandbox share does not exist: {options.Sandbox}. Use --sandbox-share or --no-sandbox.");
        CheckDestinations(OutputPaths(options), options.Overwrite);
        if (options.Channels.Contains("Sideload"))
        {
            foreach (var installer in Installers.Where(installer => !File.Exists(Path.Combine(ScriptDir, installer))))
                throw new FileNotFoundException($"Missing installer: {Path.Combine(ScriptDir, installer)}");
        }

        var tool = FindBuildTool(options.MSBuild);
        var logDir = Path.Combine(options.Output, options.Version, "logs");
        Directory.CreateDirectory(logDir);
        Console.WriteLine($"Using {tool.Display}");

        var working = Path.Combine(options.Output, ".packaging-" + Path.GetRandomFileName());
        Directory.CreateDirectory(working);
        try
        {
            var files = new List<(string Source, string Destination)>();
            foreach (var channel in options.Channels)
            {
                foreach (var architecture in options.Architectures)
                {
                    files.AddRange(BuildVariant(options, tool, working, logDir, channel, architecture));
                }
            }
            // Publish only after every requested variant built, so a failure never leaves a partial release.
            PublishFiles(files, options.Overwrite);
        }
        finally
        {
            try { Directory.Delete(working, recursive: true); }
            catch (IOException e) { Console.Error.WriteLine($"Warning: could not remove {working}: {e.Message}"); }
        }
    }

    static List<(string Source, string Destination)> BuildVariant(
        Options options, BuildTool tool, string working, string logDir, string channel, string architecture)
    {
        ThrowIfCancelled();
        Console.WriteLine();
        Console.WriteLine($"Building {channel} / {architecture} / {options.Version}");

        var variant = Path.Combine(working, channel, architecture);
        Directory.CreateDirectory(variant);
        var manifest = Path.Combine(variant, "Package.appxmanifest");
        var appManifest = Path.Combine(variant, "app.manifest");
        WriteVersionedManifest(Path.Combine(ScriptDir, $"{channel.ToLowerInvariant()}.appxmanifest.txt"), manifest, options.Version, "Identity", "Version");
        WriteVersionedManifest(Path.Combine(ProjectDir, "app.manifest"), appManifest, options.Version, "assemblyIdentity", "version");

        var packageDir = Path.Combine(variant, "AppPackages");
        var log = Path.Combine(logDir, $"{channel}-{architecture}.log");
        var exitCode = RunBuild(tool, channel, architecture, manifest, appManifest, packageDir, log);
        ThrowIfCancelled();
        if (exitCode != 0) throw new InvalidOperationException($"{channel}/{architecture} build failed ({exitCode}). See {log}");

        var name = $"FluentDL_{options.Version}_{architecture}";
        var packages = Directory.Exists(packageDir)
            ? Directory.GetFiles(packageDir, $"{name}.msix", SearchOption.AllDirectories)
            : [];
        if (packages.Length != 1)
            throw new InvalidDataException($"Expected one {name}.msix in {packageDir}; found {packages.Length}. See {log}");
        ValidatePackage(packages[0], manifest, architecture, channel);

        if (channel == "Store")
            return [(packages[0], Path.Combine(options.Output, options.Version, channel, architecture, $"{name}.msix"))];

        var payload = Path.Combine(variant, name);
        Directory.CreateDirectory(payload);
        File.Copy(packages[0], Path.Combine(payload, $"{name}.msix"));
        foreach (var installer in Installers)
            File.Copy(Path.Combine(ScriptDir, installer), Path.Combine(payload, installer));
        var archive = Path.Combine(variant, $"{name}.zip");
        CreateReleaseZip(payload, archive);

        var files = new List<(string, string)> { (archive, Path.Combine(options.Output, options.Version, channel, $"{name}.zip")) };
        if (options.Sandbox is not null)
        {
            foreach (var file in Directory.GetFiles(payload).Order(StringComparer.Ordinal))
                files.Add((file, Path.Combine(options.Sandbox, name, Path.GetFileName(file))));
        }
        return files;
    }

    sealed record BuildTool(string FileName, string[] Prefix, string Display);

    static BuildTool FindBuildTool(string? msbuild)
    {
        if (msbuild is null) return new BuildTool("dotnet", ["msbuild"], "dotnet msbuild");
        var path = Path.GetFullPath(msbuild);
        return File.Exists(path)
            ? new BuildTool(path, [], path)
            : throw new FileNotFoundException($"MSBuild executable not found: {path}");
    }

    static int RunBuild(BuildTool tool, string channel, string architecture, string manifest, string appManifest, string packageDir, string log)
    {
        var properties = new Dictionary<string, string>
        {
            ["Configuration"] = "Release",
            ["Platform"] = architecture,
            ["PlatformTarget"] = architecture,
            ["RuntimeIdentifier"] = $"win-{architecture}",
            ["CustomBeforeDirectoryBuildProps"] = Path.Combine(ScriptDir, "Packaging.props"),
            ["FluentDLPackageChannel"] = channel,
            ["FluentDLPackageManifest"] = manifest,
            ["ApplicationManifest"] = appManifest,
            ["SelfContained"] = "true",
            ["WindowsAppSDKSelfContained"] = channel == "Sideload" ? "true" : "false",
            ["WindowsAppSdkIncludeVersionInfo"] = "true",
            ["WindowsPackageType"] = "MSIX",
            ["GenerateAppxPackageOnBuild"] = "true",
            ["UapAppxPackageBuildMode"] = channel == "Store" ? "StoreOnly" : "SideloadOnly",
            ["AppxPackageIsForStore"] = channel == "Store" ? "true" : "false",
            ["BuildAppxUploadPackageForUap"] = "false",
            ["AppxBundle"] = "Never",
            ["AppxBundlePlatforms"] = architecture,
            ["AppxPackageSigningEnabled"] = "false",
            ["AppxSymbolPackageEnabled"] = "false",
            ["AppxAutoIncrementPackageRevision"] = "false",
            ["GenerateTestArtifacts"] = "false",
            ["GenerateAppInstallerFile"] = "false",
            ["AppxPackageDir"] = packageDir + "\\",
            ["PublishProfile"] = "",
        };

        var start = new ProcessStartInfo(tool.FileName) { UseShellExecute = false, WorkingDirectory = ProjectDir };
        foreach (var argument in tool.Prefix) start.ArgumentList.Add(argument);
        start.ArgumentList.Add(Path.Combine(ProjectDir, "FluentDL.csproj"));
        foreach (var argument in new[] { "-nologo", "-restore", "-t:Build", "-verbosity:minimal", "-nr:false", $"-flp:LogFile={log};Verbosity=normal;Encoding=UTF-8" })
            start.ArgumentList.Add(argument);
        foreach (var (key, value) in properties) start.ArgumentList.Add($"-p:{key}={value}");

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {tool.FileName}.");
        process.WaitForExit();
        return process.ExitCode;
    }

    static void WriteVersionedManifest(string source, string destination, string version, string element, string attribute)
    {
        var content = File.ReadAllText(source);
        XDocument.Parse(content);
        var pattern = $@"(<{element}\b[^>]*?\b{attribute}="")[^""]+("")";
        var count = Regex.Matches(content, pattern).Count;
        if (count != 1) throw new InvalidDataException($"Expected exactly one {element} {attribute} in {source}; found {count}.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, Regex.Replace(content, pattern, match => match.Groups[1].Value + version + match.Groups[2].Value), new UTF8Encoding(false));
    }

    static void ValidatePackage(string package, string manifest, string architecture, string channel)
    {
        var expected = XDocument.Load(manifest).Root?.Element(PackageNs + "Identity")
            ?? throw new InvalidDataException($"No package Identity in {manifest}");
        using var archive = ZipFile.OpenRead(package);
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            if (Crc32(stream) != entry.Crc32) throw new InvalidDataException($"Corrupt MSIX member: {entry.FullName}");
        }

        XDocument actual;
        using (var stream = (archive.GetEntry("AppxManifest.xml") ?? throw new InvalidDataException($"{package} has no AppxManifest.xml.")).Open())
            actual = XDocument.Load(stream);
        var identity = actual.Root?.Element(PackageNs + "Identity") ?? throw new InvalidDataException($"No package Identity in {package}");
        foreach (var field in new[] { "Name", "Publisher", "Version" })
        {
            if ((string?)identity.Attribute(field) != (string?)expected.Attribute(field))
                throw new InvalidDataException($"{package}: {field} does not match the {channel} manifest.");
        }
        if ((string?)identity.Attribute("ProcessorArchitecture") != architecture)
            throw new InvalidDataException($"{package}: expected the {architecture} architecture.");
        if (archive.GetEntry("AppxSignature.p7x") is not null)
            throw new InvalidDataException($"{package}: expected an unsigned package.");

        var runtimeDependency = actual.Root!.Element(PackageNs + "Dependencies")?
            .Elements(PackageNs + "PackageDependency")
            .Any(dependency => ((string?)dependency.Attribute("Name") ?? "").StartsWith("Microsoft.WindowsAppRuntime")) ?? false;
        if (runtimeDependency != (channel == "Store"))
            throw new InvalidDataException($"{package}: unexpected Windows App SDK dependency for {channel}.");
    }

    static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(index =>
    {
        var value = (uint)index;
        for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
        return value;
    }).ToArray();

    static uint Crc32(Stream stream)
    {
        var crc = 0xFFFFFFFFu;
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            foreach (var value in buffer.AsSpan(0, read)) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }
        return ~crc;
    }

    static void CreateReleaseZip(string folder, string destination)
    {
        using var archive = ZipFile.Open(destination, ZipArchiveMode.Create);
        foreach (var entry in Directory.GetFileSystemEntries(folder).Order(StringComparer.Ordinal))
        {
            if (Directory.Exists(entry)) throw new InvalidDataException($"Unexpected directory in release payload: {entry}");
            archive.CreateEntryFromFile(entry, $"{Path.GetFileName(folder)}/{Path.GetFileName(entry)}", CompressionLevel.Optimal);
        }
    }

    static List<string> OutputPaths(Options options)
    {
        var paths = new List<string>();
        foreach (var channel in options.Channels)
        {
            foreach (var architecture in options.Architectures)
            {
                var name = $"FluentDL_{options.Version}_{architecture}";
                if (channel == "Store")
                {
                    paths.Add(Path.Combine(options.Output, options.Version, channel, architecture, $"{name}.msix"));
                    continue;
                }
                paths.Add(Path.Combine(options.Output, options.Version, channel, $"{name}.zip"));
                if (options.Sandbox is null) continue;

                var folder = Path.Combine(options.Sandbox, name);
                paths.Add(Path.Combine(folder, $"{name}.msix"));
                paths.AddRange(Installers.Select(installer => Path.Combine(folder, installer)));
                if (Directory.Exists(folder) && Directory.GetFiles(folder, "*.msix")
                        .Any(file => !Path.GetFileName(file).Equals($"{name}.msix", StringComparison.OrdinalIgnoreCase)))
                    throw new IOException($"Extra MSIX files in {folder}; move them before staging.");
            }
        }
        return paths;
    }

    static void CheckDestinations(IEnumerable<string> paths, bool overwrite)
    {
        foreach (var path in paths)
        {
            if (new FileInfo(path).LinkTarget is not null) throw new IOException($"Refusing to replace a symbolic link: {path}");
            if (Directory.Exists(path) || (File.Exists(path) && !overwrite))
                throw new IOException($"Output already exists: {path}. Use --overwrite to replace package files.");
        }
    }

    static void PublishFiles(List<(string Source, string Destination)> files, bool overwrite)
    {
        CheckDestinations(files.Select(file => file.Destination), overwrite);
        foreach (var (source, destination) in files)
        {
            ThrowIfCancelled();
            var folder = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(folder);
            // Copy next to the destination, then rename, so an interrupted copy is never published.
            var temp = Path.Combine(folder, ".packaging-" + Path.GetRandomFileName());
            try
            {
                File.Copy(source, temp);
                File.Move(temp, destination, overwrite);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            Console.WriteLine($"Created {destination}");
        }
    }

    static void ThrowIfCancelled()
    {
        if (cancelled) throw new OperationCanceledException();
    }
}
