# ══════════════════════════════════════════
# Atlas Balance — Backup Manual (CLI)
# ══════════════════════════════════════════

param(
    [string]$BackupPath = "C:\AtlasBalance\backups",
    [string]$PgBinPath = "C:\Program Files\PostgreSQL\16\bin",
    [string]$DbName = "atlas_balance",
    # V-02.08 (fix): el defecto era el rol runtime atlas_balance_app. Con
    # FORCE ROW LEVEL SECURITY ese rol NO ve las filas de negocio, asi que
    # pg_dump terminaba exit 0 con un backup vacio. El defecto ahora es el
    # rol owner (BYPASSRLS); el preflight de abajo aborta si el rol no puede.
    [string]$DbUser = "atlas_owner",
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

$timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
$filename = "backup_${timestamp}.dump"
$filepath = Join-Path $BackupPath $filename

if (-not (Test-Path $BackupPath)) {
    New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
}

Write-Host "Creando backup: $filepath" -ForegroundColor Yellow

$previousPassword = $env:PGPASSWORD
$env:PGPASSWORD = Convert-SecureStringToPlain (Read-Host "Password para $DbUser" -AsSecureString)

# V-02.08 (fix): preflight anti-backup-vacio. Con FORCE RLS, un rol sin
# BYPASSRLS hace un dump exit 0 SIN filas de negocio (mismo riesgo que
# Actualizar-AtlasBalance.ps1 ya aborta). Mejor abortar que fingir backup.
$psql = Join-Path $PgBinPath "psql.exe"
if (-not (Test-Path $psql)) {
    Write-Host "ERROR: no se encontro psql.exe en $PgBinPath. No se puede verificar el rol; abortando." -ForegroundColor Red
    $env:PGPASSWORD = $previousPassword
    exit 1
}

$bypassRaw = & $psql -h $DbHost -p $DbPort -U $DbUser -d $DbName -t -A -c "SELECT rolbypassrls FROM pg_roles WHERE rolname = current_user;" 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: no se pudo conectar como $DbUser para verificar BYPASSRLS (codigo: $LASTEXITCODE). Abortando." -ForegroundColor Red
    $env:PGPASSWORD = $previousPassword
    exit 1
}
if ($bypassRaw -ne 't') {
    Write-Host "ERROR: el rol '$DbUser' NO tiene BYPASSRLS." -ForegroundColor Red
    Write-Host "Con FORCE ROW LEVEL SECURITY el dump saldria vacio de datos de negocio aunque pg_dump termine bien."
    Write-Host "Usa el rol owner (atlas_owner) o un superusuario: .\backup-manual.ps1 -DbUser atlas_owner"
    $env:PGPASSWORD = $previousPassword
    exit 1
}

$pgDump = Join-Path $PgBinPath "pg_dump.exe"
try {
    & $pgDump -h $DbHost -p $DbPort -U $DbUser -F c -b -v -f $filepath $DbName

    if ($LASTEXITCODE -eq 0) {
        $size = (Get-Item $filepath).Length / 1MB
        Write-Host "Backup completado: $filepath ($([math]::Round($size, 2)) MB)" -ForegroundColor Green
        # V-02.08 (fix): propagar el resultado para automatizacion.
        exit 0
    } else {
        Write-Host "ERROR: Backup fallido (código: $LASTEXITCODE)" -ForegroundColor Red
        exit 1
    }
} finally {
    $env:PGPASSWORD = $previousPassword
}
