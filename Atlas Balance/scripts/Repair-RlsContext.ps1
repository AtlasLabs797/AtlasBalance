[CmdletBinding()]
param(
    [string]$InstallPath = "C:\AtlasBalance",
    [string]$PostgresBinPath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ConnectionInfo {
    param([Parameter(Mandatory = $true)][string]$ConnectionString)

    # DbConnectionStringBuilder de .NET Framework no conoce las claves de
    # Npgsql y puede interpretar toda la cadena como una unica propiedad
    # llamada ConnectionString. El instalador genera valores sin punto y coma,
    # asi que separamos cada par por el primer '=' para conservar passwords que
    # puedan contener otros signos iguales.
    $parts = @{}
    foreach ($segment in ($ConnectionString -split ";")) {
        $equalsIndex = $segment.IndexOf("=")
        if ($equalsIndex -le 0) {
            continue
        }

        $key = $segment.Substring(0, $equalsIndex).Trim()
        $value = $segment.Substring($equalsIndex + 1).Trim()
        if (-not [string]::IsNullOrWhiteSpace($key)) {
            $parts[$key] = $value
        }
    }

    $hostName = if ($parts.ContainsKey("Host")) { [string]$parts["Host"] } else { [string]$parts["Server"] }
    $portText = [string]$parts["Port"]
    $database = if ($parts.ContainsKey("Database")) { [string]$parts["Database"] } else { [string]$parts["Initial Catalog"] }
    $username = if ($parts.ContainsKey("Username")) {
        [string]$parts["Username"]
    }
    elseif ($parts.ContainsKey("User ID")) {
        [string]$parts["User ID"]
    }
    elseif ($parts.ContainsKey("UserId")) {
        [string]$parts["UserId"]
    }
    else {
        [string]$parts["User"]
    }
    $password = [string]$parts["Password"]

    if ([string]::IsNullOrWhiteSpace($hostName) -or
        [string]::IsNullOrWhiteSpace($database) -or
        [string]::IsNullOrWhiteSpace($username)) {
        throw "La cadena de conexion no contiene Host, Database y Username validos."
    }

    $port = 5432
    if (-not [string]::IsNullOrWhiteSpace($portText)) {
        $port = [int]$portText
    }

    return [pscustomobject]@{
        Host = $hostName
        Port = $port
        Database = $database
        Username = $username
        Password = $password
    }
}

function Invoke-Psql {
    param(
        [Parameter(Mandatory = $true)]$Connection,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    foreach ($value in @($Connection.Host, $Connection.Database, $Connection.Username)) {
        if ([string]$value -match '["\r\n]') {
            throw "La cadena de conexion contiene un valor no admitido para psql."
        }
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $script:PsqlExe
    $startInfo.Arguments = '-h "{0}" -p {1} -U "{2}" -d "{3}" -w -X -A -t -v ON_ERROR_STOP=1' -f `
        $Connection.Host, $Connection.Port, $Connection.Username, $Connection.Database
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $previousPassword = [Environment]::GetEnvironmentVariable("PGPASSWORD", "Process")
    $previousConnectTimeout = [Environment]::GetEnvironmentVariable("PGCONNECT_TIMEOUT", "Process")
    try {
        [Environment]::SetEnvironmentVariable("PGPASSWORD", $Connection.Password, "Process")
        [Environment]::SetEnvironmentVariable("PGCONNECT_TIMEOUT", "10", "Process")
        if (-not $process.Start()) {
            throw "No se pudo iniciar psql.exe."
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Write($Sql)
        $process.StandardInput.Close()
        $process.WaitForExit()

        $exitCode = $process.ExitCode
        $standardOutput = $stdoutTask.GetAwaiter().GetResult()
        $standardError = $stderrTask.GetAwaiter().GetResult()
    }
    finally {
        [Environment]::SetEnvironmentVariable("PGPASSWORD", $previousPassword, "Process")
        [Environment]::SetEnvironmentVariable("PGCONNECT_TIMEOUT", $previousConnectTimeout, "Process")
        $process.Dispose()
    }

    if ($exitCode -ne 0) {
        $diagnostic = @($standardOutput, $standardError) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        throw "psql fallo con codigo $exitCode. $($diagnostic -join [Environment]::NewLine)"
    }

    return @($standardOutput -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

if (-not (Test-IsAdministrator)) {
    throw "Ejecuta este script desde PowerShell como Administrador."
}

$configPath = Join-Path $InstallPath "api\appsettings.Production.json"
if (-not (Test-Path -LiteralPath $configPath)) {
    throw "No se encontro la configuracion instalada en $configPath."
}

if ([string]::IsNullOrWhiteSpace($PostgresBinPath)) {
    $PostgresBinPath = Join-Path $InstallPath "postgresql\16\bin"
}

$script:PsqlExe = Join-Path $PostgresBinPath "psql.exe"
if (-not (Test-Path -LiteralPath $script:PsqlExe)) {
    throw "No se encontro psql.exe en $PostgresBinPath."
}

$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$rlsSecret = ([string]$config.Security.RlsContextSecret).Trim()
$runtimeConnectionString = [string]$config.ConnectionStrings.DefaultConnection
$migrationConnectionString = [string]$config.ConnectionStrings.MigrationConnection

if ([string]::IsNullOrWhiteSpace($rlsSecret) -or $rlsSecret.Length -lt 32) {
    throw "Security:RlsContextSecret no esta configurado correctamente."
}

if ([string]::IsNullOrWhiteSpace($runtimeConnectionString) -or
    [string]::IsNullOrWhiteSpace($migrationConnectionString)) {
    throw "Faltan DefaultConnection o MigrationConnection en la configuracion instalada."
}

$runtimeConnection = Get-ConnectionInfo -ConnectionString $runtimeConnectionString
$migrationConnection = Get-ConnectionInfo -ConnectionString $migrationConnectionString
$escapedSecret = $rlsSecret.Replace("'", "''")
$repairSql = @"
SET client_min_messages TO warning;
BEGIN;
INSERT INTO atlas_security.rls_context_secret (id, secret, updated_at)
VALUES (true, '$escapedSecret', now())
ON CONFLICT (id) DO UPDATE
SET secret = EXCLUDED.secret,
    updated_at = now();
REVOKE ALL ON TABLE atlas_security.rls_context_secret FROM PUBLIC;
-- Elimina el respaldo provisional si una ejecucion anterior llego a crearlo.
-- La reparacion definitiva conserva la policy estricta basada en contexto RLS.
DROP POLICY IF EXISTS auditorias_runtime_signed_insert ON "AUDITORIAS";
COMMIT;
SELECT 'RLS_SECRET_ALIGNED';
"@

$repairResult = Invoke-Psql -Connection $migrationConnection -Sql $repairSql
if ($repairResult -notcontains "RLS_SECRET_ALIGNED") {
    throw "PostgreSQL no confirmo la alineacion del secreto RLS."
}

$payload = "auth|||false|false|auth"
$hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($rlsSecret))
try {
    $signatureBytes = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($payload))
    $signature = ([BitConverter]::ToString($signatureBytes)).Replace("-", "").ToLowerInvariant()
}
finally {
    $hmac.Dispose()
}

$verifySql = @"
SELECT
    set_config('atlas.auth_mode', 'auth', false),
    set_config('atlas.user_id', '', false),
    set_config('atlas.integration_token_id', '', false),
    set_config('atlas.is_admin', 'false', false),
    set_config('atlas.system', 'false', false),
    set_config('atlas.request_scope', 'auth', false),
    set_config('atlas.context_signature', '$signature', false);
SELECT CASE
    WHEN atlas_security.context_is_valid() AND atlas_security.is_auth_flow()
    THEN 'RLS_CONTEXT_OK'
    ELSE 'RLS_CONTEXT_INVALID'
END;
"@

$verifyResult = Invoke-Psql -Connection $runtimeConnection -Sql $verifySql
if ($verifyResult -notcontains "RLS_CONTEXT_OK") {
    throw "El rol de la aplicacion no pudo validar el contexto RLS despues de repararlo."
}

# Prueba la policy estricta con el mismo contexto auth firmado que debe publicar
# la API durante el login. La fila se revierte y nunca queda en AUDITORIAS.
$insertVerifySql = @"
BEGIN;
SELECT
    set_config('atlas.auth_mode', 'auth', false),
    set_config('atlas.user_id', '', false),
    set_config('atlas.integration_token_id', '', false),
    set_config('atlas.is_admin', 'false', false),
    set_config('atlas.system', 'false', false),
    set_config('atlas.request_scope', 'auth', false),
    set_config('atlas.context_signature', '$signature', false);
INSERT INTO "AUDITORIAS"
    (id, detalles_json, entidad_tipo, firma, origen, "timestamp", tipo_accion)
VALUES
    (gen_random_uuid(), '{}'::json, 'SEGURIDAD', repeat('A', 43) || '=', 'JOB', now(), 'RLS_REPAIR_PROBE');
ROLLBACK;
SELECT 'RLS_RUNTIME_INSERT_OK';
"@

$insertVerifyResult = Invoke-Psql -Connection $runtimeConnection -Sql $insertVerifySql
if ($insertVerifyResult -notcontains "RLS_RUNTIME_INSERT_OK") {
    throw "El rol de la aplicacion no pudo insertar en AUDITORIAS con el contexto auth firmado."
}

Restart-Service -Name "AtlasBalance.API" -Force

$healthCode = ""
for ($attempt = 1; $attempt -le 20; $attempt++) {
    try {
        $healthCode = & curl.exe -k -s -o NUL -w "%{http_code}" "https://localhost:8443/api/health"
        if ($healthCode -eq "200") {
            break
        }
    }
    catch {
        $healthCode = ""
    }

    Start-Sleep -Seconds 1
}

if ($healthCode -ne "200") {
    throw "La API se reinicio, pero /api/health no devolvio HTTP 200."
}

$installedScriptsPath = Join-Path $InstallPath "scripts"
if (-not (Test-Path -LiteralPath $installedScriptsPath)) {
    New-Item -ItemType Directory -Path $installedScriptsPath -Force | Out-Null
}
$installedScriptPath = Join-Path $installedScriptsPath "Repair-RlsContext.ps1"
if (-not [string]::Equals($PSCommandPath, $installedScriptPath, [StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $PSCommandPath -Destination $installedScriptPath -Force
}

Write-Host "Contexto RLS reparado y validado."
Write-Host "Health check HTTP 200."
Write-Host "Reparador instalado en $installedScriptPath."
