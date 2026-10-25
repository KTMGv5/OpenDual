@echo off
setlocal
echo =========================================================
echo   OpenDual PlayStation Controller Suite - Setup Installer
echo =========================================================
cd /d "%~dp0"

if exist "dist\OpenDualSetup.exe" (
    start "" "dist\OpenDualSetup.exe"
) else if exist "OpenDualSetup.exe" (
    start "" "OpenDualSetup.exe"
) else (
    echo [Info] Dist setup binary not found, running build...
    call build_all.bat
    if exist "dist\OpenDualSetup.exe" (
        start "" "dist\OpenDualSetup.exe"
    )
)
