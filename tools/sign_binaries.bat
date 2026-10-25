@echo off
setlocal
echo =========================================================
echo   OpenDual Suite - Automatic Authenticode Binary Signer
echo =========================================================

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sign_binaries.ps1"
if %ERRORLEVEL% neq 0 (
    echo [Error] Binary signing encountered errors.
    pause
    exit /b %ERRORLEVEL%
)

echo [Success] Binary signing process complete!
exit /b 0
