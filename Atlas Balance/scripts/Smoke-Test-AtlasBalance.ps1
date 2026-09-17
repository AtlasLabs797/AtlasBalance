[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
    [Parameter(Mandatory = $true)][string]$AdminEmail,
    [SecureString]$AdminPassword,
    [SecureString]$PostgresConnectionString,
    [SecureString]$AdminTotpSecret,
    [string]$SecretFile = "",
    [string]$PostgresBinPath = "",
    [string]$AuditEventTypes = "LOGIN_MFA_REQUIRED,MFA_VERIFIED,LOGIN",
    [int]$HttpTimeoutSeconds = 30,
    [switch]$SkipCertificateCheck
    # V-03.01: los secretos tambien pueden cargarse desde -SecretFile, un
    # CLIXML creado con Export-Clixml a partir de SecureString.
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Carga de helpers TOTP extraidos (ver Mfa-Totp.Tests.ps1 para cobertura).
$totpHelper = Join-Path $PSScriptRoot "Mfa-Totp.ps1"
if (-not (Test-Path -LiteralPath $totpHelper)) {
    throw "No se encontro $totpHelper."
}
. $totpHelper

$script:SmokeSensitiveValues = [System.Collections.Generic.List[string]]::new()

function ConvertFrom-SecureStringValue {
    param([SecureString]$Value)

    if ($null -eq $Value -or $Value.Length -eq 0) {
        return ""
    }

    $bstr = [IntPtr]::Zero
    try {
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        if ($bstr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
}

function Protect-SensitiveText {
    param(
        [AllowEmptyString()][string]$Text,
        [string[]]$SensitiveValues = @()
    )

    $safeText = if ($null -eq $Text) { "" } else { $Text }
    foreach ($value in @($SensitiveValues | Where-Object { -not [string]::IsNullOrEmpty($_) } | Sort-Object Length -Descending)) {
        $safeText = $safeText.Replace($value, "[REDACTED]")
    }
    return $safeText
}

function Assert-ProtectedSecretFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    try {
        $acl = Get-Acl -LiteralPath $Path -ErrorAction Stop
        if (-not $acl.AreAccessRulesProtected) {
            throw "El fichero de secretos debe tener la herencia ACL desactivada."
        }

        $allowedSids = @(
            [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value,
            "S-1-5-18",
            "S-1-5-32-544"
        )
        $ownerSid = $acl.GetOwner([System.Security.Principal.SecurityIdentifier]).Value
        if ($allowedSids -notcontains $ownerSid) {
            throw "El fichero de secretos tiene un propietario no autorizado."
        }

        foreach ($rule in $acl.Access) {
            $sid = $rule.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value
            if ($allowedSids -notcontains $sid) {
                throw "El fichero de secretos contiene una ACE para una identidad no autorizada."
            }
        }
    }
    catch {
        throw (Protect-SensitiveText -Text $_.Exception.Message)
    }
}

function Import-SecretBundle {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "No se encontro el fichero de secretos indicado."
    }

    Assert-ProtectedSecretFile -Path $Path
    try {
        $bundle = Import-Clixml -LiteralPath $Path -ErrorAction Stop
    }
    catch {
        throw "No se pudo leer el fichero de secretos protegido."
    }

    foreach ($name in @("AdminPassword", "PostgresConnectionString", "AdminTotpSecret")) {
        $property = $bundle.PSObject.Properties[$name]
        if ($null -ne $property -and $null -ne $property.Value -and
            $property.Value -isnot [SecureString]) {
            throw "El campo $name del fichero de secretos no es SecureString."
        }
    }
    return $bundle
}

function Resolve-SecureSecret {
    param(
        [SecureString]$ExplicitValue,
        $Bundle,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Prompt
    )

    if ($null -ne $ExplicitValue -and $ExplicitValue.Length -gt 0) {
        return $ExplicitValue
    }
    if ($null -ne $Bundle) {
        $property = $Bundle.PSObject.Properties[$Name]
        if ($null -ne $property -and $null -ne $property.Value -and $property.Value.Length -gt 0) {
            return [SecureString]$property.Value
        }
    }

    if ($null -eq $Host.UI -or $null -eq $Host.UI.RawUI) {
        throw "Falta $Name. Usa -SecretFile con un valor SecureString protegido."
    }
    return Read-Host -Prompt $Prompt -AsSecureString
}

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
    # Parser pequeno compatible con Windows PowerShell 5.1. No divide a
    # ciegas por ';': los valores entre comillas pueden contener separadores
    # y las comillas dobles se escapan duplicandose.
    param([Parameter(Mandatory = $true)][string]$ConnectionString)

    $parts = [ordered]@{}
    $index = 0
    while ($index -lt $ConnectionString.Length) {
        while ($index -lt $ConnectionString.Length -and ($ConnectionString[$index] -eq ';' -or [char]::IsWhiteSpace($ConnectionString[$index]))) {
            $index++
        }
        if ($index -ge $ConnectionString.Length) { break }

        $keyStart = $index
        while ($index -lt $ConnectionString.Length -and $ConnectionString[$index] -ne '=') {
            if ($ConnectionString[$index] -eq ';') {
                throw "La cadena de conexion contiene una clave sin valor."
            }
            $index++
        }
        if ($index -ge $ConnectionString.Length) {
            throw "La cadena de conexion contiene una clave sin valor."
        }
        $key = $ConnectionString.Substring($keyStart, $index - $keyStart).Trim().ToLowerInvariant()
        if ([string]::IsNullOrWhiteSpace($key)) {
            throw "La cadena de conexion contiene una clave vacia."
        }
        $index++
        while ($index -lt $ConnectionString.Length -and [char]::IsWhiteSpace($ConnectionString[$index])) { $index++ }

        if ($index -lt $ConnectionString.Length -and ($ConnectionString[$index] -eq '"' -or $ConnectionString[$index] -eq "'")) {
            $quote = $ConnectionString[$index]
            $index++
            $builder = [Text.StringBuilder]::new()
            $closed = $false
            while ($index -lt $ConnectionString.Length) {
                $character = $ConnectionString[$index]
                if ($character -eq $quote) {
                    if ($index + 1 -lt $ConnectionString.Length -and $ConnectionString[$index + 1] -eq $quote) {
                        [void]$builder.Append($quote)
                        $index += 2
                        continue
                    }
                    $index++
                    $closed = $true
                    break
                }
                [void]$builder.Append($character)
                $index++
            }
            if (-not $closed) {
                throw "La cadena de conexion contiene comillas sin cerrar."
            }
            $value = $builder.ToString()
            while ($index -lt $ConnectionString.Length -and [char]::IsWhiteSpace($ConnectionString[$index])) { $index++ }
            if ($index -lt $ConnectionString.Length -and $ConnectionString[$index] -ne ';') {
                throw "La cadena de conexion contiene texto despues de un valor entre comillas."
            }
        }
        else {
            $valueStart = $index
            while ($index -lt $ConnectionString.Length -and $ConnectionString[$index] -ne ';') { $index++ }
            $value = $ConnectionString.Substring($valueStart, $index - $valueStart).Trim()
        }
        $parts[$key] = $value
        if ($index -lt $ConnectionString.Length -and $ConnectionString[$index] -eq ';') { $index++ }
    }

    # V-02.09 (fix): $host es una variable automatica readonly de PowerShell;
    # asignarla lanza un error terminante. Se renombra a $pgHost.
    $pgHost = if ($parts.Contains("host")) { $parts["host"] } elseif ($parts.Contains("server")) { $parts["server"] } else { "" }
    $port = if ($parts.Contains("port")) { [int]$parts["port"] } else { 5432 }
    $database = if ($parts.Contains("database")) { $parts["database"] } elseif ($parts.Contains("initial catalog")) { $parts["initial catalog"] } else { "" }
    $username = if ($parts.Contains("username")) { $parts["username"] }
        elseif ($parts.Contains("user id")) { $parts["user id"] }
        elseif ($parts.Contains("userid")) { $parts["userid"] }
        elseif ($parts.Contains("user")) { $parts["user"] }
        else { "" }
    $password = if ($parts.Contains("password")) { $parts["password"] } else { "" }

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
        $safeDiagnostic = Protect-SensitiveText -Text ($diagnostic -join [Environment]::NewLine) -SensitiveValues $script:SmokeSensitiveValues
        throw "psql fallo con codigo $exitCode. $safeDiagnostic"
    }

    return @($standardOutput -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

# ----------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------

$results = [ordered]@{}
$startInstant = [DateTimeOffset]::UtcNow
$secretBundle = $null
$adminPasswordPlain = ""
$postgresConnectionStringPlain = ""
$adminTotpSecretPlain = ""
$secret = ""
$code = ""
$accessToken = ""
$refreshToken = ""
$connection = $null

try {
    $secretBundle = Import-SecretBundle -Path $SecretFile
    $adminPasswordSecure = Resolve-SecureSecret -ExplicitValue $AdminPassword -Bundle $secretBundle `
        -Name "AdminPassword" -Prompt "Password del administrador"
    $postgresConnectionStringSecure = Resolve-SecureSecret -ExplicitValue $PostgresConnectionString -Bundle $secretBundle `
        -Name "PostgresConnectionString" -Prompt "Cadena de conexion PostgreSQL"
    $adminPasswordPlain = ConvertFrom-SecureStringValue -Value $adminPasswordSecure
    $postgresConnectionStringPlain = ConvertFrom-SecureStringValue -Value $postgresConnectionStringSecure
    $script:SmokeSensitiveValues.Add($adminPasswordPlain)
    $script:SmokeSensitiveValues.Add($postgresConnectionStringPlain)

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
        password = $adminPasswordPlain
    }
    $results['Login'] = @{
        StatusCode = $login.StatusCode
        MfaRequired = [bool]$login.Body.mfaRequired
        MfaSetupRequired = [bool]$login.Body.mfaSetupRequired
        ChallengeId = if ($null -ne $login.Body.mfaChallengeId) { [string]$login.Body.mfaChallengeId } else { '' }
    }
    if ($login.StatusCode -ne 200) {
        $safeBody = Protect-SensitiveText -Text $login.RawBody -SensitiveValues $script:SmokeSensitiveValues
        throw "Login devolvio HTTP $($login.StatusCode). Body: $safeBody"
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
    #    operador ya tiene enrolado (-AdminTotpSecret o -SecretFile).
    $secret = [string]$login.Body.mfaSecret
    if ([string]::IsNullOrEmpty($secret)) {
        $totpSecure = Resolve-SecureSecret -ExplicitValue $AdminTotpSecret -Bundle $secretBundle `
            -Name "AdminTotpSecret" -Prompt "Secreto TOTP del administrador"
        $adminTotpSecretPlain = ConvertFrom-SecureStringValue -Value $totpSecure
        $script:SmokeSensitiveValues.Add($adminTotpSecretPlain)
        $secret = $adminTotpSecretPlain
    }
    else {
        $script:SmokeSensitiveValues.Add($secret)
    }
    if ([string]::IsNullOrEmpty($secret)) {
        throw "No se pudo obtener el secreto TOTP para calcular el codigo. La cuenta ya esta enrolada en MFA (la API no devuelve el secreto en ese caso): pasa -AdminTotpSecret con el secreto TOTP ya enrolado de $AdminEmail."
    }
    $code = Get-MfaTotpCode -Secret $secret
    $script:SmokeSensitiveValues.Add($code)
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
    if ($null -ne $accessToken) { $script:SmokeSensitiveValues.Add([string]$accessToken) }
    if ($null -ne $refreshToken) { $script:SmokeSensitiveValues.Add([string]$refreshToken) }
    $results['VerifyMfa'] = @{
        StatusCode = $verify.StatusCode
        HasAccessToken = ($null -ne $accessToken)
        HasRefreshToken = ($null -ne $refreshToken)
    }
    if ($verify.StatusCode -ne 200) {
        $safeBody = Protect-SensitiveText -Text $verify.RawBody -SensitiveValues $script:SmokeSensitiveValues
        throw "Verify MFA devolvio HTTP $($verify.StatusCode). Body: $safeBody"
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
    $connection = Parse-ConnectionString -ConnectionString $postgresConnectionStringPlain
    $script:SmokeSensitiveValues.Add([string]$connection.Password)
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
    $results['Error'] = Protect-SensitiveText -Text $_.Exception.Message -SensitiveValues $script:SmokeSensitiveValues
    $results['Stack'] = Protect-SensitiveText -Text $_.ScriptStackTrace -SensitiveValues $script:SmokeSensitiveValues
}
finally {
    $adminPasswordPlain = ""
    $postgresConnectionStringPlain = ""
    $adminTotpSecretPlain = ""
    $secret = ""
    $code = ""
    $accessToken = ""
    $refreshToken = ""
    $connection = $null
    $secretBundle = $null
    $adminPasswordSecure = $null
    $postgresConnectionStringSecure = $null
    $totpSecure = $null
    $script:SmokeSensitiveValues.Clear()
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
