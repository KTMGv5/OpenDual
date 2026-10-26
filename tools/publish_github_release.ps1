param(
    [string]$TagName = "v1.4.1",
    [string]$ReleaseName = "OpenDual v1.4.1 - Dynamic Audio Haptics & USB Audio Rumble Support",
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
## Highlights in OpenDual v1.4.1

This release resolves the audio-reactive haptics muting issue when controllers are selected as the default Windows audio output, adds dynamic real-time WASAPI endpoint migration, and includes full 6-axis gyro aiming, auto-profile switching, presets, and power saving.

### What's Fixed & Improved
* **Dynamic Audio Endpoint Migration (`IMMNotificationClient`)**:
  * WASAPI loopback capture now automatically tracks Windows default playback endpoint switches in real time without dropping haptics.
  * Switching between Soundbars, TV speakers, wireless headphones, and the DualSense / DS4 headphone jack seamlessly updates the haptic stream within ~50ms.
* **DualSense USB Audio Concurrent Rumble (`out[39] = 0x04`)**:
  * DualSense firmware v2 compatible vibration flags (`DS_OUTPUT_VALID_FLAG2_COMPATIBLE_VIBRATION2`) enable crisp, high-power tactile rumble concurrent with high-res USB Audio headphone playback.
* **Multi-Channel & Native PS5 Haptic Blending**:
  * Correctly processes 4-channel Quadraphonic streams (stereo headphone audio + native PS5 haptic voice-coil tracks) and 5.1/7.1 home theater LFE subwoofer channel isolation.
* **Live Audio Endpoint UI Display**:
  * Added live active render and capture device detection to the Audio tab in OpenDual Hub.
* **6-Axis Gyro Aiming Engine (from v1.4.0)**:
  * 1000Hz DualSense/DS4 gyro motion aiming with sub-pixel mouse injection, resting drift calibration, and ADS L2 / Touchpad activation.
* **Game Auto-Profile Switching & Preset Library**:
  * Background detector (<0.001% CPU) automatically switches profiles when games take focus.
  * Tuned presets included: Competitive FPS, Sim Racing, Bow RPG, Flight Sim, and Balanced.
* **Automatic Suite Updater**:
  * 1-click silent background update downloading and in-place relaunch.

### Included Signed Binaries
* `OpenDualSetup.exe` — Standalone installer and uninstaller suite
* `OpenDualHub.exe` — Desktop controller hub and configuration center
* `OpenDualDriver.exe` — 1000Hz C++20 driver service engine
* `OpenDual-v1.4.1.zip` — Portable release bundle

All binaries are digitally signed with Authenticode SHA-256 signatures.
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
    "OpenDual-v1.4.1.zip",
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
