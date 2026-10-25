@echo off
setlocal
echo =========================================================
echo   OpenDual PlayStation Controller Suite - Uninstaller
echo =========================================================

set "INSTALL_DIR=%LOCALAPPDATA%\Programs\OpenDual"

if exist "%INSTALL_DIR%\OpenDualUninstaller.exe" (
    start "" "%INSTALL_DIR%\OpenDualUninstaller.exe"
) else if exist "dist\OpenDualSetup.exe" (
    start "" "dist\OpenDualSetup.exe" --uninstall
) else (
    echo Stopping running processes...
    taskkill /f /im OpenDualHub.exe >nul 2>&1
    taskkill /f /im OpenDualDriver.exe >nul 2>&1
    
    echo Removing registry entries...
    reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenDual" /f >nul 2>&1
    reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\OpenDualHub.exe" /f >nul 2>&1
    reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\OpenDual.exe" /f >nul 2>&1
    
    echo Removing shortcuts...
    del /f /q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\OpenDual.lnk" >nul 2>&1
    del /f /q "%USERPROFILE%\Desktop\OpenDual.lnk" >nul 2>&1
    
    if exist "%INSTALL_DIR%" (
        echo Removing application files...
        rmdir /s /q "%INSTALL_DIR%" >nul 2>&1
    )
    
    echo =========================================================
    echo   OpenDual has been completely uninstalled.
    echo =========================================================
    pause
)
