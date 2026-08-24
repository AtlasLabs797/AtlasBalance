# ==============================================
# Atlas Balance - V-02.07 (BACKUP-RLS)
# Concede BYPASSRLS al rol owner en instalaciones YA desplegadas.
#
# Instalar-AtlasBalance.ps1 ya crea el owner con BYPASSRLS en instalaciones
# nuevas, pero eso no ayuda a lo que ya esta en produccion, y
# Actualizar-AtlasBalance.ps1 no tiene credenciales de superusuario de
# Postgres para hacerlo por su cuenta. Este script cubre ese hueco: se ejecuta
# una vez, a mano, con el superusuario de Postgres.
#
# Por que hace falta: las tablas de negocio llevan FORCE ROW LEVEL SECURITY, y
# pg_dump exige que el rol que lo ejecuta pueda saltarse RLS o el backup falla
# con error. Solo un superusuario puede conceder BYPASSRLS (ALTER ROLE ... WITH
# BYPASSRLS requiere ser superusuario o ya tener BYPASSRLS), por eso este
# script no puede correr como el rol owner ni como app_user.
# ==============================================

param(
    [string]$DbHost = "localhost",
    [int]$DbPort = 5432,
    [string]$DbName = "atlas_balance",
    [string]$DbOwnerUser = "atlas_balance_owner",
    [string]$PostgresAdminUser = "postgres",
    [string]$PostgresAdminPassword = "",
    [string]$PostgresBinPath = ""
)

$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Find-PostgresBin {
    param([string]$PreferredPath)

    if (-not [string]::IsNullOrWhiteSpace($PreferredPath) -and
        (Test-Path (Join-Path $PreferredPath "psql.exe"))) {
        return (Resolve-Path $PreferredPath).Path
    }

    $psqlCommand = Get-Command "psql.exe" -ErrorAction SilentlyContinue
    if ($psqlCommand) {
        return Split-Path -Parent $psqlCommand.Source
    }

    $candidates = @(
        "C:\Program Files\PostgreSQL\18\bin",
        "C:\Program Files\PostgreSQL\17\bin",
        "C:\Program Files\PostgreSQL\16\bin",
        "C:\Program Files\PostgreSQL\15\bin"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate "psql.exe")) {
            return $candidate
        }
    }

    return ""
}

function Quote-PgIdentifier {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -gt 63) {
        throw "Identificador PostgreSQL invalido."
    }

    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "Identificador PostgreSQL invalido: $Value"
    }

    return '"' + $Value.Replace('"', '""') + '"'
}

function Invoke-Psql {
    param(
        [string]$PsqlExe,
        [string]$Sql,
        [string]$Database = "postgres",
        [switch]$Scalar
    )

    $args = @(
        "-h", $DbHost,
        "-p", [string]$DbPort,
        "-U", $PostgresAdminUser,
        "-d", $Database,
        "-v", "ON_ERROR_STOP=1"
    )

    if ($Scalar) {
        $args += @("-t", "-A")
    } else {
        $args += @("-q")
    }

    # V-02.08 (fix): 2>&1 sobre nativo bajo EAP=Stop convierte NOTICE/stderr
    # de psql en terminating y daba "psql fallo" falso. Se baja EAP solo aqui.
    $previousEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = $Sql | & $PsqlExe @args 2>&1
    } finally {
        $ErrorActionPreference = $previousEap
    }
    if ($LASTEXITCODE -ne 0) {
        throw "psql fallo: $output"
    }

    if ($Scalar) {
        return (($output | Out-String).Trim())
    }
    return $output
}

if (-not (Test-IsAdmin)) {
    throw "Ejecuta este script como Administrador."
}

if ([string]::IsNullOrWhiteSpace($PostgresAdminPassword)) {
    throw "Falta -PostgresAdminPassword (la del superusuario '$PostgresAdminUser'). Este script no la pide de forma interactiva; pasala como parametro."
}

$postgresBin = Find-PostgresBin -PreferredPath $PostgresBinPath
if ([string]::IsNullOrWhiteSpace($postgresBin)) {
    throw 'No se encontro psql.exe. Indica -PostgresBinPath "C:\Program Files\PostgreSQL\17\bin".'
}
$psql = Join-Path $postgresBin "psql.exe"

$ownerRoleIdentifier = Quote-PgIdentifier $DbOwnerUser

$previousPassword = $env:PGPASSWORD
$env:PGPASSWORD = $PostgresAdminPassword
try {
    Write-Host "Concediendo BYPASSRLS al rol owner '$DbOwnerUser' en ${DbHost}:${DbPort}/$DbName..." -ForegroundColor Yellow
    Invoke-Psql -PsqlExe $psql -Database $DbName -Sql "ALTER ROLE $ownerRoleIdentifier WITH BYPASSRLS;" | Out-Null

    $ownerLiteral = $DbOwnerUser.Replace("'", "''")
    $verified = Invoke-Psql -PsqlExe $psql -Database $DbName -Scalar -Sql "SELECT rolbypassrls FROM pg_roles WHERE rolname = '$ownerLiteral';"
    if ($verified -ne "t") {
        throw "Verificacion fallida: pg_roles.rolbypassrls para '$DbOwnerUser' es '$verified' (se esperaba 't'). BYPASSRLS no quedo aplicado."
    }
} finally {
    $env:PGPASSWORD = $previousPassword
}

Write-Host "OK: el rol owner '$DbOwnerUser' tiene BYPASSRLS. pg_dump (BackupService.cs) ya puede hacer backups con FORCE RLS activo." -ForegroundColor Green
