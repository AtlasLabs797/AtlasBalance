<#
    Alias fino en ingles del instalador canonico Instalar-AtlasBalance.ps1.
    Aporta solo autoelevacion, validacion previa del paquete y el valor por
    defecto de -InstallDependencies. La version del paquete no se escribe
    aqui: se lee del archivo VERSION de la raiz del paquete.
#>

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
$versionFile = Join-Path $packageRoot "VERSION"
if (-not (Test-Path -LiteralPath $versionFile)) {
    throw "No se encontro $versionFile; no se puede determinar la version del paquete instalable."
}
$packageVersion = (Get-Content -LiteralPath $versionFile -Raw).Trim()
if ($packageVersion -notmatch '^V-\d{2}[-.]\d{2}$') {
    throw "La version del paquete en $versionFile no es valida ('$packageVersion'). Se esperaba el formato V-XX.XX."
}
$apiExe = Join-Path $packageRoot "api\AtlasBalance.API.exe"
$watchdogExe = Join-Path $packageRoot "watchdog\AtlasBalance.Watchdog.exe"
if (-not (Test-Path $apiExe) -or -not (Test-Path $watchdogExe)) {
    throw "Esta carpeta no es el paquete instalable. Genera o descarga AtlasBalance-$packageVersion-win-x64.zip. Ejecuta el instalador desde la carpeta descomprimida que contiene api\AtlasBalance.API.exe, watchdog\AtlasBalance.Watchdog.exe, scripts e Instalar Atlas Balance.cmd."
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
