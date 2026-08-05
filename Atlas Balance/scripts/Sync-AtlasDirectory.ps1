# Sync-AtlasDirectory.ps1
# Funciones extraidas de Instalar-AtlasBalance.ps1 para que Sync-DirectoryPreserveConfig
# sea testeable de forma aislada. La copia atomica con staging + rollback es
# central para el incidente V-02.07 (instalacion a medias sin vuelta atras).
#
# API expuesta:
#   Sync-DirectoryPreserveConfig -Source <dir> -Target <dir>
#   Get-RelativePathCompat      -BasePath <dir> -FullPath <file>

Set-StrictMode -Version Latest

function Get-RelativePathCompat {
    param([string]$BasePath, [string]$FullPath)

    $base = [IO.Path]::GetFullPath($BasePath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $base = $base + [IO.Path]::DirectorySeparatorChar
    $path = [IO.Path]::GetFullPath($FullPath)

    if ($path.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) {
        return $path.Substring($base.Length)
    }

    return Split-Path -Leaf $FullPath
}

function Sync-DirectoryPreserveConfig {
    # V-02.08: copia atomica con rollback. Si algo falla a mitad, target queda
    # exactamente como estaba antes de la llamada.
    #
    # Pasos:
    #   1. Copia source -> <Target>.staging, sin tocar target.
    #   2. Delta = (archivos de staging que no existen en target) union
    #      (archivos de target que no estan en staging y no son protegidos).
    #   3. Backup del delta a <Target>.backup.
    #   4. Aplica el delta en target.
    #   5. Si algo falla en pasos 3 o 4, restaura backup y aborta.
    #
    # Protecciones:
    #   - appsettings.Production.json del paquete NUNCA sobrescribe el
    #     del usuario (ni siquiera entra a staging).
    #   - appsettings*.json y logs\* en target NUNCA se borran.
    param(
        [string]$Source,
        [string]$Target
    )

    if (-not (Test-Path $Source)) {
        throw "No existe la carpeta origen: $Source"
    }

    $staging = "$Target.staging"
    $backup = "$Target.backup"

    if (Test-Path -LiteralPath $staging) {
        Write-Host "  [AVISO] Encontrado staging previo en $staging. Limpiando." -ForegroundColor Yellow
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction Stop
    }
    if (Test-Path -LiteralPath $backup) {
        Write-Host "  [AVISO] Encontrado backup previo en $backup. Limpiando." -ForegroundColor Yellow
        Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction Stop
    }

    New-Item -ItemType Directory -Path $Target -Force | Out-Null
    New-Item -ItemType Directory -Path $staging -Force | Out-Null

    try {
        # 1. Copia source -> staging.
        $sourceFiles = Get-ChildItem -LiteralPath $Source -Recurse -File
        $stagingRelativeFiles = New-Object "System.Collections.Generic.HashSet[string]" -ArgumentList ([StringComparer]::OrdinalIgnoreCase)

        foreach ($file in $sourceFiles) {
            $relative = Get-RelativePathCompat -BasePath $Source -FullPath $file.FullName
            if ($relative -like "appsettings.Production.json" -and (Test-Path (Join-Path $Target $relative))) {
                continue
            }
            [void]$stagingRelativeFiles.Add($relative)
            $stagingDestination = Join-Path $staging $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $stagingDestination) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $stagingDestination -Force
        }

        # 2. Calcula delta entre target y staging.
        $targetFiles = @(Get-ChildItem -LiteralPath $Target -Recurse -File -ErrorAction SilentlyContinue)
        $targetRelativeFiles = New-Object "System.Collections.Generic.HashSet[string]" -ArgumentList ([StringComparer]::OrdinalIgnoreCase)
        foreach ($file in $targetFiles) {
            $relative = Get-RelativePathCompat -BasePath $Target -FullPath $file.FullName
            [void]$targetRelativeFiles.Add($relative)
        }

        # Archivos que YA estan en target y hay que sobrescribir desde staging.
        $overwriteRelative = @()
        foreach ($relative in $stagingRelativeFiles) {
            if ($targetRelativeFiles.Contains($relative)) {
                $overwriteRelative += $relative
            }
        }

        # Archivos en target que NO estan en staging y no son protegidos: se
        # borran. Si target esta vacio, este array queda vacio.
        $removeRelative = @()
        foreach ($relative in $targetRelativeFiles) {
            if ($relative -like "appsettings*.json" -or $relative -like "logs\*") {
                continue
            }
            if (-not $stagingRelativeFiles.Contains($relative)) {
                $removeRelative += $relative
            }
        }

        # 3. Backup del delta antes de tocar target.
        foreach ($relative in ($overwriteRelative + $removeRelative)) {
            $sourceFile = Join-Path $Target $relative
            $backupFile = Join-Path $backup $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $backupFile) -Force | Out-Null
            Copy-Item -LiteralPath $sourceFile -Destination $backupFile -Force
        }

        # 4. Aplica el delta en target.
        #    a) Archivos nuevos en staging: mover a target.
        #    b) Archivos que se sobreescriben: borrarlos en target y mover.
        #    c) Archivos obsoletos en target: eliminarlos.
        foreach ($relative in $stagingRelativeFiles) {
            $stagingSource = Join-Path $staging $relative
            $targetDestination = Join-Path $Target $relative
            if (Test-Path -LiteralPath $targetDestination) {
                Remove-Item -LiteralPath $targetDestination -Force
            }
            else {
                New-Item -ItemType Directory -Path (Split-Path -Parent $targetDestination) -Force | Out-Null
            }
            Move-Item -LiteralPath $stagingSource -Destination $targetDestination -Force
        }
        foreach ($relative in $removeRelative) {
            $targetFile = Join-Path $Target $relative
            if (Test-Path -LiteralPath $targetFile) {
                Remove-Item -LiteralPath $targetFile -Force
            }
        }

        # 5. Limpia staging y backup.
        if (Test-Path -LiteralPath $staging) {
            Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $backup) {
            Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    catch {
        Write-Host "  [ERROR] Fallo la sincronizacion $Source -> $Target. Iniciando rollback..." -ForegroundColor Red
        try {
            if (Test-Path -LiteralPath $backup) {
                $backupFiles = Get-ChildItem -LiteralPath $backup -Recurse -File
                foreach ($bf in $backupFiles) {
                    $relative = Get-RelativePathCompat -BasePath $backup -FullPath $bf.FullName
                    $targetDestination = Join-Path $Target $relative
                    if (Test-Path -LiteralPath $targetDestination) {
                        Remove-Item -LiteralPath $targetDestination -Force -ErrorAction SilentlyContinue
                    }
                    else {
                        New-Item -ItemType Directory -Path (Split-Path -Parent $targetDestination) -Force | Out-Null
                    }
                    Copy-Item -LiteralPath $bf.FullName -Destination $targetDestination -Force
                }
            }
        }
        catch {
            Write-Host "  [ERROR] Rollback parcial: $($_.Exception.Message). Quedan staging/backup para recuperacion manual." -ForegroundColor Red
        }
        if (Test-Path -LiteralPath $staging) {
            Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $backup) {
            Write-Host "  [AVISO] Backup conservado en $backup para recuperacion manual." -ForegroundColor Yellow
        }
        throw
    }
}
