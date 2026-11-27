'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const systemManager = read('Shared/SystemManager.cs');
const standalone = read('RunMind.Standalone/MainWindow.xaml.cs');
const nativePageReader = read('Shared/NativePageReader.cs');
const agentBridge = read('Shared/AgentHostBridge.cs');
const appearance = read('Shared/AppearancePreferences.cs');
const saveDialog = read('Shared/ConversationSaveDialog.cs');
const powerToys = read('Community.PowerToys.Run.Plugin.RunMind/Main.cs');
const bootstrapper = read('Shared/WebView2RuntimeBootstrapper.cs');
const chatWindow = read('Shared/ChatWindow.html');

test('fixed Windows diagnostics bound stdout and stderr while draining both streams concurrently', () => {
  const method = systemManager.slice(
    systemManager.indexOf('private static (int ExitCode, string Output) RunFixedDiagnosticCommand'),
    systemManager.indexOf('private static string[] ParseCsvDiagnosticRow')
  );
  assert.match(method, /ReadBoundedProcessOutputAsync\(process\.StandardOutput/);
  assert.match(method, /ReadBoundedProcessOutputAsync\(process\.StandardError/);
  assert.match(method, /process\.Kill\(entireProcessTree: true\)/);
  assert.match(method, /process\.WaitForExit\(timeoutMilliseconds\)/);
  assert.match(method, /WaitForProcessExit\(process\)/, 'zero retains the documented no-automatic-timeout behavior');
  assert.doesNotMatch(method, /ReadToEndAsync|ReadToEnd\(/);
  assert.doesNotMatch(systemManager, /StandardOutput\.ReadToEndAsync|StandardError\.ReadToEndAsync/);
});

test('Windows service inventory uses the bounded fixed-command path and preserves timeout settings', () => {
  const method = systemManager.slice(systemManager.indexOf('private static object ReadWindowsServices()'));
  assert.match(method, /RunFixedDiagnosticCommand\(scPath/);
  assert.match(method, /commandTimeoutSeconds/);
  assert.match(method, /result\.ExitCode == -2/);
  assert.match(method, /result\.Output/);
  assert.doesNotMatch(method, /ReadToEndAsync|WaitForProcessExit\(/);
});

test('Standalone WebView2 denies navigation by default and only allows the exact local chat page', () => {
  const navigation = standalone.slice(standalone.indexOf('core.NavigationStarting'), standalone.indexOf('core.NavigationCompleted'));
  assert.match(navigation, /args\.Cancel = true/);
  assert.match(navigation, /if \(IsTrustedChatPageUri\(uri\)\)/);
  assert.match(navigation, /args\.Cancel = false/);
  assert.match(navigation, /UriSchemeHttp/);
  assert.match(navigation, /UriSchemeHttps/);
  assert.match(navigation, /UriSchemeMailto/);
  assert.match(navigation, /uri\.UserInfo/);
  assert.match(navigation, /args\.Handled = true/);
});

test('Standalone validates WebView message origin before invoking any native message handler', () => {
  const handler = standalone.slice(standalone.indexOf('private void OnWebMessageReceived'), standalone.indexOf('private static bool IsSupportedConversation'));
  assert.match(handler, /if \(_closing \|\| !IsTrustedChatPageSource\(args\.Source\)\) return;/);
  const guardIndex = handler.indexOf('IsTrustedChatPageSource(args.Source)');
  for (const nativeHandler of ['TryHandleWebMessage', 'TryHandleMessage', 'ConversationSaveDialog.TryHandleMessage']) {
    assert.ok(handler.indexOf(nativeHandler) > guardIndex, `${nativeHandler} follows the trusted-origin check`);
  }
});

test('every native WebView message receiver enforces HTTPS localhost, the default port, no userinfo and no fragment', () => {
  for (const [name, source] of [
    ['Standalone', standalone],
    ['PowerToys Run', powerToys],
    ['Agent bridge', agentBridge],
    ['Native reader', nativePageReader],
    ['Appearance preferences', appearance],
    ['Conversation save dialog', saveDialog],
  ]) {
    assert.match(source, /UriSchemeHttps/);
    assert.match(source, /Host\.Equals\("localhost"/);
    assert.match(source, /IsDefaultPort/);
    assert.match(source, /string\.IsNullOrEmpty\(uri\.UserInfo\)/);
    assert.match(source, /string\.IsNullOrEmpty\(uri\.Fragment\)/);
    assert.ok(name.length > 0);
  }
});

test('WebView2 bootstrapper has finite gate, cross-process lock, and installer execution waits', () => {
  assert.match(bootstrapper, /InstallLockWaitTimeout = TimeSpan\.FromMinutes\(2\)/);
  assert.match(bootstrapper, /ProcessGate\.WaitAsync\(InstallLockWaitTimeout\)/);
  assert.match(bootstrapper, /CancellationTokenSource\(InstallLockWaitTimeout\)/);
  assert.match(bootstrapper, /InstallerExecutionTimeout = TimeSpan\.FromMinutes\(5\)/);
  assert.match(bootstrapper, /WaitForExitAsync\(installerTimeout\.Token\)/);
  assert.match(bootstrapper, /process\.Kill\(entireProcessTree: true\)/);
  assert.match(bootstrapper, /WaitAsync\(TimeSpan\.FromSeconds\(10\)\)/);
  assert.doesNotMatch(bootstrapper, /private static async Task<FileStream> AcquireInstallLockAsync[\s\S]*?while \(true\)[\s\S]*?catch \(IOException\)[\s\S]*?Task\.Delay\(300\)\.ConfigureAwait\(false\)/);
});

test('ChatWindow CSP no longer permits inline JavaScript execution', () => {
  assert.match(chatWindow, /script-src 'self' 'wasm-unsafe-eval'/);
  assert.doesNotMatch(chatWindow, /script-src[^>]*'unsafe-inline'/);
  assert.match(chatWindow, /object-src 'none'/);
  assert.match(chatWindow, /base-uri 'none'/);
  assert.match(chatWindow, /form-action 'none'/);
  assert.match(chatWindow, /<script src=/);
});

test('all four hosts publish the same 3.5.0 product version and package identities', () => {
  for (const project of [
    'RunMind.Standalone/RunMind.Standalone.csproj',
    'Community.PowerToys.Run.Plugin.RunMind/Community.PowerToys.Run.Plugin.RunMind.csproj',
    'RunMind.CommandPalette/RunMind.CommandPalette.csproj',
    'RunMind.Runner/RunMind.Runner.csproj'
  ]) assert.match(read(project), /<Version>3\.5\.0<\/Version>/, project);
  assert.match(read('Community.PowerToys.Run.Plugin.RunMind/plugin.json'), /"Version":\s*"3\.5\.0"/);
  assert.match(read('RunMind.CommandPalette/RunMind.CommandPalette.csproj'), /<AppxPackageVersion>3\.5\.0\.0<\/AppxPackageVersion>/);
  assert.match(read('RunMind.CommandPalette/Package.appxmanifest'), /Version="3\.5\.0\.0"/);
});
