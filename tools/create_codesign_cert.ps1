# ==============================================================================
# OpenDual - Code Signing Certificate Generator
# ==============================================================================
# Generates a SHA-256 Code Signing Certificate in Cert:\CurrentUser\My
# ==============================================================================

[CmdletBinding()]
param(
    [string]$SubjectName = "CN=OpenDual Suite, O=OpenDual Development, C=US",
    [string]$FriendlyName = "OpenDual Code Signing Certificate",
    [switch]$InstallToRoot
)

Write-Host "`n[OpenDual] Checking for existing code signing certificate..." -ForegroundColor Cyan

$existingCert = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -like "*OpenDual*" } | Select-Object -First 1

if ($existingCert) {
    Write-Host "[OpenDual] Found existing certificate: $($existingCert.Thumbprint) ($($existingCert.Subject))" -ForegroundColor Green
    $cert = $existingCert
} else {
    Write-Host "[OpenDual] Creating new 10-year SHA-256 Code Signing Certificate..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $SubjectName `
        -KeyUsage DigitalSignature `
        -FriendlyName $FriendlyName `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(10) `
        -HashAlgorithm "SHA256"

    Write-Host "[OpenDual] Generated Certificate: $($cert.Thumbprint)" -ForegroundColor Green
}

# Export public .cer file into tools\
$cerPath = Join-Path $PSScriptRoot "OpenDual_CodeSigning.cer"
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
Write-Host "[OpenDual] Exported public certificate to: $cerPath" -ForegroundColor Gray

if ($InstallToRoot) {
    certutil -addstore -f -user root $cerPath | Out-Null
    Write-Host "[OpenDual] Installed into Trusted Root Certification Authorities." -ForegroundColor Green
}

Write-Host "[OpenDual] Code Signing Certificate ready for Authenticode signing!" -ForegroundColor Green
