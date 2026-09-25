$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$installerPath = Join-Path $PSScriptRoot "Instalar-AtlasBalance.ps1"
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($installerPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {
    throw "El instalador no tiene sintaxis PowerShell valida."
}

foreach ($name in @("Get-JsonPathValue", "Get-ConnectionStringValue", "Read-ExistingInstallConfiguration")) {
    $functionAst = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $functionAst) { throw "No se encontro la funcion $name." }
    . ([scriptblock]::Create($functionAst.Extent.Text))
}

function Assert-Equal {
    param([object]$Expected, [object]$Actual, [string]$Message)
    if ($Expected -cne $Actual) { throw "$Message Esperado='$Expected'; actual='$Actual'." }
}

Assert-Equal "p;ass" (Get-ConnectionStringValue -ConnectionString 'Host=localhost;Password="p;ass";Database=atlas' -Name "Password") "No se preservo una contraseña con punto y coma."
Assert-Equal 'p"ass' (Get-ConnectionStringValue -ConnectionString 'Password="p""ass";Host=localhost' -Name "Password") "No se preservo una comilla escapada en la contraseña."
Assert-Equal 'abc\' (Get-ConnectionStringValue -ConnectionString 'Password="abc\";Host=localhost' -Name "Password") "No se interpreto la barra inversa como caracter literal."

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("atlas-secret-preservation-" + [Guid]::NewGuid().ToString("N"))
try {
    New-Item -ItemType Directory -Path (Join-Path $tempRoot "api") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $tempRoot "watchdog") -Force | Out-Null
    $api = [ordered]@{
        ConnectionStrings = [ordered]@{
            DefaultConnection = 'Host=localhost;Port=5432;Database=atlas;Username=app;Password="p;ass"'
            MigrationConnection = 'Host=localhost;Port=5432;Database=atlas;Username=owner;Password="owner;pass"'
        }
        JwtSettings = @{ Secret = "jwt-existing" }
        Security = @{ RlsContextSecret = "rls-existing"; AuditSigningKey = "audit-existing" }
        WatchdogSettings = @{ SharedSecret = "watchdog-existing" }
    }
    $watchdog = @{ WatchdogSettings = @{ SharedSecret = "watchdog-existing" } }
    $api | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $tempRoot "api\appsettings.Production.json") -Encoding UTF8
    $watchdog | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $tempRoot "watchdog\appsettings.Production.json") -Encoding UTF8

    $existing = Read-ExistingInstallConfiguration -InstallPath $tempRoot
    Assert-Equal "p;ass" $existing.DbPassword "La reinstalacion no preservo la contraseña de la API."
    Assert-Equal "owner;pass" $existing.DbOwnerPassword "La reinstalacion no preservo la contraseña del owner."
    Assert-Equal "audit-existing" $existing.AuditSigningKey "La reinstalacion no preservo AuditSigningKey."
    Write-Host "Installer-SecretPreservation.Tests OK."
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
