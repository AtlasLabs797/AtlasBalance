param([string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$helpers = Join-Path $PSScriptRoot "Mfa-Totp.ps1"
if (-not (Test-Path -LiteralPath $helpers)) {
    throw "No se encontro $helpers."
}
. $helpers

# Vector de prueba de RFC 6238, apendice B: secreto ASCII
# "12345678901234567890" => base32 "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ".
# T=59s => HMAC-SHA1 => 8 digitos "94287082". El backend usa 6 digitos
# (TotpService.cs Digits=6), asi que truncamos a "287082".
$secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"
$epoch = [DateTimeOffset]::FromUnixTimeSeconds(59).UtcDateTime
$code = Get-MfaTotpCode -Secret $secret -UtcNow $epoch
if ($code -ne "287082") {
    throw "TOTP RFC 6238 vectorial fallo: esperado 287082, obtenido $code."
}

# Vector 2: T=1111111109s => 8 digitos "07081804" => 6 digitos "081804".
$epoch2 = [DateTimeOffset]::FromUnixTimeSeconds(1111111109).UtcDateTime
$code2 = Get-MfaTotpCode -Secret $secret -UtcNow $epoch2
if ($code2 -ne "081804") {
    throw "TOTP RFC 6238 vectorial 2 fallo: esperado 081804, obtenido $code2."
}

# Base32: caracteres no validos deben lanzar FormatException.
$badSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ!"
try {
    $null = ConvertFrom-Base32Secret -Secret $badSecret
    throw "ConvertFrom-Base32Secret acepto un caracter no base32."
}
catch [System.FormatException] {
    # Esperado.
}

# Padding '=' debe tolerarse.
$padded = ConvertFrom-Base32Secret -Secret ("$secret=")
if ($padded.Length -ne 20) {
    throw "ConvertFrom-Base32Secret no tolero el padding '='."
}

# Ventana: el codigo con Window=+1 sobre un step N es distinto al de N cuando
# cruza el limite de 30s. Verificamos que la funcion respeta la ventana.
$epoch3 = [DateTimeOffset]::FromUnixTimeSeconds(30).UtcDateTime
$code3 = Get-MfaTotpCode -Secret $secret -UtcNow $epoch3 -Window 0
$code3PlusOne = Get-MfaTotpCode -Secret $secret -UtcNow $epoch3 -Window 1
if ($code3 -eq $code3PlusOne) {
    throw "TOTP no respeta la ventana: codigos identicos para step 1 y step 2."
}

Write-Host "TOTP RFC 6238 OK."
