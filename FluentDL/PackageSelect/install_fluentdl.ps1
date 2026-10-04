param([switch]$Elevated)

$ErrorActionPreference = "Stop"

try {
    $packages = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter "*.msix" -File)
    if ($packages.Count -ne 1) {
        throw "Expected exactly one MSIX next to this installer; found $($packages.Count)."
    }

    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Add-AppxPackage -Path $packages[0].FullName -AllowUnsigned -ForceApplicationShutdown
    } else {
        if ($Elevated) {
            throw "Administrator privileges are required to install this unsigned package."
        }
        Write-Host "Installing FluentDL. Please accept the administrator prompt."
        $process = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList (
            '-NoProfile -ExecutionPolicy Bypass -File "{0}" -Elevated' -f $PSCommandPath
        )
        if ($process.ExitCode -ne 0) {
            throw "Installation failed (exit code $($process.ExitCode))."
        }
    }

    if (-not $Elevated) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
        try {
            $entry = $archive.GetEntry("AppxManifest.xml")
            if ($null -eq $entry) { throw "The MSIX has no AppxManifest.xml." }
            $reader = [IO.StreamReader]::new($entry.Open())
            try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        } finally {
            $archive.Dispose()
        }
        $installed = @(Get-AppxPackage -Name $manifest.Package.Identity.Name | Where-Object {
            $_.Publisher -eq $manifest.Package.Identity.Publisher
        })
        if ($installed.Count -ne 1) {
            throw "The package was not found for the current user. Accept elevation using the same Windows account."
        }
        $appId = $manifest.Package.Applications.Application.Id
        Start-Process "shell:AppsFolder\$($installed[0].PackageFamilyName)!$appId"
        Write-Host "FluentDL installed successfully." -ForegroundColor Green
    }
    exit 0
} catch {
    [Console]::Error.WriteLine("ERROR: $($_.Exception.Message)")
    exit 1
}
