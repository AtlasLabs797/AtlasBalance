param(
    [string]$InstallPath = "C:\AtlasBalance",
    [string]$AdminEmail = "admin@atlasbalance.local",
    [string]$PostgresBinPath = "",
    [Security.SecureString]$NewPassword,
    [switch]$GeneratePassword
)

$ErrorActionPreference = "Stop"

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

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function New-RandomSecret {
    param([int]$Length = 24)

    $alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!#%_-"
    $bytes = New-Object byte[] $Length
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    } finally {
        $rng.Dispose()
    }

    $chars = New-Object char[] $Length
    for ($i = 0; $i -lt $Length; $i++) {
        $chars[$i] = $alphabet[$bytes[$i] % $alphabet.Length]
    }
    return -join $chars
}

function Protect-SecretDirectory {
    param([string]$Path)

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    & icacls.exe $Path /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo restringir ACL en $Path. No se escribiran credenciales en claro."
    }
}

function Write-SecretFile {
    param(
        [string]$Path,
        [string]$Body
    )

    $directory = Split-Path -Parent $Path
    Protect-SecretDirectory -Path $directory
    Set-Content -LiteralPath $Path -Value $Body -Encoding UTF8
    & icacls.exe $Path /inheritance:r /grant:r "*S-1-5-32-544:F" "*S-1-5-18:F" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        throw "No se pudo restringir ACL en $Path. El archivo de credenciales se elimino."
    }
}

function Find-PostgresBin {
    param(
        [string]$PreferredPath,
        [string]$ConfiguredPath
    )

    foreach ($candidate in @($PreferredPath, $ConfiguredPath)) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and
            (Test-Path (Join-Path $candidate "psql.exe"))) {
            return (Resolve-Path $candidate).Path
        }
    }

    $psqlCommand = Get-Command "psql.exe" -ErrorAction SilentlyContinue
    if ($psqlCommand) {
        return Split-Path -Parent $psqlCommand.Source
    }

    foreach ($candidate in @(
        "C:\Program Files\PostgreSQL\18\bin",
        "C:\Program Files\PostgreSQL\17\bin",
        "C:\Program Files\PostgreSQL\16\bin",
        "C:\Program Files\PostgreSQL\15\bin",
        "C:\Program Files\PostgreSQL\16\bin"
    )) {
        if (Test-Path (Join-Path $candidate "psql.exe")) {
            return $candidate
        }
    }

    return ""
}

function Get-ConnectionInfo {
    param([string]$ConnectionString)

    $values = @{}
    foreach ($part in ($ConnectionString -split ";")) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part -notmatch "=") {
            continue
        }
        $separatorIndex = $part.IndexOf("=")
        $key = $part.Substring(0, $separatorIndex)
        $value = $part.Substring($separatorIndex + 1)
        $values[$key.Trim().ToLowerInvariant()] = $value.Trim()
    }

    $port = $values["port"]
    if ([string]::IsNullOrWhiteSpace($port)) {
        $port = "5432"
    }

    return [ordered]@{
        Host = $values["host"]
        Port = [int]$port
        Database = $values["database"]
        Username = $values["username"]
        Password = $values["password"]
    }
}

function Get-PasswordHash {
    param([string]$PlainPassword)

    $apiPath = Join-Path $InstallPath "api"
    foreach ($candidate in @(
        (Join-Path $apiPath "BCrypt.Net-Next.dll"),
        (Join-Path $apiPath "BCrypt.Net.dll")
    )) {
        if (Test-Path $candidate) {
            Add-Type -Path $candidate
            return [BCrypt.Net.BCrypt]::HashPassword($PlainPassword, 12)
        }
    }

    throw "No se encontro BCrypt.Net en $apiPath. Ejecuta este script desde una instalacion completa de Atlas Balance."
}

if (-not (Test-IsAdmin)) {
    throw "Ejecuta este script como Administrador."
}

$apiConfigPath = Join-Path $InstallPath "api\appsettings.Production.json"
if (-not (Test-Path $apiConfigPath)) {
    throw "No se encontro $apiConfigPath. Indica -InstallPath con la instalacion real."
}

$config = Get-Content -LiteralPath $apiConfigPath -Raw | ConvertFrom-Json
$connectionString = $config.ConnectionStrings.DefaultConnection
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw "appsettings.Production.json no contiene ConnectionStrings:DefaultConnection."
}

