[CmdletBinding()]
param(
    [switch]$DesktopShortcut
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
$source = Join-Path $root 'Release\Runner\win-x64\publish'
$standaloneSource = Join-Path $root 'Standalone\win-x64\publish'
$executable = Join-Path $source 'RunMind.Runner.exe'
$standaloneExecutable = Join-Path $standaloneSource 'RunMind.exe'
$installDirectory = Join-Path $env:LOCALAPPDATA 'Programs\RunMind Runner'
$installedStandaloneDirectory = Join-Path $installDirectory 'Standalone\win-x64\publish'
$startMenuDirectory = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs'
$startMenuShortcut = Join-Path $startMenuDirectory 'RunMind Runner.lnk'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'RunMind Runner.lnk'

if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "The Runner release files were not found: $executable`nRun Build-Release-x64.bat first. This installer does not build the application."
}
if (-not (Test-Path -LiteralPath $standaloneExecutable -PathType Leaf)) {
    throw "The bundled Standalone chat host was not found: $standaloneExecutable`nThe Runner release ZIP is incomplete. Download the complete Runner release archive again."
}

$running = @(Get-Process -Name 'RunMind.Runner' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    throw 'Exit RunMind Runner from its tray menu and run the installer again. The installer will not terminate or overwrite a running process.'
}

$runningBundledStandalone = @(
    Get-Process -Name 'RunMind' -ErrorAction SilentlyContinue |
        Where-Object {
            try {
                $processPath = $_.Path
                -not [string]::IsNullOrWhiteSpace($processPath) -and
                    $processPath.StartsWith($installedStandaloneDirectory, [System.StringComparison]::OrdinalIgnoreCase)
            }
            catch { $false }
        }
)
if ($runningBundledStandalone.Count -gt 0) {
    throw 'Close the chat window opened by RunMind Runner and run the installer again. The installer will not overwrite a running bundled chat host.'
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $installDirectory -Recurse -Force
New-Item -ItemType Directory -Path $installedStandaloneDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $standaloneSource '*') -Destination $installedStandaloneDirectory -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'Uninstall-RunMind-Runner.ps1') -Destination (Join-Path $installDirectory 'Uninstall-RunMind-Runner.ps1') -Force
$installedExecutable = Join-Path $installDirectory 'RunMind.Runner.exe'
if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf)) {
    throw 'The Runner executable was not found after copying the release files.'
}

New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($startMenuShortcut)
$shortcut.TargetPath = $installedExecutable
$shortcut.WorkingDirectory = $installDirectory
$shortcut.IconLocation = (Join-Path $installDirectory 'Assets\Brand\Icon.ico')
$shortcut.Description = 'Optional RunMind quick launcher'
$shortcut.Save()

if ($DesktopShortcut) {
    $desktop = $shell.CreateShortcut($desktopShortcut)
    $desktop.TargetPath = $installedExecutable
    $desktop.WorkingDirectory = $installDirectory
    $desktop.IconLocation = (Join-Path $installDirectory 'Assets\Brand\Icon.ico')
    $desktop.Description = 'Optional RunMind quick launcher'
    $desktop.Save()
}

$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\RunMindRunner'
New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'RunMind Runner'
Set-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value '3.5.0'
Set-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installDirectory
Set-ItemProperty -Path $uninstallKey -Name UninstallString -Value "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $installDirectory 'Uninstall-RunMind-Runner.ps1')`""

Write-Host 'RunMind Runner was installed for the current Windows user.' -ForegroundColor Green
Write-Host "Installed executable: $installedExecutable"
Write-Host "Bundled chat host: $(Join-Path $installedStandaloneDirectory 'RunMind.exe')"
Write-Host "Start Menu shortcut: $startMenuShortcut"
Write-Host 'Runner includes its own matching Standalone chat host; a separate Standalone installation is not required.'
Write-Host 'A separately installed RunMind Standalone is left untouched.'
Write-Host 'Runner is optional, does not require PowerToys, and does not start with Windows unless enabled in Runner Settings.'
