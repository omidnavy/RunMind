@echo off
setlocal EnableExtensions

rem Installs the already-built PowerToys Run plugin. This script never builds RunMind.
cd /d "%~dp0"

set "SOURCE=%~dp0Community.PowerToys.Run.Plugin.RunMind\bin\x64\Release"
set "TARGET=%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\Community.PowerToys.Run.Plugin.RunMind"
set "EXITCODE=0"

echo ================================================
echo  Installing RunMind for PowerToys Run
echo ================================================
echo.

if not defined LOCALAPPDATA (
    echo [ERROR] LOCALAPPDATA is not defined. Run this installer from your Windows user account.
    set "EXITCODE=1"
    goto :finish
)

if not exist "%SOURCE%\Community.PowerToys.Run.Plugin.RunMind.dll" (
    echo [ERROR] The PowerToys Run Release x64 output was not found:
    echo         "%SOURCE%"
    echo.
    echo Run Build-Release-x64.bat first to build all four hosts and release artifacts.
    set "EXITCODE=1"
    goto :finish
)

for %%F in (plugin.json ChatWindow.html ArtifactTools.js ChatWindow\theme\base.css ChatWindow\scripts\theme-loader.js) do (
    if not exist "%SOURCE%\%%F" (
        echo [ERROR] Required plugin file is missing: %%F
        echo         Expected under "%SOURCE%"
        set "EXITCODE=1"
        goto :finish
    )
)

where powershell.exe >nul 2>nul
if errorlevel 1 (
    echo [ERROR] Windows PowerShell could not be found. Cannot safely check whether PowerToys is running.
    set "EXITCODE=1"
    goto :finish
)

echo Checking whether PowerToys is running...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(PowerToys($|\.)|PowerLauncher($|\.))' }); if ($running.Count -gt 0) { Write-Host ''; Write-Host 'PowerToys must be fully closed before installing the RunMind plugin.' -ForegroundColor Yellow; $running | Select-Object ProcessName, Id | Format-Table -AutoSize; exit 10 }; exit 0"
if errorlevel 10 goto :powertoys_running
if errorlevel 1 (
    echo [ERROR] Could not verify the PowerToys process state. Installation cancelled for safety.
    set "EXITCODE=1"
    goto :finish
)

echo.
echo Source:
echo   "%SOURCE%"
echo Destination:
echo   "%TARGET%"
echo.
echo Copying the complete plugin output...
if not exist "%TARGET%" mkdir "%TARGET%"
if errorlevel 1 (
    echo [ERROR] Could not create the PowerToys Run plugin directory.
    set "EXITCODE=1"
    goto :finish
)

rem /MIR removes stale files from older plugin versions, including obsolete theme files.
rem Robocopy exit codes 0-7 indicate success; 8 or higher indicates a copy failure.
robocopy "%SOURCE%" "%TARGET%" /MIR /R:2 /W:1 /COPY:DAT /DCOPY:DAT /NFL /NDL /NP
set "ROBOCOPY_EXIT=%ERRORLEVEL%"
if %ROBOCOPY_EXIT% GEQ 8 (
    echo.
    echo [ERROR] Plugin copy failed with Robocopy exit code %ROBOCOPY_EXIT%.
    echo Make sure PowerToys is completely closed, then run this installer again.
    set "EXITCODE=1"
    goto :finish
)

for %%F in (plugin.json Community.PowerToys.Run.Plugin.RunMind.dll ChatWindow.html ArtifactTools.js ChatWindow\theme\base.css ChatWindow\scripts\theme-loader.js) do (
    if not exist "%TARGET%\%%F" (
        echo [ERROR] Installation is incomplete. Missing: %%F
        set "EXITCODE=1"
        goto :finish
    )
)

echo.
echo [SUCCESS] RunMind PowerToys Run plugin installed.
echo Start PowerToys and open PowerToys Run to use RunMind.
goto :finish

:powertoys_running
echo.
echo [WARNING] PowerToys is currently running. The RunMind plugin was NOT changed.
echo.
echo 1. Open the PowerToys system-tray menu.
echo 2. Choose Exit to close PowerToys completely.
echo 3. Run Install-RunMind-PowerToys-Run.bat again.
echo.
set "EXITCODE=2"
goto :finish

:finish
echo.
pause
exit /b %EXITCODE%
