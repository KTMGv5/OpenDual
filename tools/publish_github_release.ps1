param(
    [string]$TagName = "v1.7.0",
    [string]$ReleaseName = "OpenDual v1.7.0 - Pro Response Suite, 3D Hardware Lab, Modern Installer & Initial Setup Wizard",
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
## Highlights in OpenDual v1.7.0

OpenDual v1.7.0 is a major desktop & kernel release featuring a **Modern Dark-Glass Installer Suite**, a **Guided First-Run Initial Setup Wizard**, the **Pro Response & Sensor Tuning Suite** (10 kernel-level DualSense hardware modifiers), an **Interactive 2D Controller Schematic**, and an overhauled **3D Spatial Gimbal & Hardware Diagnostic Lab**.

---

### What's New in v1.7.0

* **Modern Dark-Glass Installer & Uninstaller (`OpenDualSetup.exe`)**:
  * Redesigned WPF installer wizard with live progress tracking, component toggles (1000Hz Direct HID Startup Service, ViGEmBus verification, Desktop/Start Menu shortcuts), automatic update/repair detection, and built-in clean uninstaller.
* **Guided Initial Setup Wizard (`InitialSetupWindow`)**:
  * 3-step onboarding assistant on first launch (and accessible anytime from `Settings -> Initial Setup Wizard`) to configure UI rendering performance (`Dynamic 60 FPS Wave Art` vs `Lightweight Low-Spec Mode`), starting playstyle preset (`Standard Balanced`, `Esports Pro Response`, `FPS Gyro & Recoil Assist`, `Sim Racing`), Audio-to-Haptics, and header clock/telemetry display.
* **Pro Response & Sensor Tuning Suite (1000Hz C++20 Kernel Engine)**:
  * **Dual-Stage Trigger**: Half-pull R2 aims down sights (L2); full pull past the 85% tactile wall fires R2.
  * **Rapid Trigger Instant Reset (`<1.2mm`)**: Dynamically tracks peak trigger depth and resets the moment you release upward by 1.2mm.
  * **Outer-Ring Auto-Sprint (`L3 at 96%`)**: Automatically engages L3 sprint at outer stick deflection without clicking the physical switch.
  * **Touchpad Gyro Pause**: Touching the capacitive touchpad pauses gyro aiming so you can recenter your wrists like lifting a mouse.
  * **Touchpad Haptic Scroll Detents**: Pulses the palm voice coils for 1ms as your finger crosses virtual touchpad scroll notches.
  * **G-Shock Impact Stabilizer**: Detects `>1.85G` physical controller jolts and damps stick/gyro spikes for 25ms.
  * **Player LED Live Telemetry Meter**: Uses the 5 under-touchpad Player LEDs as a real-time trigger pressure or audio peak meter.
  * **Microphone Hardware Preamp Boost**: Optimizes the internal DualSense microphone array gain and noise gate.
  * **Adaptive Vertical Recoil Assist**: Applies subtle downward pitch stabilization while firing R2 with Gyro Aim active.
  * **Trigger Motor Thermal Protection**: Tracks L2/R2 actuator coil duty cycle and prevents overheating during sustained resistance.
* **3D Spatial Gimbal & Interactive Controller Schematic**:
  * Authentic two-tone 3D DualSense model in the **Hardware Lab** with accurate BMI260 6-axis IMU orientation (`Pitch`, `Roll`, `Yaw`, `G-Force`) and one-click **Recenter View**.
  * Live 2D vector schematic in **Live Input Visualizer** highlighting every pressed button, bumper, trigger, analog stick position, and multi-touch finger coordinate in real time.
* **Zero-Allocation UI Responsiveness**:
  * Cached XMB visual trees, active-only inspector telemetry rendering, and single-timer smooth scrolling for zero UI lag even on low-end hardware.

---

### Included Signed Assets
* `OpenDualSetup.exe` - Complete standalone GUI installer, updater, and uninstaller suite (Recommended)
* `OpenDualHub.exe` - Standalone Cross-Media Bar Controller Hub executable
* `OpenDualDriver.exe` - Standalone 1000Hz C++20 Direct HID Driver engine
* `OpenDual-v1.7.0.zip` - Portable bundle including all signed binaries and assets
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
