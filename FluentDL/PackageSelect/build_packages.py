import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile


SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_DIR = SCRIPT_DIR.parent
CHANNELS = ("Store", "Sideload")
ARCHITECTURES = ("x64", "arm64")
INSTALLERS = ("install_fluentdl.cmd", "install_fluentdl.ps1")
PACKAGE_NS = "{http://schemas.microsoft.com/appx/manifest/foundation/windows10}"

def environment_path(name):
    value = os.environ.get(name, "").strip()
    return Path(os.path.expandvars(value)).expanduser() if value else None


def package_version(value):
    if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", value):
        raise argparse.ArgumentTypeError("Version must have four numeric parts, e.g. 3.8.0.0.")
    parts = [int(part) for part in value.split(".")]
    if parts[0] == 0 or any(part > 65535 for part in parts):
        raise argparse.ArgumentTypeError("Version components must be 0-65535, with a nonzero major version.")
    return value


def find_msbuild(explicit_path):
    if explicit_path is not None:
        candidate = explicit_path.resolve()
    else:
        vswhere = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / (
            r"Microsoft Visual Studio\Installer\vswhere.exe"
        )
        if not vswhere.is_file():
            raise FileNotFoundError("Visual Studio Installer not found. Supply --msbuild with MSBuild.exe's path.")
        result = subprocess.run(
            [str(vswhere), "-latest", "-prerelease", "-products", "*",
             "-requires", "Microsoft.Component.MSBuild", "-find", r"MSBuild\Current\Bin\MSBuild.exe"],
            check=True, capture_output=True, text=True,
        )
        paths = result.stdout.strip().splitlines()
        if not paths:
            raise FileNotFoundError("Visual Studio MSBuild not found. Install the WinUI/MSIX build tools.")
        candidate = Path(paths[0])
    if not candidate.is_file():
        raise FileNotFoundError(f"MSBuild executable not found: {candidate}")
    return candidate


def write_versioned_manifest(source, destination, version, element, attribute):
    content = source.read_text(encoding="utf-8-sig")
    ET.fromstring(content)
    pattern = rf'(<{element}\b[^>]*?\b{attribute}=")[^"]+(")'
    content, count = re.subn(
        pattern, lambda match: match[1] + version + match[2], content,
    )
    if count != 1:
        raise ValueError(f"Expected exactly one {element} {attribute} in {source}; found {count}.")
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(content, encoding="utf-8")


def msbuild_command(msbuild, channel, architecture, manifest, app_manifest, package_dir, log):
    properties = {
        "Configuration": "Release",
        "Platform": architecture,
        "PlatformTarget": architecture,
        "RuntimeIdentifier": f"win-{architecture}",
        "CustomBeforeDirectoryBuildProps": str(SCRIPT_DIR / "Packaging.props"),
        "FluentDLPackageChannel": channel,
        "FluentDLPackageManifest": str(manifest),
        "ApplicationManifest": str(app_manifest),
        "SelfContained": "true",
        "WindowsAppSDKSelfContained": str(channel == "Sideload").lower(),
        "WindowsAppSdkIncludeVersionInfo": "true",
        "WindowsPackageType": "MSIX",
        "GenerateAppxPackageOnBuild": "true",
        "UapAppxPackageBuildMode": "StoreOnly" if channel == "Store" else "SideloadOnly",
        "AppxPackageIsForStore": str(channel == "Store").lower(),
        "BuildAppxUploadPackageForUap": "false",
        "AppxBundle": "Never",
        "AppxBundlePlatforms": architecture,
        "AppxPackageSigningEnabled": "false",
        "AppxSymbolPackageEnabled": "false",
        "AppxAutoIncrementPackageRevision": "false",
        "GenerateTestArtifacts": "false",
        "GenerateAppInstallerFile": "false",
        "AppxPackageDir": str(package_dir) + "\\",
        "PublishProfile": "",
    }
    return [
        str(msbuild), str(PROJECT_DIR / "FluentDL.csproj"),
        "-nologo", "-restore", "-t:Build", "-verbosity:minimal", "-nr:false",
        f"-flp:LogFile={log};Verbosity=normal;Encoding=UTF-8",
        *(f"-p:{key}={value}" for key, value in properties.items()),
    ]


