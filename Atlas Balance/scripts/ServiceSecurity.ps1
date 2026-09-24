# Funciones compartidas para registrar y verificar los servicios de Atlas Balance.
# Este archivo no contiene secretos persistentes: las credenciales solo viven en
# memoria durante el registro del servicio.

function New-AtlasServiceSecret {
    param([int]$Length = 48)

    $alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!#%_-"
    $bytes = New-Object byte[] $Length
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $chars = New-Object char[] $Length
    for ($i = 0; $i -lt $Length; $i++) { $chars[$i] = $alphabet[$bytes[$i] % $alphabet.Length] }
    return -join $chars
}

function Test-AtlasServiceAccountName {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name) -or $Name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$') {
        throw "Nombre de cuenta de servicio invalido: '$Name'."
    }
}

function Initialize-AtlasServiceAccount {
    param([string]$Name, [string]$Description)

    Test-AtlasServiceAccountName -Name $Name
    $existing = Get-LocalUser -Name $Name -ErrorAction SilentlyContinue
    $secret = New-AtlasServiceSecret
    $securePassword = ConvertTo-SecureString -String $secret -AsPlainText -Force
    $secret = $null

    if ($existing) {
        if (-not $existing.Enabled) { Enable-LocalUser -Name $Name -ErrorAction Stop }
        Set-LocalUser -Name $Name -Password $securePassword -PasswordNeverExpires $true -UserMayChangePassword $false -ErrorAction Stop
    }
    else {
        New-LocalUser -Name $Name -Password $securePassword -PasswordNeverExpires -UserMayNotChangePassword -AccountNeverExpires -Description $Description -ErrorAction Stop | Out-Null
    }

    $user = Get-LocalUser -Name $Name -ErrorAction Stop
    $privilegedGroupSids = @(
        "S-1-5-32-544", "S-1-5-32-548", "S-1-5-32-549",
        "S-1-5-32-550", "S-1-5-32-551", "S-1-5-32-547"
    )
    foreach ($groupSid in $privilegedGroupSids) {
        $members = @(Get-LocalGroupMember -SID $groupSid -ErrorAction SilentlyContinue)
        if ($members | Where-Object { $_.SID.Value -eq $user.SID.Value }) {
            throw "La cuenta '$Name' pertenece a un grupo local privilegiado ($groupSid); se rechaza el registro del servicio."
        }
    }

    $principal = ".\$Name"
    return [pscustomobject]@{
        Name = $Name
        Principal = $principal
        ComputerPrincipal = "$env:COMPUTERNAME\$Name"
        Sid = $user.SID.Value
        Credential = New-Object System.Management.Automation.PSCredential($principal, $securePassword)
    }
}

function Get-AtlasServiceLogonRightSids {
    param([string]$PolicyPath)

    $content = Get-Content -LiteralPath $PolicyPath -Raw -ErrorAction Stop
    $match = [regex]::Match($content, '(?im)^\s*SeServiceLogonRight\s*=\s*(?<values>[^\r\n]*)')
    if (-not $match.Success) { return @() }
    return @($match.Groups['values'].Value -split ',' | ForEach-Object { $_.Trim().TrimStart('*') } | Where-Object { $_ -match '^S-1-' })
}

