param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

$ErrorActionPreference = "Stop"

$rootPath = (Resolve-Path -LiteralPath $Root).Path

# Directorios a excluir del escaneo (comparacion por segmentos, no por subcadena).
$excludedSegmentNames = @(
    ".git",
    "node_modules",
    "bin",
    "obj",
    "Atlas Balance Release",
    "tools",
    "pgdata"
)

# Regex de deteccion de secretos. Se mantiene como en la version previa.
$patterns = @(
    @{ Name = "Atlas/OpenAI/OpenRouter token"; Regex = '(sk_atlas_balance_[A-Za-z0-9_-]{32,}|sk-or-v1-[A-Za-z0-9_-]{32,}|sk-proj-[A-Za-z0-9_-]{32,}|sk-[A-Za-z0-9_-]{48,})' },
    @{ Name = "JWT or shared secret assignment"; Regex = '(JwtSettings__Secret|WatchdogSettings__SharedSecret|RlsContext__Secret|ATLAS_[A-Z0-9_]*SECRET)[^`r`n]{0,80}[:=][^`r`n]{12,}' },
    @{ Name = "Private key"; Regex = '-----BEGIN (RSA |EC |OPENSSH |)PRIVATE KEY-----' },
    @{ Name = "Connection string with password"; Regex = '(Host|Server)=.+;(Password|Pwd)=(?!\$|X{2,}\b|test\b|x\b|<)[^;`r`n]{12,}' }
)

$allowedTemplateSuffixes = @(".template", ".example", ".sample")
$hits = New-Object System.Collections.Generic.List[string]

