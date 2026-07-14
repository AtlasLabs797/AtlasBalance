# ==============================================
# Atlas Balance - Drill de Backup/Restore
# Valida el ciclo completo: pg_dump -> pg_restore -> verificacion de recuentos.
# No modifica la BD "atlas_balance" original (solo lectura + pg_dump).
# Crea y destruye una BD temporal "atlas_restore_drill".
# ==============================================

param(
    [string]$ContainerName = "atlas_balance_db",
    [string]$DbName = "atlas_balance",
    [string]$DrillDbName = "atlas_restore_drill",
    [string]$DbUser = "postgres",
    [string]$LocalPgBinPath = "C:\Proyectos\Atlas Balance Dev\tools\pgsql\bin",
    [string]$LocalDbHost = "127.0.0.1",
    [int]$LocalDbPort = 5433
)

$ErrorActionPreference = "Stop"
$exitCode = 1
$dumpFileContainer = "/tmp/restore-drill.dump"
$dumpFileLocal = $null
$mode = $null

function Write-Section($text) {
    Write-Host ""
    Write-Host "=== $text ===" -ForegroundColor Cyan
}

function Invoke-Checked {
    param(
        [string]$Exe,
        [string[]]$Arguments,
        [string]$FailMessage,
        [switch]$AllowNonFatal
    )
    $priorEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = @(& $Exe @Arguments 2>&1)
    } finally {
        $ErrorActionPreference = $priorEap
    }
    $code = $LASTEXITCODE
    $output = @($output | ForEach-Object { $_.ToString() })
    $output | ForEach-Object { Write-Host $_ }
    if ($code -ne 0 -and -not $AllowNonFatal) {
        throw "$FailMessage (exit code $code)"
    }
    return @{ Code = $code; Output = $output }
}

# Tablas clave a comparar (nombres reales del esquema, ver Documentacion/SPEC.md)
$keyTables = @("USUARIOS", "EXTRACTOS", "CUENTAS", "TITULARES", "AUDITORIAS")

