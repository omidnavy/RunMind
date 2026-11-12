@echo off
setlocal EnableExtensions

rem This script only installs or uninstalls the MSIX produced by Build-Release-x64.bat.
rem It does not restore, compile, publish, package, or modify Developer Mode.
cd /d "%~dp0"

set "ACTION=%~1"
set "EXITCODE=0"
if not defined ACTION set "ACTION=install"

if /I "%ACTION%"=="help" goto :help
if /I "%ACTION%"=="uninstall" goto :uninstall
if /I not "%ACTION%"=="install" goto :help_error

echo ================================================
echo  Installing RunMind Command Palette
echo ================================================
echo.

echo Installing the prebuilt signed MSIX from Release\CommandPalette\x64...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Deploy-RunMind-CommandPalette.ps1" -Action Install
if errorlevel 1 (
    echo.
    echo [ERROR] RunMind Command Palette installation failed.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo [SUCCESS] RunMind Command Palette installed.
echo Open PowerToys Command Palette and run "Reload Command Palette extensions".
echo The installer also attempts to set the direct RunMind alias for empty chat.
goto :finish

:uninstall
echo ================================================
echo  Uninstalling RunMind Command Palette
echo ================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Deploy-RunMind-CommandPalette.ps1" -Action Uninstall
if errorlevel 1 (
    echo.
    echo [ERROR] RunMind Command Palette uninstallation failed.
    set "EXITCODE=1"
    goto :finish
)
echo.
echo [SUCCESS] RunMind Command Palette uninstalled.
goto :finish

:help_error
echo [ERROR] Unknown action: %ACTION%
echo.

:help
echo Usage: Install-RunMind-CommandPalette.bat [install^|uninstall^|help]
echo.
echo   No argument  Install the already-built and signed Command Palette MSIX.
echo   install      Same as the default action.
echo   uninstall    Remove the current user's Command Palette package.
echo   help         Show this help.
echo.
echo The installer also attempts to configure the default RunMind direct alias.
echo.
echo Build all three RunMind hosts first with Build-Release-x64.bat.
if /I not "%ACTION%"=="help" set "EXITCODE=1"
goto :finish

:finish
echo.
pause
exit /b %EXITCODE%
