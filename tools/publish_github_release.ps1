param(
    [string]$TagName = "v1.5.1",
    [string]$ReleaseName = "OpenDual v1.5.1 - 1000 Hz USB Polling Rate Overclocking Assistant",
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
## Highlights in OpenDual v1.5.1

OpenDual v1.5.1 delivers a built-in **1000 Hz USB Polling Rate Overclocking Assistant** with 1-click **SweetLow HIDUSBF** integration, giving competitive players an effortless path to slash DualSense input latency from 4.0ms down to a true 1.0ms esports response time.

---

### What's New in v1.5.1

* **1000 Hz USB Polling Rate Overclocking Assistant**:
  * **Hardware Latency Unlocked**: Standard DualSense controllers default to 250 Hz (4.0ms interval) on Windows USB because Sony's hardware descriptor reserves USB bus bandwidth for 48kHz audio endpoints. The new assistant guides users through applying SweetLow's low-latency filter driver to achieve 1000 Hz (1.0ms delay) on standard USB.
  * **1-Click Package Downloader**: Automatically downloads and extracts the official SweetLow HIDUSBF archive directly into `%LocalAppData%\OpenDual\tools\hidusbf\` with real-time download progress tracking.
  * **1-Click Driver Certificate Installer**: Installs `SweetLow.CER` directly into Windows Trusted Root and Trusted Publishers stores via elevated `certutil`, ensuring Windows 10 & 11 64-bit load the filter driver without security warnings.
  * **1-Click Elevated Setup Launcher**: Launches `Setup.exe` with Administrator elevation directly from the OpenDual interface.
  * **Windows Core Isolation Shortcut**: Direct 1-click launcher for `windowsdefender://coreisolation` for quick toggling if Memory Integrity (HVCI) blocks filter loading.

* **Real-Time Live Polling & Latency Benchmark**:
  * Live rolling benchmark tracking Current Hz, Latency (ms), Min Latency, Max Latency, and Average Latency.
  * Live filter service detection: verifies whether `hidusbf.sys` is installed in the Windows Service Control Manager and whether `LowerFilters` is active on DualSense (`VID_054C&PID_0CE6`).
  * Instant reconnection celebration: as soon as the user unplugs and replugs their controller, OpenDual automatically verifies the 1000 Hz stream and displays a success banner.

* **Dashboard & In-Game Control Center HUD Integration**:
  * **Dashboard Header**: Live polling rate badge displays `1000 Hz Active (Esports 1.0ms)` when overclocked, or `1000 Hz Assistant (Boost to 1ms)` when running at factory default.
  * **System Tab**: Added a dedicated "1000 Hz USB Polling Rate Overclocking" card with status readout and one-click assistant access.
  * **Control Center HUD**: Displays live [1000Hz (1.0ms)] overclock indicator directly in the in-game overlay.

* **Codebase Verification & Security**:
  * MSVC C/C++ Static Code Analysis (`/p:RunCodeAnalysis=true`) passed with 0 warnings and 0 defects.
  * Full .NET C# build passed with 0 warnings, 0 errors.
  * Documentation and release notes verified with 0 non-ASCII characters.

---

### Included Signed Binaries
* `OpenDualSetup.exe` - Standalone installer and uninstaller suite
* `OpenDualHub.exe` - Desktop controller hub and configuration center
* `OpenDualDriver.exe` - 1000Hz C++20 driver service engine
* `OpenDual-v1.5.1.zip` - Portable release bundle

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
