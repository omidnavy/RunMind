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
