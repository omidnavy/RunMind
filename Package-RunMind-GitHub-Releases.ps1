[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath

function Get-ProjectVersion([string]$relativePath) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Version source is missing: $path" }
    [xml]$xml = Get-Content -LiteralPath $path -Raw
    $node = $xml.SelectSingleNode("//*[local-name()='Version']")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) { throw "No Version property found in $relativePath" }
    return $node.InnerText.Trim()
}

function Copy-ReleaseTree([string]$relativeSource, [string]$relativeDestination, [string]$stage) {
    $source = Join-Path $root $relativeSource
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw "Required release output is missing: $source" }
    $destination = Join-Path $stage $relativeDestination
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $destination -Recurse -Force | Out-Null
}

function Copy-ReleaseFile([string]$relativeSource, [string]$relativeDestination, [string]$stage) {
    $source = Join-Path $root $relativeSource
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required release file is missing: $source" }
    $destination = Join-Path $stage $relativeDestination
    $parent = Split-Path -Parent $destination
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    Copy-Item -LiteralPath $source -Destination $destination -Force | Out-Null
}

function New-RunMindReleaseZip([string]$name, [scriptblock]$populate, [string]$outputDirectory, [string]$version) {
    $stage = Join-Path ([System.IO.Path]::GetTempPath()) ("RunMind-GitHub-Release-{0}-{1}-{2}" -f $version, $name, [guid]::NewGuid().ToString('N'))
    $zipPath = Join-Path $outputDirectory ("{0}-{1}.zip" -f $name, $version)
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    try {
        & $populate $stage
        if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
        [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
        $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            if ($archive.Entries.Count -eq 0) { throw "Release archive is empty: $zipPath" }
        }
        finally { $archive.Dispose() }
        Write-Host ("[PACKAGED] {0}" -f (Split-Path -Leaf $zipPath)) -ForegroundColor Green
    }
    finally {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($Version)) { $Version = Get-ProjectVersion 'RunMind.Standalone\RunMind.Standalone.csproj' }
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid release version: $Version" }

    $versionSources = @(
        'RunMind.Standalone\RunMind.Standalone.csproj',
        'RunMind.Runner\RunMind.Runner.csproj',
        'Community.PowerToys.Run.Plugin.RunMind\Community.PowerToys.Run.Plugin.RunMind.csproj',
        'RunMind.CommandPalette\RunMind.CommandPalette.csproj'
    )
    foreach ($source in $versionSources) {
        $actual = Get-ProjectVersion $source
        if ($actual -ne $Version) { throw "Version mismatch: $source declares $actual, expected $Version." }
    }

    $pluginManifest = Join-Path $root 'Community.PowerToys.Run.Plugin.RunMind\plugin.json'
    $plugin = Get-Content -LiteralPath $pluginManifest -Raw | ConvertFrom-Json
    if ([string]$plugin.Version -ne $Version) { throw "PowerToys Run plugin manifest version '$($plugin.Version)' does not match $Version." }
    $runnerInstaller = Get-Content -LiteralPath (Join-Path $root 'Install-RunMind-Runner.ps1') -Raw
    if ($runnerInstaller -notmatch [regex]::Escape("-Name DisplayVersion -Value '$Version'")) { throw "Runner installer display version does not match $Version." }

    $paletteProject = Join-Path $root 'RunMind.CommandPalette\RunMind.CommandPalette.csproj'
    [xml]$paletteXml = Get-Content -LiteralPath $paletteProject -Raw
    $paletteVersion = $paletteXml.SelectSingleNode("//*[local-name()='AppxPackageVersion']").InnerText.Trim()
    $expectedPaletteVersion = '{0}.0' -f $Version
    if ($paletteVersion -ne $expectedPaletteVersion) { throw "Command Palette MSIX version '$paletteVersion' does not match '$expectedPaletteVersion'." }
    $paletteManifest = Join-Path $root 'RunMind.CommandPalette\Package.appxmanifest'
    [xml]$manifestXml = Get-Content -LiteralPath $paletteManifest -Raw
    $manifestIdentity = $manifestXml.SelectSingleNode("//*[local-name()='Identity']")
    if ($null -eq $manifestIdentity -or $manifestIdentity.GetAttribute('Version') -ne $expectedPaletteVersion) { throw "Command Palette package manifest version does not match '$expectedPaletteVersion'." }

    $outputDirectory = Join-Path $root 'Release\GitHub-Releases'
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $stagedOutputDirectory = Join-Path $outputDirectory ('.staging-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stagedOutputDirectory -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $standalone = Join-Path $root 'Release\Standalone\win-x64\publish'
    $runner = Join-Path $root 'Release\Runner\win-x64\publish'
    $pluginOutput = Join-Path $root 'Community.PowerToys.Run.Plugin.RunMind\bin\x64\Release'
    $paletteOutput = Join-Path $root 'Release\CommandPalette\x64'
    foreach ($required in @(
        (Join-Path $standalone 'RunMind.exe'),
        (Join-Path $runner 'RunMind.Runner.exe'),
        (Join-Path $pluginOutput 'Community.PowerToys.Run.Plugin.RunMind.dll'),
        (Join-Path $paletteOutput 'RunMind.CommandPalette.msix'),
        (Join-Path $paletteOutput 'RunMind.CommandPalette.cer')
    )) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "A required host artifact was not produced: $required" }
    }

    New-RunMindReleaseZip 'standalone' {
        param($stage)
        Copy-ReleaseTree 'Release\Standalone\win-x64\publish' 'Release\Standalone\win-x64\publish' $stage
        Set-Content -LiteralPath (Join-Path $stage 'RELEASE-INSTALL.txt') -Encoding UTF8 -Value @(
            "RunMind Standalone $Version",
            'Run RunMind.exe from Release\Standalone\win-x64\publish.',
            'This is a framework-dependent Windows x64 build and requires the .NET 10 Desktop Runtime and Microsoft Edge WebView2 Runtime.',
            'No installer is included; this archive is a portable published application folder.'
        )
    } $stagedOutputDirectory $Version

    New-RunMindReleaseZip 'runner' {
        param($stage)
        Copy-ReleaseTree 'Release\Runner\win-x64\publish' 'Release\Runner\win-x64\publish' $stage
        Copy-ReleaseTree 'Release\Standalone\win-x64\publish' 'Standalone\win-x64\publish' $stage
        Copy-ReleaseFile 'Install-RunMind-Runner.ps1' 'Install-RunMind-Runner.ps1' $stage
        Copy-ReleaseFile 'Uninstall-RunMind-Runner.ps1' 'Uninstall-RunMind-Runner.ps1' $stage
        Set-Content -LiteralPath (Join-Path $stage 'RELEASE-INSTALL.txt') -Encoding UTF8 -Value @(
            "RunMind Runner $Version",
            'Run Install-RunMind-Runner.ps1 to install for the current Windows user.',
            'The installer creates the Start Menu shortcut. Use Uninstall-RunMind-Runner.ps1 to remove it.',
            'The ZIP includes the matching Standalone chat host under Standalone\win-x64\publish; the Runner installer installs it as a private companion.',
            'Runner and its bundled chat host are framework-dependent and require the .NET 10 Desktop Runtime and Microsoft Edge WebView2 Runtime.',
            'The bundled chat host is installed inside the Runner directory and does not replace or uninstall a separately installed RunMind Standalone.'
        )
    } $stagedOutputDirectory $Version

    New-RunMindReleaseZip 'powertoysrun' {
        param($stage)
        Copy-ReleaseTree 'Community.PowerToys.Run.Plugin.RunMind\bin\x64\Release' 'Community.PowerToys.Run.Plugin.RunMind\bin\x64\Release' $stage
        Copy-ReleaseFile 'Install-RunMind-PowerToys-Run.bat' 'Install-RunMind-PowerToys-Run.bat' $stage
        Set-Content -LiteralPath (Join-Path $stage 'RELEASE-INSTALL.txt') -Encoding UTF8 -Value @(
            "RunMind PowerToys Run plugin $Version",
            'Close PowerToys completely, then run Install-RunMind-PowerToys-Run.bat.',
            'The installer copies the included plugin output into the current user PowerToys Run Plugins directory.',
            'PowerToys must already be installed. The plugin uses the shared RunMind Agent experience.'
        )
    } $stagedOutputDirectory $Version

    New-RunMindReleaseZip 'commandpalette' {
        param($stage)
        Copy-ReleaseTree 'Release\CommandPalette\x64' 'Release\CommandPalette\x64' $stage
        Copy-ReleaseFile 'Install-RunMind-CommandPalette.bat' 'Install-RunMind-CommandPalette.bat' $stage
        Copy-ReleaseFile 'Deploy-RunMind-CommandPalette.ps1' 'Deploy-RunMind-CommandPalette.ps1' $stage
        Copy-ReleaseFile 'Configure-RunMind-CommandPalette-Alias.ps1' 'Configure-RunMind-CommandPalette-Alias.ps1' $stage
        Copy-ReleaseFile 'RunMind.CommandPalette\Package.appxmanifest' 'Package.appxmanifest' $stage
        Set-Content -LiteralPath (Join-Path $stage 'RELEASE-INSTALL.txt') -Encoding UTF8 -Value @(
            "RunMind Command Palette extension $Version",
            'Run Install-RunMind-CommandPalette.bat to install the included signed MSIX.',
            'The installer verifies the package identity and signing certificate before installation.',
            'Microsoft PowerToys Command Palette is required. After installation, reload Command Palette extensions.'
        )
    } $stagedOutputDirectory $Version

    foreach ($hostName in @('standalone', 'runner', 'powertoysrun', 'commandpalette')) {
        $fileName = "{0}-{1}.zip" -f $hostName, $Version
        $stagedZip = Join-Path $stagedOutputDirectory $fileName
        $finalZip = Join-Path $outputDirectory $fileName
        Move-Item -LiteralPath $stagedZip -Destination $finalZip -Force
        Write-Host ("[SUCCESS] {0} ({1:N0} bytes)" -f $finalZip, (Get-Item -LiteralPath $finalZip).Length) -ForegroundColor Green
    }

    Remove-Item -LiteralPath $stagedOutputDirectory -Recurse -Force
    Write-Host ''
    Write-Host "[SUCCESS] Four GitHub release archives created in: $outputDirectory" -ForegroundColor Green
    exit 0
}
catch {
    if ($stagedOutputDirectory -and (Test-Path -LiteralPath $stagedOutputDirectory)) { Remove-Item -LiteralPath $stagedOutputDirectory -Recurse -Force }
    Write-Host ''
    Write-Host '[ERROR] GitHub release archive packaging failed.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
