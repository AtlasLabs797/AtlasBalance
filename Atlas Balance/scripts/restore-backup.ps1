# ══════════════════════════════════════════
# Atlas Balance — Restaurar Backup (CLI)
# ADVERTENCIA: Esto reemplazará TODA la base de datos
# ══════════════════════════════════════════

param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFile,
    [string]$PgBinPath = "C:\Program Files\PostgreSQL\16\bin",
    [string]$DbName = "atlas_balance",
    [string]$DbUser = "atlas_balance_app",
    [string]$DbHost = "localhost",
    [int]$DbPort = 5432
)

function Convert-SecureStringToPlain {
    param([Security.SecureString]$Value)

    if ($null -eq $Value) {
        return ""
    }

    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    }
}

if (-not (Test-Path $BackupFile)) {
    Write-Host "ERROR: Archivo no encontrado: $BackupFile" -ForegroundColor Red
    exit 1
}

if ([IO.Path]::GetExtension($BackupFile) -ne ".dump") {
    Write-Host "ERROR: Solo se aceptan backups .dump" -ForegroundColor Red
    exit 1
}

Write-Host "═══════════════════════════════════════" -ForegroundColor Red
Write-Host "  ADVERTENCIA: Restauración destructiva" -ForegroundColor Red
Write-Host "  Base de datos: $DbName" -ForegroundColor Red
Write-Host "  Archivo: $BackupFile" -ForegroundColor Red
Write-Host "═══════════════════════════════════════" -ForegroundColor Red

$confirm = Read-Host "`n¿Estás COMPLETAMENTE seguro? Escribe 'RESTAURAR' para confirmar"
if ($confirm -ne 'RESTAURAR') {
    Write-Host "Cancelado." -ForegroundColor Yellow
    exit 0
}

$apiServiceName = "AtlasBalance.API"
$apiService = Get-Service -Name $apiServiceName -ErrorAction SilentlyContinue
if ($apiService -and $apiService.Status -eq 'Running') {
    Write-Host "Deteniendo $apiServiceName..." -ForegroundColor Yellow
    Stop-Service -Name $apiServiceName -Force
    Start-Sleep -Seconds 3
}

$previousPassword = $env:PGPASSWORD
$env:PGPASSWORD = Convert-SecureStringToPlain (Read-Host "Password para $DbUser" -AsSecureString)

$pgRestore = Join-Path $PgBinPath "pg_restore.exe"

Write-Host "Restaurando..." -ForegroundColor Yellow
try {
    & $pgRestore -h $DbHost -p $DbPort -U $DbUser -d $DbName --clean --if-exists -v $BackupFile

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Restauración completada" -ForegroundColor Green
    } else {
        Write-Host "Restauración completada con advertencias (código: $LASTEXITCODE)" -ForegroundColor Yellow
    }
} finally {
    $env:PGPASSWORD = $previousPassword
}

# Restart API service
if ($apiService) {
    Write-Host "Reiniciando $apiServiceName..." -ForegroundColor Yellow
    Start-Service -Name $apiServiceName
    Write-Host "Servicio iniciado" -ForegroundColor Green
}
