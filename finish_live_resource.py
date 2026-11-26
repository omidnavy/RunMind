from pathlib import Path
root=Path('/mnt/data/runmind-live-resource')

# Native: per-process CPU shares for process-memory sorting, session stop identity validation.
p=root/'Shared/SystemManager.cs'
s=p.read_text(encoding='utf-8-sig')
s=s.replace('''            var topCpu = new List<(double MachineShare, object Data)>();
            var processorCount = Math.Max(1, Environment.ProcessorCount);
''','''            var topCpu = new List<(double MachineShare, object Data)>();
            var cpuShareByProcess = new Dictionary<int, double>();
            var processorCount = Math.Max(1, Environment.ProcessorCount);
''',1)
s=s.replace('''                if (machineShare < 0.01) continue;
                topCpu.Add((machineShare, new
''','''                if (machineShare < 0.01) continue;
                cpuShareByProcess[current.ProcessId] = Math.Round(machineShare, 2);
                topCpu.Add((machineShare, new
''',1)
s=s.replace('''                workingSetBytes = item.WorkingSetBytes,
                privateMemoryBytes = item.PrivateMemoryBytes,
                startedAtUtc = item.StartedAtTicks > 0 ? new DateTimeOffset(item.StartedAtTicks, TimeSpan.Zero) : (DateTimeOffset?)null
            }).ToArray();

            var currentNetwork = ReadNetworkByteSnapshot();
''','''                workingSetBytes = item.WorkingSetBytes,
                privateMemoryBytes = item.PrivateMemoryBytes,
                machineCpuSharePercent = cpuShareByProcess.TryGetValue(item.ProcessId, out var memoryRowCpuShare) ? memoryRowCpuShare : (double?)null,
                startedAtUtc = item.StartedAtTicks > 0 ? new DateTimeOffset(item.StartedAtTicks, TimeSpan.Zero) : (DateTimeOffset?)null
            }).ToArray();

            var currentNetwork = ReadNetworkByteSnapshot();
''',1)
old='''    private object SystemManagerLiveResourcesStop(JsonElement args)
    {
        var requestedId = ReadBoundedExactString(args, "session_id", 64);
        lock (_liveResourceSync)
        {
            if (_liveResourceSession is null) return new { success = true, stopped = true, alreadyStopped = true };
            if (requestedId.Length > 0 && !string.Equals(_liveResourceSession.Id, requestedId, StringComparison.Ordinal))
                return new { success = false, stopped = false, reason = "session-id-mismatch" };
            _liveResourceSession = null;
            return new { success = true, stopped = true, stoppedAtUtc = DateTimeOffset.UtcNow };
        }
    }
'''
new='''    private object SystemManagerLiveResourcesStop(JsonElement args)
    {
        var requestedId = ReadBoundedExactString(args, "session_id", 64);
        lock (_liveResourceSync)
        {
            if (_liveResourceSession is null) return new { success = true, stopped = true, alreadyStopped = true };
            if (requestedId.Length != 32 || !requestedId.All(Uri.IsHexDigit) || !string.Equals(_liveResourceSession.Id, requestedId, StringComparison.Ordinal))
                return new { success = false, stopped = false, reason = "session-id-mismatch" };
            _liveResourceSession = null;
            return new { success = true, stopped = true, stoppedAtUtc = DateTimeOffset.UtcNow };
        }
    }
'''
if old not in s: raise SystemExit('stop method anchor missing')
s=s.replace(old,new,1)
# Measure initial sampling timestamp after baselines have been read.
s=s.replace('''            var now = Stopwatch.GetTimestamp();
            var initialProcesses = ReadResourceProcessSnapshot(out _);
            var initialSystemCpu = ReadSystemCpuTimes();
            var initialNetwork = ReadNetworkByteSnapshot();
            var session = new LiveResourceSession
''','''            var initialProcesses = ReadResourceProcessSnapshot(out _);
            var initialSystemCpu = ReadSystemCpuTimes();
            var initialNetwork = ReadNetworkByteSnapshot();
            var now = Stopwatch.GetTimestamp();
            var session = new LiveResourceSession
''',1)
p.write_text(s,encoding='utf-8-sig')

