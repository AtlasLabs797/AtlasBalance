param(
    [string]$InstallPath = "C:\AtlasBalance",
    [string]$Url = ""
)

$ErrorActionPreference = "Stop"

$launcher = Join-Path $PSScriptRoot "Launch-AtlasBalance.ps1"
if (-not (Test-Path $launcher)) {
    throw "No se encontro $launcher."
}

if ([string]::IsNullOrWhiteSpace($Url)) {
    & $launcher -InstallPath $InstallPath
} else {
    & $launcher -InstallPath $InstallPath -Url $Url
}
