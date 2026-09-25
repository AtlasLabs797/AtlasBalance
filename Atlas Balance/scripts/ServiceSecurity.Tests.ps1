$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Read-ParsedScript {
    param([Parameter(Mandatory = $true)][string]$Path)

    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    Assert-True ($errors.Count -eq 0) "Sintaxis invalida en $Path."
    return [pscustomobject]@{ Text = Get-Content -LiteralPath $Path -Raw; Ast = $ast }
}

$installServices = Read-ParsedScript -Path (Join-Path $PSScriptRoot "install-services.ps1")
$securityModule = Read-ParsedScript -Path (Join-Path $PSScriptRoot "ServiceSecurity.ps1")
$mainInstaller = Read-ParsedScript -Path (Join-Path $PSScriptRoot "Instalar-AtlasBalance.ps1")
$updateScript = Read-ParsedScript -Path (Join-Path $PSScriptRoot "Actualizar-AtlasBalance.ps1")

Assert-True ($installServices.Text -match '\[string\]\$ApiServiceAccount') "Falta una cuenta especifica para API."
Assert-True ($installServices.Text -match '\[string\]\$WatchdogServiceAccount') "Falta una cuenta especifica para Watchdog."
Assert-True ($installServices.Text -match '(?s)Install-AtlasService.*-Credential\s+\$watchdogAccount\.Credential') "Watchdog no se registra con credencial explicita."
Assert-True ($installServices.Text -match '(?s)Install-AtlasService.*-Credential\s+\$apiAccount\.Credential') "API no se registra con credencial explicita."
Assert-True ($installServices.Text -notmatch '(?s)New-Service(?!.*-Credential)') "Existe un registro New-Service sin credencial explicita."
Assert-True ($securityModule.Text -match 'RejectBuiltIn') "No se rechazan cuentas integradas."
Assert-True ($securityModule.Text -match 'SeServiceLogonRight') "No se configura Log on as a service."
Assert-True ($securityModule.Text -match 'SeDenyServiceLogonRight') "No se validan denegaciones de inicio de servicio."
Assert-True ($mainInstaller.Text -match 'SeDenyServiceLogonRight') "El instalador principal no valida denegaciones de inicio de servicio."
Assert-True ($securityModule.Text -match 'S-1-5-32-551' -and $securityModule.Text -match 'S-1-5-32-549') "No se rechazan grupos locales privilegiados relevantes."
Assert-True ($securityModule.Text -match 'CCLCSWLOCRCRPWP' -and $securityModule.Text -match '\[regex\]::Replace') "El control del servicio no se normaliza a una mascara exacta."
Assert-True ($securityModule.Text -match 'ApiAccount\.ComputerPrincipal.*:R') "No se concede lectura minima a la configuracion API."
Assert-True ($securityModule.Text -match 'WatchdogAccount\.ComputerPrincipal.*:R') "No se concede lectura minima a la configuracion Watchdog."
Assert-True ($securityModule.Text -match 'Grant-AtlasServiceControl') "No se limita el control del servicio API al Watchdog."
Assert-True ($securityModule.Text -match 'Install-AtlasUpdateTask') "No existe una ruta protegida para el actualizador elevado."
Assert-True ($securityModule.Text -match 'Register-ScheduledTask') "No se registra la tarea de actualización protegida."
Assert-True ($securityModule.Text -match '<UserId>S-1-5-18</UserId>') "La tarea protegida no se ejecuta como SYSTEM."
Assert-True ($securityModule.Text -match '<LogonType>ServiceAccount</LogonType>') "La tarea protegida no usa una cuenta de servicio del sistema."
Assert-True ($securityModule.Text -match '<RunLevel>HighestAvailable</RunLevel>') "La tarea protegida no solicita el nivel de ejecución necesario."
Assert-True ($securityModule.Text -match 'GRGX.*\$\(\$WatchdogAccount\.Sid\)') "Watchdog no tiene únicamente lectura y ejecución sobre la tarea protegida."
Assert-True ($securityModule.Text -notmatch 'SecurityDescriptor>D:\(A;;FA;;;SY\)\(A;;FA;;;BA\)\(A;;FA;;;\$\(\$WatchdogAccount\.Sid\)\)') "Watchdog conserva control total sobre la tarea protegida."
Assert-True ($securityModule.Text -notmatch 'GetNetworkCredential\(\)\.Password') "La contraseña de la cuenta Watchdog se expone al registrar la tarea."
Assert-True ($securityModule.Text -match '\$\{WatchdogAccount\.ComputerPrincipal\}:\(OI\)\(CI\)M') "Watchdog no conserva Modify sobre el area de solicitudes/actualizaciones."
Assert-True ($securityModule.Text -match '\$\{WatchdogAccount\.ComputerPrincipal\}:\(OI\)\(CI\)RX') "Watchdog no queda limitado a lectura/ejecucion sobre los binarios."
Assert-True ($securityModule.Text -notmatch 'UpdaterAccount|AtlasBalanceUpdaterSvc') "La cuenta actualizadora separada podría conservar una vía de escalada."
Assert-True ($mainInstaller.Text -match 'ApiAccount\.ComputerPrincipal.*:R') "El instalador principal no concede lectura minima a la configuracion API."
Assert-True ($mainInstaller.Text -match 'WatchdogAccount\.ComputerPrincipal.*:R') "El instalador principal no concede lectura minima a la configuracion Watchdog."
Assert-True ($mainInstaller.Text -match 'certsPath') "El instalador principal no concede lectura del certificado a API."
Assert-True ($mainInstaller.Text -match '"/remove:g"\) \+ \$normalSids \+ @\("/remove:d"\) \+ \$normalSids') "El instalador principal concatena mal los grupos en icacls."
Assert-True ($mainInstaller.Text -match '"/setowner", "\*S-1-5-32-544"') "El instalador principal no fija el propietario del arbol de instalacion."
Assert-True ($updateScript.Text -match 'Assert-AtlasServiceIdentities') "La actualizacion no valida las identidades de servicio."
Assert-True ($updateScript.Text -match 'ElevatedUpdate') "La actualizacion no tiene una ruta explícita para el actualizador dedicado."

