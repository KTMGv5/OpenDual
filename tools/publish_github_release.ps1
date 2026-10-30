param(
    [string]$TagName = "v1.6.3",
    [string]$ReleaseName = "OpenDual v1.6.3 - Quadraphonic Haptic Audio Streaming and Battery & Charging Alerts",
    [string]$DistDir = "dist"
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "  Publishing GitHub Release: $TagName" -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan

# 1. Fetch GitHub Token from Git Credential Helper
$p = New-Object System.Diagnostics.Process
$p.StartInfo.FileName = "git"
$p.StartInfo.Arguments = "credential fill"
$p.StartInfo.UseShellExecute = $false
$p.StartInfo.RedirectStandardInput = $true
$p.StartInfo.RedirectStandardOutput = $true
$p.StartInfo.CreateNoWindow = $true
$p.Start() | Out-Null
$p.StandardInput.WriteLine("protocol=https")
$p.StandardInput.WriteLine("host=github.com")
$p.StandardInput.WriteLine("")
$out = $p.StandardOutput.ReadToEnd()
$p.WaitForExit()

$token = ""
foreach ($line in ($out -split "`r?`n")) {
    if ($line.StartsWith("password=")) {
        $token = $line.Substring(9)
    }
}

if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Error "Could not retrieve GitHub credentials from git credential helper."
    exit 1
}

$repo = "KTMGv5/OpenDual"
$headers = @{
    "Authorization" = "token $token"
    "User-Agent" = "OpenDualReleaseScript"
    "Accept" = "application/vnd.github.v3+json"
}

# 2. Release Notes
$body = @"
## Highlights in OpenDual v1.6.3

OpenDual v1.6.3 introduces native DualSense Quadraphonic Haptic Audio Streaming (Channels 3 & 4) for high-fidelity voice-coil haptics while preserving crystal-clear headphone audio, alongside real-time Battery Health & Charging Desktop Notifications and live visual dashboard badges.

---

### What's New and Improved in v1.6.3

* **Native DualSense Quadraphonic Haptic Audio Streaming**:
  * Solved the DualSense hardware limitation where active controller audio mutes standard emulated HID rumble.
  * DriverCore automatically attaches a WASAPI quadraphonic stream targeting Channels 3 & 4 (DualSense Voice-Coil Actuators), streaming 48kHz audio haptics without interfering with stereo headphone playback on Channels 1 & 2.
  * System audio loopback (from speakers, soundbars, or headphones) continues delivering tactile rumble seamlessly across all audio output devices.

* **Real-Time Battery Health & Charging Desktop Notifications**:
  * Live Charging Badges on the Dashboard and Quick HUD displaying live percentage and full charge status.
  * Added desktop notification alerts for Charger Connected, Charger Disconnected, and Battery Full (100%).
  * Staged low battery warnings (<15% Low, <5% Critical) to notify you before your controller powers down during gaming sessions.
  * Configurable toggle under Settings: 'Battery Health & Charging Desktop Notifications'.

* **Codebase Verification & Security**:
  * Authenticode code signing verified across all distributed executables.
  * Clean MSVC C++20 and .NET 10 compilation with 0 warnings and 0 errors.

---

### Included Signed Binaries
* OpenDualSetup.exe - Standalone installer and uninstaller suite
* OpenDualHub.exe - Desktop controller hub and configuration center
* OpenDualDriver.exe - 1000Hz C++20 driver service engine
* OpenDual-v1.6.3.zip - Portable release bundle
"@

# 3. Check if release already exists
$releaseUrl = "https://api.github.com/repos/$repo/releases/tags/$TagName"
$existingRelease = $null
try {
    $existingRelease = Invoke-RestMethod -Uri $releaseUrl -Headers $headers -Method Get
    Write-Host "[OK] Existing release found for $TagName (ID: $($existingRelease.id))" -ForegroundColor Yellow
} catch {
    # Not found, create it
}

$releaseId = $null
$uploadUrlTemplate = ""

if ($existingRelease) {
    $releaseId = $existingRelease.id
    $uploadUrlTemplate = $existingRelease.upload_url
    Write-Host "Updating release metadata for $TagName..." -ForegroundColor Cyan
    $updatePayload = @{
        name = $ReleaseName
        body = $body
        draft = $false
        prerelease = $false
    } | ConvertTo-Json
    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($updatePayload)
    $updated = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/$releaseId" -Headers $headers -Method Patch -Body $bodyBytes -ContentType "application/json"
} else {
    Write-Host "Creating new GitHub Release $TagName..." -ForegroundColor Cyan
    $createPayload = @{
        tag_name = $TagName
        target_commitish = "main"
        name = $ReleaseName
        body = $body
        draft = $false
        prerelease = $false
    } | ConvertTo-Json
    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($createPayload)
    $newRelease = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases" -Headers $headers -Method Post -Body $bodyBytes -ContentType "application/json"
    $releaseId = $newRelease.id
    $uploadUrlTemplate = $newRelease.upload_url
    Write-Host "[OK] Created release with ID: $releaseId" -ForegroundColor Green
}

# 4. Upload Assets to Release
# Clean upload URL template: e.g. "https://uploads.github.com/.../assets{?name,label}"
$uploadBase = $uploadUrlTemplate -replace '\{\?name,label\}', ''

$assetsToUpload = @(
    "OpenDual-$TagName.zip",
    "OpenDualSetup.exe",
    "OpenDualHub.exe",
    "OpenDualDriver.exe"
)

# Fetch existing assets to avoid 422 duplicate errors
$assetsList = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/$releaseId/assets" -Headers $headers -Method Get

foreach ($assetName in $assetsToUpload) {
    $filePath = Join-Path $DistDir $assetName
    if (-not (Test-Path $filePath)) {
        Write-Warning "Asset not found: $filePath (skipping)"
        continue
    }

    # Delete existing asset if it exists
    $existingAsset = $assetsList | Where-Object { $_.name -eq $assetName }
    if ($existingAsset) {
        Write-Host "Deleting previous asset '$assetName' (ID: $($existingAsset.id))..." -ForegroundColor Yellow
        Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/assets/$($existingAsset.id)" -Headers $headers -Method Delete
    }

    Write-Host "Uploading '$assetName' ($((Get-Item $filePath).Length) bytes)..." -ForegroundColor Cyan
    $fileBytes = [System.IO.File]::ReadAllBytes($filePath)
    $uploadUri = "$uploadBase`?name=$assetName"

    $uploadHeaders = @{
        "Authorization" = "token $token"
        "User-Agent" = "OpenDualReleaseScript"
        "Content-Type" = "application/octet-stream"
    }

    $uploadResponse = Invoke-RestMethod -Uri $uploadUri -Headers $uploadHeaders -Method Post -Body $fileBytes
    Write-Host "  -> [OK] Uploaded $assetName" -ForegroundColor Green
}

Write-Host "=========================================================" -ForegroundColor Green
Write-Host "  Release published successfully!" -ForegroundColor Green
Write-Host "  URL: https://github.com/$repo/releases/tag/$TagName" -ForegroundColor Green
Write-Host "=========================================================" -ForegroundColor Green
