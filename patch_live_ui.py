from pathlib import Path
root=Path('/mnt/data/runmind-live-resource')
p=root/'Shared/ChatWindow.html'
s=p.read_text(encoding='utf-8-sig')
old='''            <nav class="agent-settings-tabs system-manager-tabs" aria-label="System Manager tabs">
              <button type="button" class="agent-settings-tab" data-system-manager-tab="settings" aria-selected="true">Settings</button>
              <button type="button" class="agent-settings-tab" data-system-manager-tab="adapters" aria-selected="false">Application Adapters</button>
            </nav>
            <section class="agent-settings-page" id="systemManagerSettingsPage">
'''
new='''            <nav class="agent-settings-tabs system-manager-tabs" aria-label="System Manager tabs">
              <button type="button" class="agent-settings-tab" data-system-manager-tab="settings" aria-selected="true">Settings</button>
              <button type="button" class="agent-settings-tab" data-system-manager-tab="monitor" aria-selected="false">Live Monitor</button>
              <button type="button" class="agent-settings-tab" data-system-manager-tab="adapters" aria-selected="false">Application Adapters</button>
            </nav>
            <section class="agent-settings-page live-resource-page" data-system-manager-view="monitor" hidden aria-label="Live resource monitor">
              <div class="agent-settings-card live-resource-intro">
                <div class="agent-settings-card-heading"><div><h3>Live Resource Intelligence</h3><p>Watch Windows CPU, memory, network throughput and process activity using native read-only measurements.</p></div><span class="live-resource-indicator" id="liveResourceIndicator" aria-hidden="true"></span></div>
                <div class="live-resource-controls">
                  <label class="setting-field"><span>Session length</span><select id="liveResourceDuration"><option value="60">1 minute</option><option value="300" selected>5 minutes</option><option value="900">15 minutes</option></select></label>
                  <label class="setting-field"><span>Sampling interval</span><select id="liveResourceInterval"><option value="1" selected>1 second</option><option value="2">2 seconds</option><option value="5">5 seconds</option></select></label>
                  <button type="button" class="button primary" id="liveResourceStart">Start monitoring</button>
                  <button type="button" class="button" id="liveResourceStop" disabled>Stop</button>
                </div>
                <p class="setting-note" id="liveResourceStatus" role="status" aria-live="polite">Monitoring is off. Enable System Manager and its Resource Monitoring capability in Settings to begin.</p>
                <div class="live-resource-progress" aria-hidden="true"><span id="liveResourceProgress"></span></div>
              </div>
              <div class="live-resource-metrics" aria-label="Current resource measurements">
                <article class="live-resource-metric"><span>CPU utilization</span><strong id="liveResourceCpu">—</strong><small id="liveResourceCpuNote">Waiting for first sample</small></article>
                <article class="live-resource-metric"><span>Physical memory</span><strong id="liveResourceMemory">—</strong><small id="liveResourceMemoryNote">Waiting for first sample</small></article>
                <article class="live-resource-metric"><span>Network receive</span><strong id="liveResourceNetworkIn">—</strong><small id="liveResourceNetworkOut">Send —</small></article>
                <article class="live-resource-metric"><span>Processes observed</span><strong id="liveResourceProcessCount">—</strong><small id="liveResourceProcessNote">Up to 1,000 accessible processes</small></article>
              </div>
              <div class="live-resource-charts">
                <article class="live-resource-chart-card"><header><strong>CPU utilization</strong><span>0–100%</span></header><svg viewBox="0 0 520 132" role="img" aria-label="Live CPU utilization chart"><path class="live-chart-grid" d="M0 12H520M0 66H520M0 120H520"/><polyline id="liveResourceCpuLine" class="live-chart-line cpu" points=""/></svg><footer><span>Session history</span><strong id="liveResourceCpuPeak">Peak —</strong></footer></article>
                <article class="live-resource-chart-card"><header><strong>Memory utilization</strong><span>0–100%</span></header><svg viewBox="0 0 520 132" role="img" aria-label="Live physical memory utilization chart"><path class="live-chart-grid" d="M0 12H520M0 66H520M0 120H520"/><polyline id="liveResourceMemoryLine" class="live-chart-line memory" points=""/></svg><footer><span>Session history</span><strong id="liveResourceMemoryPeak">Peak —</strong></footer></article>
                <article class="live-resource-chart-card live-resource-chart-wide"><header><strong>Network throughput</strong><span>Observed active interfaces</span></header><svg viewBox="0 0 520 132" role="img" aria-label="Live network receive and send throughput chart"><path class="live-chart-grid" d="M0 12H520M0 66H520M0 120H520"/><polyline id="liveResourceReceiveLine" class="live-chart-line receive" points=""/><polyline id="liveResourceSendLine" class="live-chart-line send" points=""/></svg><footer><span><i class="live-chart-key receive"></i>Receive <i class="live-chart-key send"></i>Send</span><strong id="liveResourceNetworkPeak">Peak —</strong></footer></article>
              </div>
              <article class="live-resource-process-card"><header><div><h3>Top processes</h3><p>CPU share is normalized against total machine capacity. Process identity is matched by PID and start time.</p></div><button type="button" class="button" id="liveResourceExport" disabled>Export CSV</button></header><div class="live-resource-process-table-wrap"><table class="live-resource-process-table"><thead><tr><th>Process</th><th>PID</th><th>CPU share</th><th>Working set</th><th>Private memory</th></tr></thead><tbody id="liveResourceProcesses"><tr><td colspan="5">Start monitoring to collect process measurements.</td></tr></tbody></table></div><p class="setting-note" id="liveResourceAlert" aria-live="polite">No live threshold alerts.</p></article>
              <p class="live-resource-disclaimer">Read-only, user-started and bounded to 15 minutes. Samples are held in memory only and are not written to disk. Monitoring stops when this System Manager panel or RunMind window closes. Network counters aggregate active non-loopback interfaces and may overlap on VPN/virtual adapters. Physical disk throughput remains in the existing finite resource report, not this low-overhead live session.</p>
            </section>
            <section class="agent-settings-page" id="systemManagerSettingsPage">
'''
if old not in s: raise SystemExit('html nav anchor missing')
s=s.replace(old,new,1)
s=s.replace('''<label class="agent-settings-checkbox"><input id="systemManagerResourceMonitoring" type="checkbox"><span>Finite CPU, memory, process and supported disk-I/O sampling (maximum 60 seconds)</span></label>''','''<label class="agent-settings-checkbox"><input id="systemManagerResourceMonitoring" type="checkbox"><span>Resource monitoring: finite reports (maximum 60 seconds) and explicit live sessions (maximum 15 minutes)</span></label>''',1)
p.write_text(s,encoding='utf-8-sig')

