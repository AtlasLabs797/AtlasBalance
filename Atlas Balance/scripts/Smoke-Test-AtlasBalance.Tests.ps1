$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$scriptPath = Join-Path $PSScriptRoot "Smoke-Test-AtlasBalance.ps1"
$scriptText = Get-Content -LiteralPath $scriptPath -Raw
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {
    throw "Smoke-Test-AtlasBalance.ps1 no tiene sintaxis PowerShell valida."
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param([object]$Expected, [object]$Actual, [string]$Message)
    if ($Expected -cne $Actual) {
        throw "$Message Esperado='$Expected'; actual='$Actual'."
    }
}

$parameterTypes = @{}
foreach ($parameter in $ast.ParamBlock.Parameters) {
    $parameterTypes[$parameter.Name.VariablePath.UserPath] = $parameter.StaticType
}
foreach ($name in @("AdminPassword", "PostgresConnectionString", "AdminTotpSecret")) {
    Assert-Equal ([SecureString]) $parameterTypes[$name] "El parametro $name no es SecureString."
}
Assert-True ($scriptText -match "Import-Clixml") "El smoke no carga el bundle protegido."
Assert-True ($scriptText -match "Read-Host[\s\S]*AsSecureString") "El smoke no tiene prompt seguro."
Assert-True ($scriptText -notmatch '\[string\]\$(AdminPassword|PostgresConnectionString|AdminTotpSecret)') "Hay un secreto declarado como string."
Assert-True ($scriptText -notmatch "(?im)\b(Add-History|Get-History|Set-PSReadLineOption)\b") "El smoke toca el historial de PowerShell."

foreach ($name in @("ConvertFrom-SecureStringValue", "Protect-SensitiveText", "Assert-ProtectedSecretFile", "Import-SecretBundle", "Parse-ConnectionString")) {
    $functionAst = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $functionAst) { throw "No se encontro la funcion $name." }
    . ([scriptblock]::Create($functionAst.Extent.Text))
}

$canaryPassword = "SmokePassword-canary-7f1c"
$canaryTotp = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"
$connectionString = 'Host=localhost;Port=5432;Database=atlas;Username=app;Password="p;ass";Application Name="smoke;test"'
$secureCanary = ConvertTo-SecureString -String $canaryPassword -AsPlainText -Force
Assert-Equal $canaryPassword (ConvertFrom-SecureStringValue -Value $secureCanary) "No se pudo recuperar el SecureString solo en memoria."

