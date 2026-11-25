@echo off
setlocal EnableExtensions

rem One release entry point for all RunMind hosts:
rem   1) Standalone desktop app
rem   2) PowerToys Run plugin
rem   3) PowerToys Command Palette extension (signed MSIX)
rem Installation is intentionally separate: Install-RunMind-PowerToys-Run.bat and Install-RunMind-CommandPalette.bat.
cd /d "%~dp0"

rem Opt out of .NET CLI telemetry and suppress first-run/logo noise for this
rem release build. These settings are inherited by every dotnet process below.
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1"

set "SOLUTION=RunMind.sln"
set "ACTION=%~1"
set "EXITCODE=0"
if not defined ACTION set "ACTION=build"

if /I "%ACTION%"=="help" goto :help
if /I not "%ACTION%"=="build" goto :help_error

echo ================================================
echo  Building RunMind Release x64 (all three hosts)
echo ================================================
echo.

rem Remove obsolete Command Palette source files left behind when a newer ZIP
rem is extracted over an older source folder.
if exist "RunMind.CommandPalette\ConfigureRunMindPage.cs" (
    del /q "RunMind.CommandPalette\ConfigureRunMindPage.cs"
    echo [INFO] Removed obsolete ConfigureRunMindPage.cs.
)
if exist "RunMind.CommandPalette\AskRunMindPage.cs" (
    del /q "RunMind.CommandPalette\AskRunMindPage.cs"
    echo [INFO] Removed obsolete AskRunMindPage.cs.
)

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

for %%P in (
    "Community.PowerToys.Run.Plugin.RunMind\Community.PowerToys.Run.Plugin.RunMind.csproj"
    "RunMind.Standalone\RunMind.Standalone.csproj"
    "RunMind.CommandPalette\RunMind.CommandPalette.csproj"
) do (
    if not exist "%%~P" (
        echo [ERROR] Required RunMind host project is missing: %%~P
        set "EXITCODE=1"
        goto :finish
    )
)

echo [1/5] Preparing pinned PDF.js, Tesseract.js, English and Persian OCR resources...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Prepare-RunMind-OCR.ps1"
if errorlevel 1 (
    echo.
    echo [ERROR] PDF/OCR resource preparation failed. Release build cancelled.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [2/5] Restoring the full solution and the Windows x64 runtime pack...
dotnet restore "%SOLUTION%" --runtime win-x64 -p:Configuration=Release -p:Platform=x64
if errorlevel 1 (
    echo.
    echo [ERROR] dotnet restore failed. Release build cancelled.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [3/5] Cleaning all three Release x64 projects...
dotnet clean "%SOLUTION%" --configuration Release -p:Platform=x64
if errorlevel 1 (
    echo.
    echo [ERROR] dotnet clean failed. Release build cancelled.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [4/5] Building the complete Release x64 solution...
echo        - RunMind Standalone
echo        - PowerToys Run plugin
echo        - PowerToys Command Palette extension
dotnet build "%SOLUTION%" --configuration Release -p:Platform=x64
if errorlevel 1 (
    echo.
    echo [ERROR] Solution build failed.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [5/5] Packaging and signing the Command Palette MSIX (not installing)...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-RunMind-CommandPalette.ps1" -Configuration Release -Platform x64
if errorlevel 1 (
    echo.
    echo [ERROR] Command Palette release packaging failed.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [SUCCESS] All three RunMind hosts were built for Release x64.
echo [SUCCESS] The Command Palette MSIX and public signing certificate are in:
echo          "%~dp0Release\CommandPalette\x64"
echo.
echo Nothing was installed. Use Install-RunMind-PowerToys-Run.bat and Install-RunMind-CommandPalette.bat separately.
goto :finish

:help_error
echo [ERROR] Unknown action: %ACTION%
echo.

:help
echo Usage: Build-Release-x64.bat [build^|help]
echo.
echo   No argument  Build all three Release x64 hosts and package/sign the Command Palette MSIX.
echo   build        Same as the default action.
echo   help         Show this help.
if /I not "%ACTION%"=="help" set "EXITCODE=1"
goto :finish

:finish
echo.
pause
exit /b %EXITCODE%