def validate_package(package, manifest, architecture, channel):
    expected = ET.parse(manifest).getroot().find(f"{PACKAGE_NS}Identity")
    if expected is None:
        raise ValueError(f"No package Identity in {manifest}")
    with zipfile.ZipFile(package) as archive:
        broken = archive.testzip()
        if broken is not None:
            raise ValueError(f"Corrupt MSIX member: {broken}")
        root = ET.fromstring(archive.read("AppxManifest.xml"))
        identity = root.find(f"{PACKAGE_NS}Identity")
        if identity is None:
            raise ValueError(f"No package Identity in {package}")
        for field in ("Name", "Publisher", "Version"):
            if identity.get(field) != expected.get(field):
                raise ValueError(f"{package}: {field} does not match the {channel} manifest.")
        if identity.get("ProcessorArchitecture") != architecture:
            raise ValueError(f"{package}: expected {architecture} architecture.")
        if "AppxSignature.p7x" in archive.namelist():
            raise ValueError(f"{package}: expected an unsigned package.")
        dependencies = root.findall(f"{PACKAGE_NS}Dependencies/{PACKAGE_NS}PackageDependency")
        runtime_dependency = any(
            item.get("Name", "").startswith("Microsoft.WindowsAppRuntime") for item in dependencies
        )
        if runtime_dependency != (channel == "Store"):
            raise ValueError(f"{package}: unexpected Windows App SDK dependency for {channel}.")


def create_release_zip(folder, destination):
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for file in sorted(folder.iterdir()):
            if not file.is_file():
                raise ValueError(f"Unexpected directory in release payload: {file}")
            archive.write(file, f"{folder.name}/{file.name}")


def output_paths(version, channels, architectures, output, sandbox):
    paths = []
    for channel in channels:
        for architecture in architectures:
            name = f"FluentDL_{version}_{architecture}"
            if channel == "Store":
                paths.append(output / version / channel / architecture / f"{name}.msix")
            else:
                paths.append(output / version / channel / f"{name}.zip")
                if sandbox is not None:
                    folder = sandbox / name
                    paths.extend(folder / file for file in (f"{name}.msix", *INSTALLERS))
                    if folder.is_dir():
                        unexpected = [file for file in folder.glob("*.msix") if file.name != f"{name}.msix"]
                        if unexpected:
                            raise FileExistsError(f"Extra MSIX files in {folder}; move them before staging.")
    return paths


def check_destinations(paths, overwrite):
    for path in paths:
        if path.is_symlink():
            raise FileExistsError(f"Refusing to replace a symbolic link: {path}")
        if path.exists() and (not overwrite or not path.is_file()):
            raise FileExistsError(f"Output already exists: {path}. Use --overwrite to replace package files.")


def publish_files(files, overwrite):
    check_destinations([destination for _, destination in files], overwrite)
    for source, destination in files:
        destination.parent.mkdir(parents=True, exist_ok=True)
        # Copy on the destination volume, then rename, so interrupted copies aren't published.
        with tempfile.NamedTemporaryFile(dir=destination.parent, prefix=".packaging-", delete=False) as temp:
            temp_path = Path(temp.name)
        try:
            shutil.copyfile(source, temp_path)
            if overwrite:
                temp_path.replace(destination)
            else:
                temp_path.rename(destination)
        finally:
            temp_path.unlink(missing_ok=True)
        print(f"Created {destination}", flush=True)


