# Release packaging

`BuildPackages.cs` builds the Store and sideload MSIX packages for x64 and ARM64.
It runs with the .NET 10 SDK or later. From the `FluentDL` project folder:

```powershell
dotnet run --file .\PackageSelect\BuildPackages.cs -- --version 3.8.0.0
```

See the wiki's [Packaging](https://github.com/DerekYang2/FluentDL/wiki/Packaging) page for
requirements, options, output files and Windows Sandbox testing.
