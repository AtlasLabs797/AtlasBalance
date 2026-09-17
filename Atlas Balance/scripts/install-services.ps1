# Atlas Balance - registro endurecido de servicios Windows.
# Ejecutar como Administrador en el servidor.

param(
    [string]$InstallPath = "C:\AtlasBalance",
    [string]$ApiPort = "443",
    [string]$ApiServiceAccount = "AtlasBalanceApiSvc",
    [string]$WatchdogServiceAccount = "AtlasBalanceWatchdogSvc"
)

$ErrorActionPreference = "Stop"
$apiServiceName = "AtlasBalance.API"
$watchdogServiceName = "AtlasBalance.Watchdog"
$securityModule = Join-Path $PSScriptRoot "ServiceSecurity.ps1"
if (-not (Test-Path -LiteralPath $securityModule)) { throw "No se encontro ServiceSecurity.ps1 junto al instalador de servicios." }
. $securityModule

if (-not (Test-IsAdmin)) { throw "Ejecuta este script como Administrador." }
$apiExe = Join-Path $InstallPath "api\AtlasBalance.API.exe"
$watchdogExe = Join-Path $InstallPath "watchdog\AtlasBalance.Watchdog.exe"
if (-not (Test-Path -LiteralPath $apiExe) -or -not (Test-Path -LiteralPath $watchdogExe)) { throw "No se encuentran los binarios API/Watchdog en $InstallPath." }

foreach ($dir in @("api", "watchdog", "scripts", "backups", "exports", "logs", "updates", "config")) {
    New-Item -ItemType Directory -Path (Join-Path $InstallPath $dir) -Force | Out-Null
}

$apiAccount = Initialize-AtlasServiceAccount -Name $ApiServiceAccount -Description "Cuenta minima del servicio Atlas Balance API"
$watchdogAccount = Initialize-AtlasServiceAccount -Name $WatchdogServiceAccount -Description "Cuenta minima del servicio Atlas Balance Watchdog"
Grant-AtlasLogOnAsService -Sids @($apiAccount.Sid, $watchdogAccount.Sid) -InstallPath $InstallPath
Protect-AtlasInstallTree -InstallPath $InstallPath -ApiAccount $apiAccount -WatchdogAccount $watchdogAccount

Install-AtlasService -Name $watchdogServiceName -DisplayName "Atlas Balance - Watchdog" -Description "Servicio de backup y actualizacion de Atlas Balance" -ExePath $watchdogExe -Credential $watchdogAccount.Credential -ExpectedPrincipal $watchdogAccount.Principal
Install-AtlasService -Name $apiServiceName -DisplayName "Atlas Balance - API" -Description "API REST y frontend para Atlas Balance" -ExePath $apiExe -Credential $apiAccount.Credential -ExpectedPrincipal $apiAccount.Principal
Grant-AtlasServiceControl -ServiceName $apiServiceName -AccountSid $watchdogAccount.Sid
Grant-AtlasServiceControl -ServiceName $watchdogServiceName -AccountSid $watchdogAccount.Sid

Start-Service -Name $watchdogServiceName
Start-Service -Name $apiServiceName
Start-Sleep -Seconds 3
$apiStatus = (Get-Service -Name $apiServiceName).Status
$watchdogStatus = (Get-Service -Name $watchdogServiceName).Status
Write-Host "${apiServiceName}: $apiStatus"
Write-Host "${watchdogServiceName}: $watchdogStatus"
if ($apiStatus -ne "Running" -or $watchdogStatus -ne "Running") { throw "Uno o ambos servicios no arrancaron." }
Assert-AtlasServiceIdentities -ApiServiceName $apiServiceName -WatchdogServiceName $watchdogServiceName | Out-Null
Install-AtlasUpdateTask -InstallPath $InstallPath -WatchdogAccount $watchdogAccount
Write-Host "Servicios registrados con cuentas separadas, credenciales explicitas y SeServiceLogonRight verificado." -ForegroundColor Green
