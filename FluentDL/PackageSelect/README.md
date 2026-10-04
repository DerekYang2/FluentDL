# Release packaging

Double-click `build_packages.cmd`, or run it without options, and answer the prompts.
It builds the Store and sideload MSIX packages for x64 and ARM64 with `BuildPackages.cs`,
which needs the .NET 10 SDK or later.

To skip the prompts, pass options, for example:

```powershell
.\PackageSelect\build_packages.cmd --version 3.8.0.0 --channel Store
```

See the wiki's [Packaging](https://github.com/DerekYang2/FluentDL/wiki/Packaging) page for
requirements, options, output files and Windows Sandbox testing.