# UI: CPU/memory process ranking, reset charts at start.
p=root/'Shared/ChatWindow.html'
s=p.read_text(encoding='utf-8-sig')
old='''              <article class="live-resource-process-card"><header><div><h3>Top processes</h3><p>CPU share is normalized against total machine capacity. Process identity is matched by PID and start time.</p></div><button type="button" class="button" id="liveResourceExport" disabled>Export CSV</button></header><div class="live-resource-process-table-wrap"><table class="live-resource-process-table"><thead><tr><th>Process</th><th>PID</th><th>CPU share</th><th>Working set</th><th>Private memory</th></tr></thead><tbody id="liveResourceProcesses"><tr><td colspan="5">Start monitoring to collect process measurements.</td></tr></tbody></table></div><p class="setting-note" id="liveResourceAlert" aria-live="polite">No live threshold alerts.</p></article>
'''
new='''              <article class="live-resource-process-card"><header><div><h3>Top processes</h3><p>CPU share is normalized against total machine capacity. Process identity is matched by PID and start time.</p><div class="live-resource-sort" role="group" aria-label="Rank processes by"><button type="button" class="setting-reset active" id="liveResourceSortCpu" aria-pressed="true">CPU</button><button type="button" class="setting-reset" id="liveResourceSortMemory" aria-pressed="false">Memory</button></div></div><button type="button" class="button" id="liveResourceExport" disabled>Export CSV</button></header><div class="live-resource-process-table-wrap"><table class="live-resource-process-table"><thead><tr><th>Process</th><th>PID</th><th>CPU share</th><th>Working set</th><th>Private memory</th></tr></thead><tbody id="liveResourceProcesses"><tr><td colspan="5">Start monitoring to collect process measurements.</td></tr></tbody></table></div><p class="setting-note" id="liveResourceAlert" aria-live="polite">No live threshold alerts.</p></article>
'''
if old not in s: raise SystemExit('process card anchor missing')
s=s.replace(old,new,1)
p.write_text(s,encoding='utf-8-sig')