def build_packages(args):
    channels = CHANNELS if args.channel == "All" else (args.channel,)
    architectures = ARCHITECTURES if args.architecture == "All" else (args.architecture,)
    if "Store" in channels and args.version.split(".")[-1] != "0":
        raise ValueError("Store versions must end in .0; the Store reserves the revision component.")
    output = args.output.resolve()
    sandbox = None if args.no_sandbox or args.sandbox_share is None else args.sandbox_share.resolve()
    if sandbox is not None and "Sideload" in channels and not sandbox.is_dir():
        raise FileNotFoundError(f"Sandbox share does not exist: {sandbox}. Use --sandbox-share or --no-sandbox.")
    destinations = output_paths(args.version, channels, architectures, output, sandbox)
    check_destinations(destinations, args.overwrite)
    for installer in INSTALLERS:
        if "Sideload" in channels and not (SCRIPT_DIR / installer).is_file():
            raise FileNotFoundError(f"Missing installer: {SCRIPT_DIR / installer}")
    msbuild = find_msbuild(args.msbuild)
    output.mkdir(parents=True, exist_ok=True)
    log_dir = output / args.version / "logs"
    log_dir.mkdir(parents=True, exist_ok=True)
    print(f"Using {msbuild}", flush=True)
    with tempfile.TemporaryDirectory(prefix=".packaging-", dir=output) as temp:
        working = Path(temp)
        files = []
        for channel in channels:
            for architecture in architectures:
                print(f"\nBuilding {channel} / {architecture} / {args.version}", flush=True)
                variant = working / channel / architecture
                variant.mkdir(parents=True)
                manifest = variant / "Package.appxmanifest"
                app_manifest = variant / "app.manifest"
                write_versioned_manifest(
                    SCRIPT_DIR / f"{channel.lower()}.appxmanifest.txt",
                    manifest, args.version, "Identity", "Version",
                )
                write_versioned_manifest(
                    PROJECT_DIR / "app.manifest", app_manifest, args.version, "assemblyIdentity", "version",
                )
                package_dir = variant / "AppPackages"
                log = log_dir / f"{channel}-{architecture}.log"
                command = msbuild_command(
                    msbuild, channel, architecture, manifest, app_manifest, package_dir, log,
                )
                result = subprocess.run(command, cwd=PROJECT_DIR, check=False)
                if result.returncode != 0:
                    raise RuntimeError(f"{channel}/{architecture} build failed ({result.returncode}). See {log}")
                name = f"FluentDL_{args.version}_{architecture}"
                packages = list(package_dir.rglob(f"{name}.msix"))
                if len(packages) != 1:
                    raise ValueError(f"Expected one {name}.msix in {package_dir}; found {len(packages)}. See {log}")
                validate_package(packages[0], manifest, architecture, channel)
                if channel == "Store":
                    files.append((packages[0], output / args.version / channel / architecture / f"{name}.msix"))
                else:
                    payload = variant / name
                    payload.mkdir()
                    shutil.copyfile(packages[0], payload / f"{name}.msix")
                    for installer in INSTALLERS:
                        shutil.copyfile(SCRIPT_DIR / installer, payload / installer)
                    archive = variant / f"{name}.zip"
                    create_release_zip(payload, archive)
                    files.append((archive, output / args.version / channel / archive.name))
                    if sandbox is not None:
                        files.extend((file, sandbox / name / file.name) for file in sorted(payload.iterdir()))
        # A failed build never publishes a partial set of newly built variants.
        publish_files(files, args.overwrite)


def parse_args(argv=None):
    parser = argparse.ArgumentParser(
        description="Build Store MSIX files and sideload release ZIPs; stage unpacked sideloads for Windows Sandbox.",
    )
    parser.add_argument("--version", required=True, type=package_version, help="Package version, e.g. 3.8.0.0")
    parser.add_argument("--channel", choices=("All", *CHANNELS), default="All")
    parser.add_argument("--architecture", choices=("All", *ARCHITECTURES), default="All")
    parser.add_argument("--output", type=Path, default=environment_path("FLUENTDL_PACKAGE_OUTPUT") or PROJECT_DIR / "artifacts",
                        help="Artifact root (default: FLUENTDL_PACKAGE_OUTPUT or the project's artifacts folder)")
    sandbox = parser.add_mutually_exclusive_group()
    sandbox.add_argument("--sandbox-share", type=Path, default=environment_path("FLUENTDL_SANDBOX_SHARE"),
                         help="Existing staging folder (default: FLUENTDL_SANDBOX_SHARE; unset disables staging)")
    sandbox.add_argument("--no-sandbox", action="store_true", help="Build artifacts without copying to the sandbox share")
    parser.add_argument("--msbuild", type=Path, default=environment_path("FLUENTDL_MSBUILD"),
                        help="MSBuild executable (default: FLUENTDL_MSBUILD or auto-detect Visual Studio, including previews)")
    parser.add_argument("--overwrite", action="store_true", help="Replace matching output files, leaving other files alone")
    return parser.parse_args(argv)


def main():
    args = parse_args()
    try:
        build_packages(args)
    except (OSError, ValueError, RuntimeError, subprocess.CalledProcessError, ET.ParseError, zipfile.BadZipFile) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("Packaging cancelled.", file=sys.stderr)
        return 130
    print("\nPackaging complete.", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