p=root/'Shared/ChatWindow/styles/chat.css'
s=p.read_text(encoding='utf-8-sig')
css='''
/* Live Resource Intelligence — bounded native Windows telemetry dashboard. */
#systemManagerMenu .toolbar-panel:has(.live-resource-page:not([hidden])) { width: min(760px, calc(100vw - 24px)); }
.live-resource-page { min-width: 0; }
.live-resource-intro .agent-settings-card-heading { align-items: center; }
.live-resource-indicator { width: 9px; height: 9px; flex: 0 0 auto; border-radius: 50%; background: var(--color-muted); box-shadow: 0 0 0 4px color-mix(in srgb, var(--color-muted) 12%, transparent); }
.live-resource-indicator.active { background: #42b982; box-shadow: 0 0 0 4px color-mix(in srgb, #42b982 15%, transparent), 0 0 14px color-mix(in srgb, #42b982 55%, transparent); animation: live-resource-pulse 1.8s ease-in-out infinite; }
@keyframes live-resource-pulse { 50% { box-shadow: 0 0 0 7px transparent, 0 0 18px color-mix(in srgb, #42b982 60%, transparent); } }
.live-resource-controls { display: grid; grid-template-columns: 1fr 1fr auto auto; gap: 8px; align-items: end; margin-top: 12px; }
.live-resource-controls .setting-field { margin: 0; min-width: 0; }
.live-resource-controls select { width: 100%; min-width: 0; }
.live-resource-progress { height: 3px; margin-top: 10px; overflow: hidden; border-radius: 99px; background: var(--color-border); }
.live-resource-progress span { display: block; width: 0; height: 100%; background: var(--color-accent); transition: width .3s linear; }
.live-resource-metrics { display: grid; grid-template-columns: repeat(4,minmax(0,1fr)); gap: 8px; }
.live-resource-metric,.live-resource-chart-card,.live-resource-process-card { min-width: 0; padding: 12px; border: 1px solid var(--color-border); border-radius: 10px; background: var(--color-surface); }
.live-resource-metric { display: flex; flex-direction: column; gap: 5px; }
.live-resource-metric>span,.live-resource-metric small { color: var(--color-muted); font-size: 10px; }
.live-resource-metric strong { font-size: clamp(18px,3vw,25px); font-variant-numeric: tabular-nums; line-height: 1.15; }
.live-resource-metric small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.live-resource-charts { display: grid; grid-template-columns: repeat(2,minmax(0,1fr)); gap: 8px; }
.live-resource-chart-card header,.live-resource-chart-card footer,.live-resource-process-card>header { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
.live-resource-chart-card header strong,.live-resource-process-card h3 { font-size: 12px; }
.live-resource-chart-card header span,.live-resource-chart-card footer,.live-resource-process-card header p { color: var(--color-muted); font-size: 10px; }
.live-resource-chart-card svg { display: block; width: 100%; height: auto; margin-top: 8px; overflow: visible; }
.live-chart-grid { fill: none; stroke: var(--color-border); stroke-width: 1; stroke-dasharray: 3 5; opacity: .7; }
.live-chart-line { fill: none; stroke-width: 2.6; stroke-linecap: round; stroke-linejoin: round; vector-effect: non-scaling-stroke; }
.live-chart-line.cpu { stroke: var(--color-accent); }
.live-chart-line.memory { stroke: #4bbd9b; }
.live-chart-line.receive { stroke: var(--color-accent); }
.live-chart-line.send { stroke: #65a6e8; }
.live-resource-chart-card footer { margin-top: 4px; }
.live-resource-chart-wide { grid-column: 1 / -1; }
.live-chart-key { display: inline-block; width: 7px; height: 7px; margin: 0 4px 0 8px; border-radius: 50%; background: var(--color-accent); }
.live-chart-key.send { background: #65a6e8; }
.live-resource-process-card>header { align-items: flex-start; margin-bottom: 10px; }
.live-resource-process-card h3 { margin: 0 0 4px; }
.live-resource-process-card header p { margin: 0; max-width: 470px; }
.live-resource-process-table-wrap { overflow-x: auto; }
.live-resource-process-table { width: 100%; border-collapse: collapse; font-size: 10px; white-space: nowrap; }
.live-resource-process-table th,.live-resource-process-table td { padding: 7px 6px; border-bottom: 1px solid var(--color-border); text-align: right; font-variant-numeric: tabular-nums; }
.live-resource-process-table th:first-child,.live-resource-process-table td:first-child { text-align: left; }
.live-resource-process-table th { color: var(--color-muted); font-weight: 600; }
.live-resource-process-table tr:last-child td { border-bottom: 0; }
.live-resource-alert { margin-top: 9px; }
.live-resource-alert.warning { color: var(--color-warning, #d9973f); }
.live-resource-disclaimer { margin: 0; color: var(--color-muted); font-size: 10px; line-height: 1.55; }
@media (max-width: 640px) {
  #systemManagerMenu .toolbar-panel:has(.live-resource-page:not([hidden])) { width: min(500px, calc(100vw - 16px)); }
  .live-resource-controls { grid-template-columns: 1fr 1fr; }
  .live-resource-controls button { width: 100%; }
  .live-resource-metrics { grid-template-columns: repeat(2,minmax(0,1fr)); }
  .live-resource-charts { grid-template-columns: 1fr; }
  .live-resource-chart-wide { grid-column: auto; }
}
@media (prefers-reduced-motion: reduce) { .live-resource-indicator.active { animation: none; } .live-resource-progress span { transition: none; } }
'''
s += '\n'+css
p.write_text(s,encoding='utf-8-sig')

