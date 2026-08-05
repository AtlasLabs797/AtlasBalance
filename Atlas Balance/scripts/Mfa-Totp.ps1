# Mfa-Totp.ps1
# Helpers TOTP (RFC 6238) y codificacion base32 extraidos para ser
# reutilizables desde Smoke-Test-AtlasBalance.ps1 y testables de forma
# aislada. Algoritmo identico al TotpService.cs del backend (HMAC-SHA1,
# 6 digitos, periodo 30s, ventana +/- 1).

Set-StrictMode -Version Latest

$script:MfaTotpBase32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"

function ConvertFrom-Base32Secret {
    param([Parameter(Mandatory = $true)][string]$Secret)

    $normalized = $Secret.Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant()
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        throw "Secreto TOTP vacio."
    }

    $bytes = [System.Collections.Generic.List[byte]]::new()
    $buffer = 0
    $bitsLeft = 0
    foreach ($ch in $normalized.ToCharArray()) {
        $value = $script:MfaTotpBase32Alphabet.IndexOf([string]$ch, [StringComparison]::Ordinal)
        if ($value -lt 0) {
            throw [System.FormatException]::new("Secreto TOTP contiene un caracter no base32: '$ch'.")
        }
        $buffer = ($buffer -shl 5) -bor $value
        $bitsLeft += 5
        if ($bitsLeft -ge 8) {
            [void]$bytes.Add([byte](($buffer -shr ($bitsLeft - 8)) -band 0xFF))
            $bitsLeft -= 8
        }
    }
    return ,$bytes.ToArray()
}

function Get-MfaTotpCode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Secret,
        [DateTime]$UtcNow = [DateTime]::UtcNow,
        [int]$Window = 0
    )

    $secretBytes = ConvertFrom-Base32Secret -Secret $Secret
    $now = [DateTimeOffset]::new($UtcNow.ToUniversalTime()).ToUnixTimeSeconds()
    $step = [long]([Math]::Floor($now / 30)) + $Window
    if ($step -lt 0) {
        throw [System.ArgumentOutOfRangeException]::new("Ventana TOTP anterior a la epoca.")
    }
    $counter = [System.Net.IPAddress]::HostToNetworkOrder($step)
    $counterBytes = [BitConverter]::GetBytes($counter)

    $hmac = [System.Security.Cryptography.HMACSHA1]::new($secretBytes)
    try {
        $hash = $hmac.ComputeHash($counterBytes)
    }
    finally {
        $hmac.Dispose()
    }

    $offset = $hash[$hash.Length - 1] -band 0x0F
    $binary =
        (($hash[$offset] -band 0x7F) -shl 24) -bor
        (($hash[$offset + 1] -band 0xFF) -shl 16) -bor
        (($hash[$offset + 2] -band 0xFF) -shl 8) -bor
        ($hash[$offset + 3] -band 0xFF)
    $otp = $binary % 1000000
    return $otp.ToString("D6")
}
