[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall', 'TrustCertificate')]
    [string]$Action = 'Install',
    [string]$CertificatePath,
    [string]$Publisher
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step([string]$Message) {
    Write-Host "`n$Message" -ForegroundColor Cyan
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object -TypeName System.Security.Principal.WindowsPrincipal -ArgumentList $identity
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ProjectProperty([xml]$ProjectXml, [string]$Name) {
    $node = $ProjectXml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='$Name']")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "The Command Palette project does not define the '$Name' property."
    }
    return $node.InnerText.Trim()
}

function Get-InstalledRunMindPackages([string]$Name, [string]$PublisherName) {
    return @(
        Get-AppxPackage -Name $Name -ErrorAction SilentlyContinue |
            Where-Object { $_.Publisher -eq $PublisherName }
    )
}

function Import-RunMindTrustCertificate([string]$Path, [string]$ExpectedPublisher) {
    if (-not (Test-IsAdministrator)) {
        throw 'The certificate trust step needs administrator approval. Retry and approve the Windows elevation prompt.'
    }
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw 'The exported public signing certificate was not found.'
    }

    $certificate = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $Path
    if ($certificate.Subject -ne $ExpectedPublisher) {
        throw "The certificate subject '$($certificate.Subject)' does not match package publisher '$ExpectedPublisher'."
    }
    $ekuExtension = $certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } | Select-Object -First 1
    $hasCodeSigningEku = $false
    if ($null -ne $ekuExtension) {
        $eku = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension -ArgumentList $ekuExtension, $ekuExtension.Critical
        $hasCodeSigningEku = @($eku.EnhancedKeyUsages | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' }).Count -gt 0
    }
    if (-not $hasCodeSigningEku) {
        throw 'The certificate does not have the Code Signing enhanced key usage.'
    }

    $existingTrust = Get-ChildItem -Path Cert:\LocalMachine\TrustedPeople -ErrorAction Stop |
        Where-Object { $_.Thumbprint -eq $certificate.Thumbprint } |
        Select-Object -First 1
    if ($null -eq $existingTrust) {
        Import-Certificate -FilePath $Path -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    }
    Write-Host 'RunMind package signing certificate is trusted by this Windows installation.' -ForegroundColor Green
}

