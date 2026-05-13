param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Quote-Argument {
    param([string]$Value)

    if ($Value -notmatch "[\s']") {
        return $Value
    }

    return "'" + ($Value -replace "'", "''") + "'"
}

$scriptPath = $MyInvocation.MyCommand.Path
$installer = Join-Path $PSScriptRoot "Instalar-AtlasBalance.ps1"
if (-not (Test-Path $installer)) {
    throw "No se encontro $installer."
}

$packageRoot = Split-Path -Parent $PSScriptRoot
$apiExe = Join-Path $packageRoot "api\AtlasBalance.API.exe"
$watchdogExe = Join-Path $packageRoot "watchdog\AtlasBalance.Watchdog.exe"
if (-not (Test-Path $apiExe) -or -not (Test-Path $watchdogExe)) {
    throw "Esta carpeta no es el paquete instalable. Genera o descarga AtlasBalance-V-01.06-win-x64.zip. Ejecuta el instalador desde la carpeta descomprimida que contiene api\AtlasBalance.API.exe, watchdog\AtlasBalance.Watchdog.exe, scripts e install.cmd."
}

$forwardArgs = @()
if ($RemainingArgs -notcontains "-InstallDependencies" -and $RemainingArgs -notcontains "-SkipDatabaseSetup") {
    $forwardArgs += "-InstallDependencies"
}
$forwardArgs += $RemainingArgs

if (-not (Test-IsAdmin)) {
    $argumentList = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", (Quote-Argument $scriptPath)
    ) + ($forwardArgs | ForEach-Object { Quote-Argument $_ })

    Start-Process -FilePath "powershell.exe" -ArgumentList ($argumentList -join " ") -Verb RunAs | Out-Null
    exit 0
}

& $installer @forwardArgs
