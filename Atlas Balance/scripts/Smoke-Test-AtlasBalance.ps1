[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
    [Parameter(Mandatory = $true)][string]$AdminEmail,
    [Parameter(Mandatory = $true)][string]$AdminPassword,
    [Parameter(Mandatory = $true)][string]$PostgresConnectionString,
    [string]$PostgresBinPath = "",
    [string]$AuditEventTypes = "LOGIN_MFA_REQUIRED,MFA_VERIFIED,LOGIN",
    [int]$HttpTimeoutSeconds = 30,
    [switch]$SkipCertificateCheck,
    # V-02.08 (revision PR #33): AuthService solo devuelve mfaSecret en la
    # respuesta de login cuando MfaSetupRequired es true (enrolamiento
    # inicial); para un admin ya enrolado lo omite a proposito, y el smoke no
    # puede calcular el TOTP sin el secreto. El operador debe pasar aqui el
    # secreto TOTP ya enrolado del admin (el mismo con el que su
    # autenticador genera codigos) para poder ejecutar el smoke contra una
    # cuenta existente.
    [string]$AdminTotpSecret = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Carga de helpers TOTP extraidos (ver Mfa-Totp.Tests.ps1 para cobertura).
$totpHelper = Join-Path $PSScriptRoot "Mfa-Totp.ps1"
if (-not (Test-Path -LiteralPath $totpHelper)) {
    throw "No se encontro $totpHelper."
}
. $totpHelper

if ($SkipCertificateCheck) {
    # Certificados self-signed en primera instalacion on-premise.
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
}

function Invoke-ApiJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [hashtable]$Body,
        $Session,
        [string]$SessionVariableName
    )

    $uri = "$ApiBaseUrl$Path"
    $params = @{
        Method = $Method
        Uri = $uri
        TimeoutSec = $HttpTimeoutSeconds
        UseBasicParsing = $true
    }
    if ($null -ne $Session) {
        $params['WebSession'] = $Session
    }
    if ($null -ne $SessionVariableName) {
        $params['SessionVariable'] = $SessionVariableName
    }
    if ($null -ne $Body) {
        $params['Body'] = ($Body | ConvertTo-Json -Depth 10 -Compress)
        $params['ContentType'] = 'application/json'
    }

    try {
        $response = Invoke-WebRequest @params -ErrorAction Stop
        $bodyText = $response.RawContentStream
        if ($null -eq $bodyText) {
            $bodyText = ""
        }
        else {
            $reader = [System.IO.StreamReader]::new($response.RawContentStream)
            $bodyText = $reader.ReadToEnd()
            $reader.Close()
        }
        $parsed = $null
        if (-not [string]::IsNullOrEmpty($bodyText)) {
            # V-02.08 (fix): -Depth no existe en PS 5.1; el binding error se
            # tragaba el catch y parsed era siempre null con fallos enganosos.
            try {
                if ($PSVersionTable.PSVersion.Major -ge 6) {
                    $parsed = $bodyText | ConvertFrom-Json -Depth 12
                } else {
                    $parsed = $bodyText | ConvertFrom-Json
                }
            }
            catch { $parsed = $null }
        }
        return @{
            StatusCode = [int]$response.StatusCode
            Body = $parsed
            RawBody = $bodyText
            Headers = $response.Headers
        }
    }
    catch [System.Net.WebException] {
        $statusCode = 0
        $stream = $_.Exception.Response
        if ($null -ne $stream) {
            $statusCode = [int]$stream.StatusCode
        }
        $bodyText = ""
        if ($null -ne $stream) {
            $reader = [System.IO.StreamReader]::new($stream.GetResponseStream())
            $bodyText = $reader.ReadToEnd()
            $reader.Close()
        }
        $parsed = $null
        if (-not [string]::IsNullOrEmpty($bodyText)) {
            # V-02.08 (fix): -Depth no existe en PS 5.1 (ver nota anterior).
            try {
                if ($PSVersionTable.PSVersion.Major -ge 6) {
                    $parsed = $bodyText | ConvertFrom-Json -Depth 6
                } else {
                    $parsed = $bodyText | ConvertFrom-Json
                }
            }
            catch { $parsed = $bodyText }
        }
        return @{ StatusCode = $statusCode; Body = $parsed; RawBody = $bodyText }
    }
}

function Get-CookieValue {
    # V-02.08 (revision PR #33): en produccion la API emite cookies con
    # prefijo __Host-atlas-<nombre> (ver AuthController.CookieName en el
    # backend); solo en Development usa el nombre corto (access_token,
    # refresh_token). Acepta ambos juegos de nombres para que el smoke
    # funcione contra una instalacion real (produccion).
    param(
        [Parameter(Mandatory = $true)]$Headers,
        [Parameter(Mandatory = $true)][string]$CookieName
    )

    if ($null -eq $Headers) {
        return $null
    }
    $candidateNames = @($CookieName, "__Host-atlas-$($CookieName.Replace('_', '-'))")
    foreach ($h in $Headers.Keys) {
        if ($h -ieq "Set-Cookie") {
            foreach ($value in $Headers[$h]) {
                $head = ($value -split ';')[0]
                foreach ($candidate in $candidateNames) {
                    if ($head -match "^$([regex]::Escape($candidate))=") {
                        return ($head -split '=', 2)[1]
                    }
                }
            }
        }
    }
    return $null
}

