[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$reportDirectory = Join-Path $root 'artifacts\windows-validation'
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$logPath = Join-Path $reportDirectory ("validation-{0}-{1}.log" -f $Configuration, $Platform)
Start-Transcript -Path $logPath -Force | Out-Null

try {
    Write-Host '=== RunMind P0 / full Windows validation ===' -ForegroundColor Cyan
    Write-Host "Configuration: $Configuration | Platform: $Platform"
    Push-Location $root
    try {
        $node = Get-Command node -ErrorAction Stop
        $dotnet = Get-Command dotnet -ErrorAction Stop
        $nodeVersionText = (& $node.Source --version).Trim()
        if ($LASTEXITCODE -ne 0 -or $nodeVersionText -notmatch '^v?(?<major>\d+)\.') { throw "Could not determine the installed Node.js version: $nodeVersionText" }
        $nodeMajor = [int]$Matches.major
        if ($nodeMajor -lt 24) { throw "RunMind P0 requires Node.js 24 or later for the project test/runtime baseline. Detected: $nodeVersionText" }
        $dotnetSdkText = (& $dotnet.Source --version).Trim()
        if ($LASTEXITCODE -ne 0 -or $dotnetSdkText -notmatch '^(?<major>\d+)\.') { throw "Could not determine the installed .NET SDK version: $dotnetSdkText" }
        $dotnetMajor = [int]$Matches.major
        if ($dotnetMajor -lt 10) { throw "RunMind P0 targets .NET 10. Install the .NET 10 SDK. Detected: $dotnetSdkText" }
        Write-Host "Node.js: $nodeVersionText"
        Write-Host ".NET SDK: $dotnetSdkText"
        dotnet --info

        Write-Host '--- All portable Node integration and contract tests (P0 / Phase 1 / Phase 2) ---' -ForegroundColor Cyan
        $testFiles = @(Get-ChildItem (Join-Path $root 'tests') -Filter '*.test.cjs' | Sort-Object FullName | ForEach-Object { $_.FullName })
        if ($testFiles.Count -eq 0) { throw 'No Node integration test files were found.' }
        & $node.Source --test --test-concurrency=1 @testFiles
        if ($LASTEXITCODE -ne 0) { throw "Node tests failed with exit code $LASTEXITCODE." }

        Write-Host '--- Build all three Windows hosts ---' -ForegroundColor Cyan
        dotnet build (Join-Path $root 'RunMind.sln') --configuration $Configuration --property:Platform=$Platform
        if ($LASTEXITCODE -ne 0) { throw "Solution build failed with exit code $LASTEXITCODE." }

        Write-Host '--- Build result ---' -ForegroundColor Green
        Write-Host 'All three host projects compiled successfully.'
        Write-Host 'Windows runtime acceptance remains a separate step: follow docs/P0-WINDOWS-ACCEPTANCE.md for all three hosts.'
    }
    finally { Pop-Location }
}
catch {
    Write-Error $_
    exit 1
}
finally {
    Stop-Transcript | Out-Null
}