# P-UPGRADE: una instalacion V-02.09 (LocalSystem) debe migrarse, no abortar.
# La actualizacion SI puede volver a registrar el servicio durante esa
# migracion (Install-AtlasService), asi que la asercion anterior que lo
# prohibia por completo se sustituye por una mas precisa: solo se permite
# dentro de la ruta de migracion (Repair-AtlasServiceIdentities), nunca fuera.
Assert-True ($updateScript.Text -match 'Repair-AtlasServiceIdentities') "La actualizacion no migra instalaciones heredadas con cuentas integradas a cuentas dedicadas."
Assert-True ($updateScript.Text -match '(?s)try\s*\{\s*\$serviceIdentities\s*=\s*Assert-AtlasServiceIdentities.*catch\s*\{.*Repair-AtlasServiceIdentities') "La migracion de identidades no esta enganchada al fallo de Assert-AtlasServiceIdentities."
Assert-True ($securityModule.Text -match 'function Repair-AtlasServiceIdentities') "Falta la funcion compartida de migracion de identidades de servicio."
Assert-True ($securityModule.Text -match '(?s)function Repair-AtlasServiceIdentities.*Initialize-AtlasServiceAccount.*Grant-AtlasLogOnAsService.*Protect-AtlasInstallTree.*Install-AtlasUpdateTask.*Install-AtlasService.*Grant-AtlasServiceControl') "La migracion no reusa las mismas funciones que el instalador."

# P1b: config\update-runner ya no debe conceder escritura a Watchdog; solo
# Administrators/SYSTEM escriben ahi (el runner elevado corre como SYSTEM).
Assert-True ($securityModule.Text -notmatch '(?s)update-runner"\s*\r?\n\s*New-Item[^\r\n]*\r?\n\s*Invoke-AtlasIcacls[^\r\n]*WatchdogAccount') "config\update-runner sigue concediendo acceso de escritura a Watchdog."
Assert-True ($mainInstaller.Text -notmatch '(?s)update-runner"\s*\r?\n\s*New-Item[^\r\n]*\r?\n\s*Invoke-Icacls[^\r\n]*WatchdogAccount') "El instalador principal sigue concediendo acceso de escritura a Watchdog sobre update-runner."

