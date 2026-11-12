$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$commandId = 'runmind.open-empty-chat'
$aliasName = 'RunMind'
$hostProcessPattern = '(?i)(CmdPal|CommandPalette)'

function Write-Notice([string]$Message) {
    Write-Host "[RunMind alias] $Message" -ForegroundColor Yellow
}

function Get-JsonProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties | Where-Object { $_.Name -ieq $Name } | Select-Object -First 1
    return $property
}

function Set-JsonProperty($Object, [string]$Name, $Value) {
    $property = Get-JsonProperty -Object $Object -Name $Name
    if ($null -ne $property) {
        $property.Value = $Value
    }
    else {
        $Object | Add-Member -MemberType NoteProperty -Name $Name -Value $Value
    }
}

try {
    $runningHost = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match $hostProcessPattern -and $_.ProcessName -notmatch '(?i)^RunMind\.CommandPalette$' })
    if ($runningHost.Count -gt 0) {
        Write-Notice 'Command Palette is running. No settings were changed. Fully exit Command Palette, then run this script again to apply the default alias.'
        return
    }

    # Command Palette currently uses Utilities.BaseSettingsPath("Microsoft.CmdPal"):
    # unpackaged: %LOCALAPPDATA%\Microsoft.CmdPal; packaged: the host package LocalState.
    $candidatePaths = New-Object System.Collections.Generic.List[string]
    $unpackagedSettings = Join-Path $env:LOCALAPPDATA 'Microsoft.CmdPal\settings.json'
    $candidatePaths.Add($unpackagedSettings)

    try {
        $hostPackages = @(Get-AppxPackage -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '(?i)(Microsoft\.PowerToys|Microsoft\.CommandPalette)' })
        foreach ($package in $hostPackages) {
            if (-not [string]::IsNullOrWhiteSpace($package.PackageFamilyName)) {
                $candidatePaths.Add((Join-Path $env:LOCALAPPDATA ("Packages\{0}\LocalState\settings.json" -f $package.PackageFamilyName)))
            }
        }
    }
    catch {
        # The unpackaged configuration path remains available when Appx discovery is unavailable.
    }

    $existingPaths = @($candidatePaths | Select-Object -Unique | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($existingPaths.Count -eq 0) {
        Write-Notice 'No existing Command Palette settings.json was found. Open Command Palette once, close it completely, and run this script again. No file was created.'
        return
    }

    $validPaths = New-Object System.Collections.Generic.List[string]
    foreach ($path in $existingPaths) {
        try {
            $probe = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            # A valid host settings.json is a candidate even before its custom
            # alias map has been created. The script can initialize that map.
            if ($probe -is [pscustomobject]) { $validPaths.Add($path) }
        }
        catch {
            # Ignore unrelated or malformed candidate settings files; never rewrite them.
        }
    }

    if ($validPaths.Count -eq 0) {
        Write-Notice 'A candidate settings file exists, but none contains the Command Palette Aliases collection. No settings were changed.'
        return
    }
    if ($validPaths.Count -gt 1) {
        Write-Notice 'More than one possible Command Palette settings file was found. No settings were changed to avoid editing the wrong profile.'
        $validPaths | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
        return
    }

    $settingsPath = $validPaths[0]
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $aliasesProperty = Get-JsonProperty -Object $settings -Name 'Aliases'
    if ($null -eq $aliasesProperty -or $null -eq $aliasesProperty.Value) {
        $aliases = New-Object PSObject
        Set-JsonProperty -Object $settings -Name 'Aliases' -Value $aliases
    }
    else {
        $aliases = $aliasesProperty.Value
    }
    $aliasProperties = @($aliases.PSObject.Properties)

    $existingAlias = $aliasProperties | Where-Object { $_.Name -ieq $aliasName } | Select-Object -First 1
    if ($null -ne $existingAlias) {
        $existingCommandId = [string]$existingAlias.Value.CommandId
        if ($existingCommandId -ne $commandId) {
            Write-Notice "The direct alias '$aliasName' already belongs to another command ('$existingCommandId'). It was preserved; no settings were changed."
            return
        }

        $existingAliasEntry = $existingAlias.Value
        Set-JsonProperty -Object $existingAliasEntry -Name 'Alias' -Value $aliasName
        Set-JsonProperty -Object $existingAliasEntry -Name 'IsDirect' -Value $true
        if ($existingAlias.Name -cne $aliasName) {
            $aliases.PSObject.Properties.Remove($existingAlias.Name)
            Set-JsonProperty -Object $aliases -Name $aliasName -Value $existingAliasEntry
        }
    }
    else {
        # Match the property casing used by the host's own serialized CommandAlias entries.
        $sampleAlias = $aliasProperties[0].Value
        $sampleCommandId = Get-JsonProperty -Object $sampleAlias -Name 'CommandId'
        $sampleAliasName = Get-JsonProperty -Object $sampleAlias -Name 'Alias'
        $sampleIsDirect = Get-JsonProperty -Object $sampleAlias -Name 'IsDirect'
        if ($null -eq $sampleCommandId -or $null -eq $sampleAliasName -or $null -eq $sampleIsDirect) {
            Write-Notice 'The host alias entry format was not recognized. No settings were changed.'
            return
        }
        $aliasEntry = New-Object PSObject
        $aliasEntry | Add-Member -MemberType NoteProperty -Name $sampleCommandId.Name -Value $commandId
        $aliasEntry | Add-Member -MemberType NoteProperty -Name $sampleAliasName.Name -Value $aliasName
        $aliasEntry | Add-Member -MemberType NoteProperty -Name $sampleIsDirect.Name -Value $true
        Set-JsonProperty -Object $aliases -Name $aliasName -Value $aliasEntry
    }

    $updatedJson = ConvertTo-Json -InputObject $settings -Depth 100
    $backupPath = '{0}.runmind-backup-{1}' -f $settingsPath, (Get-Date -Format 'yyyyMMdd-HHmmssfff')
    $temporaryPath = "$settingsPath.runmind-tmp"
    Copy-Item -LiteralPath $settingsPath -Destination $backupPath -ErrorAction Stop
    [System.IO.File]::WriteAllText($temporaryPath, $updatedJson + [Environment]::NewLine, ([System.Text.UTF8Encoding]::new($false)))
    try {
        [System.IO.File]::Replace($temporaryPath, $settingsPath, $null)
    }
    catch {
        if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue }
        throw
    }

    Write-Host "[SUCCESS] Direct alias '$aliasName' now opens 'Open chat with empty context'." -ForegroundColor Green
    Write-Host "Settings: $settingsPath" -ForegroundColor DarkGray
    Write-Host "Backup:   $backupPath" -ForegroundColor DarkGray
    Write-Host 'Restart Command Palette to load the alias.' -ForegroundColor Yellow
}
catch {
    Write-Host '[WARNING] The Command Palette alias could not be configured automatically.' -ForegroundColor Yellow
    Write-Host $_.Exception.Message -ForegroundColor Yellow
    # Alias setup is optional; do not roll back a successful package installation.
}
