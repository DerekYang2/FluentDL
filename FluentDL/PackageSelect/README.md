# Building release packages

Requires Python 3.10+ and Visual Studio with the Windows SDK and WinUI/MSIX build tools.
Run these commands from the `FluentDL` project directory (the parent of `PackageSelect`).
No need to run the old Store/sideload switching scripts first.

## Build all four variants

```powershell
python .\PackageSelect\build_packages.py --version 3.8.0.0
```

This builds Store and Sideload packages for both x64 and ARM64:

- Store `.msix` files: `artifacts\3.8.0.0\Store\x64` and `Store\arm64`.
- Sideload release ZIPs: `artifacts\3.8.0.0\Sideload`.
- With a sandbox share configured, unpacked sideload packages and installers go
  into `FluentDL_3.8.0.0_x64` and `FluentDL_3.8.0.0_arm64` beneath that share.

Change the version for a new release; Store versions must end in `.0`.
Use `--overwrite` only when replacing existing outputs for the same version.
Store packages and ZIPs are not copied to the sandbox share.

## Local paths (not committed)

No machine-specific paths are required in source. Command-line options override
environment defaults:

| Option | Environment variable | Default when unset |
| --- | --- | --- |
| `--output` | `FLUENTDL_PACKAGE_OUTPUT` | `artifacts` under the project |
| `--sandbox-share` | `FLUENTDL_SANDBOX_SHARE` | No sandbox copy |
| `--msbuild` | `FLUENTDL_MSBUILD` | Auto-detect Visual Studio, including previews |

`--no-sandbox` disables copying even if the environment variable is set.
Blank environment values are treated as unset. Configured paths must exist
where required; invalid paths produce errors rather than silently falling back.
Sandbox staging is needed only for Sideload builds.

For example, set your existing sandbox shared folder for this terminal:

```powershell
$env:FLUENTDL_SANDBOX_SHARE = 'C:\path\to\your\SandboxShare'
```

To persist it for your Windows user (not the repository):

```powershell
[Environment]::SetEnvironmentVariable(
    'FLUENTDL_SANDBOX_SHARE', $env:FLUENTDL_SANDBOX_SHARE, 'User')
```

Restart terminals and Visual Studio after changing user-level variables so new
processes inherit them. Use the same pattern for an optional artifact root or
MSBuild override; leave them unset to use the defaults. Relative command-line
paths resolve from the current working directory.

## Other examples

```powershell
# Build only sideload x64.
python .\PackageSelect\build_packages.py --version 3.8.0.0 --channel Sideload --architecture x64 --overwrite

# Build everything without copying to SandboxShare.
python .\PackageSelect\build_packages.py --version 3.8.0.0 --no-sandbox --overwrite

# Show all options.
python .\PackageSelect\build_packages.py --help
```

If your terminal is already inside `PackageSelect`, use `python .\build_packages.py`
instead, with the same arguments. Default output locations stay the same.

## What the build does

The script uses the current project/package references and versioned copies of
the channel manifests. It does not modify source manifests or the project.
Intermediates are isolated under each project's `obj\Packaging` and `bin\Packaging`.
Do not run concurrent builds of the same variant.

Store outputs are unsigned `.msix` files, not `.msixupload`; sideload outputs are
ZIPs containing the self-contained MSIX plus `install_fluentdl.cmd` and its
PowerShell helper. Extract a ZIP before running the CMD. Unsigned installation
requires elevation using the same Windows account. Building ARM64 on x64 does not
verify ARM64 execution.

Identity, version, architecture, and Windows App SDK dependency mode are checked
before the selected variants are published. Build failures leave existing release
packages untouched; diagnostics remain under `artifacts\<version>\logs`.
Do not publish build logs without reviewing their local paths.

Release builds generate portable PDBs by default. Keep matching PDBs and binaries
privately for each released version/architecture before rebuilding; the release
ZIP is not a symbol archive. `AppxSymbolPackageEnabled=false` disables the extra
symbol package, not managed stack traces.

## Legacy configuration switchers

`select.py` and `select.ps1` remain available for manually changing the source
project between Store/Sideload configurations. Unlike `build_packages.py`, these
replace source files and create `.bak` backups. Commit or save your work first.
Use `Restore` before switching again if you need the original configuration.
Both entry points preserve current package references through the Python implementation.

The legacy templates use project-relative package directories by default.
`FLUENTDL_STORE_PACKAGE_DIR` and `FLUENTDL_SIDELOAD_PACKAGE_DIR` override the
corresponding directory through MSBuild's environment properties. These optional
values should end in a backslash. They do not affect `build_packages.py`, which
always supplies its own isolated staging directory.

## Checks

```powershell
python -m unittest discover -s .\PackageSelect -p "test_*.py"
```
