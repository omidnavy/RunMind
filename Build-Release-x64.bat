@echo off
setlocal EnableExtensions

rem One release entry point for all RunMind hosts:
rem   1) Standalone desktop app
rem   2) PowerToys Run plugin
rem   3) PowerToys Command Palette extension (signed MSIX)
rem   4) Optional RunMind Runner launcher
rem Installation is intentionally separate: use the host-specific installers, including Install-RunMind-Runner.ps1.
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
echo  Building RunMind Release x64 (four hosts)
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

if exist "RunMind.Runner\RunnerChatWindow.xaml" (
    del /f /q "RunMind.Runner\RunnerChatWindow.xaml"
    echo [INFO] Removed retired RunnerChatWindow.xaml.
)
if exist "RunMind.Runner\RunnerChatWindow.xaml.cs" (
    del /f /q "RunMind.Runner\RunnerChatWindow.xaml.cs"
    echo [INFO] Removed retired RunnerChatWindow.xaml.cs.
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
    "RunMind.Runner\RunMind.Runner.csproj"
) do (
    if not exist "%%~P" (
        echo [ERROR] Required RunMind host project is missing: %%~P
        set "EXITCODE=1"
        goto :finish
    )
)

echo [1/8] Preparing versioned PDF.js/Tesseract packages and English/Persian OCR language data...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Prepare-RunMind-OCR.ps1"
if errorlevel 1 (
    echo.
    echo [ERROR] PDF/OCR resource preparation failed. Release build cancelled.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [2/8] Cleaning all Release x64 projects...
dotnet clean "%SOLUTION%" --configuration Release -p:Platform=x64
if errorlevel 1 (
    echo.
    echo [ERROR] dotnet clean failed. Release build cancelled.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [3/8] Restoring the full solution and the Windows x64 runtime pack...
dotnet restore "%SOLUTION%" --runtime win-x64 -p:Configuration=Release -p:Platform=x64
if errorlevel 1 (
    echo.
    echo [ERROR] dotnet restore failed. Release build cancelled.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [4/8] Building the complete Release x64 solution...
echo        - RunMind Standalone
echo        - PowerToys Run plugin
echo        - PowerToys Command Palette extension
echo        - Optional RunMind Runner
rem Step [3/8] restored the full solution with --runtime win-x64 after Clean.
rem Do not run an implicit restore here: it would overwrite Standalone's RID-specific
rem assets file with a framework-only target, breaking the later --no-restore publish.
dotnet build "%SOLUTION%" --configuration Release --no-restore -p:Platform=x64
if errorlevel 1 (
    echo.
    echo [ERROR] Solution build failed.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [5/8] Packaging and signing the Command Palette MSIX (not installing)...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-RunMind-CommandPalette.ps1" -Configuration Release -Platform x64
if errorlevel 1 (
    echo.
    echo [ERROR] Command Palette release packaging failed.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [6/8] Publishing RunMind Standalone for the Runner handoff (framework-dependent win-x64)...
dotnet publish "RunMind.Standalone\RunMind.Standalone.csproj" --configuration Release --runtime win-x64 --self-contained false --no-restore -p:Platform=x64 -o "Release\Standalone\win-x64\publish"
if errorlevel 1 (
    echo.
    echo [ERROR] RunMind Standalone publish failed. Runner handoff cannot be deployed without the updated Standalone host.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [7/8] Publishing the optional RunMind Runner (framework-dependent win-x64)...
dotnet publish "RunMind.Runner\RunMind.Runner.csproj" --configuration Release --runtime win-x64 --self-contained false --no-restore -p:Platform=x64 -o "Release\Runner\win-x64\publish"
if errorlevel 1 (
    echo.
    echo [ERROR] RunMind Runner publish failed.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [8/8] Verifying Standalone and Runner executables...
if not exist "Release\Standalone\win-x64\publish\RunMind.exe" (
    echo [ERROR] Updated RunMind Standalone executable was not produced.
    set "EXITCODE=1"
    goto :finish
)
if not exist "Release\Runner\win-x64\publish\RunMind.Runner.exe" (
    echo [ERROR] RunMind Runner executable was not produced.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [SUCCESS] All four RunMind hosts were built for Release x64.
echo [SUCCESS] Standalone handoff host: "%~dp0Release\Standalone\win-x64\publish"
echo [SUCCESS] Optional Runner: "%~dp0Release\Runner\win-x64\publish"
echo [SUCCESS] The Command Palette MSIX and public signing certificate are in:
echo          "%~dp0Release\CommandPalette\x64"
echo.
echo Nothing was installed. Use Install-RunMind-PowerToys-Run.bat, Install-RunMind-CommandPalette.bat, and Install-RunMind-Runner.ps1 separately.
goto :finish

:help_error
echo [ERROR] Unknown action: %ACTION%
echo.

:help
echo Usage: Build-Release-x64.bat [build^|help]
echo.
echo   No argument  Build all Release x64 hosts, publish Standalone and Runner, and package/sign the Command Palette MSIX.
echo   build        Same as the default action.
echo   help         Show this help.
if /I not "%ACTION%"=="help" set "EXITCODE=1"
goto :finish

:finish
echo.
pause
exit /b %EXITCODE%
