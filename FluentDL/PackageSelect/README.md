# Release packaging

`build_packages.py` builds the Store and sideload MSIX packages for x64 and ARM64.
From the `FluentDL` project folder:

```powershell
python .\PackageSelect\build_packages.py --version 3.8.0.0
```

See the wiki's [Packaging](https://github.com/DerekYang2/FluentDL/wiki/Packaging) page for
requirements, options, output files and Windows Sandbox testing.
