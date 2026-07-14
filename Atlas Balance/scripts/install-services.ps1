# ══════════════════════════════════════════
# Atlas Balance — Instalar Windows Services
# Ejecutar como Administrador en el servidor
# ══════════════════════════════════════════

param(
    [string]$InstallPath = "C:\AtlasBalance",
    [string]$ApiPort = "443",
    [string]$ServiceAccount = "AtlasBalanceSvc"
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

# V-02-05 (CONFIG-019): crear cuenta de servicio de bajo privilegio si no existe.
# LocalService es mas restrictivo que LocalSystem y suficiente para la app
# (no necesita acceso a HKLM fuera de su path, ni a otros servicios).
$serviceAccountExists = Get-LocalUser -Name $ServiceAccount -ErrorAction SilentlyContinue
if (-not $serviceAccountExists) {
    Write-Host "Creando cuenta de servicio de bajo privilegio: $ServiceAccount" -ForegroundColor Yellow
    $securePassword = ConvertTo-SecureString -String ([Guid]::NewGuid().ToString() + [Guid]::NewGuid().ToString().Substring(0, 8)) -AsPlainText -Force
    New-LocalUser -Name $ServiceAccount `
        -Password $securePassword `
        -PasswordNeverExpires `
        -UserMayNotChangePassword `
        -AccountNeverExpires `
        -Description "Cuenta de servicio para Atlas Balance (bajo privilegio)" `
        -ErrorAction Stop
    # Permitir logon como servicio. Requiere secedit / export.
    Write-Host "Cuenta $ServiceAccount creada. Configure 'Logon as a service' manualmente si es necesario." -ForegroundColor Yellow
}

# ACLs: el servicio solo necesita lectura/escritura en su install path.
$serviceAccountObj = "$env:COMPUTERNAME\$ServiceAccount"
$installPathAcl = Get-Acl $InstallPath
$readRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    $serviceAccountObj, "Read,Write,Modify,Delete,Synchronize", "ContainerInherit,ObjectInherit", "None", "Allow")
$installPathAcl.AddAccessRule($readRule)
try {
    Set-Acl -Path $InstallPath -AclObject $installPathAcl -ErrorAction SilentlyContinue
} catch {
    Write-Warning "No se pudieron aplicar ACLs sobre $InstallPath. Revisar manualmente."
}

# Instalar el servicio. New-Service sin -Credential usa LocalSystem por defecto.
New-Service -Name $apiServiceName `
    -BinaryPathName ('"' + $apiExe + '"') `
    -DisplayName "Atlas Balance - API" `
    -Description "API REST y frontend para Atlas Balance" `
    -StartupType Automatic

# Configurar auto-restart on failure
sc.exe failure $apiServiceName reset=86400 actions=restart/10000/restart/30000/restart/60000

Write-Host "$apiServiceName instalado (cuenta: $serviceAccountObj si configuro 'Logon as a service')" -ForegroundColor Green

Write-Host "" -ForegroundColor Yellow
Write-Host "IMPORTANTE: para que la cuenta de bajo privilegio funcione, configure:" -ForegroundColor Yellow
Write-Host "  secpol.msc -> Local Policies -> User Rights Assignment -> Log on as a service" -ForegroundColor Yellow
Write-Host "  Agregue el usuario $ServiceAccount si no aparece." -ForegroundColor Yellow

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
