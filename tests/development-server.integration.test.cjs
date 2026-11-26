'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');

const root = path.resolve(__dirname, '..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const bridge = read('Shared/DevelopmentServerManager.cs');
const dispatch = read('Shared/AgentHostBridge.cs');
const tools = read('Shared/ChatWindow/scripts/agent/tools.js');
const providers = read('Shared/ChatWindow/scripts/providers.js');
const nativeDebugger = read('Shared/NodeDebuggerBridge.cs');
const webViewBootstrapper = read('Shared/WebView2RuntimeBootstrapper.cs');
const standaloneWindow = read('RunMind.Standalone/MainWindow.xaml.cs');
const powerToysHost = read('Community.PowerToys.Run.Plugin.RunMind/Main.cs');

test('server manager is dispatched through the existing native Agent bridge', () => {
  assert.match(dispatch, /"developmentServer\.manage"\s*=>\s*DevelopmentServerManageAsync/);
  assert.match(dispatch, /"process\.workspace\.status"\s*=>\s*Task\.FromResult\(ProcessWorkspaceStatus/);
  assert.match(tools, /callAgentHost\('developmentServer\.manage'/);
});

test('all three hosts compile and deploy the shared native bridge source', () => {
  for (const project of [
    'RunMind.Standalone/RunMind.Standalone.csproj',
    'Community.PowerToys.Run.Plugin.RunMind/Community.PowerToys.Run.Plugin.RunMind.csproj',
    'RunMind.CommandPalette/RunMind.CommandPalette.csproj',
  ]) {
    assert.ok(fs.existsSync(path.join(root, project)), `${project} exists`);
    assert.match(fs.readFileSync(path.join(root, project), 'utf8'), /DevelopmentServerManager\.cs/);
  }
  assert.ok(fs.existsSync(path.join(root, 'Shared/DevelopmentServerManager.cs')));
});

test('tool schema exposes the complete lifecycle without arbitrary command input', () => {
  assert.match(providers, /name: 'development_server_manage'/);
  for (const action of ['discover', 'start', 'list', 'logs', 'health', 'stop']) assert.match(providers, new RegExp(`'${action}'`));
  assert.doesNotMatch(providers.slice(providers.indexOf("name: 'development_server_manage'"), providers.indexOf("name: 'api_test_run'")), /command_line|shell_script|executable:/);
});

test('project startup is resolved only from an existing package.json script', () => {
  assert.match(bridge, /Path\.Combine\(working, "package\.json"\)/);
  assert.match(bridge, /TryGetProperty\("scripts"/);
  assert.match(bridge, /TryGetProperty\(script, out var command\)/);
  assert.match(bridge, /string\.Equals\(actualCommand, approvedCommand, StringComparison\.Ordinal\)/);
  assert.match(bridge, /changed after its approval preview/);
  assert.match(tools, /payload\.expected_command = String\(selectedScript\.command/);
  assert.match(bridge, /script\.Any\(ch => !\(char\.IsLetterOrDigit\(ch\) \|\| ch is '-' or '_' or ':' or '\.'\)\)/);
  assert.doesNotMatch(bridge, /ProcessStartInfo\([^\n]*ReadString\(args, "command/);
});

test('discovery recommends conventional scripts without assuming they exist', () => {
  assert.match(bridge, /new\[\] \{ "dev", "start", "serve" \}/);
  assert.match(bridge, /suggested_script = suggested/);
  const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-server-discovery-'));
  try {
    fs.writeFileSync(path.join(temp, 'package.json'), JSON.stringify({ scripts: { build: 'vite build', dev: 'vite --host 127.0.0.1' } }));
    const manifest = JSON.parse(fs.readFileSync(path.join(temp, 'package.json'), 'utf8'));
    assert.deepEqual(Object.keys(manifest.scripts), ['build', 'dev']);
    assert.equal(manifest.scripts.dev, 'vite --host 127.0.0.1');
  } finally { fs.rmSync(temp, { recursive: true, force: true }); }
});

test('startup and termination both require fresh approval', () => {
  assert.match(tools, /title: 'Start this development server\?'/);
  assert.match(tools, /title: 'Stop this RunMind development server\?'/);
  assert.match(tools, /alwaysAsk: true/);
  assert.match(tools, /deepResearchEnabled \|\| taskContext\?\.deepResearch \|\| taskContext\?\.subAgentDepth/);
});

test('readiness and health probes are restricted to loopback HTTP endpoints', () => {
  assert.match(bridge, /uri\.Scheme is not \("http" or "https"\)/);
  assert.match(bridge, /uri\.IsLoopback \|\| uri\.Host\.Equals\("localhost"/);
  assert.match(bridge, /DevelopmentHealthClient\.GetAsync/);
  assert.match(bridge, /readiness_timeout_seconds/);
});

test('port conflicts are detected without terminating the process using the port', () => {
  assert.match(bridge, /new TcpListener\(IPAddress\.Loopback, parsedPort\)/);
  assert.match(bridge, /Port \{parsedPort\} is already in use/);
  assert.match(bridge, /EADDRINUSE/);
  assert.match(bridge, /RunMind did not stop or modify the process using it/);
});

test('multiple servers and output capture are bounded', () => {
  assert.match(bridge, /MaximumDevelopmentServers = 8/);
  assert.match(bridge, /MaximumServerLogCharacters = 48 \* 1024/);
  assert.match(bridge, /MaximumServerLogCharacters/);
  assert.match(bridge, /TakeLast\(120\)/);
});

test('only a process session created and retained by this host can be terminated', () => {
  assert.match(bridge, /DevelopmentServers\.TryGetValue\(id, out var session\)/);
  assert.match(bridge, /session\.Process\.Kill\(true\)/);
  assert.match(bridge, /Only servers started by this host can be stopped/);
  assert.match(tools, /process tree only if this host originally launched and still owns the recorded server session/);
  assert.doesNotMatch(bridge, /Process\.GetProcessById\([^\n]*\)\.Kill/);
});

test('the lifecycle is backed by a real owned Process with stdout and stderr streams', () => {
  assert.match(bridge, /new Process \{ StartInfo = start, EnableRaisingEvents = true \}/);
  assert.match(bridge, /BeginOutputReadLine\(\)/);
  assert.match(bridge, /BeginErrorReadLine\(\)/);
  assert.match(bridge, /process\.Exited \+=/);
});

test('project script execution uses Node/npm arguments rather than a user-provided shell fragment', () => {
  assert.match(bridge, /FindExecutable\("node\.exe"\)/);
  assert.match(bridge, /FindExecutable\("npm\.cmd"\)/);
  assert.match(bridge, /start\.ArgumentList\.Add\(npmCli\)/);
  assert.match(bridge, /start\.ArgumentList\.Add\("run"\)/);
  assert.match(bridge, /start\.ArgumentList\.Add\(script\)/);
});


test('System Manager timeout input uses shared field styling and settings tabs use four columns', () => {
  const html = read('Shared/ChatWindow.html');
  const css = read('Shared/ChatWindow/styles/chat.css');
  assert.match(html, /class="agent-settings-field setting-field"><span>Native command timeout/);
  assert.match(css, /\.agent-settings-tabs \{[^}]*grid-template-columns: repeat\(4, minmax\(0, 1fr\)\)/);
});


test('native workspace resolver handles both repository-root and selected-project-root paths', () => {
  assert.match(dispatch, /ResolveProcessWorkspacePath\(relative, "working_directory", requireDirectory: true\)/);
  assert.match(dispatch, /var maximumOverlap = Math\.Min\(rootSegments\.Length, relativeSegments\.Length\)/);
  assert.match(dispatch, /SequenceEqual\(relativeSegments\.Take\(overlap\), StringComparer\.OrdinalIgnoreCase\)/);
  assert.match(dispatch, /Select the package folder as the workspace and leave working_directory empty/);
  assert.match(nativeDebugger, /ResolveProcessWorkspacePath\(scriptPath, "script_path", requireDirectory: false\)/);
  assert.match(providers, /If the selected workspace is the fixture folder itself, pass server\.cjs/);

  const resolve = (workspace, relative, exists) => {
    const path = require('node:path').win32;
    const root = path.resolve(workspace);
    const direct = path.resolve(root, relative);
    if (direct === root || direct.startsWith(root + path.sep)) {
      if (exists(direct)) return direct;
    } else {
      throw new Error('path escaped workspace');
    }
    const rootParts = root.replace(/[\\/]+$/, '').split(/[\\/]+/);
    const relativeParts = relative.replace(/[\\/]+/g, path.sep).split(path.sep).filter(Boolean);
    for (let overlap = Math.min(rootParts.length, relativeParts.length); overlap > 0; overlap--) {
      const suffix = rootParts.slice(rootParts.length - overlap);
      if (suffix.every((part, index) => part.toLowerCase() === relativeParts[index].toLowerCase())) {
        const candidate = path.resolve(root, ...relativeParts.slice(overlap));
        if (candidate === root || candidate.startsWith(root + path.sep)) {
          if (exists(candidate)) return candidate;
        }
      }
    }
    throw new Error('path not found');
  };

  const fixture = path.join(root, 'test-fixtures', 'development-server');
  const winPath = require('node:path').win32;
  const absoluteFixture = winPath.resolve('C:\\RunMind\\test-fixtures\\development-server');
  const fixtureFile = winPath.join(absoluteFixture, 'server.cjs');
  const exists = candidate => [absoluteFixture, fixtureFile].some(expected => winPath.normalize(candidate).toLowerCase() === winPath.normalize(expected).toLowerCase());
  assert.equal(resolve(absoluteFixture, 'test-fixtures/development-server', exists), absoluteFixture);
  assert.equal(resolve(absoluteFixture, 'test-fixtures/development-server/server.cjs', exists), fixtureFile);
  assert.equal(resolve(absoluteFixture, 'server.cjs', exists), fixtureFile);
  assert.ok(fs.existsSync(path.join(fixture, 'server.cjs')));
});

test('API, Playwright, server management, and debugger all share the native workspace resolver', () => {
  assert.match(dispatch, /var working = GetProcessWorkingDirectory\(args\);/);
  assert.match(dispatch, /private string GetProcessWorkingDirectory\(JsonElement args\)/);
  assert.match(nativeDebugger, /var working = GetProcessWorkingDirectory\(args\);/);
  assert.match(dispatch, /"api\.test\.run"\s*=>\s*ApiTestRunAsync/);
  assert.match(dispatch, /"playwright\.run"\s*=>\s*PlaywrightRunAsync/);
  assert.match(dispatch, /"developmentServer\.manage"\s*=>\s*DevelopmentServerManageAsync/);
});

test('WebView2 environment and controller startup is serialized across host processes per user-data folder', () => {
  assert.match(webViewBootstrapper, /AcquireUserDataFolderInitializationLockAsync/);
  assert.match(webViewBootstrapper, /FileShare\.None/);
  assert.match(webViewBootstrapper, /SHA256\.HashData/);
  assert.match(standaloneWindow, /await using \(var initializationLock = await WebView2RuntimeBootstrapper\.AcquireUserDataFolderInitializationLockAsync\(profile\)\)/);
  assert.match(standaloneWindow, /await _webView\.EnsureCoreWebView2Async\(environment\);/);
  assert.match(powerToysHost, /AcquireUserDataFolderInitializationLockAsync\(userDataFolder\)/);
  assert.match(powerToysHost, /await webView\.EnsureCoreWebView2Async\(environment\);/);
  for (const project of [
    'RunMind.Standalone/RunMind.Standalone.csproj',
    'RunMind.CommandPalette/RunMind.CommandPalette.csproj',
    'Community.PowerToys.Run.Plugin.RunMind/Community.PowerToys.Run.Plugin.RunMind.csproj',
  ]) assert.match(read(project), /WebView2RuntimeBootstrapper\.cs/);
});
