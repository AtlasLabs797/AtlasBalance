param(
    [string]$PackagePath = "",
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
$packageRoot = if ([string]::IsNullOrWhiteSpace($PackagePath)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($PackagePath)
}
$scriptsRoot = Join-Path $packageRoot "scripts"
$updater = Join-Path $scriptsRoot "Actualizar-AtlasBalance.ps1"
if (-not (Test-Path $updater)) {
    throw "No se encontro $updater."
}

$apiExe = Join-Path $packageRoot "api\GestionCaja.API.exe"
$watchdogExe = Join-Path $packageRoot "watchdog\GestionCaja.Watchdog.exe"
if (-not (Test-Path $apiExe) -or -not (Test-Path $watchdogExe)) {
    throw "Esta carpeta no es el paquete de actualizacion. Usa -PackagePath con la carpeta descomprimida de AtlasBalance-V-XX-win-x64 o ejecuta update.cmd desde dentro del paquete."
}

if (-not (Test-IsAdmin)) {
    $argumentList = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", (Quote-Argument $scriptPath),
        "-PackagePath", (Quote-Argument $packageRoot)
    ) + ($RemainingArgs | ForEach-Object { Quote-Argument $_ })

    Start-Process -FilePath "powershell.exe" -ArgumentList ($argumentList -join " ") -Verb RunAs | Out-Null
    exit 0
}

& $updater @RemainingArgs
