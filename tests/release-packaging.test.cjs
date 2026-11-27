'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');

test('release builder creates all four versioned GitHub host archives after successful builds', () => {
  const builder = read('Build-Release-x64.bat');
  const packager = read('Package-RunMind-GitHub-Releases.ps1');
  assert.match(builder, /Package-RunMind-GitHub-Releases\.ps1/);
  assert.match(builder, /\[9\/9\] Creating the four GitHub release ZIP archives/);
  assert.match(builder, /Release\\GitHub-Releases/);
  for (const host of ['standalone', 'runner', 'powertoysrun', 'commandpalette']) {
    assert.match(packager, new RegExp(`New-RunMindReleaseZip '${host}'`));
  }
  assert.match(packager, /\"\{0\}-\{1\}\.zip\" -f \$name, \$version/);
  assert.match(packager, /Get-ProjectVersion/);
  assert.match(packager, /Version mismatch/);
  assert.match(packager, /Package\.appxmanifest/);
  assert.match(packager, /manifestIdentity\.GetAttribute\('Version'\)/);
});

test('PowerToys Run release archive includes its installer and exact plugin output layout', () => {
  const packager = read('Package-RunMind-GitHub-Releases.ps1');
  assert.match(packager, /Copy-ReleaseTree 'Community\.PowerToys\.Run\.Plugin\.RunMind\\bin\\x64\\Release'/);
  assert.match(packager, /Copy-ReleaseFile 'Install-RunMind-PowerToys-Run\.bat'/);
});

test('Command Palette release archive is independently installable with its required scripts and MSIX identity source', () => {
  const packager = read('Package-RunMind-GitHub-Releases.ps1');
  for (const pathFragment of [
    "'Install-RunMind-CommandPalette.bat'",
    "'Deploy-RunMind-CommandPalette.ps1'",
    "'Configure-RunMind-CommandPalette-Alias.ps1'",
    "'RunMind.CommandPalette\\Package.appxmanifest'",
    "'Release\\CommandPalette\\x64'"
  ]) assert.ok(packager.includes(pathFragment), `Missing Command Palette release content: ${pathFragment}`);
});

test('Runner and Standalone archives contain the correct published outputs and Runner install scripts', () => {
  const packager = read('Package-RunMind-GitHub-Releases.ps1');
  assert.match(packager, /Copy-ReleaseTree 'Release\\Standalone\\win-x64\\publish'/);
  assert.match(packager, /Copy-ReleaseTree 'Release\\Runner\\win-x64\\publish'/);
  assert.match(packager, /Copy-ReleaseTree 'Release\\Standalone\\win-x64\\publish' 'Release\\Runner\\win-x64\\publish\\Standalone\\win-x64\\publish'/);
  assert.match(packager, /Copy-ReleaseFile 'Install-RunMind-Runner\.ps1'/);
  assert.match(packager, /Copy-ReleaseFile 'Uninstall-RunMind-Runner\.ps1'/);
});


test('Windows validation checks the four generated GitHub release ZIPs', () => {
  const validator = read('Validate-RunMind-Windows.ps1');
  assert.match(validator, /Release\\GitHub-Releases/);
  assert.match(validator, /foreach \(\$hostName in/);
  assert.doesNotMatch(validator, /foreach \(\$host in/);
  for (const host of ['standalone', 'runner', 'powertoysrun', 'commandpalette']) {
    assert.ok(validator.includes(`'${host}'`));
  }
});


test('Command Palette release installer reads identity from its packaged manifest, not a project file', () => {
  const packager = read('Package-RunMind-GitHub-Releases.ps1');
  const installer = read('Deploy-RunMind-CommandPalette.ps1');
  assert.match(packager, /Copy-ReleaseFile 'RunMind\.CommandPalette\\Package\.appxmanifest' 'Package\.appxmanifest'/);
  assert.doesNotMatch(packager, /Copy-ReleaseFile 'RunMind\.CommandPalette\\RunMind\.CommandPalette\.csproj'/);
  assert.match(installer, /Join-Path \$root 'Package\.appxmanifest'/);
  assert.match(installer, /Get-PackageIdentity/);
  assert.doesNotMatch(installer, /RunMind\.CommandPalette\\RunMind\.CommandPalette\.csproj/);
});

test('Runner release installs a private Standalone companion without replacing a separate Standalone install', () => {
  const installer = read('Install-RunMind-Runner.ps1');
  const locator = read('RunMind.Runner/StandaloneLocator.cs');
  assert.match(installer, /Join-Path \$source 'Standalone\\win-x64\\publish'/);
  assert.match(installer, /bundled chat host/i);
  assert.match(installer, /installedStandaloneDirectory/);
  assert.match(installer, /desktopShortcutPath/);
  assert.doesNotMatch(installer, /\$desktopShortcut\s*=/);
  assert.match(locator, /FindBundledStandalonePaths/);
  assert.match(locator, /AppContext\.BaseDirectory/);
  assert.match(locator, /\"Standalone\"/);
});
