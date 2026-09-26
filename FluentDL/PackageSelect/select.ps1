[CmdletBinding()]
param(
    [ValidateSet("Store", "Sideload", "Restore")]
    [string]$Mode,
    [string]$Version
)

$ErrorActionPreference = "Stop"
$arguments = @((Join-Path $PSScriptRoot "select.py"))
if ($Mode) { $arguments += @("--mode", $Mode) }
if ($Version) { $arguments += @("--version", $Version) }

# Share the package-reference-preserving implementation instead of replacing it with stale templates.
& python @arguments
exit $LASTEXITCODE
