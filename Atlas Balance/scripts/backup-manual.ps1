# ══════════════════════════════════════════
# Atlas Balance — Backup Manual (CLI)
# ══════════════════════════════════════════

param(
    [string]$BackupPath = "C:\AtlasBalance\backups",
    [string]$PgBinPath = "C:\Program Files\PostgreSQL\14\bin",
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

$timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
$filename = "backup_${timestamp}.dump"
$filepath = Join-Path $BackupPath $filename

if (-not (Test-Path $BackupPath)) {
    New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
}

Write-Host "Creando backup: $filepath" -ForegroundColor Yellow

$previousPassword = $env:PGPASSWORD
$env:PGPASSWORD = Convert-SecureStringToPlain (Read-Host "Password para $DbUser" -AsSecureString)

$pgDump = Join-Path $PgBinPath "pg_dump.exe"
try {
    & $pgDump -h $DbHost -p $DbPort -U $DbUser -F c -b -v -f $filepath $DbName

    if ($LASTEXITCODE -eq 0) {
        $size = (Get-Item $filepath).Length / 1MB
        Write-Host "Backup completado: $filepath ($([math]::Round($size, 2)) MB)" -ForegroundColor Green
    } else {
        Write-Host "ERROR: Backup fallido (código: $LASTEXITCODE)" -ForegroundColor Red
    }
} finally {
    $env:PGPASSWORD = $previousPassword
}