# P1a: la API necesita Modify sobre la raiz de updates\ (descarga/verifica/
# extrae el paquete ahi con ActualizacionService); Watchdog se queda en RX
# ahi y solo tiene Modify sobre updates\requests, donde la API NO debe
# escribir (el runner elevado SYSTEM es el unico que reextrae y confia en un
# paquete, y lo hace desde su propia copia verificada, no desde updates\
# directamente escrito por nadie mas).
function Get-AtlasIcaclsBlock {
    param([string]$Text, [string]$Anchor)
    $match = [regex]::Match($Text, [regex]::Escape($Anchor) + '(?s).*?(?=\r?\n\s*\}\s*(elseif|else)\b)')
    Assert-True $match.Success "No se encontro el bloque de ACL para '$Anchor'."
    return $match.Value
}

$updatesBlockSecurityModule = Get-AtlasIcaclsBlock -Text $securityModule.Text -Anchor '$relative -eq "updates")'
Assert-True ($updatesBlockSecurityModule -match 'ApiAccount\.ComputerPrincipal\}:\(OI\)\(CI\)M') "ServiceSecurity.ps1 no concede Modify a la API sobre la raiz de updates\ (la API descarga/extrae el paquete ahi)."
Assert-True ($updatesBlockSecurityModule -match 'WatchdogAccount\.ComputerPrincipal\}:\(OI\)\(CI\)RX') "ServiceSecurity.ps1 concede a Watchdog algo distinto de RX sobre la raiz de updates\."

$updatesBlockMainInstaller = Get-AtlasIcaclsBlock -Text $mainInstaller.Text -Anchor '$relative -eq "updates")'
Assert-True ($updatesBlockMainInstaller -match 'ApiAccount\.ComputerPrincipal\}:\(OI\)\(CI\)M') "El instalador principal no concede Modify a la API sobre la raiz de updates\."
Assert-True ($updatesBlockMainInstaller -match 'WatchdogAccount\.ComputerPrincipal\}:\(OI\)\(CI\)RX') "El instalador principal concede a Watchdog algo distinto de RX sobre la raiz de updates\."

$requestsBlockSecurityModule = ($securityModule.Text -split '\$requestPath = Join-Path \$InstallPath "updates\\requests"')[1].Substring(0, 400)
Assert-True ($requestsBlockSecurityModule -notmatch 'ApiAccount\.ComputerPrincipal') "ServiceSecurity.ps1 concede a la API acceso sobre updates\requests; solo Watchdog debe depositar solicitudes ahi."
Assert-True ($requestsBlockSecurityModule -match 'WatchdogAccount\.ComputerPrincipal\}:\(OI\)\(CI\)M') "ServiceSecurity.ps1 no concede Modify a Watchdog sobre updates\requests."

# P6: regex de cuentas integradas y calificacion de dominio/host.
Assert-True ($securityModule.Text -match 'NT AUTHORITY\\\\\.\+') "El regex de cuentas integradas no cubre NT AUTHORITY\\<cuenta> con sufijo."
Assert-True ($securityModule.Text -match 'NT SERVICE\\\\\.\+') "El regex de cuentas integradas no cubre NT SERVICE\\<cuenta> con sufijo."
Assert-True ($securityModule.Text -match 'allowedPrefixes') "No se restringe el prefijo de dominio/host aceptado para cuentas de servicio locales."

Write-Host "ServiceSecurity static tests OK."

# --- Pruebas funcionales puras (sin dependencias del SO) --------------------
. (Join-Path $PSScriptRoot "ServiceSecurity.ps1")

$builtInCases = @(
    "LocalSystem", "LocalService", "NetworkService", "SYSTEM",
    "NT AUTHORITY\LocalService", "NT AUTHORITY\NetworkService", "NT AUTHORITY\SYSTEM",
    "NT SERVICE\TrustedInstaller", "NT SERVICE\MSSQLSERVER", ""
)
foreach ($case in $builtInCases) {
    Assert-True (Test-AtlasBuiltInServiceAccount -StartName $case) "Test-AtlasBuiltInServiceAccount deberia rechazar '$case' (P6)."
}

$nonBuiltInCases = @(".\AtlasBalanceApiSvc", "$env:COMPUTERNAME\AtlasBalanceWatchdogSvc", "AtlasBalanceApiSvc")
foreach ($case in $nonBuiltInCases) {
    Assert-True (-not (Test-AtlasBuiltInServiceAccount -StartName $case)) "Test-AtlasBuiltInServiceAccount no deberia rechazar la cuenta dedicada '$case' (P6)."
}

Write-Host "ServiceSecurity functional tests OK."