function Get-AtlasServiceLogonPolicy {
    param([string]$InstallPath)

    $securityDirectory = Join-Path $InstallPath "config\ServiceSecurity"
    New-Item -ItemType Directory -Path $securityDirectory -Force | Out-Null
    Invoke-AtlasIcacls -Arguments @($securityDirectory, "/inheritance:r", "/grant:r", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F")
    $policyPath = Join-Path $securityDirectory "export.inf"
    try {
        & secedit.exe /export /cfg $policyPath /areas USER_RIGHTS | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "secedit /export fallo con $LASTEXITCODE." }
        $content = Get-Content -LiteralPath $policyPath -Raw -ErrorAction Stop
        $read = {
            param([string]$Name)
            $match = [regex]::Match($content, "(?im)^\s*$Name\s*=\s*(?<values>[^\r\n]*)")
            if (-not $match.Success) { return @() }
            return @($match.Groups["values"].Value -split ',' | ForEach-Object {
                $_.Trim().TrimStart('*').ToUpperInvariant()
            } | Where-Object { $_ -match '^S-1-' })
        }
        return [pscustomobject]@{
            ServiceLogon = @(& $read "SeServiceLogonRight")
            DenyServiceLogon = @(& $read "SeDenyServiceLogonRight")
        }
    }
    finally {
        Remove-Item -LiteralPath $securityDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-AtlasLocalGroupMembership {
    param([string]$MemberSid, [string]$GroupSid)

    $members = @(Get-LocalGroupMember -SID $GroupSid -ErrorAction SilentlyContinue)
    return [bool]($members | Where-Object { $_.SID.Value -eq $MemberSid })
}

function Assert-AtlasServiceAccountSecurity {
    param([string]$AccountName, [string]$AccountSid, [string]$InstallPath)

    $privilegedGroupSids = @(
        "S-1-5-32-544", "S-1-5-32-547", "S-1-5-32-548",
        "S-1-5-32-549", "S-1-5-32-550", "S-1-5-32-551"
    )
    foreach ($groupSid in $privilegedGroupSids) {
        if (Test-AtlasLocalGroupMembership -MemberSid $AccountSid -GroupSid $groupSid) {
            throw "La cuenta de servicio '$AccountName' pertenece a un grupo local privilegiado ($groupSid)."
        }
    }

    $policy = Get-AtlasServiceLogonPolicy -InstallPath $InstallPath
    if ($policy.DenyServiceLogon -contains $AccountSid.ToUpperInvariant()) {
        throw "SeDenyServiceLogonRight bloquea la cuenta de servicio '$AccountName'."
    }
    foreach ($deniedGroupSid in $policy.DenyServiceLogon) {
        if (Test-AtlasLocalGroupMembership -MemberSid $AccountSid -GroupSid $deniedGroupSid) {
            throw "SeDenyServiceLogonRight bloquea a la cuenta de servicio '$AccountName' mediante el grupo $deniedGroupSid."
        }
    }
    if ($policy.ServiceLogon -notcontains $AccountSid.ToUpperInvariant()) {
        throw "La cuenta de servicio '$AccountName' no tiene SeServiceLogonRight."
    }
}

function Grant-AtlasLogOnAsService {
    param([string[]]$Sids, [string]$InstallPath)

    $securityDirectory = Join-Path $InstallPath "config\ServiceSecurity"
    New-Item -ItemType Directory -Path $securityDirectory -Force | Out-Null
    & icacls.exe $securityDirectory /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No se pudo proteger el directorio temporal de la politica de seguridad." }

    $policyPath = Join-Path $securityDirectory "export.inf"
    $verifyPath = Join-Path $securityDirectory "verify.inf"
    $databasePath = Join-Path $securityDirectory "service-rights.sdb"
    try {
        & secedit.exe /export /cfg $policyPath /areas USER_RIGHTS | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "secedit /export fallo con $LASTEXITCODE." }
        $content = Get-Content -LiteralPath $policyPath -Raw
        $current = @(Get-AtlasServiceLogonRightSids -PolicyPath $policyPath)
        $deniedMatch = [regex]::Match($content, '(?im)^\s*SeDenyServiceLogonRight\s*=\s*(?<values>[^\r\n]*)')
        $denied = if ($deniedMatch.Success) {
            @($deniedMatch.Groups['values'].Value -split ',' | ForEach-Object { $_.Trim().TrimStart('*') } | Where-Object { $_ -match '^S-' })
        } else { @() }
        $blocked = @($Sids | Where-Object { $denied -contains $_.ToUpperInvariant() })
        if ($blocked.Count -gt 0) { throw "La politica SeDenyServiceLogonRight bloquea la cuenta de servicio: $($blocked -join ', ')." }
        $desired = @($current + $Sids | ForEach-Object { $_.ToUpperInvariant() } | Sort-Object -Unique)
        $line = "SeServiceLogonRight = " + (($desired | ForEach-Object { "*$_" }) -join ",")
        $match = [regex]::Match($content, '(?im)^\s*SeServiceLogonRight\s*=\s*[^\r\n]*')
        if ($match.Success) {
            $content = $content.Remove($match.Index, $match.Length).Insert($match.Index, $line)
        }
        else {
            if ($content -notmatch '(?im)^\[Privilege Rights\]') { $content += "`r`n[Privilege Rights]`r`n" }
            $content += "$line`r`n"
        }
        Set-Content -LiteralPath $policyPath -Value $content -Encoding Unicode
        & secedit.exe /configure /db $databasePath /cfg $policyPath /areas USER_RIGHTS /quiet | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "secedit /configure fallo con $LASTEXITCODE." }
        & secedit.exe /export /cfg $verifyPath /areas USER_RIGHTS | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "No se pudo exportar la politica para verificarla." }
        $verified = @(Get-AtlasServiceLogonRightSids -PolicyPath $verifyPath)
        foreach ($sid in $Sids) {
            if ($verified -notcontains $sid.ToUpperInvariant()) { throw "No se verifico SeServiceLogonRight para SID $sid." }
        }
    }
    finally {
        Remove-Item -LiteralPath $securityDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-AtlasIcacls {
    param([string[]]$Arguments)

    & icacls.exe @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls fallo sobre '$($Arguments[0])' con codigo $LASTEXITCODE." }
}

function Protect-AtlasInstallTree {
    param([string]$InstallPath, [pscustomobject]$ApiAccount, [pscustomobject]$WatchdogAccount)

    $normalSids = @("*S-1-1-0", "*S-1-5-11", "*S-1-5-32-545", "*S-1-5-4")
    Invoke-AtlasIcacls -Arguments ((@($InstallPath, "/inheritance:r", "/remove:g") + $normalSids + @("/remove:d") + $normalSids))
    Invoke-AtlasIcacls -Arguments @($InstallPath, "/setowner", "*S-1-5-32-544")
    Invoke-AtlasIcacls -Arguments @(
        $InstallPath, "/grant:r",
        "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F",
        "${ApiAccount.ComputerPrincipal}:(OI)(CI)RX", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)RX")

    foreach ($relative in @("api", "watchdog", "scripts", "backups", "exports", "updates", "logs", "api\logs", "watchdog\logs")) {
        $path = Join-Path $InstallPath $relative
        if (-not (Test-Path -LiteralPath $path)) { New-Item -ItemType Directory -Path $path -Force | Out-Null }
        $grant = if ($relative -in @("api", "watchdog")) {
            @("${ApiAccount.ComputerPrincipal}:(OI)(CI)RX", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)RX")
        } elseif ($relative -eq "scripts") {
            @("${ApiAccount.ComputerPrincipal}:(OI)(CI)RX", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)RX")
        } elseif ($relative -eq "updates") {
            # SECURITY (P1a): la API descarga, verifica firma/digest y extrae
            # el paquete de actualizacion directamente bajo updates\ (ver
            # ActualizacionService.DownloadAndPreparePackageAsync), asi que
            # necesita Modify ahi. Esto ya no reabre el hueco original: el
            # runner elevado (SYSTEM) nunca confia en lo que haya bajo
            # updates\; copia el ZIP+firma a una carpeta propia en
            # config\update-runner y vuelve a verificar la firma sobre esa
            # copia antes de extraerla y ejecutar nada (ver
            # ElevatedUpdateRunner). Watchdog solo necesita Modify sobre
            # updates\requests (mas abajo) para depositar pending-update.json;
            # aqui solo lee para localizar el ZIP que la API preparo.
            @("${ApiAccount.ComputerPrincipal}:(OI)(CI)M", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)RX")
        } elseif ($relative -eq "backups") {
            # La API crea/borra sus propios dumps aqui (BackupService.CreateBackupAsync
            # via pg_dump, ApplyRetentionAsync, GoogleDriveBackupService.ImportAsync) y
            # Watchdog escribe su propio backup previo a actualizar y la copia de
            # rollback de binarios (CreateDatabaseBackupAsync/CreatePackageRollbackCopy);
            # ambos necesitan Modify.
            @("${ApiAccount.ComputerPrincipal}:(OI)(CI)M", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)M")
        } elseif ($relative -eq "exports") {
            @("${ApiAccount.ComputerPrincipal}:(OI)(CI)M", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)RX")
        } elseif ($relative -eq "api\logs") {
            @("${ApiAccount.ComputerPrincipal}:(OI)(CI)M")
        } elseif ($relative -eq "watchdog\logs") {
            @("${WatchdogAccount.ComputerPrincipal}:(OI)(CI)M")
        } else {
            @("${ApiAccount.ComputerPrincipal}:(OI)(CI)M", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)M")
        }
        Invoke-AtlasIcacls -Arguments (@($path, "/T", "/C", "/remove:g", $ApiAccount.ComputerPrincipal, $WatchdogAccount.ComputerPrincipal, "/remove:d", $ApiAccount.ComputerPrincipal, $WatchdogAccount.ComputerPrincipal))
        Invoke-AtlasIcacls -Arguments (@($path, "/grant:r") + $grant)
    }

    $requestPath = Join-Path $InstallPath "updates\requests"
    New-Item -ItemType Directory -Path $requestPath -Force | Out-Null
    Invoke-AtlasIcacls -Arguments @($requestPath, "/inheritance:r", "/grant:r", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F", "${WatchdogAccount.ComputerPrincipal}:(OI)(CI)M")

    $configPath = Join-Path $InstallPath "config"
    if (Test-Path -LiteralPath $configPath) {
        Invoke-AtlasIcacls -Arguments @($configPath, "/inheritance:r", "/grant:r", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F")
    }
    # SECURITY (P1b): update-runner es donde el runner elevado (SYSTEM, via
    # la tarea programada AtlasBalance.Update) copia su propio runtime antes
    # de ejecutarlo. Si Watchdog tuviera Modify aqui podria plantar un DLL
    # junto al exe copiado y SYSTEM lo cargaria al arrancar el apphost
    # (la publicacion no es single-file). Solo Administrators/SYSTEM escriben.
    $runnerPath = Join-Path $configPath "update-runner"
    New-Item -ItemType Directory -Path $runnerPath -Force | Out-Null
    Invoke-AtlasIcacls -Arguments @($runnerPath, "/inheritance:r", "/grant:r", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F")

    # Los ficheros de configuracion y el certificado se protegen antes de
    # sincronizar el paquete. Conservar solo Administrators/SYSTEM dejaria a
    # los servicios sin poder arrancar; conceder Modify permitiria alterar
    # secretos o binarios. Cada servicio recibe solo lectura del recurso que
    # necesita.
    $apiConfigPath = Join-Path $InstallPath "api\appsettings.Production.json"
    if (Test-Path -LiteralPath $apiConfigPath) {
        Invoke-AtlasIcacls -Arguments @($apiConfigPath, "/inheritance:r", "/grant:r", "*S-1-5-32-544:F", "*S-1-5-18:F", "${ApiAccount.ComputerPrincipal}:R")
    }
    $watchdogConfigPath = Join-Path $InstallPath "watchdog\appsettings.Production.json"
    if (Test-Path -LiteralPath $watchdogConfigPath) {
        Invoke-AtlasIcacls -Arguments @($watchdogConfigPath, "/inheritance:r", "/grant:r", "*S-1-5-32-544:F", "*S-1-5-18:F", "${WatchdogAccount.ComputerPrincipal}:R")
    }
    $certsPath = Join-Path $InstallPath "certs"
    if (Test-Path -LiteralPath $certsPath) {
        Invoke-AtlasIcacls -Arguments @($certsPath, "/inheritance:r", "/grant:r", "*S-1-5-32-544:(OI)(CI)F", "*S-1-5-18:(OI)(CI)F", "${ApiAccount.ComputerPrincipal}:(OI)(CI)RX")
    }

    foreach ($file in @("VERSION", "atlas-balance.runtime.json", "watchdog-state.json")) {
        $path = Join-Path $InstallPath $file
        if (Test-Path -LiteralPath $path) { Invoke-AtlasIcacls -Arguments @($path, "/grant:r", "${WatchdogAccount.ComputerPrincipal}:M") }
    }

    foreach ($path in @(
        (Join-Path $env:ProgramData "AtlasBalance\keys"),
        (Join-Path $env:ProgramData "AtlasBalance\logs\security"))) {
        if (Test-Path -LiteralPath $path) { Invoke-AtlasIcacls -Arguments @($path, "/grant:r", "${ApiAccount.ComputerPrincipal}:(OI)(CI)M") }
    }
}

function Install-AtlasUpdateTask {
    param([string]$InstallPath, [pscustomobject]$WatchdogAccount)

    $runnerScript = Join-Path $InstallPath "scripts\Run-AtlasElevatedUpdate.ps1"
    if (-not (Test-Path -LiteralPath $runnerScript)) {
        throw "No se encontro el runner protegido de actualizaciones: $runnerScript."
    }

    $requestPath = Join-Path $InstallPath "updates\requests\pending-update.json"
    $powershellPath = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
    $taskName = "AtlasBalance.Update"
    $escape = { param([string]$Value) [System.Security.SecurityElement]::Escape($Value) }
    $commandXml = & $escape $powershellPath
    $argumentsXml = & $escape ("-NoProfile -ExecutionPolicy Bypass -File `"$runnerScript`" -InstallPath `"$InstallPath`" -RequestPath `"$requestPath`"")
    $taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Author>Atlas Balance</Author>
    <Description>Actualizacion firmada de Atlas Balance con privilegio temporal.</Description>
    <SecurityDescriptor>D:(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;$($WatchdogAccount.Sid))</SecurityDescriptor>
  </RegistrationInfo>
  <Principals>
    <Principal id="Author">
      <UserId>S-1-5-18</UserId>
      <LogonType>ServiceAccount</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$commandXml</Command>
      <Arguments>$argumentsXml</Arguments>
      <WorkingDirectory>$(& $escape $InstallPath)</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
"@
    $securityDirectory = Join-Path $InstallPath "config\ServiceSecurity"
    New-Item -ItemType Directory -Path $securityDirectory -Force | Out-Null
    $xmlPath = Join-Path $securityDirectory "AtlasBalance.Update.xml"
    try {
        Set-Content -LiteralPath $xmlPath -Value $taskXml -Encoding Unicode
        Register-ScheduledTask -TaskName $taskName -Xml (Get-Content -LiteralPath $xmlPath -Raw) -User "SYSTEM" -Force | Out-Null
    }
    finally {
        Remove-Item -LiteralPath $xmlPath -Force -ErrorAction SilentlyContinue
    }
}

function Get-AtlasWindowsService {
    param([string]$Name)
    return Get-CimInstance -ClassName Win32_Service -Filter "Name='$Name'" -ErrorAction Stop
}

function Test-AtlasBuiltInServiceAccount {
    param([string]$StartName)
    return [string]::IsNullOrWhiteSpace($StartName) -or $StartName -match '^(LocalSystem|LocalService|NetworkService|SYSTEM|NT AUTHORITY\\.+|NT SERVICE\\.+)$'
}

function Test-AtlasServiceIdentity {
    param([string]$Name, [string]$InstallPath = "C:\AtlasBalance", [switch]$RejectBuiltIn)

    $service = Get-AtlasWindowsService -Name $Name
    if ($null -eq $service) { throw "No existe el servicio $Name." }
    if ($RejectBuiltIn -and (Test-AtlasBuiltInServiceAccount -StartName ([string]$service.StartName))) {
        throw "El servicio $Name usa '$($service.StartName)'; se rechazan LocalSystem/SYSTEM y cuentas integradas."
    }
    if ($RejectBuiltIn) {
        $startName = [string]$service.StartName
        $segments = @($startName -split '\\')
        # SECURITY (P6): si viene calificado con dominio/host, solo se acepta
        # ".", el nombre local del equipo o ninguna calificacion. De lo
        # contrario "DOMINIO\nombre" se aceptaba con solo tener un usuario
        # local del mismo nombre, colando identidades de dominio ajenas.
        if ($segments.Count -gt 1) {
            $prefix = $segments[0]
            $allowedPrefixes = @(".", $env:COMPUTERNAME)
            if (($allowedPrefixes | Where-Object { $_ -eq $prefix }).Count -eq 0) {
                throw "El servicio $Name usa una cuenta calificada por un dominio o host no permitido ('$startName')."
            }
        }
        $accountName = $segments[-1]
        $user = Get-LocalUser -Name $accountName -ErrorAction SilentlyContinue
        if ($null -eq $user) {
            throw "El servicio $Name no usa una cuenta local administrable; se rechazan identidades heredadas o de dominio."
        }
        Assert-AtlasServiceAccountSecurity -AccountName $accountName -AccountSid $user.SID.Value -InstallPath $InstallPath
    }
    return $service
}

function Assert-AtlasServiceIdentities {
    param(
        [string]$ApiServiceName = "AtlasBalance.API",
        [string]$WatchdogServiceName = "AtlasBalance.Watchdog",
        [string]$InstallPath = "C:\AtlasBalance"
    )

    $api = Test-AtlasServiceIdentity -Name $ApiServiceName -InstallPath $InstallPath -RejectBuiltIn
    $watchdog = Test-AtlasServiceIdentity -Name $WatchdogServiceName -InstallPath $InstallPath -RejectBuiltIn
    if ([string]::Equals([string]$api.StartName, [string]$watchdog.StartName, [StringComparison]::OrdinalIgnoreCase)) {
        throw "API y Watchdog deben usar cuentas distintas."
    }
    return [pscustomobject]@{ Api = $api; Watchdog = $watchdog }
}

function Test-AtlasCurrentIdentityIsServiceAccount {
    param([string]$ServiceName)

    $service = Test-AtlasServiceIdentity -Name $ServiceName -RejectBuiltIn
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $startName = [string]$service.StartName
    $accountName = ($startName -split '\\')[-1]
    $user = Get-LocalUser -Name $accountName -ErrorAction SilentlyContinue
    return $null -ne $user -and $currentSid -eq $user.SID.Value
}

function Grant-AtlasServiceControl {
    param([string]$ServiceName, [string]$AccountSid)

    $descriptor = (& sc.exe sdshow $ServiceName | Where-Object { $_ -match '^D:' } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($descriptor)) { throw "No se pudo leer el descriptor de $ServiceName." }
    $acePattern = '\(A;;[^;]*;;;' + [regex]::Escape($AccountSid) + '\)'
    $descriptor = [regex]::Replace($descriptor, $acePattern, '')
    $ace = "(A;;CCLCSWLOCRCRPWP;;;${AccountSid})"
    $descriptor = if ($descriptor -match 'S:') { $descriptor -replace 'S:', "$ace`$&" } else { $descriptor + $ace }
    & sc.exe sdset $ServiceName $descriptor | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "No se pudo conceder a Watchdog el control minimo de $ServiceName." }
    $verified = (& sc.exe sdshow $ServiceName | Where-Object { $_ -match '^D:' } | Select-Object -First 1)
    if ($verified -notmatch [regex]::Escape($ace)) {
        throw "No se verifico el control de $ServiceName para Watchdog."
    }
}

function Test-AtlasServiceRegistration {
    param([string]$Name, [string]$ExpectedPrincipal)

    $service = Test-AtlasServiceIdentity -Name $Name
    $expectedName = $ExpectedPrincipal.TrimStart('.\')
    $accepted = @($ExpectedPrincipal, "$env:COMPUTERNAME\$expectedName")
    if ($accepted -notcontains [string]$service.StartName) { throw "$Name no usa la cuenta minima '$ExpectedPrincipal'." }
}

function Install-AtlasService {
    param([string]$Name, [string]$DisplayName, [string]$Description, [string]$ExePath, [pscredential]$Credential, [string]$ExpectedPrincipal)

    $existing = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($existing) {
        if ($existing.Status -ne "Stopped") { Stop-Service -Name $Name -Force; $existing.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30)) }
        & sc.exe delete $Name | Out-Null
        Start-Sleep -Seconds 2
    }
    New-Service -Name $Name -BinaryPathName ('"' + $ExePath + '"') -DisplayName $DisplayName -Description $Description -StartupType Automatic -Credential $Credential | Out-Null
    & sc.exe failure $Name reset=86400 actions=restart/10000/restart/30000/restart/60000 | Out-Null
    Test-AtlasServiceRegistration -Name $Name -ExpectedPrincipal $ExpectedPrincipal
}

# SECURITY (P-UPGRADE): instalaciones anteriores a V-03.01 (p.ej. V-02.09)
# registran los servicios como LocalSystem; Assert-AtlasServiceIdentities con
# -RejectBuiltIn las rechaza, lo que antes abortaba cualquier actualizacion
# sobre una instalacion legado sin cambiar nada. Esta funcion migra esa
# instalacion a cuentas dedicadas reusando EXACTAMENTE las mismas funciones
# que usa el instalador en una instalacion nueva (Initialize-AtlasServiceAccount,
# Grant-AtlasLogOnAsService, Protect-AtlasInstallTree, Install-AtlasService,
# Grant-AtlasServiceControl, Install-AtlasUpdateTask), en vez de duplicar esa
# logica aqui. El llamador debe volver a invocar Assert-AtlasServiceIdentities
# despues para confirmar que la migracion dejo las identidades conformes.
function Repair-AtlasServiceIdentities {
    param(
        [string]$ApiServiceName = "AtlasBalance.API",
        [string]$WatchdogServiceName = "AtlasBalance.Watchdog",
        [Parameter(Mandatory = $true)][string]$InstallPath,
        [string]$ApiServiceAccountName = "AtlasBalanceApiSvc",
        [string]$WatchdogServiceAccountName = "AtlasBalanceWatchdogSvc"
    )

    $apiService = Get-AtlasWindowsService -Name $ApiServiceName
    $watchdogService = Get-AtlasWindowsService -Name $WatchdogServiceName
    if ($null -eq $apiService -or $null -eq $watchdogService) {
        throw "No se puede migrar identidades: falta el servicio $ApiServiceName o $WatchdogServiceName."
    }

    $apiAccount = Initialize-AtlasServiceAccount -Name $ApiServiceAccountName -Description "Cuenta dedicada del servicio API de Atlas Balance (migrada desde una instalacion anterior)"
    $watchdogAccount = Initialize-AtlasServiceAccount -Name $WatchdogServiceAccountName -Description "Cuenta dedicada del servicio Watchdog de Atlas Balance (migrada desde una instalacion anterior)"

    Grant-AtlasLogOnAsService -Sids @($apiAccount.Sid, $watchdogAccount.Sid) -InstallPath $InstallPath
    Protect-AtlasInstallTree -InstallPath $InstallPath -ApiAccount $apiAccount -WatchdogAccount $watchdogAccount
    Install-AtlasUpdateTask -InstallPath $InstallPath -WatchdogAccount $watchdogAccount

    $apiExe = Join-Path $InstallPath "api\AtlasBalance.API.exe"
    $watchdogExe = Join-Path $InstallPath "watchdog\AtlasBalance.Watchdog.exe"
    Install-AtlasService -Name $WatchdogServiceName -DisplayName "Atlas Balance - Watchdog" -Description "Backups y actualizaciones de Atlas Balance" -ExePath $watchdogExe -Credential $watchdogAccount.Credential -ExpectedPrincipal $watchdogAccount.Principal
    Install-AtlasService -Name $ApiServiceName -DisplayName "Atlas Balance - API" -Description "API y frontend de Atlas Balance" -ExePath $apiExe -Credential $apiAccount.Credential -ExpectedPrincipal $apiAccount.Principal
    Grant-AtlasServiceControl -ServiceName $ApiServiceName -AccountSid $watchdogAccount.Sid
    Grant-AtlasServiceControl -ServiceName $WatchdogServiceName -AccountSid $watchdogAccount.Sid

    return [pscustomobject]@{
        Api = $apiAccount
        Watchdog = $watchdogAccount
    }
}
