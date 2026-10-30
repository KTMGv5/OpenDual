param(
    [string]$TagName = "v1.6.4",
    [string]$ReleaseName = "OpenDual v1.6.4 - 6-Axis Gyro Aiming Overhaul, DualSense Speaker Output, and Mic Mute Toast",
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
## Highlights in OpenDual v1.6.4

OpenDual v1.6.4 introduces a complete 6-Axis Gyro Aiming Overhaul, native DualSense physical speaker audio output with hardware control, on-screen Microphone Mute / Unmute OSD toast notifications, and refined audio-reactive haptics defaults.

---

### What's New and Improved in v1.6.4

* **Complete 6-Axis Gyro Aiming Overhaul**:
  * Precision Gyroscope motion aiming supporting both Mouse Emulation and Right Stick Emulation.
  * Coordinate Space selection: Local (Standard Controller), Player Space (natural tilt aiming), and World Space.
  * Fixed inversion and axis mapping across all display resolutions.
  * Activation Modes: Always On, Hold to Aim (L2 ADS / Touchpad / R1 / L1), Inverted, and Toggle.
  * Real-time calibration and hardware anti-drift rejection filter.

* **DualSense Hardware Internal Speaker Route**:
  * Route game and voice audio directly to the DualSense physical internal speaker with hardware volume control.
  * Automatically sets DualSense HID flags (valid_flag0 0x20/0x80, audio flags 0x02) to unmute the internal speaker hardware.
  * Seamless one-click audio device switching between PC speakers and controller speaker.

* **On-Screen Mic Muted / Unmuted Toast Overlay**:
  * OpenDual signature dark glass floating OSD pill with drop shadow and smooth fade animations.
  * Instant visual feedback when pressing the DualSense hardware mic mute button (Amber glowing border for Muted, Neon Green for Unmuted).
  * Runs as a non-activating overlay that never steals window or game focus.

* **Refined Audio-Reactive Haptics & Recoil Defaults**:
  * Audio haptics disabled by default to prevent unwanted rumble.
  * Default intensity set to sweet-spot 30%, 40 Hz sub-bass cutoff filter, and 20% recoil transient threshold.
  * Default profile tuned for PlayStation 5 balanced experience with custom rainbow RGB.

* **Streamlined Distribution**:
  * Exactly two signed release assets: OpenDualSetup.exe installer and OpenDual-v1.6.4.zip portable archive.
  * MSVC C++20 and .NET 10 compilation with Authenticode digital signatures.

---

### Included Signed Assets
* OpenDualSetup.exe - Complete standalone installer and uninstaller suite (Recommended)
* OpenDual-v1.6.4.zip - Portable bundle including all binaries and tools
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
