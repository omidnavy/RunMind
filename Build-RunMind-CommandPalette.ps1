[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Step([string]$Message) {
    Write-Host "`n$Message" -ForegroundColor Cyan
}

function Get-ProjectProperty([xml]$ProjectXml, [string]$Name) {
    $node = $ProjectXml.SelectSingleNode("/*[local-name()='Project']/*[local-name()='PropertyGroup']/*[local-name()='$Name']")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "The Command Palette project does not define the '$Name' property."
    }
    return $node.InnerText.Trim()
}

try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The .NET SDK was not found. Install the .NET 10 SDK and check PATH.'
    }

    $root = Split-Path -Parent $PSCommandPath
    $projectDirectory = Join-Path $root 'RunMind.CommandPalette'
    $projectPath = Join-Path $projectDirectory 'RunMind.CommandPalette.csproj'
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Command Palette project not found: $projectPath"
    }

    [xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
    $targetFramework = Get-ProjectProperty -ProjectXml $projectXml -Name 'TargetFramework'
    $runtimeIdentifier = (Get-ProjectProperty -ProjectXml $projectXml -Name 'RuntimeIdentifiers').Split(';')[0].Trim()
    $publisher = Get-ProjectProperty -ProjectXml $projectXml -Name 'AppxPackagePublisher'
    $identityName = Get-ProjectProperty -ProjectXml $projectXml -Name 'AppxPackageIdentityName'
    $appxVersion = Get-ProjectProperty -ProjectXml $projectXml -Name 'AppxPackageVersion'

    $friendlyName = 'RunMind Command Palette local development certificate'
    $certificate = Get-ChildItem -Path Cert:\CurrentUser\My -ErrorAction Stop |
        Where-Object {
            $_.Subject -eq $publisher -and
            $_.FriendlyName -eq $friendlyName -and
            $_.HasPrivateKey -and
            $_.NotAfter -gt (Get-Date) -and
            $_.NotBefore -le (Get-Date)
        } |
        Sort-Object -Property NotAfter -Descending |
        Select-Object -First 1

    if ($null -eq $certificate) {
        Write-Step 'Creating a local MSIX signing certificate (no Developer Mode required)'
        $certificate = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $publisher `
            -KeyUsage DigitalSignature `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -TextExtension @(
                '2.5.29.37={text}1.3.6.1.5.5.7.3.3',
                '2.5.29.19={text}'
            ) `
            -FriendlyName $friendlyName `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -NotAfter (Get-Date).AddYears(5)
    }

    if ($certificate.Subject -ne $publisher -or -not $certificate.HasPrivateKey) {
        throw 'The signing certificate does not have the required publisher identity and private key.'
    }

    $releaseDirectory = Join-Path $root 'Release\CommandPalette\x64'
    New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

    $packagePath = Join-Path $releaseDirectory 'RunMind.CommandPalette.msix'
    $certificatePath = Join-Path $releaseDirectory 'RunMind.CommandPalette.cer'
    Get-ChildItem -LiteralPath $releaseDirectory -Filter '*.msix' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction Stop
    Remove-Item -LiteralPath $certificatePath -Force -ErrorAction SilentlyContinue

    $packageOutputForMsbuild = [System.IO.Path]::GetFullPath($releaseDirectory).Replace('\', '/') + '/'
    $buildArguments = @(
        'build',
        $projectPath,
        '--configuration', $Configuration,
        "-p:Platform=$Platform",
        "-p:RuntimeIdentifier=$runtimeIdentifier",
        '-p:GenerateAppxPackageOnBuild=true',
        '-p:UapAppxPackageBuildMode=SideloadOnly',
        '-p:AppxBundle=Never',
        '-p:AppxPackageSigningEnabled=true',
        "-p:PackageCertificateThumbprint=$($certificate.Thumbprint)",
        "-p:AppxPackageDir=$packageOutputForMsbuild"
    )

    Write-Step 'Packaging and signing the Command Palette MSIX'
    Write-Host "Identity: $identityName`_$appxVersion`_x64" -ForegroundColor DarkGray
    Write-Host "Publisher: $publisher" -ForegroundColor DarkGray
    Write-Host "Target: $targetFramework ($runtimeIdentifier)" -ForegroundColor DarkGray
    Write-Host "Release output: $releaseDirectory" -ForegroundColor DarkGray
    & dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "The Command Palette MSIX build failed with dotnet exit code $LASTEXITCODE."
    }

    $generatedPackages = @(
        Get-ChildItem -LiteralPath $releaseDirectory -Filter '*.msix' -File -Recurse |
            Where-Object { $_.FullName -ne $packagePath }
    )
    if ($generatedPackages.Count -ne 1) {
        throw "Expected one generated x64 MSIX package, but found $($generatedPackages.Count) under '$releaseDirectory'."
    }
    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force
    }
    Move-Item -LiteralPath $generatedPackages[0].FullName -Destination $packagePath

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $entryNames = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        $requiredEntries = @(
            'AppxManifest.xml',
            'RunMind.CommandPalette.exe',
            'RunMind.CommandPalette.dll',
            'RunMind.CommandPalette.deps.json',
            'RunMind.CommandPalette.runtimeconfig.json',
            'Microsoft.CommandPalette.Extensions.dll',
            'Microsoft.CommandPalette.Extensions.Toolkit.dll',
            'Shmuelie.WinRTServer.dll',
            'Microsoft.Windows.SDK.NET.dll',
            'Microsoft.Web.WebView2.Core.dll',
            'Microsoft.Web.WebView2.Wpf.dll',
            'ChatWindow.html',
            'ArtifactTools.js',
            'Assets/Brand/Icon.ico',
            'Assets/Brand/Icon.svg',
            'Assets/StoreLogo.png',
            'Assets/Square150x150Logo.png',
            'Assets/Square44x44Logo.png',
            'Public/README.txt'
        )
        foreach ($entry in $requiredEntries) {
            if ($entryNames -notcontains $entry) {
                throw "The generated MSIX is missing '$entry'. The release package will not be accepted."
            }
        }

        $manifestEntry = $archive.GetEntry('AppxManifest.xml')
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
    $appExtension = $packageManifest.SelectSingleNode("//*[local-name()='AppExtension']")
    $exeServer = $packageManifest.SelectSingleNode("//*[local-name()='ComServer']/*[local-name()='ExeServer']")
    $comClass = $packageManifest.SelectSingleNode("//*[local-name()='ComServer']/*[local-name()='ExeServer']/*[local-name()='Class']")
    $createInstance = $packageManifest.SelectSingleNode("//*[local-name()='CreateInstance']")
    if ($null -eq $manifestIdentity -or $manifestIdentity.GetAttribute('Name') -ne $identityName -or $manifestIdentity.GetAttribute('Publisher') -ne $publisher) {
        throw 'The generated MSIX identity does not match the Command Palette project identity and publisher.'
    }
    if ($null -eq $appExtension -or $appExtension.GetAttribute('Name') -ne 'com.microsoft.commandpalette' -or $appExtension.GetAttribute('PublicFolder') -ne 'Public') {
        throw 'The generated MSIX does not declare the expected Command Palette app extension and Public folder.'
    }
    if ($null -eq $exeServer -or $exeServer.GetAttribute('Executable') -ne 'RunMind.CommandPalette.exe' -or $exeServer.GetAttribute('Arguments') -ne '-RegisterProcessAsComServer') {
        throw 'The generated MSIX does not register the expected out-of-process Command Palette COM server executable.'
    }
    if ($null -eq $comClass -or $null -eq $createInstance -or $comClass.GetAttribute('Id') -ne $createInstance.GetAttribute('ClassId')) {
        throw 'The generated MSIX COM server CLSID does not match its Command Palette CreateInstance CLSID.'
    }

    $extensionSource = Get-Content -LiteralPath (Join-Path $projectDirectory 'RunMindExtension.cs') -Raw
    $sourceGuid = [regex]::Match($extensionSource, '\[Guid\("(?<guid>[0-9A-Fa-f-]{36})"\)\]')
    if (-not $sourceGuid.Success -or $sourceGuid.Groups['guid'].Value -ne $comClass.GetAttribute('Id')) {
        throw 'The Command Palette COM CLSID in RunMindExtension.cs does not match the generated package manifest.'
    }

    Write-Step 'Exporting the public signing certificate for the separate installer'
    Export-Certificate -Cert $certificate -FilePath $certificatePath -Force | Out-Null

    if (-not (Test-Path -LiteralPath $certificatePath -PathType Leaf)) {
        throw 'The public package-signing certificate was not exported for the installer.'
    }

    Write-Host ''
    Write-Host '[SUCCESS] All three RunMind hosts were built and the Command Palette MSIX was packaged and signed.' -ForegroundColor Green
    Write-Host "MSIX: $packagePath" -ForegroundColor DarkGray
    Write-Host "Certificate: $certificatePath" -ForegroundColor DarkGray
    Write-Host 'The package was not installed. Run Install-RunMind-CommandPalette.bat separately.' -ForegroundColor Yellow
}
catch {
    Write-Host ''
    Write-Host '[ERROR] RunMind Command Palette release packaging failed.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
