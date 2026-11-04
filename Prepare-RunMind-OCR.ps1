$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$VendorRoot = Join-Path $Root 'Vendor'
$PdfRoot = Join-Path $VendorRoot 'PdfJs'
$TesseractRoot = Join-Path $VendorRoot 'Tesseract'
$TesseractCoreRoot = Join-Path $TesseractRoot 'core'
$OcrDataRoot = Join-Path $Root 'OCR\tessdata'
$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("RunMind-OCR-" + [guid]::NewGuid().ToString('N'))

$PdfJsVersion = '6.5.438'
$TesseractVersion = '7.0.0'
$TesseractCoreVersion = '7.0.0'
$LanguageDataBase = 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/main'

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Test-NonEmptyFile([string]$Path, [long]$MinimumBytes = 1) {
    return (Test-Path -LiteralPath $Path -PathType Leaf) -and ((Get-Item -LiteralPath $Path).Length -ge $MinimumBytes)
}

function Test-OcrAssetsReady {
    $requiredFiles = @(
        (Join-Path $PdfRoot 'build\pdf.mjs'),
        (Join-Path $PdfRoot 'build\pdf.worker.mjs'),
        (Join-Path $TesseractRoot 'tesseract.min.js'),
        (Join-Path $TesseractRoot 'worker.min.js'),
        (Join-Path $TesseractCoreRoot 'tesseract-core.wasm.js'),
        (Join-Path $TesseractCoreRoot 'tesseract-core-simd.wasm.js'),
        (Join-Path $TesseractCoreRoot 'tesseract-core-lstm.wasm.js'),
        (Join-Path $TesseractCoreRoot 'tesseract-core-simd-lstm.wasm.js'),
        (Join-Path $TesseractCoreRoot 'tesseract-core-simd-lstm.wasm'),
        (Join-Path $TesseractCoreRoot 'tesseract-core-relaxedsimd-lstm.wasm.js'),
        (Join-Path $TesseractCoreRoot 'tesseract-core-relaxedsimd-lstm.wasm'),
        (Join-Path $OcrDataRoot 'eng.traineddata'),
        (Join-Path $OcrDataRoot 'fas.traineddata')
    )
    foreach ($file in $requiredFiles) {
        if (-not (Test-NonEmptyFile -Path $file -MinimumBytes 128)) { return $false }
    }
    foreach ($directory in @('cmaps', 'iccs', 'image_decoders', 'standard_fonts', 'wasm')) {
        $path = Join-Path $PdfRoot $directory
        if (-not (Test-Path -LiteralPath $path -PathType Container)) { return $false }
        if (-not (Get-ChildItem -LiteralPath $path -File -Recurse | Select-Object -First 1)) { return $false }
    }
    foreach ($language in @('eng', 'fas')) {
        if (-not (Test-NonEmptyFile -Path (Join-Path $OcrDataRoot "${language}.traineddata") -MinimumBytes 200000)) { return $false }
    }
    return $true
}

function Invoke-Download([string]$Uri, [string]$Destination) {
    $directory = Split-Path -Parent $Destination
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    Write-Host "  Downloading $([System.IO.Path]::GetFileName($Destination))" -ForegroundColor DarkGray
    Invoke-WebRequest -Uri $Uri -OutFile $Destination -UseBasicParsing
    if (-not (Test-NonEmptyFile -Path $Destination -MinimumBytes 128)) {
        throw "Download returned an empty or suspiciously small file: $Uri"
    }
}

function Expand-NpmPackage([string]$Name, [string]$Version) {
    $archive = Join-Path $TempRoot "$Name-$Version.tgz"
    $extract = Join-Path $TempRoot "$Name-$Version"
    New-Item -ItemType Directory -Path $extract -Force | Out-Null
    $encodedName = [uri]::EscapeDataString($Name)
    $url = "https://registry.npmjs.org/$encodedName/-/$Name-$Version.tgz"
    Invoke-Download -Uri $url -Destination $archive
    & tar.exe -xzf $archive -C $extract --strip-components=1
    if ($LASTEXITCODE -ne 0) { throw "Could not unpack npm package $Name@$Version (tar exit $LASTEXITCODE)." }
    return $extract
}

if (Test-OcrAssetsReady) {
    Write-Host 'RunMind PDF/OCR resources are already present. Skipping downloads; this build can proceed offline.' -ForegroundColor Green
    exit 0
}

