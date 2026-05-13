# ══════════════════════════════════════════
# Atlas Balance — Instalar Windows Services
# Ejecutar como Administrador en el servidor
# ══════════════════════════════════════════

param(
    [string]$InstallPath = "C:\AtlasBalance",
    [string]$ApiPort = "443"
)

Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Instalación de Windows Services" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: Ejecutar como Administrador" -ForegroundColor Red
    exit 1
}

$apiExe = Join-Path $InstallPath "api\AtlasBalance.API.exe"
$watchdogExe = Join-Path $InstallPath "watchdog\AtlasBalance.Watchdog.exe"
$apiServiceName = "AtlasBalance.API"
$watchdogServiceName = "AtlasBalance.Watchdog"

# Verify binaries exist
if (-not (Test-Path $apiExe)) {
    Write-Host "ERROR: No se encuentra $apiExe" -ForegroundColor Red
    Write-Host "Primero ejecutar: dotnet publish -c Release -o $InstallPath\api" -ForegroundColor Yellow
    exit 1
}
if (-not (Test-Path $watchdogExe)) {
    Write-Host "ERROR: No se encuentra $watchdogExe" -ForegroundColor Red
    Write-Host "Primero ejecutar: dotnet publish -c Release -o $InstallPath\watchdog" -ForegroundColor Yellow
    exit 1
}

# Create required directories
$dirs = @("$InstallPath\backups", "$InstallPath\exports", "$InstallPath\logs", "$InstallPath\certs")
foreach ($dir in $dirs) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "Directorio creado: $dir" -ForegroundColor Green
    }
}

# ── Install API Service ──
Write-Host "`nInstalando $apiServiceName..." -ForegroundColor Yellow

$apiService = Get-Service -Name $apiServiceName -ErrorAction SilentlyContinue
if ($apiService) {
    Write-Host "Servicio $apiServiceName ya existe. Deteniendo..." -ForegroundColor Yellow
    Stop-Service -Name $apiServiceName -Force
    sc.exe delete $apiServiceName
    Start-Sleep -Seconds 2
}

New-Service -Name $apiServiceName `
    -BinaryPathName ('"' + $apiExe + '"') `
    -DisplayName "Atlas Balance - API" `
    -Description "API REST y frontend para Atlas Balance" `
    -StartupType Automatic

# Configure auto-restart on failure
sc.exe failure $apiServiceName reset=86400 actions=restart/10000/restart/30000/restart/60000

Write-Host "$apiServiceName instalado" -ForegroundColor Green

# ── Install Watchdog Service ──
Write-Host "`nInstalando $watchdogServiceName..." -ForegroundColor Yellow

$watchdogService = Get-Service -Name $watchdogServiceName -ErrorAction SilentlyContinue
if ($watchdogService) {
    Write-Host "Servicio $watchdogServiceName ya existe. Deteniendo..." -ForegroundColor Yellow
    Stop-Service -Name $watchdogServiceName -Force
    sc.exe delete $watchdogServiceName
    Start-Sleep -Seconds 2
}

New-Service -Name $watchdogServiceName `
    -BinaryPathName ('"' + $watchdogExe + '"') `
    -DisplayName "Atlas Balance - Watchdog" `
    -Description "Servicio de backup y actualización para Atlas Balance" `
    -StartupType Automatic

sc.exe failure $watchdogServiceName reset=86400 actions=restart/10000/restart/30000/restart/60000

Write-Host "$watchdogServiceName instalado" -ForegroundColor Green

# ── Start services ──
Write-Host "`nIniciando servicios..." -ForegroundColor Yellow
Start-Service -Name $watchdogServiceName
Start-Service -Name $apiServiceName

Start-Sleep -Seconds 3

$apiStatus = (Get-Service -Name $apiServiceName).Status
$watchdogStatus = (Get-Service -Name $watchdogServiceName).Status

Write-Host "`n═══════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  ${apiServiceName}:      $apiStatus" -ForegroundColor $(if ($apiStatus -eq 'Running') { 'Green' } else { 'Red' })
Write-Host "  ${watchdogServiceName}: $watchdogStatus" -ForegroundColor $(if ($watchdogStatus -eq 'Running') { 'Green' } else { 'Red' })
Write-Host "═══════════════════════════════════════" -ForegroundColor Cyan

if ($apiStatus -eq 'Running') {
    Write-Host "`nAcceder a: https://localhost:$ApiPort" -ForegroundColor Green
    Write-Host "Login inicial: usuario de SeedAdmin__Email y password de SeedAdmin__Password. No uses defaults en produccion." -ForegroundColor Yellow
}
