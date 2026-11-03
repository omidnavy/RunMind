@echo off
setlocal

rem Run from the folder containing this batch file and the solution.
cd /d "%~dp0"

set "SOLUTION=Community.PowerToys.Run.Plugin.LocalLLM.sln"
set "EXITCODE=0"

echo ================================================
echo  Building Community PowerToys Run Plugin (x64)
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
    echo [ERROR] .NET SDK was not found. Install the .NET SDK or check PATH.
    set "EXITCODE=1"
    goto :finish
)

echo [1/2] Cleaning Release x64...
dotnet clean "%SOLUTION%" --configuration Release -p:Platform=x64
if errorlevel 1 (
    echo.
    echo [ERROR] dotnet clean failed. Build cancelled.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [2/2] Building Release x64...
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
