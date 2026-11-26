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

test('live sampling remains capability-gated and uses strict duration and interval bounds', () => {
  const start = native.slice(native.indexOf('private object SystemManagerLiveResourcesStart'), native.indexOf('private object SystemManagerLiveResourcesSample'));
  const sample = native.slice(native.indexOf('private object SystemManagerLiveResourcesSample'), native.indexOf('private object SystemManagerLiveResourcesStop'));
  assert.match(start, /RequireSystemManagerCapability\("resourceMonitoring"\)/);
  assert.match(sample, /RequireSystemManagerCapability\("resourceMonitoring"\)/);
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
  assert.match(native, /private void StopLiveResourceSessionForDispose\(\)[\s\S]*?lock \(_liveResourceSync\) _liveResourceSession = null/);
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

test('native telemetry remains bounded and does not persist samples', () => {
  const start = native.slice(native.indexOf('private object SystemManagerLiveResourcesStart'), native.indexOf('private object SystemManagerLiveResourcesSample'));
  const sample = native.slice(native.indexOf('private object SystemManagerLiveResourcesSample'), native.indexOf('private object SystemManagerLiveResourcesStop'));
  assert.match(start, /storage = "volatile-memory-only"/);
  assert.match(start, /not persisted/);
  assert.match(sample, /Take\(8\)/);
  assert.match(sample, /first 1,000 processes/);
  assert.match(sample, /85% CPU, 90% memory/);
  assert.doesNotMatch(start + sample, /File\.WriteAllText|FileStream|Directory\.CreateDirectory|Registry\./);
});

test('dashboard has explicit Start/Stop, interval and duration controls, charts, process ranking and export', () => {
  for (const id of ['liveResourceStart','liveResourceStop','liveResourceDuration','liveResourceInterval','liveResourceCpuLine','liveResourceMemoryLine','liveResourceReceiveLine','liveResourceSendLine','liveResourceIoReadLine','liveResourceIoWriteLine','liveResourceSortCpu','liveResourceSortMemory','liveResourceProcesses','liveResourceExport'])
    assert.match(html, new RegExp(`id="${id}"`));
  assert.match(html, /<option value="60">1 minute/);
  assert.match(html, /<option value="300" selected>5 minutes/);
  assert.match(html, /<option value="900">15 minutes/);
  assert.match(html, /<option value="1" selected>1 second/);
  assert.match(html, /<option value="2">2 seconds/);
  assert.match(html, /<option value="5">5 seconds/);
  assert.match(html, /Process I\/O transfer[\s\S]*?not disk-only/);
  assert.match(html, /Export CSV/);
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

test('dashboard stops when leaving the tab, closing the panel or disposing the page', () => {
  assert.match(application, /tab !== 'monitor' && liveResourceSession/);
  assert.match(application, /systemManagerMenu[\s\S]*?!event\.target\.open && liveResourceSession/);
  assert.match(application, /window\.addEventListener\('pagehide'/);
  assert.match(application, /systemManager\.liveResources\.stop/);
  assert.match(application, /Monitoring stopped because the Live Monitor tab was left/);
});

test('charts and process ranking are rendered without an external chart library', () => {
  assert.match(application, /function liveResourceChartPoints/);
  assert.match(application, /setAttribute\('d', path\.trim\(\)\)/);
  assert.match(application, /liveResourceProcessSort === 'memory' \? latest\.processes\?\.topMemory/);
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