function Parse-ConnectionString {
    # Misma justificativa que Repair-RlsContext.ps1: DbConnectionStringBuilder
    # de .NET Framework no conoce las claves de Npgsql.
    param([Parameter(Mandatory = $true)][string]$ConnectionString)

    $parts = [ordered]@{}
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

    # V-02.09 (fix): $host es una variable automatica readonly de PowerShell;
    # asignarla lanza un error terminante. Se renombra a $pgHost.
    $pgHost = if ($parts.Contains("Host")) { $parts["Host"] } elseif ($parts.Contains("Server")) { $parts["Server"] } else { "" }
    $port = if ($parts.Contains("Port")) { [int]$parts["Port"] } else { 5432 }
    $database = if ($parts.Contains("Database")) { $parts["Database"] } elseif ($parts.Contains("Initial Catalog")) { $parts["Initial Catalog"] } else { "" }
    $username = if ($parts.Contains("Username")) { $parts["Username"] }
        elseif ($parts.Contains("User ID")) { $parts["User ID"] }
        elseif ($parts.Contains("UserId")) { $parts["UserId"] }
        else { $parts["User"] }
    $password = $parts["Password"]

    if ([string]::IsNullOrWhiteSpace($pgHost) -or
        [string]::IsNullOrWhiteSpace($database) -or
        [string]::IsNullOrWhiteSpace($username)) {
        throw "La cadena de conexion no contiene Host, Database y Username."
    }

    return [pscustomobject]@{
        Host = $pgHost
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
        $diagnostic = @($standardOutput, $standardError) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        throw "psql fallo con codigo $exitCode. $($diagnostic -join [Environment]::NewLine)"
    }

    return @($standardOutput -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

# ----------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------

$results = [ordered]@{}
$startInstant = [DateTimeOffset]::UtcNow

try {
    if ([string]::IsNullOrEmpty($PostgresBinPath)) {
        # Convencion del instalador: cuando Atlas gestiona PostgreSQL
        # en la misma maquina, los binarios viven en <InstallPath>\postgresql\16\bin.
        $candidate = Join-Path ${env:ProgramFiles} "Atlas Balance\postgresql\16\bin\psql.exe"
        if (Test-Path -LiteralPath $candidate) {
            $PostgresBinPath = Split-Path -LiteralPath $candidate -Parent
        }
        else {
            $psql = Get-Command psql.exe -ErrorAction SilentlyContinue
            if ($null -ne $psql) {
                $PostgresBinPath = Split-Path -LiteralPath $psql.Path -Parent
            }
        }
    }
    if ([string]::IsNullOrEmpty($PostgresBinPath) -or -not (Test-Path -LiteralPath (Join-Path $PostgresBinPath "psql.exe"))) {
        throw "No se encontro psql.exe. Pasa -PostgresBinPath."
    }
    $script:PsqlExe = Join-Path $PostgresBinPath "psql.exe"

    # 1. Liveness: la API responde.
    $live = Invoke-ApiJson -Method "GET" -Path "/api/health"
    $results['Liveness'] = @{
        StatusCode = $live.StatusCode
        Ok = ($live.StatusCode -eq 200)
    }
    if (-not $results['Liveness'].Ok) {
        throw "Liveness fallo: HTTP $($live.StatusCode). La API no esta sirviendo."
    }

    # 2. Login: respuesta 200 con mfaChallengeId.
    $login = Invoke-ApiJson -Method "POST" -Path "/api/auth/login" -Body @{
        email = $AdminEmail
        password = $AdminPassword
    }
    $results['Login'] = @{
        StatusCode = $login.StatusCode
        MfaRequired = [bool]$login.Body.mfaRequired
        MfaSetupRequired = [bool]$login.Body.mfaSetupRequired
        ChallengeId = if ($null -ne $login.Body.mfaChallengeId) { [string]$login.Body.mfaChallengeId } else { '' }
    }
    if ($login.StatusCode -ne 200) {
        throw "Login devolvio HTTP $($login.StatusCode). Body: $($login.RawBody)"
    }
    if (-not $results['Login'].MfaRequired) {
        throw "Login no requirio MFA pero el smoke exige cuenta con MFA activo."
    }
    if ([string]::IsNullOrEmpty($results['Login'].ChallengeId)) {
        throw "Login no devolvio mfaChallengeId."
    }
    if ($results['Login'].MfaSetupRequired -and [string]::IsNullOrEmpty([string]$login.Body.mfaSecret)) {
        throw "Login con MFA pendiente de enrolar no devolvio mfaSecret."
    }

    # 3. Generar TOTP. La API solo devuelve mfaSecret cuando MfaSetupRequired
    #    es true (enrolamiento inicial); para un admin ya enrolado el
    #    servidor lo omite a proposito y hay que usar el secreto TOTP que el
    #    operador ya tiene enrolado (-AdminTotpSecret).
    $secret = [string]$login.Body.mfaSecret
    if ([string]::IsNullOrEmpty($secret)) {
        $secret = $AdminTotpSecret
    }
    if ([string]::IsNullOrEmpty($secret)) {
        throw "No se pudo obtener el secreto TOTP para calcular el codigo. La cuenta ya esta enrolada en MFA (la API no devuelve el secreto en ese caso): pasa -AdminTotpSecret con el secreto TOTP ya enrolado de $AdminEmail."
    }
    $code = Get-MfaTotpCode -Secret $secret
    $results['TotpCode'] = @{
        # No logueamos el secreto bajo ningun concepto.
        Computed = $true
        Length = $code.Length
    }

    # 4. Verify MFA: emite cookies de sesion. Reusamos la misma sesion HTTP
    #    para validar que las cookies sirven contra /api/auth/me.
    $session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    $verify = Invoke-ApiJson -Method "POST" -Path "/api/auth/mfa/verify" -Session $session -Body @{
        challengeId = $results['Login'].ChallengeId
        code = $code
        rememberDevice = $false
    }
    $accessToken = Get-CookieValue -Headers $verify.Headers -CookieName "access_token"
    $refreshToken = Get-CookieValue -Headers $verify.Headers -CookieName "refresh_token"
    $results['VerifyMfa'] = @{
        StatusCode = $verify.StatusCode
        HasAccessToken = ($null -ne $accessToken)
        HasRefreshToken = ($null -ne $refreshToken)
    }
    if ($verify.StatusCode -ne 200) {
        throw "Verify MFA devolvio HTTP $($verify.StatusCode). Body: $($verify.RawBody)"
    }
    if (-not $results['VerifyMfa'].HasAccessToken -or -not $results['VerifyMfa'].HasRefreshToken) {
        throw "Verify MFA no devolvio cookies de sesion. Headers: $($verify.Headers.Keys -join ', ')"
    }

    # 5. /api/auth/me con la sesion: confirma que el JWT y el middleware
    #    de estado (security_stamp) funcionan.
    $me = Invoke-ApiJson -Method "GET" -Path "/api/auth/me" -Session $session
    $results['Me'] = @{
        StatusCode = $me.StatusCode
    }
    if ($me.StatusCode -ne 200) {
        throw "/api/auth/me devolvio HTTP $($me.StatusCode). La sesion no es valida."
    }

    # 6. AUDITORIAS: los eventos esperados deben estar firmados y persistidos.
    #    Ventana amplia para incluir LOGIN_MFA_REQUIRED del paso 2 y
    #    MFA_VERIFIED + LOGIN del paso 4.
    $connection = Parse-ConnectionString -ConnectionString $PostgresConnectionString
    $windowStart = $startInstant.AddMinutes(-2).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    $windowEnd = $startInstant.AddMinutes(2).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    $auditedTypes = $AuditEventTypes -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    $typeList = ($auditedTypes | ForEach-Object { "'$_'" }) -join ","
    if ([string]::IsNullOrEmpty($typeList)) {
        $typeList = "'LOGIN_MFA_REQUIRED','MFA_VERIFIED','LOGIN'"
    }

    $auditSql = @"
SELECT tipo_accion, COUNT(*)
FROM "AUDITORIAS"
WHERE "timestamp" BETWEEN TIMESTAMP '$windowStart' AND TIMESTAMP '$windowEnd'
  AND tipo_accion IN ($typeList)
GROUP BY tipo_accion;
"@
    $rows = Invoke-Psql -Connection $connection -Sql $auditSql
    $auditCounts = [ordered]@{}
    foreach ($row in $rows) {
        $parts = $row -split '\|'
        if ($parts.Count -ge 2) {
            $auditCounts[$parts[0].Trim()] = [int]$parts[1].Trim()
        }
    }
    $results['Auditorias'] = @{
        VentanaUtc = "$windowStart .. $windowEnd"
        Conteos = $auditCounts
    }
    foreach ($tipo in $auditedTypes) {
        if (-not $auditCounts.Contains($tipo) -or $auditCounts[$tipo] -lt 1) {
            throw "AUDITORIAS no contiene $tipo en la ventana del smoke. Conteos: $($auditCounts | ConvertTo-Json -Compress)"
        }
    }
}
catch {
    $results['Error'] = $_.Exception.Message
    $results['Stack'] = $_.ScriptStackTrace
}

$results['ElapsedSeconds'] = [int]([DateTimeOffset]::UtcNow - $startInstant).TotalSeconds
$results['ApiBaseUrl'] = $ApiBaseUrl
$results['AdminEmail'] = $AdminEmail
$results['StartedUtc'] = $startInstant.ToString("yyyy-MM-ddTHH:mm:ssZ")

$json = $results | ConvertTo-Json -Depth 8
Write-Host $json

if ($results.Contains('Error')) {
    exit 1
}
exit 0
