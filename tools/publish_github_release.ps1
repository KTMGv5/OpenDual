param(
    [string]$TagName = "v1.6.6",
    [string]$ReleaseName = "OpenDual v1.6.6 - Interactive Gyro Calibrator & Touchpad Volume Slider HUD",
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
## Highlights in OpenDual v1.6.6

OpenDual v1.6.6 introduces two major new features: the **Interactive Gyro Aiming Calibrator** (a physical aim-and-shoot calibration wizard using the Cross button) and the **Touchpad Edge Volume Gesture with Floating Neon HUD** for seamless on-controller master audio control, along with full Windows volume synchronization and headphone hotplug stabilization.

---

### What's New and Improved in v1.6.6

* **Feature 1: Interactive Gyro Aiming Calibrator ("Aim & Press Cross Target Wizard")**:
  * An interactive target aiming arena window (`GyroCalibratorWindow`) designed to calibrate your gyroscope for any monitor or TV resolution (1080p, 1440p, 4K).
  * Prompts the player to physically point the controller crosshair at 5 on-screen bullseye targets (Center, Far Right, Far Left, High, Low) and press Cross (X) to lock each point.
  * Measures baseline zero-rate resting drift, axis alignment, and physical angular range of motion.
  * Automatically calculates and applies optimal horizontal sensitivity, vertical sensitivity, and anti-jitter deadzone directly to your active controller profile.

* **Feature 2: Touchpad Edge Volume Slider & Floating Neon Master Volume HUD**:
  * Slide your finger vertically along the right edge of the DualSense touchpad to smoothly adjust Windows Master Volume up or down on the fly without pausing your game.
  * A custom floating dark-glass volume HUD pill (`VolumeHudWindow`) with an animated neon cyan progress bar and speaker glyph appears on screen whenever volume changes.
  * Focus-independent overlay (`WS_EX_NOACTIVATE`) that never steals window or game focus.

* **Windows Master Volume Hardware DAC Synchronization**:
  * DualSense hardware audio volume is synchronized directly with Windows master volume via CoreAudio WASAPI `IAudioEndpointVolume`.
  * Adjusting volume via the Windows taskbar slider, multimedia keyboard keys, system mixer, or touchpad gesture scales the hardware DAC for both internal speaker and 3.5mm headphones.

* **Headphone Hotplug Detection & Gain Stabilization**:
  * Live 3.5mm headphone detection using DualSense status report byte 54 (`DS_STATUS1_HP_DETECT`).
  * Mutes the analog headphone stage when controller speaker mode is active, preventing current draw on the shared power rails and eliminating the volume jump when unplugging headphones.

* **Pristine Stereo Headphone Audio**:
  * Bit-accurate stereo headphone routing mode (0x00 `L_R_X`) when controller speaker output is turned off.

---

### Included Signed Assets
* OpenDualSetup.exe - Complete standalone installer and uninstaller suite (Recommended)
* OpenDualHub.exe - Standalone GUI Controller Hub executable
* OpenDualDriver.exe - Standalone 1000Hz C++20 Direct HID Driver engine
* OpenDual-v1.6.6.zip - Portable bundle including all binaries and tools
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
    "OpenDualSetup.exe",
    "OpenDualHub.exe",
    "OpenDualDriver.exe",
    "OpenDual-$TagName.zip"
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
