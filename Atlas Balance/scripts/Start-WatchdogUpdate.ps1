# V-02.06 (F3.4) Start-WatchdogUpdate.ps1
# Helper para que un Watchdog Watchdog antiguo (pre-V-01.09) pueda
# actualizarse a V-02.06 aunque su instalacion no tenga la convencion
# "paquete completo validado" ni `UpdateInstallPath`. Lo invoca el
# servicio Windows AtlasBalanceWatchdog cuando detecta que su VERSION
# local es `< 1.9.0` y existe una GitHub Release `latest` con `V-01.09`
# o superior (incluyendo V-02.06).
#
# El helper:
#   1. Para el servicio AtlasBalanceWatchdog (con un timeout duro).
#   2. Descarga el ZIP firmado desde GitHub Release `latest`.
#   3. Verifica firma RSA con la clave publica de UpdateSecurity.
#   4. Llama a Actualizar-AtlasBalance.ps1 con -SkipWatchdog para que el
#      script de actualizacion estandar reemplace paquete completo.
#   5. Re-arranca el servicio.
#
# Pensado para consola admin. Sin admin, aborta con codigo 3.

[CmdletBinding()]
param(
    [string]$InstallPath = 'C:\AtlasBalance',
    [string]$ServiceName = 'AtlasBalanceWatchdog',
    [string]$Repo = 'AtlasLabs797/AtlasBalance',
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Stop-ServiceSafe {
    param([string]$Name, [int]$TimeoutSec)
    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $svc) { return }
    if ($svc.Status -eq 'Stopped') { return }
    Write-Host "Deteniendo $Name ..."
    Stop-Service -Name $Name -Force -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Service -Name $Name).Status -ne 'Stopped' -and (Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
    }
    if ((Get-Service -Name $Name).Status -ne 'Stopped') {
        Write-Error "$Name no se detuvo en $TimeoutSec segundos. Aborta."
        exit 4
    }
}

function Start-ServiceSafe {
    param([string]$Name)
    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $svc) { Write-Warning "$Name no esta registrado"; return }
    if ($svc.Status -eq 'Running') { return }
    Write-Host "Iniciando $Name ..."
    Start-Service -Name $Name -ErrorAction Stop
}

# -----------------------------------------------------------------------
# 0. Permisos
# -----------------------------------------------------------------------

if (-not (Test-IsAdministrator)) {
    Write-Error "Start-WatchdogUpdate.ps1 requiere consola elevada (Stop-Service / Start-Service)."
    exit 3
}

if (-not (Test-Path $InstallPath)) {
    Write-Error "InstallPath no existe: $InstallPath"
    exit 5
}

$versionFile = Join-Path $InstallPath 'VERSION'
if (Test-Path $versionFile) {
    $currentVersion = (Get-Content $versionFile -Raw).Trim()
    Write-Host "Version local: $currentVersion"
} else {
    Write-Warning "No se encontro $versionFile. Continuando por si Watchdog lo borro."
    $currentVersion = 'V-0.0.0'
}

# -----------------------------------------------------------------------
# 1. Descargar el ZIP firmado desde GitHub Release latest
# -----------------------------------------------------------------------

$zipPath = Join-Path $env:TEMP "atlas-balance-bootstrap-$([Guid]::NewGuid().ToString('N')).zip"
$sigPath = "$zipPath.sig"

Write-Host "Descargando GitHub Release latest de $Repo ..."
$headers = @{
    'User-Agent' = 'AtlasBalance-Bootstrap/1.0'
    'Accept'     = 'application/vnd.github+json'
}
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers $headers -TimeoutSec 30
$asset = $release.assets | Where-Object { $_.name -like '*.zip' -and $_.name -notlike '*.sig' } | Select-Object -First 1
$sigAsset = $release.assets | Where-Object { $_.name -like '*.zip.sig' } | Select-Object -First 1
if (-not $asset) {
    Write-Error "La release latest no contiene ZIP firmado. Aborta."
    exit 6
}

Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zipPath -TimeoutSec 120
if ($sigAsset) {
    Invoke-WebRequest -Uri $sigAsset.browser_download_url -OutFile $sigPath -TimeoutSec 30
}

# -----------------------------------------------------------------------
# 2. Verificar firma RSA contra clave publica (si esta en el instalador)
# -----------------------------------------------------------------------

$watchdogSettings = Get-Content (Join-Path $InstallPath 'watchdog\appsettings.Production.json') -Raw -ErrorAction SilentlyContinue
$publicKeyPem = $null
if ($watchdogSettings) {
    $parsed = $watchdogSettings | ConvertFrom-Json -ErrorAction SilentlyContinue
    if ($parsed.UpdateSecurity -and $parsed.UpdateSecurity.ReleaseSigningPublicKeyPem) {
        $publicKeyPem = $parsed.UpdateSecurity.ReleaseSigningPublicKeyPem
    }
}

if ($publicKeyPem -and (Test-Path $sigPath)) {
    Write-Host "Verificando firma RSA del ZIP ..."
    $rsa = [System.Security.Cryptography.RSA]::Create()
    $rsa.ImportFromPem($publicKeyPem)
    $valid = $rsa.VerifyData(
        [System.IO.File]::ReadAllBytes($zipPath),
        [System.IO.File]::ReadAllBytes($sigPath),
        [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $rsa.Dispose()
    if (-not $valid) {
        Write-Error "La firma RSA del ZIP no valida. Aborta sin instalar."
        Remove-Item $zipPath, $sigPath -ErrorAction SilentlyContinue
        exit 7
    }
    Write-Host "Firma OK"
} else {
    Write-Warning "Sin clave publica o sin .sig. Saltando verificacion (modo legacy)."
}

# -----------------------------------------------------------------------
# 3. Parar Watchdog y delegar al actualizador estandar
# -----------------------------------------------------------------------

Stop-ServiceSafe -Name $ServiceName -TimeoutSec $TimeoutSeconds

$installer = Join-Path $InstallPath 'scripts\Actualizar-AtlasBalance.ps1'
if (-not (Test-Path $installer)) {
    Write-Error "No se encontro $installer. Reinstala V-01.09+ primero."
    exit 8
}

Write-Host "Aplicando paquete via Actualizar-AtlasBalance.ps1 ..."
& $installer -SkipWatchdog -SourcePath (Split-Path $zipPath -Parent) -PackageZipPath $zipPath

if ($LASTEXITCODE -ne 0) {
    Write-Error "Actualizar-AtlasBalance.ps1 fallo con exit code $LASTEXITCODE"
    Start-ServiceSafe -Name $ServiceName
    exit 9
}

# -----------------------------------------------------------------------
# 4. Re-arrancar Watchdog
# -----------------------------------------------------------------------

Start-ServiceSafe -Name $ServiceName
Write-Host "Watchdog actualizado y reiniciado."

# Limpieza
Remove-Item $zipPath, $sigPath -ErrorAction SilentlyContinue
exit 0
