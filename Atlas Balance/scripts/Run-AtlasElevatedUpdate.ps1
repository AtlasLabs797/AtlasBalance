param(
    [Parameter(Mandatory = $true)][string]$InstallPath,
    [Parameter(Mandatory = $true)][string]$RequestPath
)

$ErrorActionPreference = "Stop"
$watchdogBinary = Join-Path $InstallPath "watchdog\AtlasBalance.Watchdog.exe"
$runnerDirectory = Join-Path $InstallPath "config\update-runner"
$runnerBinary = Join-Path $runnerDirectory "AtlasBalance.Watchdog.exe"

if (-not (Test-Path -LiteralPath $watchdogBinary -PathType Leaf)) {
    throw "No se encontro el binario protegido del runner de actualizacion."
}
New-Item -ItemType Directory -Path $runnerDirectory -Force | Out-Null
Copy-Item -LiteralPath $watchdogBinary -Destination $runnerBinary -Force

& $runnerBinary `
    --run-elevated-update `
    --install-path $InstallPath `
    --request-path $RequestPath
exit $LASTEXITCODE