$redacted = Protect-SensitiveText -Text "password=$canaryPassword connection=$connectionString totp=$canaryTotp" `
    -SensitiveValues @($canaryPassword, $connectionString, $canaryTotp)
Assert-True ($redacted -notmatch [regex]::Escape($canaryPassword)) "La password aparece en un diagnostico."
Assert-True ($redacted -notmatch [regex]::Escape($connectionString)) "La cadena de conexion aparece en un diagnostico."
Assert-True ($redacted -notmatch [regex]::Escape($canaryTotp)) "El TOTP aparece en un diagnostico."

$parsed = Parse-ConnectionString -ConnectionString $connectionString
Assert-Equal "localhost" $parsed.Host "No se parseo Host."
Assert-Equal "p;ass" $parsed.Password "El parser rompio un punto y coma entre comillas."
Assert-Equal "atlas" $parsed.Database "La prueba de parser no devolvio Database."

$quotedPassword = Parse-ConnectionString -ConnectionString 'Host=localhost;Database=atlas;Username=app;Password="p""ass;word"'
Assert-Equal 'p"ass;word' $quotedPassword.Password "El parser no desescapo comillas dobles."

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("atlas-smoke-secret-test-" + [Guid]::NewGuid().ToString("N"))
$childTemp = Join-Path $tempRoot "child-temp"
$secretPath = Join-Path $tempRoot "smoke-secrets.xml"
$hostPath = (Get-Process -Id $PID).Path
$stdoutPath = Join-Path $tempRoot "stdout.txt"
$stderrPath = Join-Path $tempRoot "stderr.txt"

try {
    New-Item -ItemType Directory -Path $childTemp -Force | Out-Null
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    $bundle = [pscustomobject]@{
        AdminPassword = $secureCanary
        PostgresConnectionString = (ConvertTo-SecureString -String $connectionString -AsPlainText -Force)
        AdminTotpSecret = (ConvertTo-SecureString -String $canaryTotp -AsPlainText -Force)
    }
    $bundle | Export-Clixml -LiteralPath $secretPath

    $acl = Get-Acl -LiteralPath $secretPath
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.Access)) { [void]$acl.RemoveAccessRule($rule) }
    $currentSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    $currentRule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        $currentSid,
        [System.Security.AccessControl.FileSystemRights]::FullControl,
        [System.Security.AccessControl.AccessControlType]::Allow)
    [void]$acl.AddAccessRule($currentRule)
    Set-Acl -LiteralPath $secretPath -AclObject $acl

    $loaded = Import-SecretBundle -Path $secretPath
    Assert-True ($loaded.AdminPassword -is [SecureString]) "El bundle no conserva AdminPassword como SecureString."
    Assert-True ((Get-Acl -LiteralPath $secretPath).AreAccessRulesProtected) "El bundle no tiene ACL protegida."

    # En algunos hosts PowerShell 5.1 exige SeSecurityPrivilege al volver a
    # aplicar el objeto ACL completo. icacls modifica únicamente la DACL y
    # permite conservar esta prueba de rechazo sin privilegios adicionales.
    & icacls.exe $secretPath /grant '*S-1-1-0:(W)' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No se pudo preparar la ACL insegura de prueba." }
    $unsafeRejected = $false
    try { Assert-ProtectedSecretFile -Path $secretPath } catch { $unsafeRejected = $true }
    Assert-True $unsafeRejected "Se aceptó una ACE no autorizada de escritura."

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $hostPath
    $argumentValues = @(
        "-NoLogo", "-NoProfile", "-NonInteractive", "-File", $scriptPath,
        "-ApiBaseUrl", "http://127.0.0.1:1", "-AdminEmail", "admin@example.invalid",
        "-SecretFile", $secretPath, "-PostgresBinPath", $env:WINDIR,
        "-HttpTimeoutSeconds", "1")
    $startInfo.Arguments = ($argumentValues | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' '
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.EnvironmentVariables["TEMP"] = $childTemp
    $startInfo.EnvironmentVariables["TMP"] = $childTemp

    $child = [Diagnostics.Process]::new()
    $child.StartInfo = $startInfo
    if (-not $child.Start()) { throw "No se pudo iniciar el smoke hijo." }
    $commandLine = $startInfo.Arguments
    try {
        $snapshot = Get-CimInstance Win32_Process -Filter "ProcessId = $($child.Id)" -ErrorAction Stop
        if ($null -ne $snapshot) { $commandLine += " $($snapshot.CommandLine)" }
    }
    catch {
        # StartInfo.Arguments sigue siendo una comprobacion suficiente si WMI/CIM no esta disponible.
    }
    Assert-True ($commandLine -notmatch [regex]::Escape($canaryPassword)) "La password aparece en los argumentos del proceso."
    Assert-True ($commandLine -notmatch [regex]::Escape($connectionString)) "La cadena de conexion aparece en los argumentos del proceso."
    Assert-True ($commandLine -notmatch [regex]::Escape($canaryTotp)) "El TOTP aparece en los argumentos del proceso."

    $stdoutTask = $child.StandardOutput.ReadToEndAsync()
    $stderrTask = $child.StandardError.ReadToEndAsync()
    if (-not $child.WaitForExit(15000)) {
        $child.Kill()
        throw "El smoke hijo no termino dentro del timeout."
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    foreach ($output in @($stdout, $stderr)) {
        Assert-True ($output -notmatch [regex]::Escape($canaryPassword)) "Una salida del smoke contiene la password."
        Assert-True ($output -notmatch [regex]::Escape($connectionString)) "Una salida del smoke contiene la cadena de conexion."
        Assert-True ($output -notmatch [regex]::Escape($canaryTotp)) "Una salida del smoke contiene el TOTP."
    }

    Assert-Equal 0 @(Get-ChildItem -LiteralPath $childTemp -Force).Count "El smoke creo temporales en TEMP."
    Write-Host "Smoke-Test-AtlasBalance secret handling tests OK."
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
