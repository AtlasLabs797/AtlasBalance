[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallPath,
    [string]$AdminEmail = "",
    [string]$ApiBaseUrl = "",
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# No transcript: el password SMTP y los secretos del appsettings no pueden
# terminar en un fichero de transcripcion de PowerShell.
function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-SmtpConfiguracion {
    # Lee CONFIGURACIONES directamente con un select parametrizado. Evita
    # DbConnectionStringBuilder de .NET Framework (mismo problema que
    # Repair-RlsContext.ps1 y Reset-AdminPassword.ps1).
    # V-02.08 (revision PR #33): la firma original declaraba -Connection y
    # -PsqlExe como obligatorios, pero el unico llamador pasa -InstallPath (el
    # unico dato que la funcion realmente necesita desde fuera: la cadena de
    # conexion la construye ella misma leyendo appsettings.Production.json, y
    # psql.exe lo toma de $script:PsqlExe, ya inicializado por el bloque
    # principal antes de esta llamada). El binding fallaba siempre.
    param(
        [Parameter(Mandatory = $true)][string]$InstallPath
    )

    $configPath = Join-Path $InstallPath "api\appsettings.Production.json"
    if (-not (Test-Path -LiteralPath $configPath)) {
        throw "No se encontro $configPath."
    }

    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $runtimeConnectionString = [string]$config.ConnectionStrings.DefaultConnection
    $migrationConnectionString = [string]$config.ConnectionStrings.MigrationConnection

    if ([string]::IsNullOrWhiteSpace($runtimeConnectionString) -or
        [string]::IsNullOrWhiteSpace($migrationConnectionString)) {
        throw "Faltan DefaultConnection o MigrationConnection en $configPath."
    }

    $parts = [ordered]@{}
    foreach ($segment in ($runtimeConnectionString -split ";")) {
        $equalsIndex = $segment.IndexOf("=")
        if ($equalsIndex -le 0) { continue }
        $key = $segment.Substring(0, $equalsIndex).Trim()
        $value = $segment.Substring($equalsIndex + 1).Trim()
        if (-not [string]::IsNullOrWhiteSpace($key)) {
            $parts[$key] = $value
        }
    }

    $host = if ($parts.Contains("Host")) { $parts["Host"] } elseif ($parts.Contains("Server")) { $parts["Server"] } else { "" }
    $port = if ($parts.Contains("Port")) { [int]$parts["Port"] } else { 5432 }
    $database = if ($parts.Contains("Database")) { $parts["Database"] } elseif ($parts.Contains("Initial Catalog")) { $parts["Initial Catalog"] } else { "" }
    $username = if ($parts.Contains("Username")) { $parts["Username"] }
        elseif ($parts.Contains("User ID")) { $parts["User ID"] }
        elseif ($parts.Contains("UserId")) { $parts["UserId"] }
        else { $parts["User"] }
    $password = $parts["Password"]

    if ([string]::IsNullOrWhiteSpace($host) -or
        [string]::IsNullOrWhiteSpace($database) -or
        [string]::IsNullOrWhiteSpace($username)) {
        throw "La cadena de conexion no contiene Host, Database y Username."
    }

    $connection = [pscustomobject]@{
        Host = $host
        Port = $port
        Database = $database
        Username = $username
        Password = $password
    }

    $sql = @"
SELECT clave, valor
FROM "CONFIGURACIONES"
WHERE clave IN ('smtp_host', 'smtp_user', 'smtp_from', 'smtp_port', 'smtp_use_ssl')
ORDER BY clave;
"@
    $rows = Invoke-Psql -Connection $connection -Sql $sql
    $configuraciones = [ordered]@{}
    foreach ($row in $rows) {
        $parts = $row -split '\|'
        if ($parts.Count -ge 2) {
            $configuraciones[$parts[0].Trim()] = $parts[1].Trim()
        }
    }
    return $configuraciones
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
    if (-not (Test-IsAdministrator)) {
        throw "Ejecuta este script como Administrador."
    }

    $postgresBinPath = Join-Path $InstallPath "postgresql\16\bin"
    $script:PsqlExe = Join-Path $postgresBinPath "psql.exe"
    if (-not (Test-Path -LiteralPath $script:PsqlExe)) {
        throw "No se encontro psql.exe en $postgresBinPath."
    }

    $configuraciones = Get-SmtpConfiguracion -InstallPath $InstallPath
    $results['ConfiguracionesLeidas'] = @{
        claves = $configuraciones.Keys
    }

    $smtpHost = $configuraciones['smtp_host']
    if ([string]::IsNullOrWhiteSpace($smtpHost)) {
        # V-02.08: distinguir "no configurado" de "configurado y fallo".
        # Sin smtp_host, las alertas por correo no se envian pero el sistema
        # puede funcionar. WARN no bloqueante.
        $results['Estado'] = "no_configurado"
        $results['Detalle'] = "SMTP no configurado: no hay valor en CONFIGURACIONES.smtp_host. Las alertas por correo no se enviaran hasta que se configure."
        Write-Host ($results | ConvertTo-Json -Depth 8)
        exit 0
    }

    # SMTP configurado. La API expone /api/configuracion/smtp/test como
    # endpoint autenticado admin-only. Si no nos autenticamos, lo
    # simulamos con un cliente HTTP contra el endpoint publico.
    $resolvedApiUrl = $ApiBaseUrl
    if ([string]::IsNullOrWhiteSpace($resolvedApiUrl)) {
        $configPath = Join-Path $InstallPath "api\appsettings.Production.json"
        $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
        $url = if ($config.Kestrel -and $config.Kestrel.Endpoints -and $config.Kestrel.Endpoints.Https) {
            [string]$config.Kestrel.Endpoints.Https.Url
        } else { "" }
        if ($url -match "https://(\S+):(\d+)") {
            $resolvedApiUrl = "https://$($Matches[1])`:$($Matches[2])"
        } else {
            $resolvedApiUrl = "https://localhost:8443"
        }
    }

    # Llamar /api/configuracion/smtp/test sin autenticacion devuelve 401.
    # Eso es lo correcto: la API exige auth y no queremos un endpoint
    # anonimo que envie correos. Lo que reportamos al operador es si
    # SMTP esta configurado y si la API lo expone. La verificacion real
    # del envio la hace el operador desde la UI con su sesion admin.
    $probeUrl = "$resolvedApiUrl/api/configuracion/smtp/test"
    $estado = "configurado_no_probado"
    $detalle = "SMTP configurado en $smtpHost. La verificacion real del envio (con autenticacion admin) se hace desde /configuracion > SMTP en la UI; este script solo confirma la presencia del host."
    if ($AdminEmail -and $TimeoutSeconds -gt 0) {
        # Si el operador quiere verificar la ruta publica (404, 401, 503),
        # la probamos. No conseguimos un 200 sin sesion admin, asi que
        # cualquier respuesta distinta de timeout/red se considera que
        # el endpoint existe.
        try {
            $statusCode = (& curl.exe -k -s -o NUL -w "%{http_code}" -X POST -H "Content-Type: application/json" -d "{}" --max-time $TimeoutSeconds $probeUrl 2>$null)
            if ($LASTEXITCODE -eq 0) {
                $estado = "endpoint_accesible"
                $detalle = "El endpoint $probeUrl respondio HTTP $statusCode (sin sesion admin). Para verificar el envio real, usa la UI con tu admin."
            }
            else {
                $detalle = "El endpoint $probeUrl no responde. La API puede no estar levantada o el puerto no esta bien configurado."
            }
        }
        catch {
            $detalle = "curl fallo al sondear $probeUrl. Verifica la URL y que la API este corriendo."
        }
    }

    $results['Estado'] = $estado
    $results['Detalle'] = $detalle
    $results['SmtpHost'] = $smtpHost
    $results['ApiBaseUrl'] = $resolvedApiUrl
}
catch {
    $results['Estado'] = "error"
    $results['Error'] = $_.Exception.Message
}

$results['ElapsedSeconds'] = [int]([DateTimeOffset]::UtcNow - $startInstant).TotalSeconds
$json = $results | ConvertTo-Json -Depth 8
Write-Host $json

if ($results['Estado'] -eq "error") {
    exit 2
}
exit 0
