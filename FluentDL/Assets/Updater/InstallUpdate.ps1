param([Parameter(Mandatory)][string]$PlanPath)
$ErrorActionPreference = 'Stop'
$plan = $null
$source = $null
$packageLock = $null
$archiveLock = $null
$proceeded = $false
$folder = Split-Path -Parent ([IO.Path]::GetFullPath($PlanPath))

function Reply([bool]$Ready) {
    $temporary = Join-Path $folder 'ready.tmp'
    @{ Nonce=$plan.Nonce; Ready=$Ready } | ConvertTo-Json | Set-Content -LiteralPath $temporary -Encoding UTF8
    Move-Item -LiteralPath $temporary -Destination (Join-Path $folder 'ready.json') -Force
}

try {
    $plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
    $publisher = 'CN=DD45FB67-1F5A-4180-88DE-453755BBC3D4, OID.2.25.311729368913984317654407730594956997722=1'
    if ($plan.Name -ne 'DerekYang2.FluentDL' -or $plan.Publisher -ne $publisher -or
        $plan.Architecture -notin @('x64','arm64') -or $plan.Nonce -notmatch '^[a-f0-9]{32}$') {
        throw 'Invalid FluentDL update plan.'
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or $identity.User.Value -ne $plan.UserSid) {
        throw 'Accept administrator permission with the same Windows account that runs FluentDL.'
    }
    $installed = @(Get-AppxPackage -Name $plan.Name | Where-Object { $_.Publisher -eq $plan.Publisher })
    if ($installed.Count -ne 1 -or $plan.Aumid -ne ($installed[0].PackageFamilyName + '!App')) { throw 'The installed package identity changed.' }
    $source = Get-Process -Id $plan.ProcessId
    if ($source.StartTime.ToUniversalTime().Ticks -ne $plan.Started -or
        $source.Path -ne (Join-Path $installed[0].InstallLocation 'FluentDL.exe')) { throw 'The running FluentDL process changed.' }
    $target = [Version]$plan.Version
    if ($target -le [Version]$installed[0].Version) { throw 'This update is not newer than the installed app.' }
    if ($plan.Tag -notin @($target.ToString(), ('v' + $target), ('V' + $target))) { throw 'Invalid release tag.' }
    $release = Invoke-RestMethod -Uri ('https://api.github.com/repos/DerekYang2/FluentDL/releases/tags/' + [Uri]::EscapeDataString($plan.Tag)) -Headers @{ 'User-Agent'='FluentDL-Updater/1.0'; Accept='application/vnd.github+json' } -TimeoutSec 20
    $name = 'FluentDL_' + $target + '_' + $plan.Architecture
    $asset = @($release.assets | Where-Object { $_.name -eq ($name + '.zip') })
    if ($release.draft -or $release.prerelease -or $asset.Count -ne 1 -or $asset[0].digest -notmatch '^sha256:[a-fA-F0-9]{64}$') {
        throw 'GitHub did not confirm a stable release asset.'
    }
    # Independent verification prevents a modified local plan from authorizing arbitrary elevated package installation.
    $archiveLock = [IO.File]::Open($plan.ArchivePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    if ($archiveLock.Length -ne $asset[0].size -or $archiveLock.Length -gt 1073741824 -or
        ('sha256:' + (Get-FileHash -LiteralPath $plan.ArchivePath -Algorithm SHA256).Hash) -ne $asset[0].digest) { throw 'The archive does not match GitHub.' }
    $packageLock = [IO.File]::Open($plan.PackagePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $package = Get-Item -LiteralPath $plan.PackagePath
    if ($package.PSIsContainer -or $package.Length -ne $plan.Size -or (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash -ne $plan.Sha256) {
        throw 'The downloaded package failed verification.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $releaseZip = [IO.Compression.ZipFile]::OpenRead($plan.ArchivePath)
    try {
        $entry = $releaseZip.GetEntry($name + '/' + $name + '.msix')
        if (-not $entry -or $entry.Length -ne $package.Length) { throw 'The verified archive has no matching package.' }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $expected = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $stream.Dispose(); $sha.Dispose() }
        if ($expected -ne $plan.Sha256) { throw 'The package does not match the verified release archive.' }
    } finally { $releaseZip.Dispose() }
    $zip = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $manifest = $zip.GetEntry('AppxManifest.xml')
        if (-not $manifest -or $manifest.Length -gt 1048576) { throw 'Invalid update manifest.' }
        $stream = $manifest.Open()
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $settings.MaxCharactersInDocument = 1048576
        $reader = [Xml.XmlReader]::Create($stream, $settings)
        try { $xml = [Xml.XmlDocument]::new(); $xml.Load($reader) } finally { $reader.Dispose(); $stream.Dispose() }
        $actual = $xml.Package.Identity
        if ($actual.Name -ne $plan.Name -or $actual.Publisher -ne $plan.Publisher -or $actual.ProcessorArchitecture -ne $plan.Architecture -or
            $actual.Version -ne $plan.Version) { throw 'The update package identity does not match.' }
    } finally { $zip.Dispose() }

    Reply $true
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    while (-not (Test-Path -LiteralPath (Join-Path $folder 'proceed'))) {
        if ($source.HasExited -or [DateTime]::UtcNow -ge $deadline -or (Test-Path -LiteralPath (Join-Path $folder 'cancel'))) {
            throw 'Installation was canceled before FluentDL closed.'
        }
        Start-Sleep -Milliseconds 200
    }
    if ((Get-Content -LiteralPath (Join-Path $folder 'proceed') -Raw).Trim() -ne $plan.Nonce) { throw 'Invalid installation confirmation.' }
    $proceeded = $true
    if (-not $source.WaitForExit(120000)) { throw 'FluentDL did not close normally. The update was not installed.' }
    if ((Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash -ne $plan.Sha256) { throw 'The update changed after validation.' }
    Add-AppxPackage -Path $package.FullName -AllowUnsigned
    $packageLock.Dispose()
    $archiveLock.Dispose()
    $updated = @(Get-AppxPackage -Name $plan.Name | Where-Object { $_.Publisher -eq $plan.Publisher -and [Version]$_.Version -eq $target })
    if ($updated.Count -ne 1) { throw 'Windows did not confirm the new app version.' }
    @{ Success=$true; Version=$plan.Version } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'result.json') -Encoding UTF8
    Start-Process ('shell:AppsFolder\' + $plan.Aumid)
    exit 0
}
catch {
    if ($packageLock) { $packageLock.Dispose() }
    if ($archiveLock) { $archiveLock.Dispose() }
    $errorType = $_.Exception.GetType().Name
    $errorMessage = $_.Exception.Message
    if ($plan) {
        try { Reply $false } catch { }
        try { @{ Success=$false; Error=$errorType } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'result.json') -Encoding UTF8 } catch { }
    }
    Write-Host ('Update failed: ' + $errorMessage)
    if ($proceeded -and $source -and $source.HasExited) {
        try { Start-Process ('shell:AppsFolder\' + $plan.Aumid) } catch { }
    }
    [void](Read-Host 'Press Enter to close this installer')
    exit 1
}
