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
const core = read('Shared/ChatWindow/scripts/core.js');
const html = read('Shared/ChatWindow.html');
const runtime = read('Shared/ChatWindow/scripts/agent/runtime.js');

const capabilities = [
  'eventLogInformation', 'crashInvestigation', 'resourceMonitoring',
  'scheduledTaskInformation', 'scheduledTaskManagement', 'storageHealth',
  'securityPosture', 'targetedNetworkDiagnostics'
];

test('P2 System Manager capabilities default to opt-in and persist through native settings', () => {
  for (const capability of capabilities) {
    assert.match(native, new RegExp(`\\[\\"${capability}\\"\\] = false`));
    assert.match(native, new RegExp(`\\"${capability}\\"`));
    assert.match(core, new RegExp(`${capability}:read\\('systemManager`));
    assert.match(core, new RegExp(`${capability}:'systemManager`));
  }
  assert.match(native, /private static void RequireSystemManagerCapability/);
  assert.match(native, /RequireSystemCapability\("diagnostics", capability\)/);
});

test('all seven P2 areas are exposed through tool schemas and native dispatch', () => {
  const pairs = [
    ['system_manager_read_event_log', 'systemManager.eventLog'],
    ['system_manager_investigate_crashes', 'systemManager.crashes'],
    ['system_manager_resource_monitor', 'systemManager.resources'],
    ['system_manager_scheduled_tasks', 'systemManager.scheduledTasks'],
    ['system_manager_storage_health', 'systemManager.storage'],
    ['system_manager_security_posture', 'systemManager.security'],
    ['system_manager_targeted_network_diagnostics', 'systemManager.networkProbe']
  ];
  for (const [tool, operation] of pairs) {
    assert.ok(providers.includes(tool), `${tool} missing from Agent tool catalog`);
    assert.ok(tools.includes(tool), `${tool} missing from Agent execution router`);
    assert.ok(bridge.includes(`"${operation}"`), `${operation} missing from native dispatcher`);
  }
});

