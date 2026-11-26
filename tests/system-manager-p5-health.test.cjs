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
const application = read('Shared/ChatWindow/scripts/application.js');
const html = read('Shared/ChatWindow.html');
const runtime = read('Shared/ChatWindow/scripts/agent/runtime.js');

test('P5 health-history storage is disabled by default and included in settings load/save', () => {
  assert.match(native, /\["healthHistory"\] = false/);
  assert.match(native, /"targetedNetworkDiagnostics", "healthHistory"/);
  assert.match(core, /healthHistory:read\('systemManagerHealthHistory'\)/);
  assert.match(core, /healthHistory:'systemManagerHealthHistory'/);
  assert.match(application, /systemManagerHealthHistory/);
  assert.match(html, /id="systemManagerHealthHistory"/);
  assert.match(html, /opt-in; last 30 summaries/);
});

test('service inspection includes startup, dependency, failure/recovery and trigger configuration', () => {
  assert.match(native, /RunServiceControlAsync\(new\[\] \{ "qfailure", service \}/);
  assert.match(native, /RunServiceControlAsync\(new\[\] \{ "qfailureflag", service \}/);
  assert.match(native, /RunServiceControlAsync\(new\[\] \{ "qtriggerinfo", service \}/);
  assert.match(native, /"dependencies"\)/);
  assert.match(native, /maxDepth = 5, maxNodes = 50/);
  assert.match(native, /new ServiceDependencyBudget\(50\)/);
  assert.match(native, /ParseServiceConfiguration\(config\.output\)/);
  assert.match(native, /startupMode/);
  assert.match(native, /serviceAccount = ParseServiceControlField/);
  assert.match(native, /depth > 5/);
  assert.match(native, /visited\.Add\(service\)/);
  assert.match(native, /dependency\.StartsWith\('\+'\)/);
  assert.match(providers, /'dependencies','start','stop','restart'/);
  assert.match(tools, /\['list','inspect','config','dependents','dependencies'\]\.includes\(action\)/);
});

test('approved Windows service state changes retain mandatory native confirmation and final-state verification', () => {
  assert.match(native, /RunMind · \{action\} Windows service[\s\S]{0,300}alwaysAsk: true/);
  assert.match(native, /restartCycleCompleted/);
  assert.match(native, /actualState = ParseServiceState\(state\.output\)/);
  assert.match(native, /var verified = actualState == desiredState && restartCycleCompleted/);
  assert.match(native, /success = verified, accepted, completed = true, verified/);
  assert.match(runtime, /Service start\/stop\/restart always require fresh native confirmation and verified state/);
});

test('crash and service investigation includes broader SCM failures and recurrence groups', () => {
  for (const id of ['7000','7001','7023','7024','7031','7034','7038','7043']) assert.ok(native.includes(id), `missing SCM event ${id}`);
  assert.match(native, /recurringServiceFailures/);
  assert.match(native, /failedDependency/);
  assert.match(native, /repeated-service-failures/);
  assert.match(native, /recurringApplicationCrashes/);
  assert.match(native, /EndsWith\("\|\|", StringComparison\.Ordinal\)/);
  assert.match(native, /recurringNetworkFailures/);
  assert.match(native, /item\.occurrences >= 3/);
});

test('health report is native-dispatched, read-only, capability-gated and exposed to Agent sessions', () => {
  assert.match(bridge, /"systemManager\.healthReport" => SystemManagerHealthReportAsync/);
  assert.match(tools, /system_manager_health_report/);
  assert.match(tools, /callAgentHost\('systemManager\.healthReport'/);
  assert.match(providers, /addSystemTool\('system_manager_health_report'/);
  assert.match(providers, /\['systemInformation','serviceInformation','networkInformation','eventLogInformation','crashInvestigation','resourceMonitoring','storageHealth'\]\.some\(capOn\)/);
  assert.doesNotMatch(providers, /'storageHealth','targetedNetworkDiagnostics'\]\.some\(capOn\).*system_manager_health_report/);
  assert.match(native, /var canCollect = new\[\] \{ "systemInformation", "serviceInformation", "networkInformation", "eventLogInformation", "crashInvestigation", "resourceMonitoring", "storageHealth" \}\.Any\(Enabled\)/);
  assert.match(native, /on-demand evidence report/);
  assert.match(native, /No unsolicited remote probe or port scan/);
  assert.match(runtime, /runs on demand and queries only individually enabled capabilities/);
});

test('health correlation is time-bucketed and explicitly avoids causal claims', () => {
  assert.match(native, /TimeSpan\.FromMinutes\(5\)/);
  assert.match(native, /temporal proximity is not proof of causality/);
  assert.match(native, /does not infer root cause, network causality, or service-to-process causality/);
  assert.match(native, /resourceEventCorrelation/);
  assert.match(native, /resourceSampleStartUtc/);
  assert.match(native, /nearbyEventCount/);
  assert.match(native, /nearbyResourceEvents/);
  assert.match(native, /nearbyEvents = nearbyResourceEvents/);
  assert.match(native, /time overlap does not attribute resource pressure/);
  assert.match(native, /repeated-service-failures/);
  assert.match(native, /repeated-application-crashes/);
  assert.match(native, /repeated-network-failures/);
});

test('health history is opt-in, local, bounded, symlink-safe and contains summary data only', () => {
  assert.match(native, /SystemManagerHealthHistoryPath.*health-history\.json/);
  assert.match(native, /512 \* 1024/);
  assert.match(native, /snapshots\.Count > 30/);
  assert.match(native, /IsReparsePoint\(SystemManagerHealthHistoryPath\)/);
  assert.match(native, /EnsureNoReparseAncestors\(SystemManagerRoot\)/);
  assert.match(native, /SystemManagerHealthHistorySync/);
  assert.match(native, /SystemManagerHealthHistoryMutexName/);
  assert.match(native, /mutex\.WaitOne\(TimeSpan\.FromSeconds\(5\)\)/);
  assert.match(native, /issueKeys/);
  assert.match(native, /serviceStates/);
  assert.match(native, /does not persist raw Event Log messages, executable paths, connection endpoints or process command lines/);
  assert.match(native, /Historical comparisons compare on-demand snapshots only/);
  assert.doesNotMatch(native, /SystemManagerHealthReportAsync[\s\S]{0,15000}Task\.Run\(.*health/i);
});

test('historical comparison covers CPU, memory, disk, issue counts and service state changes', () => {
  assert.match(native, /BuildHealthComparison/);
  assert.match(native, /cpuPercentPoints/);
  assert.match(native, /memoryPercentPoints/);
  assert.match(native, /diskQueue/);
  assert.match(native, /changedServiceStates/);
  assert.match(native, /previousSnapshotUtc/);
});

test('network inventory includes process-owned TCP connections and UDP endpoints with bounded counts', () => {
  assert.match(native, /netstat\.Output\.Split/);
  assert.match(native, /new NetworkEndpoint\(local, remote, state, pid, processName, executablePath, startedAtUtc\)/);
  assert.match(native, /if \(state\.Equals\(\"LISTENING\", StringComparison\.OrdinalIgnoreCase\)\)[\s\S]{0,220}else if \(activeConnections\.Count < 200\) activeConnections\.Add\(endpoint\)/);
  assert.match(native, /activeTcpConnections = activeConnections/);
  assert.match(native, /udpEndpoints/);
  assert.match(native, /udpEndpointCount/);
  assert.match(native, /tcpRowsProcessed > 500/);
  assert.match(native, /activeConnections\.Count < 200/);
  assert.match(native, /udpEndpoints\.Count >= 200/);
  assert.match(native, /processExecutableIdentityEnabled = includeExecutableIdentity/);
});

test('TCP listener conflict reporting is limited to overlapping local bindings and labelled as possible', () => {
  assert.match(native, /FindPotentialTcpListenerConflicts/);
  assert.match(native, /classification = "possible-listener-conflict"/);
  assert.match(native, /dualStackWildcardOverlap/);
  assert.match(native, /socket reuse and Windows dual-stack behavior can allow legitimate coexistence/i);
  assert.match(native, /possibleTcpListenerConflicts/);
  assert.match(providers, /addSystemTool\('system_manager_find_port_owner'/);
  assert.match(providers, /specific local TCP\/UDP port/);
});

test('resource telemetry includes system memory, process private/working memory and supported disk I/O', () => {
  assert.match(native, /GlobalMemoryStatusEx/);
  assert.match(native, /memoryLoadPercent = status\.MemoryLoad/);
  assert.match(native, /Win32_PerfFormattedData_PerfDisk_PhysicalDisk/);
  assert.match(native, /averageQueueLength/);
  assert.match(native, /PrivateMemorySize64/);
  assert.match(native, /topWorkingSetProcesses/);
  assert.match(native, /topPrivateMemoryProcesses/);
  assert.match(native, /cpuReadings\.Average\(\)/);
  assert.match(native, /totalProcessPrivateMemoryBytes/);
  assert.match(native, /duration is < 1 or > 60/);
  assert.match(native, /Disk I\/O telemetry depends on the Windows performance CIM provider/);
});

test('health report carries resource hotspots and disk/storage coverage without background monitoring', () => {
  assert.match(native, /resourceHotspots/);
  assert.match(native, /ProcessHotspotEvidence/);
  assert.match(native, /Top sampled processes:/);
  assert.match(native, /cpuProcessEvidence/);
  assert.match(native, /memoryProcessEvidence/);
  assert.match(native, /topCpuProcesses/);
  assert.match(native, /topWorkingSetProcesses/);
  assert.match(native, /Finite CPU, physical memory, process and supported disk-I\/O sampling/);
  assert.match(native, /not a continuous monitor/);
  assert.match(runtime, /not continuous monitoring/);
  assert.match(runtime, /remote probes, port scans, or automatic remediation/);
});

test('targeted DNS, route and TCP diagnostics stay user-directed and bounded', () => {
  assert.match(native, /Dns\.GetHostAddressesAsync\(host, dnsTimeout\.Token\)/);
  assert.match(native, /Find-NetRoute -RemoteIPAddress/);
  assert.match(native, /client\.ConnectAsync\(address, port, connectTimeout\.Token\)/);
  assert.match(native, /ConfirmSystemManagerAction\("targetedNetworkDiagnostics"/);
  assert.match(runtime, /For explicit DNS\/routing\/TCP failure diagnosis, ask for or use the user-selected hostname\/IP/);
  assert.match(runtime, /never probe arbitrary destinations from a general report/);
});
