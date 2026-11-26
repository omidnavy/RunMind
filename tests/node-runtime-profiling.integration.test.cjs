const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');

const root = path.resolve(__dirname, '..');
const bridgePath = path.join(root, 'Shared', 'NodeDebuggerBridge.cs');
const hostPath = path.join(root, 'Shared', 'AgentHostBridge.cs');
const toolsPath = path.join(root, 'Shared', 'ChatWindow', 'scripts', 'agent', 'tools.js');
const providersPath = path.join(root, 'Shared', 'ChatWindow', 'scripts', 'providers.js');

function waitFor(predicate, timeoutMs, description) {
  const started = Date.now();
  return new Promise((resolve, reject) => {
    const check = () => {
      const value = predicate();
      if (value) return resolve(value);
      if (Date.now() - started >= timeoutMs) return reject(new Error(`Timed out waiting for ${description}.`));
      setTimeout(check, 20);
    };
    check();
  });
}

function connectInspector(url) {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(url);
    const onError = event => reject(event.error || new Error('Inspector WebSocket connection failed.'));
    socket.addEventListener('open', () => {
      socket.removeEventListener('error', onError);
      resolve(socket);
    }, { once: true });
    socket.addEventListener('error', onError, { once: true });
  });
}

function createInspector(socket) {
  let nextId = 0;
  const pending = new Map();
  const events = [];
  socket.addEventListener('message', event => {
    let payload;
    try { payload = JSON.parse(String(event.data)); } catch (_) { return; }
    if (Number.isInteger(payload.id) && pending.has(payload.id)) {
      const request = pending.get(payload.id);
      pending.delete(payload.id);
      if (payload.error) request.reject(new Error(payload.error.message || 'Inspector command failed.'));
      else request.resolve(payload.result || {});
    } else if (payload.method) events.push(payload);
  });
  return {
    events,
    command(method, params = {}) {
      const id = ++nextId;
      return new Promise((resolve, reject) => {
        pending.set(id, { resolve, reject });
        socket.send(JSON.stringify({ id, method, params }));
      });
    }
  };
}

test('Node Inspector exposes runtime metrics, CPU profiles, and streamed heap snapshots', async t => {
  assert.equal(typeof WebSocket, 'function', 'Requires Node.js with the built-in WebSocket client.');
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-runtime-profile-'));
  const child = spawn(process.execPath, ['--inspect=127.0.0.1:0', '-e', `
    global.retained = [];
    setInterval(() => {
      const batch = Array.from({ length: 300 }, (_, i) => ({ i, value: 'runtime-profile-' + i + '-'.repeat(80) }));
      global.retained.push(...batch);
      if (global.retained.length > 3000) global.retained.splice(0, 1500);
    }, 15);
    setInterval(() => { let n = 0; for (let i = 0; i < 20000; i++) n += Math.sqrt(i); global.work = n; }, 5);
  `], { cwd: directory, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let stderr = '';
  child.stderr.setEncoding('utf8');
  child.stderr.on('data', chunk => { stderr = (stderr + chunk).slice(-16000); });
  let socket;
  t.after(async () => {
    try { socket?.close(); } catch (_) {}
    try { if (child.exitCode === null) child.kill(); } catch (_) {}
    try { fs.rmSync(directory, { recursive: true, force: true }); } catch (_) {}
  });

  const match = await waitFor(() => stderr.match(/Debugger listening on (ws:\/\/127\.0\.0\.1:\d+\/[^\s]+)/), 8000, 'Node Inspector endpoint');
  socket = await connectInspector(match[1]);
  const inspector = createInspector(socket);

  await inspector.command('Runtime.enable');
  const heap = await inspector.command('Runtime.getHeapUsage');
  assert.ok(heap.usedSize > 0);
  assert.ok(heap.totalSize >= heap.usedSize);

  await inspector.command('Profiler.enable');
  await inspector.command('Profiler.setSamplingInterval', { interval: 1000 });
  await inspector.command('Profiler.start');
  await new Promise(resolve => setTimeout(resolve, 350));
  const stopped = await inspector.command('Profiler.stop');
  assert.ok(Array.isArray(stopped.profile?.nodes) && stopped.profile.nodes.length > 0);
  assert.ok(Array.isArray(stopped.profile?.samples) && stopped.profile.samples.length > 0);
  assert.ok(Array.isArray(stopped.profile?.timeDeltas));

  await inspector.command('HeapProfiler.enable');
  const snapshot = await inspector.command('HeapProfiler.takeHeapSnapshot', { reportProgress: false, captureNumericValue: true });
  assert.ok(snapshot);
  const chunks = inspector.events.filter(event => event.method === 'HeapProfiler.addHeapSnapshotChunk');
  assert.ok(chunks.length > 0, 'Heap snapshot data must arrive as streamed chunks.');
  const snapshotText = chunks.map(event => String(event.params?.chunk || '')).join('');
  assert.ok(snapshotText.length > 1000);
  const snapshotDocument = JSON.parse(snapshotText);
  assert.ok(snapshotDocument.snapshot?.meta?.node_fields?.includes('id'));
});

test('RunMind runtime profiling tools are routed through the shared debugger session and guarded', () => {
  const bridge = fs.readFileSync(bridgePath, 'utf8');
  const host = fs.readFileSync(hostPath, 'utf8');
  const tools = fs.readFileSync(toolsPath, 'utf8');
  const providers = fs.readFileSync(providersPath, 'utf8');
  for (const operation of ['debugger.runtime.diagnostics','debugger.runtime.cpuProfile','debugger.runtime.heapSnapshot','debugger.runtime.memoryMonitor'])
    assert.ok(host.includes(`"${operation}"`), `Missing native dispatch operation ${operation}.`);
  for (const method of ['Runtime.getHeapUsage','Profiler.start','Profiler.stop','HeapProfiler.takeHeapSnapshot'])
    assert.ok(bridge.includes(`"${method}"`), `Missing Inspector method ${method}.`);
  assert.match(bridge, /128L \* 1024 \* 1024/);
  assert.match(bridge, /32 \* 1024 \* 1024/);
  assert.match(bridge, /maximumRetainedSamples = 120/);
  assert.match(bridge, /HeapProfiler\.addHeapSnapshotChunk/);
  assert.match(tools, /Start Node\.js CPU profiling\?/);
  assert.match(tools, /Capture a Node\.js heap snapshot\?/);
  assert.match(tools, /runtimeTools/);
  for (const name of ['node_runtime_diagnostics','node_runtime_cpu_profile','node_runtime_heap_snapshot','node_runtime_memory_monitor'])
    assert.ok(providers.includes(`name: '${name}'`), `Missing exposed runtime tool ${name}.`);
});
