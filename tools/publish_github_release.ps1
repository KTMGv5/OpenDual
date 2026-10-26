param(
    [string]$TagName = "v1.5.0",
    [string]$ReleaseName = "OpenDual v1.5.0 - Interactive In-Game HUD Control Center & Touchpad Notch Overhaul",
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
## Highlights in OpenDual v1.5.0

OpenDual v1.5.0 introduces an interactive **In-Game Control Center HUD** inspired by the PS5 dashboard, complete with controller-driven tile navigation and live PC hardware telemetry, plus an overhaul to the **Touchpad Engine** for clean, slot-by-slot inventory hotbar scrolling in games like Minecraft.

---

### What's New in v1.5.0

* **Interactive PS5 Control Center In-Game HUD**:
  * **Persistent Overlay**: Summon with the PS button or system tray; stays open persistently until explicitly closed with Circle (O) or PS button (no auto-hide timeout when in HUD mode). Ephemeral toasts still auto-dismiss cleanly.
  * **Zero Focus Stealing**: Built with Win32 `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` and topmost z-ordering - interact with controller shortcuts without pulling focus away from fullscreen games.
  * **Adjustable HUD Scaling**: Dynamic scale modes (Compact 85%, Standard 100%, Large 115%, Couch/4K TV 130%) so the HUD is never clipped on high-DPI displays.
  * **5 Controller-Navigable Tiles**:
    * **Tile 0 (Game & Profile)**: Displays active game executable and active profile; D-Pad Up/Down cycles profiles, Cross (X) applies.
    * **Tile 1 (Master Audio & Mic)**: Live Windows volume bar and endpoint name; D-Pad Up/Down adjusts volume in +/-5% steps, Cross (X) toggles Master Mute.
    * **Tile 2 (6-Axis Gyro Aiming)**: Motion aiming status (Active ADS / Disabled) and live sensitivity; D-Pad Up/Down adjusts sensitivity (+/-0.1x), Cross (X) toggles Gyro on/off.
    * **Tile 3 (PC Hardware Specs)**: Zero-overhead CPU % utilization (`GetSystemTimes`), RAM load % and used GB / total GB (`GlobalMemoryStatusEx`).
    * **Tile 4 (Touchpad & Haptics)**: Live touchpad sensitivity and stereo haptic summary; D-Pad Up/Down adjusts sensitivity (+/-0.1x), Cross (X) toggles Touchpad on/off.
  * **Universal Quick Actions**: Square ([]) instantly toggles microphone mute from anywhere in the HUD. Mouse clicks and touch inputs are also fully supported.
  * **Input Debouncing**: Rate-limited controller repeats (320ms hold, 130ms tick) with edge-triggered button actions ensure rapid, accurate tile navigation.

* **Touchpad Engine Overhaul (Minecraft & Inventory Safe)**:
  * **Discrete Notched Scroll Mode**: Replaced high-frequency mouse wheel spam with discrete notching (`notchThreshold = 42.0f`, `scrollCooldownMs = 65ms`, standard `WHEEL_DELTA = 120`). Prevents hotbars from spinning uncontrollably in Minecraft, weapon wheels, and menus - each two-finger swipe advances cleanly one slot at a time.
  * **Micro-Jitter Deadzone & Dual-Slope Ballistics**: Filtered resting finger tremors (`std::abs(dx) <= 1 && std::abs(dy) <= 1`) with 1:1 linear tracking at low speeds for pinpoint 36x36 slot clicking, smoothly accelerating at higher velocities for rapid cross-screen travel.

* **Live Touchpad Sensitivity Telemetry**:
  * Bidirectional IPC sync for `mouseSens` and `scrollSens` between DriverCore and DesktopApp.

* **Codebase Verification & Security**:
  * MSVC C/C++ Static Code Analysis (`/p:RunCodeAnalysis=true`) passed with 0 warnings and 0 defects.
  * Full .NET C# build passed with 0 warnings, 0 errors.

---

### Included Signed Binaries
* `OpenDualSetup.exe` - Standalone installer and uninstaller suite
* `OpenDualHub.exe` - Desktop controller hub and configuration center
* `OpenDualDriver.exe` - 1000Hz C++20 driver service engine
* `OpenDual-v1.5.0.zip` - Portable release bundle

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
