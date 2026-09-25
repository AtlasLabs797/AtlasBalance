param(
    [Parameter(Mandatory = $true)][string]$InstallPath,
    [Parameter(Mandatory = $true)][string]$RequestPath
)

$ErrorActionPreference = "Stop"
$watchdogDirectory = Join-Path $InstallPath "watchdog"
$watchdogBinary = Join-Path $watchdogDirectory "AtlasBalance.Watchdog.exe"
$runnerRoot = Join-Path $InstallPath "config\update-runner"
$runnerDirectory = Join-Path $runnerRoot ("run-" + [Guid]::NewGuid().ToString("N"))
$runnerBinary = Join-Path $runnerDirectory "AtlasBalance.Watchdog.exe"

if (-not (Test-Path -LiteralPath $watchdogBinary -PathType Leaf)) {
    throw "No se encontro el binario protegido del runner de actualizacion."
}

# SECURITY (P1b/P3): esta tarea corre como SYSTEM y config\update-runner solo
# es escribible por Administrators/SYSTEM (ver ServiceSecurity.ps1
# Protect-AtlasInstallTree), asi que la cuenta de Watchdog no puede
# interferir en esta copia. Se copia el set de runtime completo (exe + dll +
# deps.json + runtimeconfig.json), no solo el exe: la publicacion no es
# single-file y el apphost necesita esos ficheros junto a el para arrancar.
# Se copia a un subdirectorio nuevo por ejecucion (y se borra al terminar)
# en vez de ejecutar in-place desde watchdog\, porque el propio update que
# este runner dispara sobrescribe watchdog\AtlasBalance.Watchdog.exe/.dll
# mientras el runner sigue vivo; un exe/dll bloqueado por el proceso en
# ejecucion rompería esa copia.
# Si algo revienta antes de fijar el codigo real (p.ej. la copia falla a
# medias), debe salir en fallo, no en 0/$null: bajo PS 5.1 "exit $exitCode"
# con $exitCode sin inicializar sale con 0 y el actualizador lo leeria como
# exito.
$exitCode = 1
try {
    New-Item -ItemType Directory -Path $runnerDirectory -Force | Out-Null
    # Recursivo: subcarpetas como runtimes\ o recursos satelite de idioma
    # tambien hacen falta para que el apphost arranque. logs\ se excluye a
    # proposito (no forma parte del runtime y no debe copiarse/borrarse aqui).
    Get-ChildItem -LiteralPath $watchdogDirectory -Force | Where-Object { $_.Name -ne "logs" } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $runnerDirectory $_.Name) -Recurse -Force
    }

    & $runnerBinary `
        --run-elevated-update `
        --install-path $InstallPath `
        --request-path $RequestPath
    $exitCode = $LASTEXITCODE
}
finally {
    Remove-Item -LiteralPath $runnerDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
exit $exitCode
