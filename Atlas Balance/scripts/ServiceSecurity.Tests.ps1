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
Assert-True ($updateScript.Text -notmatch '(?im)\b(New-Service|Install-AtlasService)\b') "La actualizacion vuelve a registrar servicios y podria perder su identidad."
Assert-True ($updateScript.Text -match 'Assert-AtlasServiceIdentities') "La actualizacion no bloquea instalaciones heredadas con cuentas integradas."
Assert-True ($updateScript.Text -match 'ElevatedUpdate') "La actualizacion no tiene una ruta explícita para el actualizador dedicado."

Write-Host "ServiceSecurity static tests OK."