try {
    if (-not (Get-Command tar.exe -ErrorAction SilentlyContinue)) {
        throw 'Windows tar.exe was not found. Use a supported Windows 10/11 installation with the built-in tar utility.'
    }
    New-Item -ItemType Directory -Path $TempRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $PdfRoot -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $PdfRoot 'build') -Force | Out-Null
    New-Item -ItemType Directory -Path $TesseractRoot, $TesseractCoreRoot, $OcrDataRoot -Force | Out-Null

    Write-Host "Preparing PDF.js $PdfJsVersion..." -ForegroundColor Cyan
    $pdfPackage = Expand-NpmPackage -Name 'pdfjs-dist' -Version $PdfJsVersion
    $pdfBuild = Join-Path $pdfPackage 'build'
    foreach ($file in @('pdf.mjs', 'pdf.worker.mjs')) {
        $source = Join-Path $pdfBuild $file
        if (-not (Test-NonEmptyFile -Path $source)) { throw "The pinned PDF.js package is missing build\$file." }
        Copy-Item -LiteralPath $source -Destination (Join-Path (Join-Path $PdfRoot 'build') $file) -Force
    }
    foreach ($directory in @('cmaps', 'iccs', 'image_decoders', 'standard_fonts', 'wasm')) {
        $source = Join-Path $pdfPackage $directory
        if (-not (Test-Path -LiteralPath $source -PathType Container)) {
            $source = Join-Path (Join-Path $pdfPackage 'web') $directory
        }
        if (Test-Path -LiteralPath $source -PathType Container) {
            $target = Join-Path $PdfRoot $directory
            New-Item -ItemType Directory -Path $target -Force | Out-Null
            Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $target -Recurse -Force
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $PdfRoot 'cmaps'))) { throw 'PDF.js CMap resources were not found in the pinned package.' }
    if (-not (Test-Path -LiteralPath (Join-Path $PdfRoot 'standard_fonts'))) { throw 'PDF.js standard font resources were not found in the pinned package.' }
    $pdfLicense = Join-Path $pdfPackage 'LICENSE'
    if (Test-Path -LiteralPath $pdfLicense) { Copy-Item -LiteralPath $pdfLicense -Destination (Join-Path $PdfRoot 'LICENSE') -Force }

    Write-Host "Preparing Tesseract.js $TesseractVersion and core $TesseractCoreVersion..." -ForegroundColor Cyan
    $tesseractPackage = Expand-NpmPackage -Name 'tesseract.js' -Version $TesseractVersion
    foreach ($file in @('tesseract.min.js', 'worker.min.js')) {
        $source = Join-Path (Join-Path $tesseractPackage 'dist') $file
        if (-not (Test-NonEmptyFile -Path $source)) { throw "The pinned Tesseract.js package is missing dist\$file." }
        Copy-Item -LiteralPath $source -Destination (Join-Path $TesseractRoot $file) -Force
    }
    $tesseractLicense = Join-Path $tesseractPackage 'LICENSE'
    if (Test-Path -LiteralPath $tesseractLicense) { Copy-Item -LiteralPath $tesseractLicense -Destination (Join-Path $TesseractRoot 'LICENSE') -Force }

    $corePackage = Expand-NpmPackage -Name 'tesseract.js-core' -Version $TesseractCoreVersion
    $coreFiles = Get-ChildItem -LiteralPath $corePackage -Recurse -File | Where-Object { $_.Name -match '^tesseract-core.*\.(js|wasm)$' }
    if (-not $coreFiles) { throw 'No Tesseract.js core runtime files were found in the pinned package.' }
    foreach ($file in $coreFiles) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $TesseractCoreRoot $file.Name) -Force }
    $coreLicense = Join-Path $corePackage 'LICENSE'
    if (Test-Path -LiteralPath $coreLicense) { Copy-Item -LiteralPath $coreLicense -Destination (Join-Path $TesseractCoreRoot 'LICENSE') -Force }

    Write-Host 'Preparing bundled English and Persian OCR data...' -ForegroundColor Cyan
    foreach ($language in @('eng', 'fas')) {
        $destination = Join-Path $OcrDataRoot "${language}.traineddata"
        Invoke-Download -Uri "$LanguageDataBase/$language.traineddata" -Destination $destination
        if ((Get-Item -LiteralPath $destination).Length -lt 200000) {
            throw "The downloaded $language OCR model is too small to be a valid tessdata_fast model."
        }
        $stream = [System.IO.File]::OpenRead($destination)
        try {
            $header = New-Object byte[] 24
            $read = $stream.Read($header, 0, $header.Length)
            $headerText = [System.Text.Encoding]::ASCII.GetString($header, 0, $read)
            if ($headerText.StartsWith('version https://git-lfs')) { throw "The $language download is a Git LFS pointer, not the OCR model." }
        }
        finally { $stream.Dispose() }
    }

    $manifest = [ordered]@{
        pdfjs = $PdfJsVersion
        tesseractJs = $TesseractVersion
        tesseractJsCore = $TesseractCoreVersion
        languageModels = [ordered]@{ eng = 'tessdata_fast'; fas = 'tessdata_fast' }
        preparedAtUtc = [DateTime]::UtcNow.ToString('o')
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $VendorRoot 'runmind-ocr-versions.json') -Encoding UTF8

    if (-not (Test-OcrAssetsReady)) { throw 'The downloaded OCR resources did not pass the final completeness check.' }
    Write-Host ''
    Write-Host 'PDF.js, Tesseract.js, English and Persian OCR resources are ready.' -ForegroundColor Green
    Write-Host 'They will be copied into both RunMind host outputs and used locally at runtime.' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host 'Could not prepare the PDF/OCR resources.' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host 'The first preparation needs internet access. Once Vendor\ and OCR\tessdata are populated, later builds can run offline.' -ForegroundColor Yellow
    exit 1
}
finally {
    if (Test-Path -LiteralPath $TempRoot) { Remove-Item -LiteralPath $TempRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
