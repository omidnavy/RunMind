@echo off
setlocal

rem Run from the folder containing this batch file and the solution.
cd /d "%~dp0"

set "SOLUTION=RunMind.sln"
set "EXITCODE=0"

echo ================================================
echo  Building RunMind PowerToys Run Plugin and Standalone (x64)
echo ================================================
echo.

if not exist "%SOLUTION%" (
    echo [ERROR] Solution file not found:
    echo         "%~dp0%SOLUTION%"
    set "EXITCODE=1"
    goto :finish
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK was not found. Install the .NET 10 SDK or check PATH.
    set "EXITCODE=1"
    goto :finish
)

echo [1/3] Preparing pinned PDF.js, Tesseract.js, English and Persian OCR resources...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Prepare-RunMind-OCR.ps1"
if errorlevel 1 (
    echo.
    echo [ERROR] PDF/OCR resource preparation failed. Build cancelled.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [2/3] Cleaning Release x64...
dotnet clean "%SOLUTION%" --configuration Release -p:Platform=x64
if errorlevel 1 (
    echo.
    echo [ERROR] dotnet clean failed. Build cancelled.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [3/3] Building Release x64...
dotnet build "%SOLUTION%" --configuration Release -p:Platform=x64
if errorlevel 1 (
    echo.
    echo [ERROR] dotnet build failed.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [SUCCESS] Clean and build completed successfully.

:finish
echo.
pause
exit /b %EXITCODE%
