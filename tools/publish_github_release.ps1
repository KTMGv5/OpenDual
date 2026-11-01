param(
    [string]$TagName = "v1.8.0",
    [string]$ReleaseName = "OpenDual v1.8.0 - 20-Feature Pro Suite: Native DS4 Mode, Stick Curves, Flick-Stick, Ambilight & Radial Wheel",
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
## Highlights in OpenDual v1.8.0

OpenDual v1.8.0 is a massive 20-feature expansion of the C++20 Direct HID kernel driver and Cross-Media Bar Desktop Hub, introducing **Dual-Mode Virtual Gamepad Emulation (`Xbox 360` + `Native DualShock 4`)**, **Interactive Stick Response Curves (`Linear`, `Precision Sniper`, `Aggressive Snap`, `Dynamic S-Curve`)**, **Flick-Stick 360° Camera Engine**, **DualSense Edge Back-Paddle Sniper Shift**, **Screen-Sync Ambilight Lightbar**, **4-Zone Touchpad Macro Pad**, **On-Screen Radial Wheel**, **Always-on-Top Mini-Widget Mode**, and **One-Click Profile Share Codes (`OD1-...`)**.

---

### All 20 New Features in v1.8.0

1. **Dual-Mode Virtual Gamepad Emulation (`Xbox 360` vs. `Native DualShock 4`)**: Switch between XInput (`Xbox 360`) and native PlayStation (`DualShock 4`) ViGEmBus output on the fly for native PlayStation button prompts and motion pass-through.
2. **HidHide Double-Input Cloak Integration & Scanner**: Built-in conflict scanner detects `HidHide`, `Steam Input`, and `DS4Windows` and launches HidHide configuration with one click.
3. **Interactive Stick Response Curve Studio**: Choose `Linear`, `Precision Sniper` (cubic fine-aim), `Aggressive Snap`, or `Dynamic S-Curve` independently for Left & Right thumbsticks with a live response curve graph.
4. **Anti-Deadzone (`0%–25%`) & Axial Deadzone Shaper**: Counteract inner game deadzones and switch between `Circular Radial` and `Axial Cross` movement snapping.
5. **Flick-Stick 360° Camera Engine**: Point the right stick in any direction for instant 360° horizontal orientation, then sweep smoothly to rotate.
6. **DualSense Edge Back-Paddle & Fn Sniper Shift Layer**: Holding DualSense Edge Fn / Back Paddles dynamically scales stick and gyro sensitivity by 50% for pixel-accurate sniping.
7. **Trigger Actuation Depth Calibration (`25%–100%`)**: Compress full 0–100% trigger output into a shorter physical pull distance.
8. **Hardware Turbo Rapid-Fire Macro (`15 Hz R2 Pulse`)**: Kernel-level 15 Hz auto-pulse while holding R2 past 50% for semi-automatic weapons.
9. **Multi-Controller Support (`P1–P4` Header Slot Selector)**: Switch active telemetry and configuration across up to 4 connected DualSense / DS4 controllers.
10. **Touchpad 4-Zone Haptic Macro Pad**: Split physical touchpad clicks into 4 independent corner macro buttons (`Top-Left`, `Top-Right`, `Bottom-Left`, `Bottom-Right`).
11. **On-Screen Radial Command Wheel (`RadialMenuWindow`)**: Summon an 8-slot circular command overlay for instant profile switching, gyro toggle, clips, and media controls.
12. **Discord (`Ctrl+Shift+M` / `Ctrl+Shift+D`) & Media Transport Shortcuts**: Bind touchpad gestures or corner clicks directly to Discord Mute/Deafen and media transport.
13. **Screen-Sync Ambient Lightbar (`🖥️ Screen Ambience`)**: Samples dominant screen colors in real time and mirrors them onto the DualSense RGB lightbar.
14. **Controller Hardware & Factory Revision Inspector**: Displays live `Firmware Version`, `Board Revision (BDM-010/020/030/040)`, and `Bluetooth MAC Address`.
15. **Battery Playtime Estimator (`~Xh Ym remaining`)**: Calculates remaining wireless playtime or time-to-full charge based on live battery telemetry.
16. **Instant Wireless Power-Off Button (`⏻ Power Off Pad`)**: Immediately powers down the controller over Bluetooth without waiting for the idle timer.
17. **Compact Mini-Widget Mode (`⧉ Mini`)**: Collapses the hub into a sleek always-on-top streamer pill showing battery, polling rate, latency, and profile.
18. **One-Click Profile Share Codes (`OD1-...`)**: Export and import complete hardware setups via compact Base64 share strings on the clipboard.
19. **Visual Button & Gesture Action Remapper**: Expanded action remapping across unistroke gestures, swipes, and 4-zone corner clicks.
20. **Automated Conflict & Environment Diagnostics**: Real-time verification of ViGEmBus, HidHide, Steam, and DS4Windows processes.

---

### Included Signed Assets
* `OpenDualSetup.exe` - Complete standalone GUI installer, updater, and uninstaller suite (Recommended)
* `OpenDualHub.exe` - Standalone Cross-Media Bar Controller Hub executable
* `OpenDualDriver.exe` - Standalone 1000Hz C++20 Direct HID Driver engine
* `OpenDual-v1.8.0.zip` - Portable bundle including all signed binaries and assets
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
