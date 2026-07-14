param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

$ErrorActionPreference = "Stop"

$rootPath = (Resolve-Path -LiteralPath $Root).Path
$excludedSegments = @(
    "\.git\",
    "\node_modules\",
    "\bin\",
    "\obj\",
    "\Atlas Balance Release\",
    "\tools\pgdata\"
)

$patterns = @(
    @{ Name = "Atlas/OpenAI/OpenRouter token"; Regex = '(sk_atlas_balance_[A-Za-z0-9_-]{32,}|sk-or-v1-[A-Za-z0-9_-]{32,}|sk-proj-[A-Za-z0-9_-]{32,}|sk-[A-Za-z0-9_-]{48,})' },
    @{ Name = "JWT or shared secret assignment"; Regex = '(JwtSettings__Secret|WatchdogSettings__SharedSecret|RlsContext__Secret|ATLAS_[A-Z0-9_]*SECRET)[^`r`n]{0,80}[:=][^`r`n]{12,}' },
    @{ Name = "Private key"; Regex = '-----BEGIN (RSA |EC |OPENSSH |)PRIVATE KEY-----' },
    @{ Name = "Connection string with password"; Regex = '(Host|Server)=.+;(Password|Pwd)=(?!\$|X{2,}\b|test\b|x\b|<)[^;`r`n]{12,}' }
)

$allowedTemplateSuffixes = @(".template", ".example", ".sample")
$hits = New-Object System.Collections.Generic.List[string]

function Get-AtlasScanFiles {
    param([string]$StartPath)

    $stack = New-Object 'System.Collections.Generic.Stack[string]'
    $stack.Push($StartPath)

    while ($stack.Count -gt 0) {
        $current = $stack.Pop()
        Get-ChildItem -LiteralPath $current -Force -ErrorAction SilentlyContinue | ForEach-Object {
            $path = $_.FullName
            if ($_.PSIsContainer) {
                $normalized = "\$($path.Substring($rootPath.Length).TrimStart('\', '/'))\"
                if (-not ($excludedSegments | Where-Object { $normalized.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 })) {
                    $stack.Push($path)
                }
                return
            }

            $_
        }
    }
}

$recursiveScanRoots = @(
    (Join-Path $rootPath ".github"),
    (Join-Path $rootPath "Atlas Balance\backend\src"),
    (Join-Path $rootPath "Atlas Balance\backend\tests"),
    (Join-Path $rootPath "Atlas Balance\frontend\src"),
    (Join-Path $rootPath "Atlas Balance\frontend\public"),
    (Join-Path $rootPath "Atlas Balance\scripts"),
    (Join-Path $rootPath "Documentacion")
) | Where-Object { Test-Path -LiteralPath $_ }

$files = @()
foreach ($fileRoot in @($rootPath, (Join-Path $rootPath "Atlas Balance"), (Join-Path $rootPath "Atlas Balance\backend"), (Join-Path $rootPath "Atlas Balance\frontend"))) {
    if (Test-Path -LiteralPath $fileRoot) {
        $files += Get-ChildItem -LiteralPath $fileRoot -File -Force -ErrorAction SilentlyContinue
    }
}

foreach ($scanRoot in $recursiveScanRoots) {
    $files += Get-AtlasScanFiles -StartPath $scanRoot
}

$files |
    Sort-Object FullName -Unique |
    Where-Object {
        $path = $_.FullName
        if ($path.Equals($PSCommandPath, [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
        -not ($excludedSegments | Where-Object { $path.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
    } |
    Where-Object {
        $name = $_.Name
        -not ($allowedTemplateSuffixes | Where-Object { $name.EndsWith($_, [StringComparison]::OrdinalIgnoreCase) })
    } |
    ForEach-Object {
        $relative = $_.FullName
        if ($relative.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) {
            $relative = $relative.Substring($rootPath.Length).TrimStart("\", "/")
        }
        $content = Get-Content -LiteralPath $_.FullName -Raw -ErrorAction SilentlyContinue
        if ($null -eq $content) {
            return
        }

        foreach ($pattern in $patterns) {
            if ([regex]::IsMatch($content, $pattern.Regex, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
                $hits.Add("$relative => $($pattern.Name)")
            }
        }
    }

if ($hits.Count -gt 0) {
    Write-Error ("Posibles secretos encontrados:`n" + ($hits -join "`n"))
    exit 1
}

Write-Host "Scanner Atlas sin hallazgos."
