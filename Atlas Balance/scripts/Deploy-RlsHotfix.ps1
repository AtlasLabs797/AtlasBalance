[CmdletBinding()]
param(
    [string]$InstallPath = "C:\AtlasBalance",
    [Parameter(Mandatory = $true)][string]$PatchedDllPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Ejecuta este script como Administrador."
}

$targetDll = Join-Path $InstallPath "api\AtlasBalance.API.dll"
if (-not (Test-Path -LiteralPath $PatchedDllPath -PathType Leaf)) {
    throw "No se encontro el DLL corregido en $PatchedDllPath."
}
if (-not (Test-Path -LiteralPath $targetDll -PathType Leaf)) {
    throw "No se encontro la API instalada en $targetDll."
}

$backupDirectory = Join-Path $InstallPath "backups\rls-hotfix"
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
$backupDll = Join-Path $backupDirectory ("AtlasBalance.API.{0}.dll" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

$watchdogWasRunning = (Get-Service -Name "AtlasBalance.Watchdog" -ErrorAction Stop).Status -eq "Running"
$apiWasRunning = (Get-Service -Name "AtlasBalance.API" -ErrorAction Stop).Status -eq "Running"

try {
    if ($watchdogWasRunning) {
        Stop-Service -Name "AtlasBalance.Watchdog" -Force
    }
    if ($apiWasRunning) {
        Stop-Service -Name "AtlasBalance.API" -Force
    }

    Copy-Item -LiteralPath $targetDll -Destination $backupDll -Force
    Copy-Item -LiteralPath $PatchedDllPath -Destination $targetDll -Force

    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $PatchedDllPath).Hash -ne
        (Get-FileHash -Algorithm SHA256 -LiteralPath $targetDll).Hash) {
        throw "El hash del DLL instalado no coincide con el parche."
    }

    Start-Service -Name "AtlasBalance.API"
    if ($watchdogWasRunning) {
        Start-Service -Name "AtlasBalance.Watchdog"
    }

    $apiReady = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        $client = [Net.Sockets.TcpClient]::new()
        try {
            $connect = $client.BeginConnect("127.0.0.1", 8443, $null, $null)
            if ($connect.AsyncWaitHandle.WaitOne(1000)) {
                $client.EndConnect($connect)
                $apiReady = $client.Connected
            }
        }
        catch {
            $apiReady = $false
        }
        finally {
            $client.Dispose()
        }

        if ($apiReady) {
            break
        }
        Start-Sleep -Seconds 1
    }

    if (-not $apiReady) {
        throw "La API corregida no abrio el puerto HTTPS 8443."
    }
}
catch {
    Stop-Service -Name "AtlasBalance.API" -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $backupDll) {
        Copy-Item -LiteralPath $backupDll -Destination $targetDll -Force
    }
    if ($apiWasRunning) {
        Start-Service -Name "AtlasBalance.API" -ErrorAction SilentlyContinue
    }
    if ($watchdogWasRunning) {
        Start-Service -Name "AtlasBalance.Watchdog" -ErrorAction SilentlyContinue
    }
    throw
}

Write-Host "Parche RLS instalado."
Write-Host "Puerto HTTPS 8443 disponible."
Write-Host "Copia de seguridad: $backupDll"
