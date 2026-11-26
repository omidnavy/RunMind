[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$reportDirectory = Join-Path $root 'artifacts\phase1-validation'
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$logPath = Join-Path $reportDirectory ("validation-{0}-{1}.log" -f $Configuration, $Platform)
Start-Transcript -Path $logPath -Force | Out-Null

try {
    Write-Host '=== RunMind Phase 1 Windows validation ===' -ForegroundColor Cyan
    Write-Host "Configuration: $Configuration | Platform: $Platform"
    Push-Location $root
    try {
        $node = Get-Command node -ErrorAction Stop
        $dotnet = Get-Command dotnet -ErrorAction Stop
        node --version
        dotnet --info

        Write-Host '--- Node integration and contract tests ---' -ForegroundColor Cyan
        node --test (Get-ChildItem (Join-Path $root 'tests') -Filter '*.test.cjs' | ForEach-Object { $_.FullName })
        if ($LASTEXITCODE -ne 0) { throw "Node tests failed with exit code $LASTEXITCODE." }

        Write-Host '--- Build all three Windows hosts ---' -ForegroundColor Cyan
        dotnet build (Join-Path $root 'RunMind.sln') --configuration $Configuration --property:Platform=$Platform
        if ($LASTEXITCODE -ne 0) { throw "Solution build failed with exit code $LASTEXITCODE." }

        Write-Host '--- Build result ---' -ForegroundColor Green
        Write-Host 'All three host projects compiled successfully.'
        Write-Host 'Runtime smoke tests remain a separate step: launch Standalone, PowerToys Run, and Command Palette; test shared tools and native operations.'
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
