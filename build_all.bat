@echo off
setlocal
echo =========================================================
echo   Building OpenDual Suite (Driver Core + Desktop + Setup)
echo =========================================================

where cmake >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [Error] CMake was not found in PATH.
    pause
    exit /b 1
)

where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [Error] .NET SDK was not found in PATH.
    pause
    exit /b 1
)

echo [1/4] Building C++20 Driver Core (OpenDualDriver)...
if not exist build_core mkdir build_core
cmake -B build_core -S DriverCore -G "Visual Studio 18 2026" -A x64
if %ERRORLEVEL% neq 0 (
    cmake -B build_core -S DriverCore
)
cmake --build build_core --config Release
if %ERRORLEVEL% neq 0 (
    echo [Error] Driver Core build failed!
    pause
    exit /b 1
)

echo [2/4] Publishing Desktop Controller Hub (OpenDualHub Single-File)...
dotnet publish DesktopApp\OpenDualHub.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
if %ERRORLEVEL% neq 0 (
    echo [Error] Desktop Hub build failed!
    pause
    exit /b 1
)

echo [3/4] Publishing Installer and Uninstaller Suite (OpenDualSetup Single-File)...
dotnet publish Installer\OpenDualSetup.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
if %ERRORLEVEL% neq 0 (
    echo [Error] Installer build failed!
    pause
    exit /b 1
)

echo [4/5] Packaging distribution into .\dist\...
taskkill /f /t /im OpenDualDriver.exe >nul 2>&1
taskkill /f /t /im OpenDualHub.exe >nul 2>&1
taskkill /f /t /im OpenDualSetup.exe >nul 2>&1
taskkill /f /t /im OpenDualUninstaller.exe >nul 2>&1
ping -n 2 127.0.0.1 >nul

if exist dist rmdir /s /q dist
mkdir dist
mkdir dist\Assets

copy /y build_core\Release\OpenDualDriver.exe dist\
copy /y DesktopApp\bin\Release\net10.0-windows\win-x64\publish\OpenDualHub.exe dist\
copy /y DesktopApp\bin\Release\net10.0-windows\win-x64\publish\OpenDualHub.pdb dist\
copy /y Installer\bin\Release\net10.0-windows\win-x64\publish\OpenDualSetup.exe dist\
copy /y Installer\bin\Release\net10.0-windows\win-x64\publish\OpenDualSetup.exe dist\OpenDualUninstaller.exe
copy /y DesktopApp\Assets\* dist\Assets\
copy /y setup.bat dist\
copy /y uninstall.bat dist\

echo [5/6] Signing Authenticode binaries in .\dist\...
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\sign_binaries.ps1" -TargetDir "dist"

echo [6/6] Creating release archive: dist\OpenDual-v1.6.2.zip...
if exist dist\*.zip del /f /q dist\*.zip
powershell -NoProfile -Command "Compress-Archive -Path dist\* -DestinationPath dist\OpenDual-v1.6.2.zip -Force"

echo =========================================================
echo   Build Successful! Artifacts are in .\dist\
echo   Run .\dist\OpenDualSetup.exe to install to registry.
echo   Run .\dist\OpenDualHub.exe to launch directly.
echo =========================================================
