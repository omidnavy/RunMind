const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.join(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const native = read('Shared/SystemManager.cs');
const bridge = read('Shared/AgentHostBridge.cs');
const tools = read('Shared/ChatWindow/scripts/agent/tools.js');
const providers = read('Shared/ChatWindow/scripts/providers.js');

test('process inventory exposes parent PID and gated diagnostics', () => {
  assert.match(native, /ReadProcessParentIds/);
  assert.match(native, /parentProcessId\s*=\s*parentIds/);
  for (const field of ['cpuSeconds', 'threadCount', 'handleCount', 'privateMemoryBytes', 'responding']) assert.ok(native.includes(field));
});

test('graceful process close is routed through native identity validation', () => {
  assert.match(bridge, /"systemManager\.process\.close"\s*=>\s*Task\.FromResult\(SystemManagerCloseProcess\(args\)\)/);
  assert.match(tools, /system_manager_close_process/);
  assert.match(providers, /system_manager_close_process/);
  const method = native.slice(native.indexOf('private static object SystemManagerCloseProcess'), native.indexOf('private static object SystemManagerTerminateProcess'));
  assert.match(method, /CloseMainWindow\(\)/);
  assert.doesNotMatch(method, /\.Kill\(/);
  assert.match(method, /expectedStartedAt/);
});

test('service restart and inspection operations are exposed with state verification', () => {
  assert.match(providers, /enum:\['list','inspect','config','dependents','start','stop','restart'\]/);
  assert.match(native, /"restart" or "config" or "dependents"/);
  assert.match(native, /ParseServiceState/);
  assert.match(native, /requestedState = desiredState, actualState/);
  assert.match(native, /ConfirmSystemManagerAction\("launchApplications"/);
});

test('container state changes are not classified as read-only and include inventory', () => {
  const readOnlySet = tools.slice(tools.indexOf('const readOnlyTools'), tools.indexOf('if (name.startsWith'));
  assert.doesNotMatch(readOnlySet, /system_manager_container_action/);
  for (const action of ['stats', 'images', 'volumes', 'networks', 'health']) assert.ok(providers.includes(`'${action}'`));
  assert.match(native, /var stateChanging = action is "start" or "stop" or "restart"/);
  assert.match(native, /inspect-based state check|Container state verified/);
});

test('diagnostics include uptime, drive capacity, gateways, and DNS servers', () => {
  assert.match(native, /uptimeMilliseconds/);
  assert.match(native, /AvailableFreeSpace/);
  assert.match(native, /gateways\s*=/);
  assert.match(native, /dnsServers\s*=/);
});

test('startup inventory is read-only and routed through native discovery capability', () => {
  assert.match(bridge, /"systemManager\.startup\.list"\s*=>\s*Task\.FromResult\(SystemManagerListStartupApplications/);
  assert.match(tools, /system_manager_list_startup_applications/);
  assert.match(providers, /system_manager_list_startup_applications/);
  assert.match(native, /RegistryHive\.CurrentUser/);
  assert.match(native, /RegistryHive\.LocalMachine/);
  assert.match(native, /RunOnce/);
  assert.match(native, /SpecialFolder\.CommonStartup/);
});

test('native state-changing operations expose accepted/completed/verified result dimensions', () => {
  assert.match(native, /success = verified, accepted, completed = true, verified/);
  assert.match(native, /success = exited, accepted = true, completed = exited, verified = exited/);
  assert.match(native, /requestedState = "exited", actualState/);
});

test('mixed container and service tools request read-only approval for inspection actions', () => {
  assert.match(tools, /Allow read-only container inspection/);
  assert.match(tools, /Allow read-only Windows service inspection/);
  assert.match(tools, /!\['start','stop','restart'\]\.includes\(action\)/);
  assert.match(tools, /\['list','inspect','config','dependents'\]\.includes\(action\)/);
});

test('service restart is only verified after an observed stop/start cycle', () => {
  assert.match(native, /var restartCycleCompleted = action != "restart"/);
  assert.match(native, /stopped = ParseServiceState\(state\.output\) == "STOPPED"/);
  assert.match(native, /restartCycleCompleted = start\.exitCode == 0/);
  assert.match(native, /actualState == desiredState && restartCycleCompleted/);
});


test('startup entries support confirmed reversible enable and disable for Run keys', () => {
  assert.match(bridge, /"systemManager\.startup\.set"\s*=>\s*Task\.FromResult\(SystemManagerSetStartupApplication/);
  assert.match(tools, /system_manager_set_startup_application/);
  assert.match(providers, /system_manager_set_startup_application/);
  assert.match(native, /DisabledStartupEntries/);
  assert.match(native, /alwaysAsk:\s*true/);
  assert.match(native, /RunOnce and Startup-folder entries remain inventory-only|RunOnce and Startup-folder entries are inventory-only/);
  assert.match(native, /requestedState = "disabled"/);
  assert.match(native, /requestedState = "enabled"/);
});

test('system diagnostics include bounded hardware, driver and listener ownership details', () => {
  assert.match(native, /driverquery\.exe/);
  assert.match(native, /Win32_Processor/);
  assert.match(native, /Win32_PhysicalMemory/);
  assert.match(native, /Win32_VideoController/);
  assert.match(native, /netstat\.exe/);
  assert.match(native, /processId = pid, processName/);
  assert.match(native, /RunFixedDiagnosticCommand/);
});


test('multi-file edits snapshot originals, back up every file before writes, and attempt rollback', () => {
  const editStart = tools.indexOf("case 'edit_files'");
  const editEnd = tools.indexOf("case 'restore_file_backup'", editStart);
  const method = tools.slice(editStart, editEnd);
  assert.ok(editStart >= 0 && editEnd > editStart);
  assert.match(method, /workspaceFilesMatchSnapshot/);
  assert.match(method, /createWorkspaceBackup/);
  assert.ok(method.indexOf('createWorkspaceBackup') < method.indexOf('createWritable'));
  assert.match(method, /catch \(rollbackError\)/);
  assert.match(method, /Rollback was incomplete/);
  assert.match(method, /Recovery backups were preserved/);
});

test('application operations are normalized into accepted, completed, verified and success dimensions', () => {
  assert.match(tools, /function normalizeSystemManagerOperationResult/);
  for (const field of ['success', 'accepted', 'completed', 'verified', 'action', 'target', 'message']) assert.match(tools, new RegExp(`${field}\\s*[,}:]`));
  assert.match(tools, /name\.startsWith\('system_manager_'\) \? normalizeSystemManagerOperationResult/);
});


test('AST refactoring tool is exposed preview-first and applies through approved file editing', () => {
  assert.match(tools, /case 'javascript_refactor'/);
  assert.match(tools, /projectIndexRefactorJavaScript/);
  assert.match(tools, /executeAgentToolInternal\('edit_file'/);
  assert.match(providers, /name: 'javascript_refactor'/);
  assert.match(providers, /apply=true routes through the existing approved edit_file flow/);
});

test('Windows validation script builds the complete three-host solution after Node tests', () => {
  const script = read('Validate-Phase1-Windows.ps1');
  assert.match(script, /node --test/);
  assert.match(script, /RunMind\.sln/);
  assert.match(script, /dotnet build/);
  assert.match(script, /Platform=\$Platform/);
});


test('System Manager result normalization recognizes adapter install, reset and removal outcomes', () => {
  assert.match(tools, /result\.installed === true/);
  assert.match(tools, /result\.reset === true/);
  assert.match(tools, /result\.removed === true/);
  assert.match(tools, /result\.installed === true/);
});


test('network diagnostics map TCP and UDP listeners to process identities', () => {
  assert.match(native, /new\[\] \{ "-ano", "-p", "tcp" \}/);
  assert.match(native, /new\[\] \{ "-ano", "-p", "udp" \}/);
  assert.match(native, /udpListeners/);
  assert.match(native, /tcpListeners/);
});
