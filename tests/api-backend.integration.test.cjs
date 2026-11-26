'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const http = require('node:http');
const { validateRequest, run, getJsonPath, MAX_REQUESTS, MAX_RESPONSE_BYTES } = require('../Shared/ApiTestRunner.cjs');

async function withServer(handler, callback) {
  const server = http.createServer(handler);
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(0, '127.0.0.1', resolve); });
  const address = server.address();
  try { return await callback(`http://127.0.0.1:${address.port}`); }
  finally { await new Promise(resolve => server.close(resolve)); }
}

test('API request validation accepts bounded same-origin HTTP test suites', () => {
  const request = validateRequest({ base_url: 'http://127.0.0.1:3000/api', requests: [
    { name: 'health', method: 'GET', path: '/health', expect: { status: 200, json: [{ path: 'status', operator: 'equals', value: 'ok' }] } },
    { name: 'create', method: 'POST', path: '/items', body_json: { name: 'demo' }, expect: { status: [200, 201] } }
  ] });
  assert.equal(request.requests.length, 2);
  assert.equal(request.requests[0].method, 'GET');
  assert.equal(MAX_REQUESTS, 20);
});

test('API validation rejects unsafe schemes, credentials, cross-origin paths and invalid methods', () => {
  const valid = { base_url: 'http://localhost:3000', requests: [{ path: '/' }] };
  assert.throws(() => validateRequest({ ...valid, base_url: 'file:///etc/passwd' }), /HTTP\/HTTPS/);
  assert.throws(() => validateRequest({ ...valid, base_url: 'http://user:secret@localhost:3000' }), /credentials/);
  assert.throws(() => validateRequest({ ...valid, base_url: 'http://localhost:3000?token=secret' }), /query/);
  assert.throws(() => validateRequest({ ...valid, requests: [{ path: 'http://example.com/' }] }), /base URL origin/);
  assert.throws(() => validateRequest({ ...valid, requests: [{ method: 'TRACE', path: '/' }] }), /unsupported HTTP method/);
  assert.throws(() => validateRequest({ ...valid, requests: [{ method: 'GET', path: '/', body_json: {} }] }), /cannot include a body/);
});

test('API validation enforces request, body, assertion and header bounds', () => {
  const base = { base_url: 'http://localhost:3000', requests: [{ path: '/' }] };
  assert.throws(() => validateRequest({ ...base, requests: [] }), /between 1 and 20/);
  assert.throws(() => validateRequest({ ...base, requests: Array.from({ length: 21 }, (_, i) => ({ path: `/${i}` })) }), /between 1 and 20/);
  assert.throws(() => validateRequest({ ...base, requests: [{ path: '/', body_text: 'x'.repeat(65537) }] }), /64 KB/);
  assert.throws(() => validateRequest({ ...base, requests: [{ path: '/', expect: { json: Array.from({ length: 11 }, () => ({ path: 'id' })) } }] }), /at most 10 JSON/);
  assert.throws(() => validateRequest({ ...base, requests: [{ path: '/', headers: { 'bad header': 'x' } }] }), /invalid header/);
});

test('JSON path reader handles nested members and array indexes without prototype traversal', () => {
  const data = { user: { roles: ['reader', 'writer'] } };
  assert.deepEqual(getJsonPath(data, 'user.roles[1]'), { exists: true, value: 'writer' });
  assert.deepEqual(getJsonPath(data, 'user.missing'), { exists: false });
  assert.deepEqual(getJsonPath(data, '__proto__.polluted'), { exists: false });
});

test('API runner executes GET and POST requests with status, JSON and header assertions', async () => {
  await withServer((req, res) => {
    if (req.url === '/health') { res.writeHead(200, { 'content-type': 'application/json', 'x-service': 'catalog' }); res.end(JSON.stringify({ status: 'ok', count: 3, user: { access_token: 'should-not-leak' } })); return; }
    if (req.url === '/items' && req.method === 'POST') { let body = ''; req.on('data', chunk => body += chunk); req.on('end', () => { const item = JSON.parse(body); res.writeHead(201, { 'content-type': 'application/json' }); res.end(JSON.stringify({ id: 42, name: item.name })); }); return; }
    res.writeHead(404); res.end('missing');
  }, async baseUrl => {
    const result = await run({ base_url: baseUrl, requests: [
      { name: 'health', path: '/health', expect: { status: 200, headers: [{ name: 'x-service', operator: 'equals', value: 'catalog' }], json: [{ path: 'status', operator: 'equals', value: 'ok' }, { path: 'count', operator: 'greater_than', value: 2 }, { path: 'user.access_token', operator: 'exists' }] } },
      { name: 'create-item', method: 'POST', path: '/items', body_json: { name: 'widget' }, expect: { status: 201, json: [{ path: 'id', operator: 'equals', value: 42 }, { path: 'name', operator: 'contains', value: 'wid' }] } }
    ] });
    assert.equal(result.success, true);
    assert.equal(result.passedRequests, 2);
    assert.equal(result.results[0].assertions.every(item => item.passed), true);
    assert.equal(result.results[0].bodyPreview.includes('should-not-leak'), false);
    assert.equal(result.results[1].status, 201);
  });
});