# Separadores validos para partir rutas (Windows + Linux).
$sepChars = @([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)

function Split-PathSegments {
    param([string]$Path)

    if ([string]::IsNullOrEmpty($Path)) {
        return @()
    }

    # Normalizar separadores para que '/' y '\' se traten igual.
    $normalized = $Path.Replace([IO.Path]::AltDirectorySeparatorChar, [IO.Path]::DirectorySeparatorChar)

    # Quitar la raiz (letra de unidad Windows o '/') antes de partir por segmentos.
    $qualifier = [IO.Path]::GetPathRoot($normalized)
    if ($qualifier -and $normalized.StartsWith($qualifier, [StringComparison]::OrdinalIgnoreCase)) {
        $normalized = $normalized.Substring($qualifier.Length)
    }

    if ([string]::IsNullOrEmpty($normalized)) {
        return @()
    }

    $raw = $normalized.Split([IO.Path]::DirectorySeparatorChar, [StringSplitOptions]::RemoveEmptyEntries)
    $segments = @()
    foreach ($segment in $raw) {
        if ($segment -in @('.', '..')) { continue }
        $segments += $segment
    }
    return $segments
}

function Test-PathExcluded {
    param(
        [string]$FullPath,
        [string]$RootPath,
        [string[]]$ExcludedSegmentNames
    )

    if ([string]::IsNullOrEmpty($FullPath)) {
        return $false
    }

    $segments = Split-PathSegments -Path $FullPath
    foreach ($segment in $segments) {
        foreach ($excluded in $ExcludedSegmentNames) {
            if ([string]::Equals($segment, $excluded, [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
    }

    return $false
}

function Get-RelativeDisplayPath {
    param(
        [string]$FullPath,
        [string]$RootPath
    )

    if ([string]::IsNullOrEmpty($FullPath)) {
        return ''
    }

    $normalizedFull = $FullPath.Replace([IO.Path]::AltDirectorySeparatorChar, [IO.Path]::DirectorySeparatorChar)
    $normalizedRoot = $RootPath.Replace([IO.Path]::AltDirectorySeparatorChar, [IO.Path]::DirectorySeparatorChar)
    $trimmedRoot = $normalizedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)

    if ($normalizedFull.StartsWith($trimmedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $relative = $normalizedFull.Substring($trimmedRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        if ($relative.Length -gt 0) {
            return $relative
        }
        return $normalizedFull
    }

    return $normalizedFull
}

function Get-AtlasScanFiles {
    param(
        [string]$StartPath,
        [string]$RootPath,
        [string[]]$ExcludedSegmentNames
    )

    $stack = New-Object 'System.Collections.Generic.Stack[string]'
    $stack.Push($StartPath)
    $collected = New-Object 'System.Collections.Generic.List[object]'

    while ($stack.Count -gt 0) {
        $current = $stack.Pop()
        Get-ChildItem -LiteralPath $current -Force -ErrorAction SilentlyContinue | ForEach-Object {
            $path = $_.FullName
            if ($_.PSIsContainer) {
                if (-not (Test-PathExcluded -FullPath $path -RootPath $RootPath -ExcludedSegmentNames $ExcludedSegmentNames)) {
                    $stack.Push($path)
                }
                return
            }

            $collected.Add($_)
        }
    }

    return ,$collected
}

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$maxSeconds = 60
$filesScanned = 0
$filesExcluded = 0

try {
    $recursiveScanRoots = @(
        (Join-Path $rootPath ".github"),
        (Join-Path $rootPath "Atlas Balance/backend/src"),
        (Join-Path $rootPath "Atlas Balance/backend/tests"),
        (Join-Path $rootPath "Atlas Balance/frontend/src"),
        (Join-Path $rootPath "Atlas Balance/frontend/public"),
        (Join-Path $rootPath "Atlas Balance/scripts"),
        (Join-Path $rootPath "Documentacion")
    ) | Where-Object { Test-Path -LiteralPath $_ }

    $files = New-Object 'System.Collections.Generic.List[object]'

    foreach ($fileRoot in @($rootPath, (Join-Path $rootPath "Atlas Balance"), (Join-Path $rootPath "Atlas Balance/backend"), (Join-Path $rootPath "Atlas Balance/frontend"))) {
        if (Test-Path -LiteralPath $fileRoot) {
            foreach ($item in (Get-ChildItem -LiteralPath $fileRoot -File -Force -ErrorAction SilentlyContinue)) {
                $files.Add($item)
            }
        }
    }

    foreach ($scanRoot in $recursiveScanRoots) {
        $collected = Get-AtlasScanFiles -StartPath $scanRoot -RootPath $rootPath -ExcludedSegmentNames $excludedSegmentNames
        foreach ($item in $collected) {
            $files.Add($item)
        }
    }

    $seen = @{}
    $files |
        Where-Object { $null -ne $_ } |
        ForEach-Object {
            $path = $_.FullName

            if ($stopwatch.Elapsed.TotalSeconds -gt $maxSeconds) {
                Write-Warning "Scanner abortado por timeout (>$maxSeconds s) durante el barrido de archivos."
                return
            }

            if ($seen.ContainsKey($path)) { return }
            $seen[$path] = $true

            if ($path.Equals($PSCommandPath, [StringComparison]::OrdinalIgnoreCase)) {
                return
            }

            if (Test-PathExcluded -FullPath $path -RootPath $rootPath -ExcludedSegmentNames $excludedSegmentNames) {
                $script:filesExcluded++
                return
            }

            $name = $_.Name
            if ($allowedTemplateSuffixes | Where-Object { $name.EndsWith($_, [StringComparison]::OrdinalIgnoreCase) }) {
                return
            }

            $relative = Get-RelativeDisplayPath -FullPath $path -RootPath $rootPath
            $content = Get-Content -LiteralPath $path -Raw -ErrorAction SilentlyContinue
            if ($null -eq $content) {
                return
            }

            $script:filesScanned++

            foreach ($pattern in $patterns) {
                if ([regex]::IsMatch($content, $pattern.Regex, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
                    $hits.Add("$relative => $($pattern.Name)")
                }
            }
        }
}
finally {
    $stopwatch.Stop()
}

Write-Host ("Scanner: {0} archivos analizados, {1} excluidos por segmento ({2:N2}s)." -f $filesScanned, $filesExcluded, $stopwatch.Elapsed.TotalSeconds)

if ($hits.Count -gt 0) {
    Write-Error ("Posibles secretos encontrados:`n" + ($hits -join "`n"))
    exit 1
}

Write-Host "Scanner Atlas sin hallazgos."