p=root/'Shared/ChatWindow/scripts/application.js'
s=p.read_text(encoding='utf-8-sig')
anchor='''    for (const id of ['systemManagerEnabled','systemManagerProfile','systemManagerLaunchPermission','systemManagerOpenPermission','systemManagerGroupApplications','systemManagerGroupProcesses','systemManagerGroupDiagnostics','systemManagerDiscoverApplications','systemManagerLaunchApplications','systemManagerOpenTargets','systemManagerListProcesses','systemManagerInspectProcesses','systemManagerTerminateProcesses','systemManagerSystemInformation','systemManagerEnvironmentInformation','systemManagerServiceInformation','systemManagerNetworkInformation','systemManagerEventLogInformation','systemManagerCrashInvestigation','systemManagerResourceMonitoring','systemManagerHealthHistory','systemManagerScheduledTaskInformation','systemManagerScheduledTaskManagement','systemManagerStorageHealth','systemManagerSecurityPosture','systemManagerTargetedNetworkDiagnostics','systemManagerCommandTimeout']) {
'''
js='''    let liveResourceSession = null;
    const liveResourceHistory = [];
    const LIVE_RESOURCE_HISTORY_LIMIT = 900;
    let liveResourcePollTimer = 0;

    function liveResourceFormatBytesPerSecond(value) {
      if (!Number.isFinite(Number(value))) return 'Unavailable';
      const bytes = Math.max(0, Number(value));
      if (bytes < 1024) return `${bytes.toFixed(0)} B/s`;
      if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB/s`;
      if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(2)} MB/s`;
      return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB/s`;
    }
    function liveResourceFormatBytes(value) {
      const bytes = Math.max(0, Number(value) || 0);
      if (bytes < 1024) return `${bytes.toFixed(0)} B`;
      if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(0)} KB`;
      if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
      return `${(bytes / 1024 ** 3).toFixed(2)} GB`;
    }
    function liveResourceSetStatus(message, active = false, warning = false) {
      const status = document.getElementById('liveResourceStatus');
      const indicator = document.getElementById('liveResourceIndicator');
      if (status) status.textContent = message;
      if (indicator) indicator.classList.toggle('active', active);
      if (status) status.classList.toggle('warning', warning);
    }
    function liveResourceChartPoints(id, values, maximum = 100) {
      const line = document.getElementById(id);
      if (!line) return;
      if (!values.length) { line.setAttribute('points', ''); return; }
      const width = 520, top = 12, bottom = 120, height = bottom - top;
      const max = Math.max(0.001, maximum);
      const points = values.map((value, index) => {
        const x = values.length < 2 ? width : index / (values.length - 1) * width;
        const y = bottom - Math.max(0, Math.min(1, (Number(value) || 0) / max)) * height;
        return `${x.toFixed(1)},${y.toFixed(1)}`;
      });
      line.setAttribute('points', points.join(' '));
    }
    function renderLiveResourceMonitor() {
      const samples = liveResourceHistory;
      const latest = samples.at(-1);
      if (!latest) return;
      const cpuValues = samples.map(item => item.machineCpuPercent).filter(Number.isFinite);
      const memoryValues = samples.map(item => item.memory?.memoryLoadPercent).filter(Number.isFinite);
      const rxValues = samples.map(item => item.network?.receiveBytesPerSecond).filter(Number.isFinite);
      const txValues = samples.map(item => item.network?.sendBytesPerSecond).filter(Number.isFinite);
      const maxNetwork = Math.max(1024, ...rxValues, ...txValues) * 1.12;
      liveResourceChartPoints('liveResourceCpuLine', samples.map(item => item.machineCpuPercent), 100);
      liveResourceChartPoints('liveResourceMemoryLine', samples.map(item => item.memory?.memoryLoadPercent), 100);
      liveResourceChartPoints('liveResourceReceiveLine', samples.map(item => item.network?.receiveBytesPerSecond), maxNetwork);
      liveResourceChartPoints('liveResourceSendLine', samples.map(item => item.network?.sendBytesPerSecond), maxNetwork);
      const cpu = Number.isFinite(latest.machineCpuPercent) ? latest.machineCpuPercent : null;
      const memory = Number.isFinite(latest.memory?.memoryLoadPercent) ? latest.memory.memoryLoadPercent : null;
      const setText = (id, value) => { const node = document.getElementById(id); if (node) node.textContent = value; };
      setText('liveResourceCpu', cpu == null ? 'Unavailable' : `${cpu.toFixed(1)}%`);
      setText('liveResourceCpuNote', cpu == null ? 'Windows CPU counter unavailable' : `Average capacity across ${latest.processorCount || '—'} logical processors`);
      setText('liveResourceMemory', memory == null ? 'Unavailable' : `${memory.toFixed(1)}%`);
      setText('liveResourceMemoryNote', latest.memory ? `${liveResourceFormatBytes(latest.memory.usedPhysicalBytes)} / ${liveResourceFormatBytes(latest.memory.totalPhysicalBytes)}` : 'Windows memory counter unavailable');
      setText('liveResourceNetworkIn', latest.network?.available ? liveResourceFormatBytesPerSecond(latest.network.receiveBytesPerSecond) : 'Unavailable');
      setText('liveResourceNetworkOut', latest.network?.available ? `Send ${liveResourceFormatBytesPerSecond(latest.network.sendBytesPerSecond)}` : 'Network counters unavailable');
      setText('liveResourceProcessCount', String(latest.processes?.count ?? '—'));
      setText('liveResourceProcessNote', latest.processes?.truncated ? 'Process inventory truncated at 1,000' : 'Accessible processes observed');
      setText('liveResourceCpuPeak', cpuValues.length ? `Peak ${Math.max(...cpuValues).toFixed(1)}%` : 'Peak unavailable');
      setText('liveResourceMemoryPeak', memoryValues.length ? `Peak ${Math.max(...memoryValues).toFixed(1)}%` : 'Peak unavailable');
      setText('liveResourceNetworkPeak', rxValues.length || txValues.length ? `Peak ${liveResourceFormatBytesPerSecond(Math.max(0,...rxValues,...txValues))}` : 'Peak unavailable');
      if (liveResourceSession) {
        const elapsed = Number(latest.elapsedSeconds) || 0;
        const percent = Math.max(0, Math.min(100, elapsed / liveResourceSession.durationSeconds * 100));
        const progress = document.getElementById('liveResourceProgress');
        if (progress) progress.style.width = `${percent}%`;
        liveResourceSetStatus(`Live · ${Math.floor(elapsed)}s elapsed · ${Math.max(0, latest.remainingSeconds ?? liveResourceSession.durationSeconds - elapsed)}s remaining · ${samples.length} samples`, true);
      }
      const body = document.getElementById('liveResourceProcesses');
      if (body) {
        const processes = Array.isArray(latest.processes?.topCpu) ? latest.processes.topCpu : [];
        body.replaceChildren();
        if (!processes.length) {
          const row = document.createElement('tr'); const cell = document.createElement('td'); cell.colSpan = 5; cell.textContent = 'No process CPU deltas are available yet; wait for another sample or check process permissions.'; row.append(cell); body.append(row);
        } else {
          for (const process of processes.slice(0,8)) {
            const row = document.createElement('tr');
            const values = [String(process.name || 'Unknown process'), String(process.processId ?? '—'), Number.isFinite(process.machineCpuSharePercent) ? `${process.machineCpuSharePercent.toFixed(2)}%` : '—', liveResourceFormatBytes(process.workingSetBytes), liveResourceFormatBytes(process.privateMemoryBytes)];
            for (const value of values) { const cell = document.createElement('td'); cell.textContent = value; row.append(cell); }
            body.append(row);
          }
        }
      }
      const alerts = [];
      if (latest.alerts?.highCpu) alerts.push('CPU is at or above the 85% display threshold.');
      if (latest.alerts?.highMemory) alerts.push('Physical memory is at or above the 90% display threshold.');
      setText('liveResourceAlert', alerts.length ? alerts.join(' ') + ' These are threshold indicators, not diagnoses.' : 'No CPU or memory display thresholds currently exceeded.');
      document.getElementById('liveResourceAlert')?.classList.toggle('warning', alerts.length > 0);
      const exportButton = document.getElementById('liveResourceExport');
      if (exportButton) exportButton.disabled = samples.length === 0;
    }
    async function pollLiveResourceMonitor() {
      if (!liveResourceSession) return;
      try {
        const state = liveResourceSession;
        const sample = await callAgentHost('systemManager.liveResources.sample', { session_id: state.sessionId }, undefined, 15000);
        if (liveResourceSession !== state) return;
        if (sample?.expired || sample?.running === false) {
          liveResourceSession = null;
          document.getElementById('liveResourceStart').disabled = false;
          document.getElementById('liveResourceStop').disabled = true;
          liveResourceSetStatus('Monitoring session ended. Collected samples remain available until this panel closes.', false);
          return;
        }
        if (sample?.accepted) {
          liveResourceHistory.push(sample);
          while (liveResourceHistory.length > LIVE_RESOURCE_HISTORY_LIMIT) liveResourceHistory.shift();
          renderLiveResourceMonitor();
        }
        if (liveResourceSession === state) liveResourcePollTimer = window.setTimeout(pollLiveResourceMonitor, sample?.accepted ? state.intervalSeconds * 1000 : Math.max(250, sample?.retryAfterMilliseconds || 500));
      } catch (error) {
        if (!liveResourceSession) return;
        const state = liveResourceSession;
        liveResourceSession = null;
        clearTimeout(liveResourcePollTimer);
        document.getElementById('liveResourceStart').disabled = false;
        document.getElementById('liveResourceStop').disabled = true;
        try { await callAgentHost('systemManager.liveResources.stop', { session_id: state.sessionId }, undefined, 5000); } catch (_) {}
        liveResourceSetStatus(`Monitoring stopped: ${error?.message || error}`, false, true);
      }
    }
    async function stopLiveResourceMonitor(message = 'Monitoring stopped by user.') {
      const state = liveResourceSession;
      if (!state) return;
      liveResourceSession = null;
      clearTimeout(liveResourcePollTimer);
      document.getElementById('liveResourceStart').disabled = false;
      document.getElementById('liveResourceStop').disabled = true;
      const progress = document.getElementById('liveResourceProgress');
      if (progress) progress.style.width = '';
      try { await callAgentHost('systemManager.liveResources.stop', { session_id: state.sessionId }, undefined, 5000); } catch (_) {}
      liveResourceSetStatus(`${message} ${liveResourceHistory.length} samples retained in this window's memory.`, false);
    }
    async function startLiveResourceMonitor() {
      if (liveResourceSession) return;
      const settings = systemManagerSettingsCache;
      if (!settings?.enabled || !settings?.groups?.diagnostics || !settings?.capabilities?.resourceMonitoring) {
        liveResourceSetStatus('Enable System Manager, System Diagnostics, and Resource Monitoring in the Settings tab first.', false, true);
        return;
      }
      const durationSeconds = Number(document.getElementById('liveResourceDuration')?.value) || 300;
      const intervalSeconds = Number(document.getElementById('liveResourceInterval')?.value) || 1;
      document.getElementById('liveResourceStart').disabled = true;
      document.getElementById('liveResourceStop').disabled = false;
      liveResourceHistory.length = 0;
      renderLiveResourceMonitor();
      liveResourceSetStatus('Starting native Windows telemetry session…', false);
      try {
        const result = await callAgentHost('systemManager.liveResources.start', { duration_seconds: durationSeconds, interval_seconds: intervalSeconds }, undefined, 15000);
        if (!result?.success || !result.sessionId) throw new Error('The native monitor did not return a valid session.');
        liveResourceSession = { sessionId: result.sessionId, durationSeconds: result.durationSeconds, intervalSeconds: result.intervalSeconds, startedAtUtc: result.startedAtUtc };
        liveResourceSetStatus(`Starting · ${durationSeconds / 60} minute limit · ${intervalSeconds}s samples`, true);
        liveResourcePollTimer = window.setTimeout(pollLiveResourceMonitor, intervalSeconds * 1000);
      } catch (error) {
        document.getElementById('liveResourceStart').disabled = false;
        document.getElementById('liveResourceStop').disabled = true;
        liveResourceSetStatus(`Could not start live monitoring: ${error?.message || error}`, false, true);
      }
    }
    function exportLiveResourceCsv() {
      if (!liveResourceHistory.length) return;
      const columns = ['sampledAtUtc','elapsedSeconds','machineCpuPercent','memoryLoadPercent','usedPhysicalBytes','totalPhysicalBytes','receiveBytesPerSecond','sendBytesPerSecond','processCount','processListTruncated'];
      const rows = [columns.join(',')];
      for (const sample of liveResourceHistory) {
        const values = [sample.sampledAtUtc,sample.elapsedSeconds,sample.machineCpuPercent,sample.memory?.memoryLoadPercent,sample.memory?.usedPhysicalBytes,sample.memory?.totalPhysicalBytes,sample.network?.receiveBytesPerSecond,sample.network?.sendBytesPerSecond,sample.processes?.count,sample.processes?.truncated];
        rows.push(values.map(value => value == null ? '' : String(value).replaceAll('"','""')).join(','));
      }
      const blob = new Blob([rows.join('\\r\\n')+'\\r\\n'], { type: 'text/csv;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a'); anchor.href = url; anchor.download = `runmind-resource-session-${new Date().toISOString().replaceAll(':','-')}.csv`;
      document.body.append(anchor); anchor.click(); anchor.remove(); URL.revokeObjectURL(url);
    }

'''
if anchor not in s: raise SystemExit('application anchor missing')
s=s.replace(anchor,js+anchor,1)
# Wire controls and stop on closing the System Manager panel.
anchor2='''    document.querySelectorAll('[data-system-manager-tab]').forEach(button => {
      button.addEventListener('click', () => setSystemManagerTab(button.dataset.systemManagerTab || 'settings'));
    });
    setSystemManagerTab('settings');
'''
new2='''    document.querySelectorAll('[data-system-manager-tab]').forEach(button => {
      button.addEventListener('click', () => setSystemManagerTab(button.dataset.systemManagerTab || 'settings'));
    });
    $('#liveResourceStart')?.addEventListener('click', () => { void startLiveResourceMonitor(); });
    $('#liveResourceStop')?.addEventListener('click', () => { void stopLiveResourceMonitor(); });
    $('#liveResourceExport')?.addEventListener('click', exportLiveResourceCsv);
    $('#systemManagerMenu')?.addEventListener('toggle', event => {
      if (event.target === $('#systemManagerMenu') && !event.target.open && liveResourceSession) void stopLiveResourceMonitor('Monitoring stopped because the System Manager panel was closed.');
    });
    window.addEventListener('pagehide', () => {
      if (!liveResourceSession) return;
      try { window.chrome?.webview?.postMessage({ type: 'runmind.agentHostRequest', requestId: `live-monitor-stop-${Date.now()}`, operation: 'systemManager.liveResources.stop', args: { session_id: liveResourceSession.sessionId } }); } catch (_) {}
      clearTimeout(liveResourcePollTimer);
      liveResourceSession = null;
    });
    setSystemManagerTab('settings');
'''
if anchor2 not in s: raise SystemExit('wire anchor missing')
s=s.replace(anchor2,new2,1)
p.write_text(s,encoding='utf-8-sig')
