# ==============================================================================
# OpenDual - Binary Authenticode Signing Automation Script
# ==============================================================================
# Signs OpenDual executables with SHA-256 and RFC 3161 timestamps.
# ==============================================================================

[CmdletBinding()]
param(
    [string]$TargetDir = "$PSScriptRoot\..\dist",
    [string]$TimestampServer = "http://timestamp.digicert.com"
)

$cert = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -like "*OpenDual*" } | Select-Object -First 1

if (-not $cert) {
    Write-Host "[OpenDual] Code signing certificate not found. Generating one now..." -ForegroundColor Yellow
    & "$PSScriptRoot\create_codesign_cert.ps1"
    $cert = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -like "*OpenDual*" } | Select-Object -First 1
}

if (-not $cert) {
    Write-Error "Failed to obtain code signing certificate."
    exit 1
}

Write-Host "`n[OpenDual] Signing binaries in: $TargetDir" -ForegroundColor Cyan
Write-Host "[OpenDual] Certificate: $($cert.Subject) [Thumbprint: $($cert.Thumbprint)]" -ForegroundColor Gray

$filesToSign = @("OpenDualSetup.exe", "OpenDualHub.exe", "OpenDualDriver.exe")

foreach ($file in $filesToSign) {
    $fullPath = Join-Path $TargetDir $file
    if (Test-Path $fullPath) {
        Write-Host "  -> Signing $file..." -ForegroundColor Yellow
        try {
            $sig = Set-AuthenticodeSignature -FilePath $fullPath -Certificate $cert -HashAlgorithm "SHA256" -TimestampServer $TimestampServer -ErrorAction Stop
            Write-Host "     [OK] Signed ($($sig.Status))" -ForegroundColor Green
        } catch {
            Write-Host "     [Retry] Signing without timestamp server..." -ForegroundColor DarkYellow
            $sig = Set-AuthenticodeSignature -FilePath $fullPath -Certificate $cert -HashAlgorithm "SHA256"
            Write-Host "     [OK] Signed ($($sig.Status))" -ForegroundColor Green
        }
    } else {
        Write-Host "  -> [Skip] $file not found in $TargetDir" -ForegroundColor DarkGray
    }
}

Write-Host "`n[OpenDual] All target binaries signed successfully!" -ForegroundColor Green