test('API runner resolves environment placeholders without returning secret values', async () => {
  await withServer((req, res) => {
    assert.equal(req.headers.authorization, 'Bearer test-secret-value');
    res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify({ ok: true, echo: 'test-secret-value' }));
  }, async baseUrl => {
    const result = await run({ base_url: baseUrl, requests: [{ path: '/?debug=test-secret-value', headers: { Authorization: 'Bearer {{env:RUNMIND_TEST_TOKEN}}' }, expect: { json: [{ path: 'ok', operator: 'equals', value: true }] } }] }, { env: { RUNMIND_TEST_TOKEN: 'test-secret-value' } });
    assert.equal(result.success, true);
    assert.equal(JSON.stringify(result).includes('test-secret-value'), false);
    assert.equal(result.results[0].path, '/?debug=[REDACTED]');
  });
});

test('API runner blocks redirects that leave the approved origin', async () => {
  await withServer((req, res) => { res.writeHead(302, { location: 'http://example.com/private' }); res.end(); }, async baseUrl => {
    const result = await run({ base_url: baseUrl, requests: [{ path: '/' }] });
    assert.equal(result.success, false);
    assert.match(result.results[0].error, /left the approved base URL origin/);
  });
});

test('API runner reports assertion failures and continues with later requests', async () => {
  await withServer((req, res) => { res.writeHead(req.url === '/missing' ? 404 : 200, { 'content-type': 'text/plain' }); res.end(req.url); }, async baseUrl => {
    const result = await run({ base_url: baseUrl, requests: [
      { path: '/missing' },
      { path: '/ok', expect: { status: 200, body_contains: '/ok' } }
    ] });
    assert.equal(result.success, false);
    assert.equal(result.failedRequests, 1);
    assert.equal(result.passedRequests, 1);
  });
});

test('API response size limit is fixed at one megabyte', () => {
  assert.equal(MAX_RESPONSE_BYTES, 1024 * 1024);
});

test('all three native hosts deploy the shared API runner and native dispatch route', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const root = path.join(__dirname, '..');
  for (const project of ['RunMind.Standalone/RunMind.Standalone.csproj', 'Community.PowerToys.Run.Plugin.RunMind/Community.PowerToys.Run.Plugin.RunMind.csproj', 'RunMind.CommandPalette/RunMind.CommandPalette.csproj']) {
    const xml = fs.readFileSync(path.join(root, project), 'utf8');
    assert.match(xml, /ApiTestRunner\.cjs/);
  }
  const bridge = fs.readFileSync(path.join(root, 'Shared/AgentHostBridge.cs'), 'utf8');
  assert.match(bridge, /"api\.test\.run"\s*=>\s*ApiTestRunAsync/);
  assert.match(bridge, /ApiTestRunAsync\(JsonElement args/);
});

test('Agent exposes API testing with fresh approval and disables delegated execution', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const root = path.join(__dirname, '..');
  const providers = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/providers.js'), 'utf8');
  const tools = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/agent/tools.js'), 'utf8');
  assert.match(providers, /name: 'api_test_run'/);
  assert.match(tools, /name === 'api_test_run'/);
  assert.match(tools, /alwaysAsk: true/);
  assert.match(tools, /API\/backend testing is disabled in Deep Research and delegated sub-agent tasks/);
  assert.match(tools, /api\.test\.run/);
});

test('API runner follows same-origin redirects using standard POST-to-GET semantics', async () => {
  let finalMethod = '';
  await withServer((req, res) => {
    if (req.url === '/start') { res.writeHead(303, { location: '/finish' }); res.end(); return; }
    finalMethod = req.method;
    res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify({ method: finalMethod }));
  }, async baseUrl => {
    const result = await run({ base_url: baseUrl, requests: [{ method: 'POST', path: '/start', body_json: { ok: true }, expect: { json: [{ path: 'method', operator: 'equals', value: 'GET' }] } }] });
    assert.equal(result.success, true);
    assert.equal(finalMethod, 'GET');
  });
});

test('API runner aborts responses larger than one megabyte', async () => {
  await withServer((req, res) => { res.writeHead(200, { 'content-type': 'text/plain' }); res.end('x'.repeat(MAX_RESPONSE_BYTES + 1)); }, async baseUrl => {
    const result = await run({ base_url: baseUrl, requests: [{ path: '/' }] });
    assert.equal(result.success, false);
    assert.match(result.results[0].error, /exceeded the 1048576 byte limit/);
  });
});
