const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.join(__dirname, '..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const native = read('Shared/SystemManager.cs');
const bridge = read('Shared/AgentHostBridge.cs');
const html = read('Shared/ChatWindow.html');
const application = read('Shared/ChatWindow/scripts/application.js');
const styles = read('Shared/ChatWindow/styles/chat.css');
const runtime = read('Shared/ChatWindow/scripts/agent/runtime.js');

test('Live Resource Intelligence is dispatched through the shared native bridge', () => {
  assert.match(bridge, /"systemManager\.liveResources\.start"\s*=>\s*Task\.Run\(\(\)\s*=>\s*SystemManagerLiveResourcesStart\(args\)/);
  assert.match(bridge, /"systemManager\.liveResources\.sample"\s*=>\s*Task\.Run\(\(\)\s*=>\s*SystemManagerLiveResourcesSample\(args\)/);
  assert.match(bridge, /"systemManager\.liveResources\.stop"\s*=>\s*Task\.Run\(\(\)\s*=>\s*SystemManagerLiveResourcesStop\(args\)/);
  for (const project of [
    'RunMind.Standalone/RunMind.Standalone.csproj',
    'RunMind.CommandPalette/RunMind.CommandPalette.csproj',
    'Community.PowerToys.Run.Plugin.RunMind/Community.PowerToys.Run.Plugin.RunMind.csproj'
  ]) assert.match(read(project), /Shared\\SystemManager\.cs/);
});

test('live sampling uses the System Manager master switch and keeps strict duration and interval bounds', () => {
  const start = native.slice(native.indexOf('private object SystemManagerLiveResourcesStart'), native.indexOf('private object SystemManagerLiveResourcesSample'));
  const sample = native.slice(native.indexOf('private object SystemManagerLiveResourcesSample'), native.indexOf('private object SystemManagerLiveResourcesStop'));
  const finiteAgentReport = native.slice(native.indexOf('private static async Task<object> SystemManagerSampleResourcesAsync'), native.indexOf('private static async Task<object> SystemManagerHealthReportAsync'));
  assert.match(start, /RequireSystemManagerEnabledForLiveMonitoring\(\)/);
  assert.match(sample, /RequireSystemManagerEnabledForLiveMonitoring\(\)/);
  assert.doesNotMatch(start + sample, /RequireSystemManagerCapability\("resourceMonitoring"\)/);
  assert.match(finiteAgentReport, /RequireSystemManagerCapability\("resourceMonitoring"\)/);
  assert.match(start, /duration is < 30 or > 900/);
  assert.match(start, /interval is not \(1 or 2 or 5\)/);
  assert.match(start, /already active in this RunMind window/);
  assert.match(start, /Stopwatch\.StartNew\(\)/);
  assert.match(sample, /session\.Clock\.Elapsed\.TotalSeconds >= session\.DurationSeconds/);
  assert.match(sample, /minimumInterval = Math\.Max\(0\.5, session\.IntervalSeconds \* 0\.7\)/);
});

test('native sample and stop require the exact active session ID', () => {
  const sample = native.slice(native.indexOf('private object SystemManagerLiveResourcesSample'), native.indexOf('private object SystemManagerLiveResourcesStop'));
  const stop = native.slice(native.indexOf('private object SystemManagerLiveResourcesStop'), native.indexOf('private void StopLiveResourceSessionForDispose'));
  assert.match(sample, /requestedId\.Length != 32 \|\| !requestedId\.All\(Uri\.IsHexDigit\)/);
  assert.match(sample, /string\.Equals\(session\.Id, requestedId, StringComparison\.Ordinal\)/);
  assert.match(stop, /requestedId\.Length != 32 \|\| !requestedId\.All\(Uri\.IsHexDigit\)/);
  assert.match(stop, /session-id-mismatch/);
  assert.match(stop, /alreadyStopped = true/);
});

test('host disposal clears volatile live resource session state', () => {
  assert.match(bridge, /private readonly object _liveResourceSync = new\(\)/);
  assert.match(bridge, /private LiveResourceSession\? _liveResourceSession/);
  assert.match(bridge, /StopLiveResourceSessionForDispose\(\)/);
  assert.match(native, /private void StopLiveResourceSessionForDispose\(\)[\s\S]*?lock \(_liveResourceSync\)[\s\S]*?session\?\.GpuTelemetry\?\.Dispose\(\)/);
  assert.match(native, /Volatile\.Read\(ref _disposed\)/);
});

test('host CPU, physical memory, process CPU/memory, network and process-I/O sources are explicit', () => {
  assert.match(native, /GetSystemTimes\(out var idle, out var kernel, out var user\)/);
  assert.match(native, /ReadSystemMemoryStatus\(\)/);
  assert.match(native, /GlobalMemoryStatusEx/);
  assert.match(native, /ReadResourceProcessSnapshot\(out var processListTruncated\)/);
  assert.match(native, /previous\.StartedAtTicks != current\.StartedAtTicks/);
  assert.match(native, /machineCpuSharePercent/);
  assert.match(native, /topMemory/);
  assert.match(native, /NetworkInterface\.GetAllNetworkInterfaces\(\)/);
  assert.match(native, /InterfaceFingerprint/);
  assert.match(native, /networkTopologyStable/);
  assert.match(native, /interface set changes/);
  assert.match(native, /GetProcessIoCounters\(process\.Handle, out var ioCounters\)/);
  assert.match(native, /must not be interpreted as physical disk throughput/);
  assert.match(native, /VPN, bridge or virtual networking configurations/);
});

test('GPU telemetry uses optional Windows PDH engine and adapter-memory counters', () => {
  assert.match(native, /PdhOpenQueryW/);
  assert.match(native, /PdhAddEnglishCounterW/);
  assert.ok(native.includes('\\\\GPU Engine(*)\\\\Utilization Percentage'));
  assert.ok(native.includes('\\\\GPU Adapter Memory(*)\\\\Dedicated Usage'));
  assert.ok(native.includes('\\\\GPU Adapter Memory(*)\\\\Shared Usage'));
  assert.match(native, /PdhGetFormattedCounterValue/);
  assert.match(native, /PdhFormatLarge \| PdhFormatNoScale/);
  assert.match(native, /driver does not expose a reliable total capacity/);
  assert.match(native, /GpuTelemetry\?\.Dispose\(\)/);
});

test('native telemetry remains bounded and does not persist samples', () => {
  const start = native.slice(native.indexOf('private object SystemManagerLiveResourcesStart'), native.indexOf('private object SystemManagerLiveResourcesSample'));
  const sample = native.slice(native.indexOf('private object SystemManagerLiveResourcesSample'), native.indexOf('private object SystemManagerLiveResourcesStop'));
  assert.match(start, /storage = "volatile-memory-only"/);
  assert.match(start, /not persisted/);
  assert.match(sample, /Take\(8\)/);
  assert.match(sample, /processEntries/);
  assert.match(sample, /OrderByDescending\(item => item\.WorkingSetBytes\)\.Take\(100\)/);
  assert.match(sample, /DistinctBy\(item => item\.ProcessId\)/);
  assert.match(sample, /first 1,000 processes/);
  assert.match(sample, /85% CPU, 90% memory/);
  assert.doesNotMatch(start + sample, /File\.WriteAllText|FileStream|Directory\.CreateDirectory|Registry\./);
});

test('dashboard has explicit Start/Stop, interval and duration controls, charts, process ranking and export', () => {
  for (const id of ['liveResourceStart','liveResourceStop','liveResourceDuration','liveResourceInterval','liveResourceCpuLine','liveResourceMemoryLine','liveResourceReceiveLine','liveResourceSendLine','liveResourceIoReadLine','liveResourceIoWriteLine','liveResourceGpuUtilization','liveResourceGpuMemory','liveResourceGpuUtilizationLine','liveResourceGpuDedicatedLine','liveResourceGpuSharedLine','liveResourceSortCpu','liveResourceSortMemory','liveResourceProcesses','liveResourceExport'])
    assert.match(html, new RegExp(`id="${id}"`));
  assert.match(html, /<option value="60">1 minute/);
  assert.match(html, /<option value="300" selected>5 minutes/);
  assert.match(html, /<option value="900">15 minutes/);
  assert.match(html, /<option value="1" selected>1 second/);
  assert.match(html, /<option value="2">2 seconds/);
  assert.match(html, /<option value="5">5 seconds/);
  assert.match(html, /Process I\/O transfer[\s\S]*?not disk-only/);
  assert.match(html, /Export CSV/);
  assert.match(html, /GPU utilization/);
  assert.match(html, /Dedicated GPU memory/);
  assert.match(application, /gpuUtilizationPercent/);
  assert.match(application, /gpuDedicatedBytes/);
  assert.match(application, /gpuSharedBytes/);
});

test('tab grid column counts match the actual Deep Research and System Manager tabs', () => {
  const systemManagerTabCount = (html.match(/data-system-manager-tab=/g) || []).length;
  const researchTabCount = (html.match(/data-research-view=/g) || []).length;
  assert.equal(systemManagerTabCount, 3);
  assert.equal(researchTabCount, 4);
  assert.match(styles, /\.system-manager-tabs\s*\{[^}]*grid-template-columns:\s*repeat\(3,/);
  assert.match(styles, /\.research-tabs\s*\{[^}]*grid-template-columns:\s*repeat\(4,/);
});

test('dashboard keeps at most 900 samples in WebView memory and never auto-starts monitoring', () => {
  assert.match(application, /const LIVE_RESOURCE_HISTORY_LIMIT = 900/);
  assert.match(application, /while \(liveResourceHistory\.length > LIVE_RESOURCE_HISTORY_LIMIT\) liveResourceHistory\.shift\(\)/);
  assert.match(application, /async function startLiveResourceMonitor\(\)/);
  assert.match(application, /liveResourceStart.*addEventListener\('click'/);
  assert.match(application, /liveResourceStop.*addEventListener\('click'/);
  assert.match(application, /liveResourceHistory\.length = 0/);
  assert.match(application, /function resetLiveResourceMonitorView\(\)/);
  assert.doesNotMatch(application, /setInterval\(\s*pollLiveResourceMonitor/);
  assert.doesNotMatch(application, /localStorage\.(?:setItem|getItem).*liveResource|indexedDB.*liveResource/i);
});

test('live monitoring survives tab changes and flyout closure but stops on explicit stop or page teardown', () => {
  assert.doesNotMatch(application, /tab !== 'monitor' && liveResourceSession/);
  assert.doesNotMatch(application, /systemManagerMenu[\s\S]*?!event\.target\.open && liveResourceSession/);
  assert.match(application, /window\.addEventListener\('pagehide'/);
  assert.match(application, /systemManager\.liveResources\.stop/);
  assert.doesNotMatch(application, /Monitoring stopped because the Live Monitor tab was left|Monitoring stopped because the System Manager panel was closed/);
  assert.match(html, /Closing this flyout or switching tabs does not stop an active session/);
});

test('charts and process ranking are rendered without an external chart library', () => {
  assert.match(application, /function liveResourceChartPoints/);
  assert.match(application, /setAttribute\('d', path\.trim\(\)\)/);
  assert.match(application, /liveResourceProcessSort === 'memory'/);
  assert.match(application, /liveResourceProcessFilter/);
  assert.match(application, /liveResourceSortMemory.*addEventListener\('click'/);
  assert.match(application, /new Blob\(\[rows\.join/);
  assert.match(styles, /\.live-resource-chart-card svg/);
  assert.match(styles, /@media \(prefers-reduced-motion: reduce\)/);
  assert.doesNotMatch(html, /(?:chart\.js|recharts|echarts|highcharts|d3\.min\.js|cdn\.jsdelivr\.net|unpkg\.com)/i);
});

test('Agent guidance distinguishes finite tool reports from UI-started live sessions', () => {
  assert.match(runtime, /system_manager_resource_monitor Agent tool remains finite \(maximum 60 seconds\)/);
  assert.match(runtime, /Live Resource Monitor tab/);
  assert.match(runtime, /not an Agent-controlled\/background monitor/);
});

test('Windows acceptance and implementation audit document the real measurement boundaries', () => {
  const acceptance = read('docs/P7-WINDOWS-ACCEPTANCE.md');
  const audit = read('docs/P7-IMPLEMENTATION-AUDIT.md');
  const guide = read('docs/LIVE-RESOURCE-INTELLIGENCE.md');
  assert.match(acceptance, /15-minute cap/);
  assert.match(acceptance, /wrong session ID/);
  assert.match(acceptance, /VPN\/virtual adapters/);
  assert.match(audit, /not physical-disk-only I\/O/i);
  assert.match(guide, /Samples are held in WebView memory only/);
  assert.match(guide, /not learned anomaly detection/);
});


test('Process Intelligence exposes a bounded selectable list with on-demand native details', () => {
  for (const id of [
    'liveResourceProcessSearch','liveResourceProcesses','liveResourceProcessDetails','liveResourceProcessTitle',
    'liveResourceProcessParent','liveResourceProcessStarted','liveResourceProcessOwner','liveResourceProcessSession',
    'liveResourceProcessSecurity','liveResourceProcessPath','liveResourceProcessWorkingDirectory','liveResourceProcessCommandLine',
    'liveResourceProcessFile','liveResourceProcessSignature','liveResourceProcessChildren','liveResourceProcessServices',
    'liveResourceProcessReveal','liveResourceProcessCopyPath','liveResourceProcessCopyCommand','liveResourceProcessClose',
    'liveResourceProcessTerminate','liveResourceProcessRestart'
  ]) assert.match(html, new RegExp(`id="${id}"`));
  assert.match(application, /callAgentHost\('systemManager\.process\.inspect'/);
  assert.match(application, /callAgentHost\(selected\[0\], identity/);
  assert.match(application, /slice\(0, 100\)/);
  assert.match(html, /Command lines may contain passwords, tokens or other secrets/);
  assert.match(application, /serviceInventoryAvailable === true/);
  assert.match(application, /serviceInventoryAvailable && !serviceHosted/);
  assert.match(native, /entries = processEntries/);
  assert.match(native, /processId = item\.ProcessId/);
  assert.match(native, /startedAtUtc = item\.StartedAtTicks > 0/);
});

test('native process inspection verifies PID, process name and creation time before returning details', () => {
  assert.match(bridge, /"systemManager\.process\.inspect"\s*=>\s*Task\.Run\(\(\)\s*=>\s*SystemManagerInspectProcess\(args\)/);
  const inspect = native.slice(native.indexOf('private static object SystemManagerInspectProcess'), native.indexOf('private static object SystemManagerRevealProcess'));
  assert.match(inspect, /RequireSystemCapability\("processes", "inspectProcesses"\)/);
  assert.match(inspect, /ReadExpectedProcessStart\(args\)/);
  assert.match(inspect, /VerifyProcessIdentity\(process, args, expectedStart\)/);
  assert.match(native, /The PID now belongs to a different process instance/);
  assert.match(inspect, /ReadProcessCimDetails\(pid\)/);
  assert.match(inspect, /ReadProcessTokenDetails\(process\)/);
  assert.match(inspect, /parentProcessId/);
  assert.match(inspect, /children/);
  assert.match(inspect, /signatureStatus/);
  assert.match(inspect, /workingDirectoryNote/);
  assert.match(inspect, /serviceInventoryAvailable/);
  assert.match(inspect, /not persisted by RunMind/);
});

test('process actions are native-confirmed and service-hosted processes are protected', () => {
  assert.match(bridge, /"systemManager\.process\.reveal"\s*=>\s*Task\.FromResult\(SystemManagerRevealProcess\(args\)\)/);
  assert.match(bridge, /"systemManager\.process\.restart"\s*=>\s*Task\.Run\(\(\)\s*=>\s*SystemManagerRestartProcess\(args\)/);
  const restart = native.slice(native.indexOf('private static object SystemManagerRestartProcess'), native.indexOf('private static object SystemManagerCloseProcess'));
  assert.match(restart, /RequireSystemCapability\("processes", "inspectProcesses"\)/);
  assert.match(restart, /RequireSystemCapability\("processes", "terminateProcesses"\)/);
  assert.match(restart, /JsonHasServiceRows\(details\)/);
  assert.match(restart, /serviceInventoryVerified/);
  assert.match(restart, /could not verify whether this process hosts Windows services/i);
  assert.match(restart, /WindowsIdentity\.GetCurrent\(\)\.Name/);
  assert.match(restart, /process\.SessionId != Process\.GetCurrentProcess\(\)\.SessionId/);
  assert.match(native, /CommandLineToArgvW/);
  assert.match(restart, /SplitWindowsCommandLine\(commandLine\)/);
  assert.match(restart, /separate confirmation is required before force termination/);
  assert.match(restart, /WorkingDirectory = Path\.GetDirectoryName\(path\)/);
  const close = native.slice(native.indexOf('private static object SystemManagerCloseProcess'), native.indexOf('private static object SystemManagerTerminateProcess'));
  const terminate = native.slice(native.indexOf('private static object SystemManagerTerminateProcess'), native.indexOf('[StructLayout(LayoutKind.Sequential)]\n    private struct NativeFileTime'));
  assert.match(close, /BlockProcessLevelServiceActions\(pid\)/);
  assert.match(terminate, /BlockProcessLevelServiceActions\(pid\)/);
  assert.match(native, /dedicated Windows service controls instead/);
  assert.match(native, /protected Windows process/);
});
