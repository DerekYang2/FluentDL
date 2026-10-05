# Fails if a native binary in a build output isn't built for the given architecture.
# Managed assemblies are skipped, since .NET runs them on any architecture.
param(
    [Parameter(Mandatory)] [string] $Folder,
    [ValidateSet('arm64', 'x64')] [string] $Architecture = 'arm64',
    # Files allowed to be another architecture, relative to the folder.
    [string[]] $Allow = @()
)

$expected = @{ arm64 = 0xAA64; x64 = 0x8664 }[$Architecture]
$names = @{ 0x14C = 'x86'; 0x8664 = 'x64'; 0xAA64 = 'arm64'; 0x1C4 = 'arm' }
$wrong = @()
$checked = 0

foreach ($file in Get-ChildItem -LiteralPath $Folder -Recurse -File -Include *.exe, *.dll) {
    $relative = $file.FullName.Substring((Resolve-Path $Folder).Path.Length).TrimStart('\')
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    if ($bytes.Length -lt 0x40 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { continue }
    $pe = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($pe -le 0 -or $pe + 24 -gt $bytes.Length) { continue }
    $machine = [BitConverter]::ToUInt16($bytes, $pe + 4)
    $optional = $pe + 24
    $magic = [BitConverter]::ToUInt16($bytes, $optional)
    $directories = $optional + $(if ($magic -eq 0x20B) { 112 } else { 96 })
    # Data directory 14 is the CLR header, which only managed assemblies have.
    if ([BitConverter]::ToUInt32($bytes, $directories + 14 * 8) -ne 0) { continue }
    $checked++
    if ($machine -ne $expected -and $Allow -notcontains $relative) {
        $name = $names[[int]$machine]; if (-not $name) { $name = '0x{0:X}' -f $machine }
        $wrong += "$relative is $name"
    }
}

Write-Host "Checked $checked native binaries in $Folder"
if ($wrong.Count -gt 0) {
    $wrong | ForEach-Object { Write-Host "::error::$_, expected $Architecture" }
    exit 1
}