$plainPassword = ""
$passwordWasGenerated = $false
if ($GeneratePassword) {
    $plainPassword = New-RandomSecret 24
    $passwordWasGenerated = $true
} elseif ($NewPassword) {
    $plainPassword = Convert-SecureStringToPlain $NewPassword
} else {
    $first = Read-Host "Nueva password temporal para $AdminEmail" -AsSecureString
    $second = Read-Host "Repite la password temporal" -AsSecureString
    $plainFirst = Convert-SecureStringToPlain $first
    $plainSecond = Convert-SecureStringToPlain $second
    if ($plainFirst -ne $plainSecond) {
        throw "Las passwords no coinciden."
    }
    $plainPassword = $plainFirst
}

if ([string]::IsNullOrWhiteSpace($plainPassword) -or $plainPassword.Length -lt 12) {
    throw "La password temporal debe tener al menos 12 caracteres."
}

$connection = Get-ConnectionInfo -ConnectionString $connectionString
$configuredPostgresBin = ""
if ($config.WatchdogSettings -and $config.WatchdogSettings.PostgresBinPath) {
    $configuredPostgresBin = [string]$config.WatchdogSettings.PostgresBinPath
}

$postgresBin = Find-PostgresBin -PreferredPath $PostgresBinPath -ConfiguredPath $configuredPostgresBin
if ([string]::IsNullOrWhiteSpace($postgresBin)) {
    throw 'No se encontro psql.exe. Indica -PostgresBinPath "C:\Program Files\PostgreSQL\17\bin".'
}

$psql = Join-Path $postgresBin "psql.exe"
$passwordHash = Get-PasswordHash -PlainPassword $plainPassword
$securityStamp = [Guid]::NewGuid().ToString("N")

$sql = @"
WITH target_user AS (
    SELECT id
    FROM "USUARIOS"
    WHERE lower(email) = lower(:'email')
      AND rol = 0
      AND deleted_at IS NULL
    LIMIT 1
),
updated_user AS (
    UPDATE "USUARIOS" u
    SET password_hash = :'password_hash',
        primer_login = TRUE,
        activo = TRUE,
        failed_login_attempts = 0,
        locked_until = NULL,
        security_stamp = :'security_stamp',
        password_changed_at = now()
    FROM target_user
    WHERE u.id = target_user.id
    RETURNING u.id
),
revoked_tokens AS (
    UPDATE "REFRESH_TOKENS" rt
    SET revocado_en = now(),
        reemplazado_por = 'admin-password-reset'
    WHERE rt.usuario_id IN (SELECT id FROM updated_user)
      AND rt.revocado_en IS NULL
      AND rt.expira_en > now()
    RETURNING rt.id
)
SELECT
    (SELECT count(*) FROM updated_user) AS usuarios_actualizados,
    (SELECT count(*) FROM revoked_tokens) AS refresh_tokens_revocados;
"@

$previousPassword = $env:PGPASSWORD
$env:PGPASSWORD = $connection.Password
try {
    $args = @(
        "-h", $connection.Host,
        "-p", [string]$connection.Port,
        "-U", $connection.Username,
        "-d", $connection.Database,
        "-v", "ON_ERROR_STOP=1",
        "-v", "email=$AdminEmail",
        "-v", "password_hash=$passwordHash",
        "-v", "security_stamp=$securityStamp",
        "-t",
        "-A",
        "-F", "|",
        "-f", "-"
    )

    $output = $sql | & $psql @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "psql fallo: $output"
    }
} finally {
    $env:PGPASSWORD = $previousPassword
}

$resultLine = (($output | Out-String).Trim() -split "\r?\n" | Select-Object -Last 1)
$parts = $resultLine -split "\|"
if ($parts.Count -lt 2 -or [int]$parts[0] -lt 1) {
    throw "No se encontro una cuenta admin activa con email $AdminEmail."
}

Write-Host "Password admin reseteada para $AdminEmail." -ForegroundColor Green
Write-Host "Login bloqueado limpiado, primer_login activado y refresh tokens revocados: $($parts[1])." -ForegroundColor Green
if ($passwordWasGenerated) {
    $credentialsDir = Join-Path $InstallPath "config"
    $credentialsFile = Join-Path $credentialsDir "RESET_ADMIN_CREDENTIALS_ONCE.txt"
    $credentialsBody = @(
        "Atlas Balance - reset de password admin"
        "Fecha: $(Get-Date -Format o)"
        "Email: $AdminEmail"
        "Password temporal: $plainPassword"
        ""
        "Borra este archivo despues de iniciar sesion y cambiar la password."
    ) -join [Environment]::NewLine
    Write-SecretFile -Path $credentialsFile -Body $credentialsBody
    Write-Host "Password temporal escrita en: $credentialsFile" -ForegroundColor Yellow
    Write-Host "Acceso restringido a Administrators. Borra el archivo tras iniciar sesion." -ForegroundColor Yellow
} else {
    Write-Host "Usa la password temporal introducida y cambiala en el primer acceso." -ForegroundColor Yellow
}