test('PowerShell diagnostics execute fixed scripts and pass user values as Base64 JSON data', () => {
  assert.match(native, /private static JsonElement RunPowerShellJson\(string fixedBody, object request/);
  assert.match(native, /JsonSerializer\.Serialize\(request\)/);
  assert.match(native, /Convert\.ToBase64String\(Encoding\.UTF8\.GetBytes/);
  assert.match(native, /-EncodedCommand/);
  assert.match(native, /No user value is interpolated into executable PowerShell source/);
  assert.match(native, /RunFixedDiagnosticCommand\(powershell/);
});


test('native diagnostic runner drains stderr with the shared bounded output reader and keeps actionable failures', () => {
  assert.match(native, /ReadBoundedProcessOutputAsync\(process\.StandardError/);
  assert.match(bridge, /const int maximum = 512 \* 1024/);
  assert.match(native, /process\.ExitCode != 0 && !string\.IsNullOrWhiteSpace\(error\.text\)/);
});

test('Event Log investigation restricts logs, time span and result count', () => {
  assert.match(native, /logName is not \("Application" or "System" or "Setup"\)/);
  assert.match(native, /hours is < 1 or > 720/);
  assert.match(native, /maximum is < 1 or > 200/);
  assert.match(native, /Get-WinEvent -FilterHashtable/);
  assert.match(native, /-MaxEvents 2000/);
  assert.match(native, /FullyQualifiedErrorId -notmatch 'NoMatchingEventsFound'/);
  assert.match(native, /message=\(\[string\]\$_\.Message\)\.Substring/);
  assert.match(providers, /enum:\['Application','System','Setup'\]/);
});

test('crash investigation covers Windows Error Reporting, Application Error, .NET Runtime and SCM signals', () => {
  for (const eventId of ['1000','1001','1026','7031','7034']) assert.ok(native.includes(eventId));
  assert.match(native, /Faulting application name/);
  assert.match(native, /exception code/);
  assert.match(native, /recurringPatterns/);
  assert.match(providers, /system_manager_investigate_crashes/);
});

test('resource monitoring samples CPU/process memory over a finite bounded interval', () => {
  assert.match(native, /duration is < 1 or > 60/);
  assert.match(native, /interval is < 1 or > 10/);
  assert.match(native, /GetSystemTimes/);
  assert.match(native, /TotalProcessorTime/);
  assert.match(native, /await Task\.Delay\(delay, cancellationToken\)/);
  assert.match(native, /sampleCount/);
  assert.match(native, /processListTruncated/);
  assert.match(native, /StartedAtTicks <= 0/);
  assert.match(runtime, /finite \(maximum 60 seconds\)/);
});

test('Scheduled Task state changes require exact task identity, native confirmation and post-action verification', () => {
  assert.match(native, /action is not \("enable" or "disable" or "start" or "stop"\)/);
  assert.match(native, /taskPath\.StartsWith\('\\\\'\)/);
  assert.match(native, /taskName\.IndexOfAny/);
  assert.ok(native.includes(String.raw`taskName.IndexOfAny(new[] { '*', '?', '[', ']', '/', '\\', ':', '<', '>', '|', '"' })`));
  assert.match(native, /taskPath\.Contains\('\/'\)/);
  assert.match(native, /ReadBoundedExactString\(args, \"task_name\", 260\)/);
  assert.match(native, /ReadBoundedExactString\(args, \"task_path\", 260\)/);
  assert.match(native, /taskName\.Any\(char\.IsControl\)/);
  assert.match(native, /taskPath\.Any\(char\.IsControl\)/);
  assert.match(bridge, /text\.Length > maximum.*exact values are never truncated/);
  assert.match(native, /ConfirmSystemManagerAction\("scheduledTaskManagement"/);
  assert.match(native, /alwaysAsk: true/);
  for (const action of ['Enable-ScheduledTask','Disable-ScheduledTask','Start-ScheduledTask','Stop-ScheduledTask']) assert.ok(native.includes(action));
  assert.match(native, /var verified = outcome\.TryGetProperty/);
  assert.doesNotMatch(native, /Register-ScheduledTask|Unregister-ScheduledTask|Set-ScheduledTask/);
  assert.match(providers, /taskActions\.push\('enable','disable','start','stop'\)/);
});

test('storage health reports disk reliability and volume state without treating missing counters as healthy', () => {
  assert.match(native, /Get-PhysicalDisk/);
  assert.match(native, /Get-StorageReliabilityCounter/);
  assert.match(native, /Get-Volume/);
  assert.match(native, /reliabilityCountersAvailable/);
  assert.match(native, /Missing SMART\/reliability counters are reported as unavailable/);
});

test('security posture reads Defender, Firewall, BitLocker, Secure Boot, TPM and UAC separately', () => {
  for (const command of ['Get-MpComputerStatus','Get-NetFirewallProfile','Get-BitLockerVolume','Confirm-SecureBootUEFI','Get-Tpm','EnableLUA']) assert.ok(native.includes(command));
  assert.match(native, /Read-only security posture snapshot/);
  assert.match(providers, /system_manager_security_posture/);
});

test('targeted network diagnostics accept one host and optional port, with mandatory confirmation for TCP', () => {
  assert.match(native, /ReadBoundedExactString\(args, \"host\", 253\)/);
  assert.match(native, /host\.Any\(character => char\.IsWhiteSpace\(character\) \|\| char\.IsControl\(character\)\)/);
  assert.match(native, /Uri\.CheckHostName\(host\) == UriHostNameType\.Unknown/);
  assert.match(native, /port is < 1 or > 65535/);
  assert.match(native, /ConfirmSystemManagerAction\("targetedNetworkDiagnostics"/);
  assert.match(native, /Dns\.GetHostAddressesAsync\(host, dnsTimeout\.Token\)/);
  assert.match(native, /client\.ConnectAsync\(address, port, connectTimeout\.Token\)/);
  assert.match(native, /remainingSeconds = timeout - tcpTimer\.Elapsed\.TotalSeconds/);
  assert.match(native, /Find-NetRoute -RemoteIPAddress/);
  assert.match(native, /attemptedAddresses/);
  assert.match(runtime, /selected local route\/interface\/next hop when available/);
  assert.match(runtime, /no scans\/ranges/);
});

test('P2 System Manager capabilities are visible in settings and Agent instructions preserve scope', () => {
  for (const id of ['systemManagerEventLogInformation','systemManagerCrashInvestigation','systemManagerResourceMonitoring','systemManagerScheduledTaskInformation','systemManagerScheduledTaskManagement','systemManagerStorageHealth','systemManagerSecurityPosture','systemManagerTargetedNetworkDiagnostics']) assert.ok(html.includes(id));
  assert.match(runtime, /Windows System Manager P2 guidance/);
  assert.match(runtime, /no scans\/ranges/);
  assert.match(runtime, /never claim persistent monitoring/);
});
