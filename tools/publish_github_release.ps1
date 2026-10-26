param(
    [string]$TagName = "v1.6.0",
    [string]$ReleaseName = "OpenDual v1.6.0 - PS5 Create Capture Bar & Windows Game Bar Integration",
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
## Highlights in OpenDual v1.6.0

OpenDual v1.6.0 brings full **PlayStation Create / Share Button** functionality with an authentic **PS5-style Create Capture Bar** directly integrated with PC gaming tools (Windows Game Bar & GameDVR), plus rock-solid HUD clock centering that eliminates side-to-side wobble from ms latency fluctuations.

---

### What's New in v1.6.0

* **PlayStation Create / Share Button (Capture Bar)**:
  * **Authentic PS5 Create Menu**: Pressing Create / Share opens a floating, non-focus-stealing capture bar (`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`) at the bottom of the screen.
  * **5 Capture Tiles**:
    * **Tile 0 (Screenshot)**: Instant high-res screenshot capture (`Win + Alt + PrtScn`).
    * **Tile 1 (Save Recent Clip)**: Instant Replay clip saving (`Win + Alt + G`).
    * **Tile 2 (Record Gameplay)**: Start / Stop full video recording (`Win + Alt + R`).
    * **Tile 3 (Xbox Game Bar)**: Direct shortcut to PC Game Bar overlay (`Win + G`).
    * **Tile 4 (Gallery)**: 1-click access to Windows `Videos\Captures` gallery.
  * **On-the-Fly Replay Buffer Duration**: On Tile 1, D-Pad Up/Down allows selecting clip length on the fly (`15s`, `30s`, `1 min`, `3 min`, `5 min`), directly updating the Windows GameDVR registry buffer.
  * **PS5-Style Quick Gestures**:
    * **Single Tap Share**: Toggles Create Capture Bar.
    * **Hold Share (> 800ms)**: Instant screenshot with shutter notification.
    * **Double-Tap Share**: Instant 30s replay clip capture (`Win + Alt + G`).
  * **Full Controller Navigation**: D-Pad Left/Right navigates tiles, Cross (X) confirms action, Circle (O) or Share dismisses.

* **Rock-Solid Centered HUD Clock**:
  * Fixed HUD layout where fluctuating sub-millisecond polling latency pushed the digital clock side-to-side.
  * The clock is now anchored across the entire HUD boundary, guaranteeing rock-solid dead-center placement regardless of left or right telemetry string widths.

* **Codebase Verification & Security**:
  * MSVC C/C++ Static Code Analysis (`/p:RunCodeAnalysis=true`) passed with 0 warnings and 0 defects.
  * Full .NET C# build passed with 0 warnings, 0 errors.
  * Documentation and release notes verified with 0 non-ASCII characters.

---

### Included Signed Binaries
* `OpenDualSetup.exe` - Standalone installer and uninstaller suite
* `OpenDualHub.exe` - Desktop controller hub and configuration center
* `OpenDualDriver.exe` - 1000Hz C++20 driver service engine
* `OpenDual-v1.6.0.zip` - Portable release bundle

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