try {
    Write-Section "1. Deteccion de modo (Docker vs. Postgres local)"

    $dockerRunning = $false
    try {
        $psOutput = & docker ps --filter "name=$ContainerName" --format "{{.Names}}" 2>&1
        if ($LASTEXITCODE -eq 0 -and ($psOutput -match [regex]::Escape($ContainerName))) {
            $dockerRunning = $true
        }
    } catch {
        $dockerRunning = $false
    }

    if ($dockerRunning) {
        $mode = "docker"
        Write-Host "Contenedor '$ContainerName' detectado y corriendo. Usando docker exec." -ForegroundColor Green
    } else {
        $mode = "local"
        Write-Host "Contenedor '$ContainerName' no esta corriendo (o el puerto lo sirve un Postgres local)." -ForegroundColor Yellow
        Write-Host "Usando binarios locales en '$LocalPgBinPath' contra ${LocalDbHost}:${LocalDbPort}." -ForegroundColor Yellow

        $psqlExe = Join-Path $LocalPgBinPath "psql.exe"
        if (-not (Test-Path $psqlExe)) {
            throw "No se encontro psql.exe en '$LocalPgBinPath' y el contenedor Docker no esta activo. Sin via de conexion."
        }
    }

    Write-Section "2. Password de conexion"
    $envFile = Join-Path (Split-Path $PSScriptRoot -Parent) ".env"
    $pgPassword = $null
    if (Test-Path $envFile) {
        $line = Get-Content $envFile | Where-Object { $_ -match '^ATLAS_BALANCE_POSTGRES_OWNER_PASSWORD=' } | Select-Object -First 1
        if ($line) {
            $pgPassword = $line -replace '^ATLAS_BALANCE_POSTGRES_OWNER_PASSWORD=', ''
        }
    }
    if (-not $pgPassword -and $mode -eq "local") {
        throw "No se pudo leer ATLAS_BALANCE_POSTGRES_OWNER_PASSWORD desde '$envFile'."
    }

    $previousPgPassword = $env:PGPASSWORD
    if ($pgPassword) {
        $env:PGPASSWORD = $pgPassword
    }

    # Helper que ejecuta psql segun el modo
    function Invoke-Psql {
        param([string]$Database, [string]$Sql, [switch]$AllowNonFatal)
        if ($mode -eq "docker") {
            $psqlArgs = @("exec", "-e", "PGPASSWORD=$($env:PGPASSWORD)", $ContainerName, "psql", "-U", $DbUser, "-d", $Database, "-t", "-A", "-c", $Sql)
            return Invoke-Checked -Exe "docker" -Arguments $psqlArgs -FailMessage "psql fallo en '$Database'" -AllowNonFatal:$AllowNonFatal
        } else {
            $psqlExe = Join-Path $LocalPgBinPath "psql.exe"
            $psqlArgs = @("-h", $LocalDbHost, "-p", $LocalDbPort, "-U", $DbUser, "-d", $Database, "-t", "-A", "-c", $Sql)
            return Invoke-Checked -Exe $psqlExe -Arguments $psqlArgs -FailMessage "psql fallo en '$Database'" -AllowNonFatal:$AllowNonFatal
        }
    }

    Write-Section "3. pg_dump de '$DbName' (formato custom)"
    if ($mode -eq "docker") {
        Invoke-Checked -Exe "docker" -Arguments @("exec", "-e", "PGPASSWORD=$($env:PGPASSWORD)", $ContainerName, "pg_dump", "-U", $DbUser, "-d", $DbName, "-F", "c", "-f", $dumpFileContainer) -FailMessage "pg_dump fallo" | Out-Null
    } else {
        $dumpFileLocal = Join-Path $env:TEMP "atlas-restore-drill-$(Get-Date -Format 'yyyyMMddHHmmss').dump"
        $pgDumpExe = Join-Path $LocalPgBinPath "pg_dump.exe"
        Invoke-Checked -Exe $pgDumpExe -Arguments @("-h", $LocalDbHost, "-p", $LocalDbPort, "-U", $DbUser, "-d", $DbName, "-F", "c", "-f", $dumpFileLocal) -FailMessage "pg_dump fallo" | Out-Null
        Write-Host "Dump generado en: $dumpFileLocal" -ForegroundColor Green
    }

    Write-Section "4. Recuentos ORIGEN ('$DbName')"
    $originCounts = @{}
    $originTableCount = [int](Invoke-Psql -Database $DbName -Sql "SELECT count(*) FROM information_schema.tables WHERE table_schema='public';").Output[0]
    Write-Host "Tablas en schema public: $originTableCount"
    foreach ($t in $keyTables) {
        $result = Invoke-Psql -Database $DbName -Sql "SELECT count(*) FROM \`"$t\`";" -AllowNonFatal
        if ($result.Code -eq 0) {
            $originCounts[$t] = [int]$result.Output[0]
        } else {
            $originCounts[$t] = $null
            Write-Host "  Aviso: no se pudo contar '$t' en origen (no existe?)." -ForegroundColor Yellow
        }
        Write-Host ("  {0,-12}: {1}" -f $t, $originCounts[$t])
    }

    Write-Section "5. Crear BD limpia '$DrillDbName'"
    Invoke-Psql -Database "postgres" -Sql "DROP DATABASE IF EXISTS $DrillDbName;" -AllowNonFatal | Out-Null
    $createResult = Invoke-Psql -Database "postgres" -Sql "CREATE DATABASE $DrillDbName OWNER $DbUser;" -AllowNonFatal
    if ($createResult.Code -ne 0) {
        $createErrorLines = $createResult.Output | Where-Object { $_ -match 'ERROR' }
        if ($createErrorLines) {
            throw "No se pudo crear la BD '$DrillDbName': $($createErrorLines -join ' | ')"
        }
    }
    Write-Host "BD '$DrillDbName' creada." -ForegroundColor Green

    Write-Section "6. pg_restore sobre '$DrillDbName'"
    $restoreResult = $null
    if ($mode -eq "docker") {
        $restoreArgs = @("exec", "-e", "PGPASSWORD=$($env:PGPASSWORD)", $ContainerName, "pg_restore", "-U", $DbUser, "-d", $DrillDbName, $dumpFileContainer)
        $restoreResult = Invoke-Checked -Exe "docker" -Arguments $restoreArgs -FailMessage "pg_restore fallo" -AllowNonFatal
    } else {
        $pgRestoreExe = Join-Path $LocalPgBinPath "pg_restore.exe"
        $restoreArgs = @("-h", $LocalDbHost, "-p", $LocalDbPort, "-U", $DbUser, "-d", $DrillDbName, $dumpFileLocal)
        $restoreResult = Invoke-Checked -Exe $pgRestoreExe -Arguments $restoreArgs -FailMessage "pg_restore fallo" -AllowNonFatal
    }

    # pg_restore devuelve exit code != 0 tambien por warnings de rol/ownership no fatales.
    # Tratamos como fatal solo si aparecen errores que no sean de "role"/"ownership"/"already exists".
    if ($restoreResult.Code -ne 0) {
        $fatalLines = $restoreResult.Output | Where-Object {
            $_ -match 'error' -and
            $_ -notmatch 'role "[^"]+" does not exist' -and
            $_ -notmatch 'must be owner' -and
            $_ -notmatch 'already exists'
        }
        if ($fatalLines) {
            Write-Host "Errores fatales detectados en pg_restore:" -ForegroundColor Red
            $fatalLines | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
            throw "pg_restore devolvio errores no tolerables."
        } else {
            Write-Host "pg_restore termino con advertencias no fatales (roles/ownership/duplicados). Continuando con verificacion." -ForegroundColor Yellow
        }
    }

    Write-Section "7. Recuentos RESTAURADO ('$DrillDbName')"
    $restoredCounts = @{}
    $restoredTableCount = [int](Invoke-Psql -Database $DrillDbName -Sql "SELECT count(*) FROM information_schema.tables WHERE table_schema='public';").Output[0]
    Write-Host "Tablas en schema public: $restoredTableCount"
    foreach ($t in $keyTables) {
        $result = Invoke-Psql -Database $DrillDbName -Sql "SELECT count(*) FROM \`"$t\`";" -AllowNonFatal
        if ($result.Code -eq 0) {
            $restoredCounts[$t] = [int]$result.Output[0]
        } else {
            $restoredCounts[$t] = $null
            Write-Host "  Aviso: no se pudo contar '$t' en restaurado." -ForegroundColor Yellow
        }
        Write-Host ("  {0,-12}: {1}" -f $t, $restoredCounts[$t])
    }

    Write-Section "8. Comparacion"
    $allMatch = $true
    if ($originTableCount -ne $restoredTableCount) {
        $allMatch = $false
        Write-Host "MISMATCH tablas: origen=$originTableCount restaurado=$restoredTableCount" -ForegroundColor Red
    } else {
        Write-Host "OK tablas: $originTableCount == $restoredTableCount" -ForegroundColor Green
    }

    foreach ($t in $keyTables) {
        $o = $originCounts[$t]
        $r = $restoredCounts[$t]
        if ($null -eq $o -or $null -eq $r) {
            $allMatch = $false
            Write-Host ("MISMATCH {0,-12}: origen={1} restaurado={2} (no comparable)" -f $t, $o, $r) -ForegroundColor Red
        } elseif ($o -ne $r) {
            $allMatch = $false
            Write-Host ("MISMATCH {0,-12}: origen={1} restaurado={2}" -f $t, $o, $r) -ForegroundColor Red
        } else {
            Write-Host ("OK       {0,-12}: {1} == {2}" -f $t, $o, $r) -ForegroundColor Green
        }
    }

    if ($allMatch) {
        $exitCode = 0
    } else {
        $exitCode = 1
    }
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
finally {
    Write-Section "9. Limpieza"
    try {
        if ($mode -eq "docker") {
            & docker exec $ContainerName rm -f $dumpFileContainer 2>&1 | Out-Null
        } elseif ($dumpFileLocal -and (Test-Path $dumpFileLocal)) {
            Remove-Item -Path $dumpFileLocal -Force -ErrorAction SilentlyContinue
            Write-Host "Dump temporal local eliminado: $dumpFileLocal" -ForegroundColor Green
        }
    } catch {
        Write-Host "Aviso: no se pudo borrar el dump temporal: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    try {
        Invoke-Psql -Database "postgres" -Sql "DROP DATABASE IF EXISTS $DrillDbName;" -AllowNonFatal | Out-Null
        Write-Host "BD temporal '$DrillDbName' eliminada." -ForegroundColor Green
    } catch {
        Write-Host "Aviso: no se pudo eliminar la BD temporal '$DrillDbName': $($_.Exception.Message)" -ForegroundColor Yellow
    }

    $env:PGPASSWORD = $previousPgPassword
}

Write-Section "RESUMEN"
if ($exitCode -eq 0) {
    Write-Host "PASS: el ciclo backup -> restore -> verificacion completo con exito." -ForegroundColor Green
} else {
    Write-Host "FAIL: revisa los mensajes anteriores." -ForegroundColor Red
}

exit $exitCode
