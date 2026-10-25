@echo off
echo =========================================================
echo   Installing ViGEmBus Driver for Windows 10 & 11
echo =========================================================

where winget >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo Attempting installation via winget...
    winget install --id ViGEm.ViGEmBus --accept-package-agreements --accept-source-agreements
    if %ERRORLEVEL% equ 0 (
        echo ViGEmBus driver installed successfully via winget!
        pause
        exit /b 0
    )
)

echo Downloading official signed ViGEmBus installer from GitHub...
powershell -Command "Invoke-WebRequest -Uri 'https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe' -OutFile '%TEMP%\ViGEmBus_Installer.exe'"
echo Launching installer with Administrator elevation...
powershell -Command "Start-Process -FilePath '%TEMP%\ViGEmBus_Installer.exe' -Verb RunAs -Wait"
echo Installation completed.
pause
