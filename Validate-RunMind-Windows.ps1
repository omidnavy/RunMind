[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSCommandPath
Push-Location $root
try {
    function Write-Step([string]$Message) { Write-Host "`n== $Message ==" -ForegroundColor Cyan }
    function Assert-File([string]$RelativePath) {
        $path = Join-Path $root $RelativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Expected release output was not found: $RelativePath" }
        Write-Host "PASS $RelativePath" -ForegroundColor Green
    }

    if (-not $SkipTests) {
        $node = Get-Command node -ErrorAction SilentlyContinue
        if ($null -eq $node) { throw 'Node.js 24+ is required. Install Node.js and reopen the terminal.' }
        $nodeVersionText = (& node --version).Trim()
        if ($LASTEXITCODE -ne 0 -or $nodeVersionText -notmatch '^v(?<major>\d+)\.') { throw "Unable to read Node.js version: $nodeVersionText" }
        $nodeMajor = [int]$Matches.major
        if ($nodeMajor -lt 24) { throw "Node.js 24 or later is required. Detected $nodeVersionText." }
        Write-Host "Node.js: $nodeVersionText" -ForegroundColor Green
    }

    if (-not $SkipBuild) {
        $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($null -eq $dotnet) { throw 'The .NET 10 SDK was not found. Install the .NET 10 SDK and check PATH.' }
        $sdkLines = @(& dotnet --list-sdks 2>&1)
        if ($LASTEXITCODE -ne 0 -or -not ($sdkLines | Where-Object { $_ -match '^10\.' })) {
            throw 'A .NET 10 SDK is required. `dotnet --list-sdks` did not report a 10.x SDK.'
        }
        Write-Host ($sdkLines | Where-Object { $_ -match '^10\.' } | ForEach-Object { "SDK: $_" }) -ForegroundColor Green
    }

    if (-not $SkipTests) {
        Write-Step 'Running the portable Node.js regression suite'
        & node --test --test-concurrency=1
        if ($LASTEXITCODE -ne 0) { throw "The Node.js test suite failed with exit code $LASTEXITCODE." }
    }

    if (-not $SkipBuild) {
        Write-Step 'Building all four hosts and release artifacts'
        $buildScript = Join-Path $root 'Build-Release-x64.bat'
        if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) { throw 'Build-Release-x64.bat was not found.' }
        $command = '"{0}" build < NUL' -f $buildScript
        $build = Start-Process -FilePath $env:ComSpec -ArgumentList @('/d', '/c', $command) -WorkingDirectory $root -Wait -PassThru -NoNewWindow
        if ($build.ExitCode -ne 0) { throw "Build-Release-x64.bat failed with exit code $($build.ExitCode)." }
    }

    Write-Step 'Verifying all four host outputs, Command Palette package and GitHub release ZIPs'
    Assert-File 'Community.PowerToys.Run.Plugin.RunMind\bin\x64\Release\Community.PowerToys.Run.Plugin.RunMind.dll'
    Assert-File 'Release\Standalone\win-x64\publish\RunMind.exe'
    Assert-File 'Release\Runner\win-x64\publish\RunMind.Runner.exe'
    Assert-File 'Release\CommandPalette\x64\RunMind.CommandPalette.msix'
    Assert-File 'Release\CommandPalette\x64\RunMind.CommandPalette.cer'
    [xml]$standaloneProject = Get-Content -LiteralPath (Join-Path $root 'RunMind.Standalone\RunMind.Standalone.csproj') -Raw
    $releaseVersion = $standaloneProject.SelectSingleNode("//*[local-name()='Version']").InnerText.Trim()
    foreach ($hostName in @('standalone', 'runner', 'powertoysrun', 'commandpalette')) {
        Assert-File ("Release\GitHub-Releases\{0}-{1}.zip" -f $hostName, $releaseVersion)
    }

    Write-Host "`n[SUCCESS] Portable tests, Release x64 outputs and four GitHub release ZIP checks completed. No host was installed." -ForegroundColor Green
}
finally {
    Pop-Location
}
