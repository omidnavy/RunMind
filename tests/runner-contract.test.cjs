const test = require('node:test');
const assert = require('node:assert/strict');
const { readFile } = require('node:fs/promises');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const read = (relativePath) => readFile(path.join(root, relativePath), 'utf8');

test('Runner is a separate optional WPF executable with no Full Agent or WebView dependency', async () => {
  const project = await read('RunMind.Runner/RunMind.Runner.csproj');
  assert.match(project, /<OutputType>WinExe<\/OutputType>/);
  assert.match(project, /<UseWPF>true<\/UseWPF>/);
  assert.match(project, /<TargetFramework>net10\.0-windows/);
  assert.doesNotMatch(project, /AgentHostBridge|WorkspaceFileBridge|WebView2|MCP|Skills/i);
  assert.doesNotMatch(project, /PackageReference/);
});

test('Runner catalog is bounded to Start Menu shortcuts and rejects reparse traversal', async () => {
  const catalog = await read('RunMind.Runner/ApplicationCatalog.cs');
  assert.match(catalog, /MaximumShortcuts\s*=\s*3_500/);
  assert.match(catalog, /MaximumSearchResults\s*=\s*9/);
  assert.match(catalog, /SpecialFolder\.StartMenu/);
  assert.match(catalog, /SpecialFolder\.CommonStartMenu/);
  assert.match(catalog, /"\*\.lnk"/);
  assert.match(catalog, /AttributesToSkip\s*=\s*FileAttributes\.ReparsePoint/);
  assert.match(catalog, /MaxRecursionDepth\s*=\s*8/);
});

test('direct path and URL opening has explicit target restrictions', async () => {
  const commands = await read('RunMind.Runner/RunnerCommandService.cs');
  assert.match(commands, /UriSchemeHttp/);
  assert.match(commands, /UriSchemeHttps/);
  assert.match(commands, /Path\.IsPathRooted/);
  assert.match(commands, /BlockedDirectFileExtensions/);
  for (const extension of ['.exe', '.bat', '.cmd', '.ps1', '.vbs', '.js', '.msi', '.lnk', '.url', '.appref-ms']) {
    assert.ok(commands.includes(`"${extension}"`), `missing blocked extension ${extension}`);
  }
  assert.doesNotMatch(commands, /UseShellExecute\s*=\s*false[\s\S]{0,200}Arguments\s*=/);
  assert.doesNotMatch(commands, /cmd\.exe|powershell\.exe|pwsh\.exe/i);
});

test('Runner settings are current-user only and startup is opt-in', async () => {
  const settings = await read('RunMind.Runner/RunnerSettings.cs');
  assert.match(settings, /StartWithWindows\s*\{\s*get;\s*set;\s*\}/);
  assert.match(settings, /"Ctrl\+Alt\+Space"/);
  assert.match(settings, /Registry\.CurrentUser/);
  assert.match(settings, /Software\\Microsoft\\Windows\\CurrentVersion\\Run/);
  assert.match(settings, /--background/);
  assert.doesNotMatch(settings, /Registry\.LocalMachine/);
});

test('release and install workflow treats Runner as an independent optional component', async () => {
  const solution = await read('RunMind.sln');
  const build = await read('Build-Release-x64.bat');
  const installer = await read('Install-RunMind-Runner.ps1');
  const uninstaller = await read('Uninstall-RunMind-Runner.ps1');
  assert.match(solution, /RunMind\.Runner\\RunMind\.Runner\.csproj/);
  assert.match(build, /RunMind\.Runner\\RunMind\.Runner\.csproj/);
  assert.match(build, /Release\\Runner\\win-x64\\publish/);
  assert.match(installer, /LOCALAPPDATA/);
  assert.match(installer, /RunMind\.Runner\.exe/);
  assert.match(uninstaller, /RunMind\.Runner/);
  assert.match(uninstaller, /RunMind Standalone, PowerToys Run and Command Palette were not changed/);
});