p=root/'Shared/ChatWindow/scripts/application.js'
s=p.read_text(encoding='utf-8-sig')
s=s.replace('''    const liveResourceHistory = [];
    const LIVE_RESOURCE_HISTORY_LIMIT = 900;
''','''    const liveResourceHistory = [];
    const LIVE_RESOURCE_HISTORY_LIMIT = 900;
    let liveResourceProcessSort = 'cpu';
''',1)
s=s.replace('''      const body = document.getElementById('liveResourceProcesses');
      if (body) {
        const processes = Array.isArray(latest.processes?.topCpu) ? latest.processes.topCpu : [];
''','''      const body = document.getElementById('liveResourceProcesses');
      if (body) {
        const source = liveResourceProcessSort === 'memory' ? latest.processes?.topMemory : latest.processes?.topCpu;
        const processes = Array.isArray(source) ? source : [];
''',1)
s=s.replace('''            const values = [String(process.name || 'Unknown process'), String(process.processId ?? '—'), Number.isFinite(process.machineCpuSharePercent) ? `${process.machineCpuSharePercent.toFixed(2)}%` : '—', liveResourceFormatBytes(process.workingSetBytes), liveResourceFormatBytes(process.privateMemoryBytes)];
''','''            const values = [String(process.name || 'Unknown process'), String(process.processId ?? '—'), Number.isFinite(process.machineCpuSharePercent) ? `${process.machineCpuSharePercent.toFixed(2)}%` : '—', liveResourceFormatBytes(process.workingSetBytes), liveResourceFormatBytes(process.privateMemoryBytes)];
''',1)
# Ensure charts and metric labels are cleared for a fresh session, rather than showing stale traces.
anchor='''    async function pollLiveResourceMonitor() {
'''
helper='''    function resetLiveResourceMonitorView() {
      for (const id of ['liveResourceCpuLine','liveResourceMemoryLine','liveResourceReceiveLine','liveResourceSendLine','liveResourceIoReadLine','liveResourceIoWriteLine']) {
        const node = document.getElementById(id);
        if (node) node.setAttribute('d', '');
      }
      for (const [id, value] of Object.entries({ liveResourceCpu:'—', liveResourceCpuNote:'Waiting for first sample', liveResourceMemory:'—', liveResourceMemoryNote:'Waiting for first sample', liveResourceNetworkIn:'—', liveResourceNetworkOut:'Send —', liveResourceProcessCount:'—', liveResourceProcessNote:'Waiting for first sample', liveResourceCpuPeak:'Peak —', liveResourceMemoryPeak:'Peak —', liveResourceNetworkPeak:'Peak —', liveResourceIoPeak:'Peak —', liveResourceAlert:'No live threshold alerts.' })) {
        const node = document.getElementById(id);
        if (node) { node.textContent = value; node.classList.remove('warning'); }
      }
      const progress = document.getElementById('liveResourceProgress');
      if (progress) progress.style.width = '0';
      const body = document.getElementById('liveResourceProcesses');
      if (body) {
        const row = document.createElement('tr');
        const cell = document.createElement('td');
        cell.colSpan = 5;
        cell.textContent = 'Collecting the first process sample…';
        row.append(cell);
        body.replaceChildren(row);
      }
    }

    async function pollLiveResourceMonitor() {
'''
if anchor not in s: raise SystemExit('poll function anchor missing')
s=s.replace(anchor,helper,1)
s=s.replace('''      liveResourceHistory.length = 0;
      renderLiveResourceMonitor();
      liveResourceSetStatus('Starting native Windows telemetry session…', false);
''','''      liveResourceHistory.length = 0;
      resetLiveResourceMonitorView();
      liveResourceSetStatus('Starting native Windows telemetry session…', false);
''',1)
old='''    $('#liveResourceStart')?.addEventListener('click', () => { void startLiveResourceMonitor(); });
    $('#liveResourceStop')?.addEventListener('click', () => { void stopLiveResourceMonitor(); });
    $('#liveResourceExport')?.addEventListener('click', exportLiveResourceCsv);
'''
new='''    $('#liveResourceStart')?.addEventListener('click', () => { void startLiveResourceMonitor(); });
    $('#liveResourceStop')?.addEventListener('click', () => { void stopLiveResourceMonitor(); });
    $('#liveResourceExport')?.addEventListener('click', exportLiveResourceCsv);
    $('#liveResourceSortCpu')?.addEventListener('click', () => {
      liveResourceProcessSort = 'cpu';
      $('#liveResourceSortCpu')?.classList.add('active');
      $('#liveResourceSortMemory')?.classList.remove('active');
      $('#liveResourceSortCpu')?.setAttribute('aria-pressed', 'true');
      $('#liveResourceSortMemory')?.setAttribute('aria-pressed', 'false');
      renderLiveResourceMonitor();
    });
    $('#liveResourceSortMemory')?.addEventListener('click', () => {
      liveResourceProcessSort = 'memory';
      $('#liveResourceSortMemory')?.classList.add('active');
      $('#liveResourceSortCpu')?.classList.remove('active');
      $('#liveResourceSortMemory')?.setAttribute('aria-pressed', 'true');
      $('#liveResourceSortCpu')?.setAttribute('aria-pressed', 'false');
      renderLiveResourceMonitor();
    });
'''
if old not in s: raise SystemExit('listener anchor missing')
s=s.replace(old,new,1)
p.write_text(s,encoding='utf-8-sig')

p=root/'Shared/ChatWindow/styles/chat.css'
s=p.read_text(encoding='utf-8-sig')
s += '''\n.live-resource-sort { display:flex; gap:5px; margin-top:7px; }\n.live-resource-sort .setting-reset { padding:3px 9px; font-size:10px; }\n.live-resource-sort .setting-reset.active { color:var(--color-accent); border-color:var(--color-accent); background:color-mix(in srgb,var(--color-accent) 10%,transparent); }\n'''
p.write_text(s,encoding='utf-8-sig')