function Ensure-RunMindCertificateTrusted([string]$Path, [string]$ExpectedPublisher) {
    $certificate = New-Object -TypeName System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $Path
    if ($certificate.Subject -ne $ExpectedPublisher) {
        throw "The package certificate publisher '$($certificate.Subject)' does not match '$ExpectedPublisher'."
    }

    $trustedCertificate = Get-ChildItem -Path Cert:\LocalMachine\TrustedPeople -ErrorAction Stop |
        Where-Object { $_.Thumbprint -eq $certificate.Thumbprint } |
        Select-Object -First 1
    if ($null -ne $trustedCertificate) {
        return $certificate
    }

    $powershellPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $quotedScriptPath = '"' + $PSCommandPath + '"'
    $quotedCertificatePath = '"' + $Path + '"'
    $elevatedArguments = "-NoProfile -ExecutionPolicy Bypass -File $quotedScriptPath -Action TrustCertificate -CertificatePath $quotedCertificatePath -Publisher `"$ExpectedPublisher`""

    Write-Host 'Windows will ask for administrator approval to trust the RunMind package certificate. This does not enable Developer Mode.' -ForegroundColor Yellow
    $elevatedProcess = Start-Process -FilePath $powershellPath -ArgumentList $elevatedArguments -Verb RunAs -Wait -PassThru
    if ($elevatedProcess.ExitCode -ne 0) {
        throw "The certificate trust step failed with exit code $($elevatedProcess.ExitCode)."
    }
    return $certificate
}

try {
    if ($Action -eq 'TrustCertificate') {
        Import-RunMindTrustCertificate -Path $CertificatePath -ExpectedPublisher $Publisher
        exit 0
    }

    $root = Split-Path -Parent $PSCommandPath
    $projectPath = Join-Path $root 'RunMind.CommandPalette\RunMind.CommandPalette.csproj'
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Command Palette project not found: $projectPath"
    }
    [xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
    $identityName = Get-ProjectProperty -ProjectXml $projectXml -Name 'AppxPackageIdentityName'
    $expectedPublisher = Get-ProjectProperty -ProjectXml $projectXml -Name 'AppxPackagePublisher'
    $installedPackages = @(Get-InstalledRunMindPackages -Name $identityName -PublisherName $expectedPublisher)

    if ($Action -eq 'Uninstall') {
        if ($installedPackages.Count -eq 0) {
            Write-Host 'RunMind Command Palette is not installed for the current Windows user.' -ForegroundColor Yellow
            exit 0
        }
        Write-Step 'Removing the RunMind Command Palette package'
        foreach ($package in $installedPackages) {
            Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop
        }
        Write-Host 'RunMind Command Palette has been uninstalled. The signing certificate remains trusted for a future install.' -ForegroundColor Green
        exit 0
    }

    $releaseDirectory = Join-Path $root 'Release\CommandPalette\x64'
    $msixPath = Join-Path $releaseDirectory 'RunMind.CommandPalette.msix'
    $certificatePath = Join-Path $releaseDirectory 'RunMind.CommandPalette.cer'
    if (-not (Test-Path -LiteralPath $msixPath -PathType Leaf)) {
        throw "The signed MSIX package does not exist: $msixPath`nRun Build-Release-x64.bat first. The installer does not build or package applications."
    }
    if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) {
        throw "The public signing certificate does not exist: $certificatePath`nRun Build-Release-x64.bat first."
    }

    Write-Step 'Checking the prebuilt RunMind Command Palette package'
    $certificate = Ensure-RunMindCertificateTrusted -Path $certificatePath -ExpectedPublisher $expectedPublisher
    $signature = Get-AuthenticodeSignature -FilePath $msixPath
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "The MSIX signature is not valid after trusting its certificate. Signature status: $($signature.Status). $($signature.StatusMessage)"
    }
    if ($null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        throw 'The MSIX signer does not match the public certificate supplied with this release.'
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($msixPath)
    try {
        $manifestEntry = $archive.GetEntry('AppxManifest.xml')
        if ($null -eq $manifestEntry) {
            throw 'The signed MSIX has no AppxManifest.xml.'
        }
        $manifestStream = $manifestEntry.Open()
        try {
            $reader = New-Object -TypeName System.IO.StreamReader -ArgumentList $manifestStream
            try { $manifestText = $reader.ReadToEnd() }
            finally { $reader.Dispose() }
        }
        finally { $manifestStream.Dispose() }
    }
    finally {
        $archive.Dispose()
    }

    [xml]$packageManifest = $manifestText
    $manifestIdentity = $packageManifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']")
    if ($null -eq $manifestIdentity -or $manifestIdentity.GetAttribute('Name') -ne $identityName -or $manifestIdentity.GetAttribute('Publisher') -ne $expectedPublisher) {
        throw 'The signed MSIX identity does not match the Command Palette project identity and publisher.'
    }

    Write-Step 'Installing the signed RunMind Command Palette package'
    $addPackageParameters = @{
        Path = $msixPath
        ErrorAction = 'Stop'
    }
    $addAppxPackageCommand = Get-Command Add-AppxPackage -ErrorAction Stop
    if ($addAppxPackageCommand.Parameters.ContainsKey('ForceUpdateFromAnyVersion')) {
        $addPackageParameters['ForceUpdateFromAnyVersion'] = $true
    }
    Add-AppxPackage @addPackageParameters

    $installed = @(Get-InstalledRunMindPackages -Name $identityName -PublisherName $expectedPublisher)
    if ($installed.Count -eq 0) {
        throw 'Windows accepted the MSIX installation command but the RunMind package could not be found afterward.'
    }

    $aliasScript = Join-Path $root 'Configure-RunMind-CommandPalette-Alias.ps1'
    if (Test-Path -LiteralPath $aliasScript -PathType Leaf) {
        Write-Step 'Setting the default RunMind direct alias (when Command Palette is closed)'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $aliasScript
        if ($LASTEXITCODE -ne 0) {
            Write-Host '[WARNING] Package installation succeeded, but the optional RunMind alias setup did not complete.' -ForegroundColor Yellow
        }
    }

    Write-Host ''
    Write-Host '[SUCCESS] The prebuilt, signed RunMind Command Palette package is installed.' -ForegroundColor Green
    Write-Host "Package file: $msixPath" -ForegroundColor DarkGray
    Write-Host "Installed location: $($installed[0].InstallLocation)" -ForegroundColor DarkGray
    Write-Host 'No build or Developer Mode change was performed by the installer.' -ForegroundColor Green
    Write-Host 'Open PowerToys Command Palette and run: Reload Command Palette extensions.' -ForegroundColor Yellow
    Write-Host 'If alias setup was skipped because Command Palette was open or its settings did not exist yet, close it and run Configure-RunMind-CommandPalette-Alias.ps1.' -ForegroundColor Yellow
}
catch {
    Write-Host ''
    Write-Host '[ERROR] RunMind Command Palette deployment failed.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
