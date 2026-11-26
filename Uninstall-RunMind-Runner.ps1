[CmdletBinding()]
param(
    [switch]$KeepSettings
)

$ErrorActionPreference = 'Stop'
$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\RunMind Runner'

$running = @(Get-Process -Name 'RunMind.Runner' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    throw 'Exit RunMind Runner from its tray menu before uninstalling it. The uninstaller will not terminate a running process.'
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Test-Path -LiteralPath $runKey) {
    Remove-ItemProperty -LiteralPath $runKey -Name RunMindRunner -ErrorAction SilentlyContinue
}
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\RunMindRunner' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\RunMind Runner.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) 'RunMind Runner.lnk') -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $installDirectory) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}
if (-not $KeepSettings) {
    $settingsDirectory = Join-Path $env:LOCALAPPDATA 'RunMind\Runner'
    if (Test-Path -LiteralPath $settingsDirectory) {
        Remove-Item -LiteralPath $settingsDirectory -Recurse -Force
    }
}

Write-Host 'RunMind Runner has been removed for the current Windows user.' -ForegroundColor Green
if ($KeepSettings) {
    Write-Host 'Runner settings were retained.'
}
Write-Host 'RunMind Standalone, PowerToys Run and Command Palette were not changed.'
