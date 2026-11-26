const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { pathToFileURL } = require('node:url');

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

function connectWebSocket(url) {
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

function createInspectorClient(socket) {
  let nextId = 0;
  const pending = new Map();
  const events = [];
  socket.addEventListener('message', message => {
    let payload;
    try { payload = JSON.parse(String(message.data)); } catch (_) { return; }
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
    },
    waitEvent(predicate, timeoutMs = 5000) {
      return waitFor(() => events.find(predicate), timeoutMs, `Inspector event (seen: ${events.map(event => event.method || 'unknown').join(', ')})`);
    }
  };
}

test('Node Inspector supports launch, pause, source breakpoint, stack evaluation and resume on loopback', async t => {
  assert.equal(typeof WebSocket, 'function', 'This integration test requires Node.js with the built-in WebSocket client.');
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-node-debugger-'));
  const scriptPath = path.join(directory, 'debug-target.cjs');
  fs.writeFileSync(scriptPath, "let value = 2;\nvalue += 3;\nconsole.log('runmind-debugger-test', value);\n", 'utf8');
  const child = spawn(process.execPath, ['--inspect-brk=127.0.0.1:0', scriptPath], { cwd: directory, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let stderr = '';
  let stdout = '';
  child.stderr.setEncoding('utf8');
  child.stdout.setEncoding('utf8');
  child.stderr.on('data', chunk => { stderr = (stderr + chunk).slice(-16000); });
  child.stdout.on('data', chunk => { stdout = (stdout + chunk).slice(-16000); });
  let socket;
  t.after(async () => {
    try { socket?.close(); } catch (_) {}
    try { if (child.exitCode === null) child.kill(); } catch (_) {}
    try { fs.rmSync(directory, { recursive: true, force: true }); } catch (_) {}
  });

  const endpointMatch = await waitFor(() => stderr.match(/Debugger listening on (ws:\/\/127\.0\.0\.1:\d+\/[^\s]+)/), 8000, 'Node Inspector endpoint');
  const endpoint = endpointMatch[1];
  const parsedEndpoint = new URL(endpoint);
  assert.equal(parsedEndpoint.hostname, '127.0.0.1', 'The inspector must bind to IPv4 loopback.');

  const targetResponse = await fetch(`http://127.0.0.1:${parsedEndpoint.port}/json/list`);
  assert.equal(targetResponse.status, 200);
  const targets = await targetResponse.json();
  assert.ok(targets.some(target => target.type === 'node' && target.webSocketDebuggerUrl === endpoint), 'Inspector discovery should return a Node target.');
  const versionResponse = await fetch(`http://127.0.0.1:${parsedEndpoint.port}/json/version`);
  const version = await versionResponse.json();
  assert.match(version.Browser, /^node\.js\//i);

  socket = await connectWebSocket(endpoint);
  const inspector = createInspectorClient(socket);
  await inspector.command('Debugger.enable');
  await inspector.command('Runtime.enable');
  await inspector.command('Debugger.setAsyncCallStackDepth', { maxDepth: 32 });
  await inspector.command('Runtime.runIfWaitingForDebugger');
  const initialPause = await inspector.waitEvent(event => event.method === 'Debugger.paused');
  assert.ok(initialPause.params.callFrames.length > 0, 'Break-on-start should expose call frames.');

  const breakpoint = await inspector.command('Debugger.setBreakpointByUrl', {
    url: pathToFileURL(scriptPath).href,
    lineNumber: 1,
    columnNumber: 0
  });
  assert.ok(breakpoint.breakpointId, 'The URL breakpoint should be registered.');
  await inspector.command('Debugger.resume');
  const hit = await inspector.waitEvent(event => event.method === 'Debugger.paused' && (event.params.hitBreakpoints || []).includes(breakpoint.breakpointId));
  const topFrame = hit.params.callFrames[0];
  assert.ok(topFrame.callFrameId);
  const evaluation = await inspector.command('Debugger.evaluateOnCallFrame', {
    callFrameId: topFrame.callFrameId,
    expression: 'value',
    returnByValue: true,
    awaitPromise: false,
    throwOnSideEffect: true
  });
  assert.equal(evaluation.result.value, 2, 'The paused frame should expose the pre-assignment local value.');
  await inspector.command('Debugger.resume');
  await waitFor(() => /runmind-debugger-test\s+5/.test(stdout), 5000, 'debugged script output');
  socket.close(); // Node keeps the process alive while an Inspector client remains attached.
  await waitFor(() => child.exitCode !== null, 5000, 'debugger detach and Node process exit');
  assert.match(stdout, /runmind-debugger-test\s+5/);
});

test('RunMind debugger tools are shared, approved, bounded, and restricted to Node loopback targets', () => {
  const bridge = fs.readFileSync(bridgePath, 'utf8');
  const host = fs.readFileSync(hostPath, 'utf8');
  const tools = fs.readFileSync(toolsPath, 'utf8');
  const providers = fs.readFileSync(providersPath, 'utf8');
  for (const operation of ['debugger.launch','debugger.attach','debugger.sessions.list','debugger.sessions.events','debugger.sessions.command','debugger.sessions.stop'])
    assert.ok(host.includes(`"${operation}"`), `Missing native dispatch operation ${operation}.`);
  assert.match(bridge, /--inspect-brk=127\.0\.0\.1:0/);
  assert.match(bridge, /IPAddress\.IsLoopback\(address\)/);
  assert.match(bridge, /string\.Equals\(typeValue\.GetString\(\), "node"/);
  assert.match(bridge, /"Debugger\.evaluateOnCallFrame"/);
  assert.match(bridge, /\["throwOnSideEffect"\] = true/);
  assert.match(bridge, /MaximumNodeDebugSessions = 8/);
  assert.match(bridge, /if \(waitForInitialPause\)/, 'Attach mode must not release --inspect-brk automatically.');
  assert.match(bridge, /public async Task<object> ResumeAsync/, 'Explicit Resume must be able to release an attached --inspect-brk target.');
  assert.match(bridge, /DrainOutputAsync\(StreamReader reader/, 'Process output must be captured with bounded chunk reads.');
  assert.match(tools, /title: 'Launch this Node\.js script under the debugger\?'/);
  assert.match(tools, /title: 'Evaluate an expression in the paused Node\.js process\?'/);
  assert.match(tools, /taskContext\?\.subAgentDepth/);
  for (const name of ['node_debugger_launch','node_debugger_attach','node_debugger_list_sessions','node_debugger_events','node_debugger_command','node_debugger_stop'])
    assert.ok(providers.includes(`name: '${name}'`), `Missing exposed debugger tool ${name}.`);
});

test('all three host projects compile the shared Node debugger bridge', () => {
  for (const project of [
    'RunMind.Standalone/RunMind.Standalone.csproj',
    'Community.PowerToys.Run.Plugin.RunMind/Community.PowerToys.Run.Plugin.RunMind.csproj',
    'RunMind.CommandPalette/RunMind.CommandPalette.csproj'
  ]) {
    const source = fs.readFileSync(path.join(root, project), 'utf8');
    assert.match(source, /NodeDebuggerBridge\.cs/);
  }
});

test('Node Inspector attach can pause and resume an already-running local Node process', async t => {
  assert.equal(typeof WebSocket, 'function');
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-node-attach-'));
  const scriptPath = path.join(directory, 'attach-target.cjs');
  fs.writeFileSync(scriptPath, "let attachCounter = 0;\nsetInterval(() => { attachCounter += 1; }, 30);\n", 'utf8');
  const child = spawn(process.execPath, ['--inspect=127.0.0.1:0', scriptPath], { cwd: directory, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  let stderr = '';
  child.stderr.setEncoding('utf8');
  child.stderr.on('data', chunk => { stderr = (stderr + chunk).slice(-16000); });
  let socket;
  t.after(async () => {
    try { socket?.close(); } catch (_) {}
    try { if (child.exitCode === null) child.kill(); } catch (_) {}
    try { fs.rmSync(directory, { recursive: true, force: true }); } catch (_) {}
  });

  const endpointMatch = await waitFor(() => stderr.match(/Debugger listening on (ws:\/\/127\.0\.0\.1:\d+\/[^\s]+)/), 8000, 'attach target Inspector endpoint');
  const endpoint = endpointMatch[1];
  const port = new URL(endpoint).port;
  const response = await fetch(`http://127.0.0.1:${port}/json/list`);
  assert.equal(response.status, 200);
  const targets = await response.json();
  assert.ok(targets.some(target => target.type === 'node'));

  socket = await connectWebSocket(endpoint);
  const inspector = createInspectorClient(socket);
  await inspector.command('Debugger.enable');
  await inspector.command('Runtime.enable');
  await inspector.command('Debugger.setAsyncCallStackDepth', { maxDepth: 32 });
  await inspector.command('Debugger.pause');
  const paused = await inspector.waitEvent(event => event.method === 'Debugger.paused');
  assert.ok(paused.params.callFrames.length > 0, 'Attaching to a running process should permit an explicit pause.');
  await inspector.command('Debugger.resume');
  await inspector.waitEvent(event => event.method === 'Debugger.resumed');
});
