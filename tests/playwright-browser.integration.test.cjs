'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const childProcess = require('node:child_process');
const vm = require('node:vm');
const { validateRequest, parseInputJson, run, MAX_ACTIONS } = require('../Shared/PlaywrightRunner.cjs');

function makeFakePlaywright(options = {}) {
  const failChannels = new Set(options.failChannels || []);
  const calls = [];
  const handlers = new Map();
  const page = {
    setDefaultTimeout(value) { calls.push(['setDefaultTimeout', value]); },
    setDefaultNavigationTimeout(value) { calls.push(['setDefaultNavigationTimeout', value]); },
    on(name, callback) { handlers.set(name, callback); },
    async goto(url) { calls.push(['goto', url]); page.currentUrl = url; for (const event of options.consoleEvents || []) handlers.get('console')?.({ type: () => event.type || 'error', text: () => event.text }); for (const request of options.failedRequests || []) handlers.get('requestfailed')?.({ url: () => request.url, failure: () => ({ errorText: request.error || 'net::ERR_FAILED' }) }); },
    url() { return page.currentUrl || 'about:blank'; },
    async title() { return 'RunMind Test Page'; },
    locator(selector) {
      const record = (name, ...args) => { calls.push([name, selector, ...args]); };
      return {
        async click() { record('click'); }, async fill(value) { record('fill', value); },
        async press(value) { record('press', value); }, async check() { record('check'); },
        async uncheck() { record('uncheck'); }, async selectOption(value) { record('selectOption', value); },
        async waitFor(options) { record('waitFor', options); }, async innerText() { record('innerText'); return 'Hello Playwright'; },
        async count() { record('count'); return 1; },
        async evaluate() { record('evaluate'); return { document: { title: 'Fixture', language: 'en', url: 'http://127.0.0.1:4320/' }, bodyText: 'Fixture content', landmarks: [{ tag: 'main', role: '', name: 'Main content' }], landmarksTruncated: false, interactiveElements: [{ tag: 'button', role: 'button', name: 'Submit', selectorHint: '#submit', type: '', disabled: false, required: false, checked: null, visible: true }], interactiveElementsTruncated: false }; },
        async ariaSnapshot() { record('ariaSnapshot'); return '- main:\n  - heading \"Fixture\" [level=1]'; }
      };
    },
    async screenshot(options) { calls.push(['screenshot', options]); fs.writeFileSync(options.path, Buffer.from('fake-png')); }
  };
  let closed = false;
  const routes = [];
  const context = { async route(pattern, handler) { routes.push({ pattern, handler }); }, async newPage() { return page; }, async close() { calls.push(['context.close']); } };
  const browser = { async newContext(options) { calls.push(['newContext', options]); return context; }, async close() { closed = true; calls.push(['browser.close']); } };
  return { calls, handlers, routes, browser, page, get closed() { return closed; }, playwright: { chromium: { async launch(launchOptions) { calls.push(['launch', launchOptions]); const channel = launchOptions.channel || 'bundled'; if (failChannels.has(channel)) { const error = new Error(`Executable doesn't exist for ${channel}`); error.code = 'ENOENT'; throw error; } return browser; } }, firefox: {}, webkit: {} } };
}

test('local browser fixture is a real accessible HTML application with a matching dev script', () => {
  const root = path.resolve(__dirname, '..');
  const fixture = path.join(root, 'test-fixtures', 'playwright-app');
  const manifest = JSON.parse(fs.readFileSync(path.join(fixture, 'package.json'), 'utf8'));
  const html = fs.readFileSync(path.join(fixture, 'index.html'), 'utf8');
  const app = fs.readFileSync(path.join(fixture, 'app.js'), 'utf8');
  const server = fs.readFileSync(path.join(fixture, 'server.cjs'), 'utf8');
  assert.equal(manifest.scripts.dev, 'node server.cjs 4320');
  for (const marker of ['<html lang="en">','<main id="main">','<nav aria-label="Primary navigation">','<label for="request-name">','aria-live="polite"','id="request-list"']) assert.ok(html.includes(marker), marker);
  assert.match(app, /addEventListener\('submit'/);
  assert.match(server, /pathname === '\/health'/);
  assert.match(html, /id="cors-probe"/);
  assert.match(app, /fetch\('http:\/\/127\.0\.0\.1:4321\/cors-probe'\)/);
  assert.match(server, /pathname === '\/cors-probe'/);
  assert.match(server, /CORS fixture response is intentionally not cross-origin readable/);
  assert.doesNotMatch(server, /writeHead\([^\n]*Access-Control-Allow-Origin/);
});

test('Playwright expected CORS policy blocks remain visible but are not application console errors or failed requests', async () => {
  const corsText = "Access to fetch at 'https://api.example.test/items' from origin 'http://127.0.0.1:4320' has been blocked by CORS policy: No 'Access-Control-Allow-Origin' header is present.";
  const fake = makeFakePlaywright({
    consoleEvents: [{ type: 'error', text: corsText }],
    failedRequests: [{ url: 'https://api.example.test/items', error: 'net::ERR_FAILED' }]
  });
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-playwright-cors-'));
  try {
    const result = await run({ expected_policy_blocks: [{ type: 'cors', url_contains: 'https://api.example.test/items' }], actions: [
      { action: 'goto', selector: '', value: 'http://127.0.0.1:4320/' }
    ] }, { playwright: fake.playwright, artifactDirectory: directory });
    assert.equal(result.success, true);
    assert.equal(result.consoleErrors.length, 0);
    assert.equal(result.failedRequests.length, 0);
    assert.equal(result.expectedPolicyBlocks.length, 1);
    assert.equal(result.unexpectedPolicyBlocks.length, 0);
    assert.equal(result.diagnostics.expectedPolicyBlockCount, 1);
    assert.equal(result.policyBlocks[0].type, 'cors');
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('Playwright unexpected CORS blocks are classified separately and ordinary transport failures remain visible', async () => {
  const fake = makeFakePlaywright({
    consoleEvents: [{ type: 'error', text: "Access to fetch at 'https://api.example.test/items' from origin 'http://127.0.0.1:4320' has been blocked by CORS policy." }],
    failedRequests: [
      { url: 'https://api.example.test/items', error: 'net::ERR_FAILED' },
      { url: 'https://offline.example.test/data', error: 'net::ERR_CONNECTION_REFUSED' }
    ]
  });
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-playwright-cors-unexpected-'));
  try {
    const result = await run({ actions: [{ action: 'goto', selector: '', value: 'http://127.0.0.1:4320/' }] }, { playwright: fake.playwright, artifactDirectory: directory });
    assert.equal(result.consoleErrors.length, 0);
    assert.equal(result.unexpectedPolicyBlocks.length, 1);
    assert.equal(result.failedRequests.length, 1);
    assert.match(result.failedRequests[0].url, /offline\.example\.test/);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('Playwright reports expected policy blocks that did not occur', async () => {
  const fake = makeFakePlaywright();
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-playwright-cors-missing-'));
  try {
    const result = await run({ expected_policy_blocks: [{ type: 'cors', url_contains: 'https://api.example.test' }], actions: [
      { action: 'goto', selector: '', value: 'http://127.0.0.1:4320/' }
    ] }, { playwright: fake.playwright, artifactDirectory: directory });
    assert.equal(result.expectedPolicyBlocks.length, 0);
    assert.equal(result.missingExpectedPolicyBlocks.length, 1);
    assert.equal(result.diagnostics.missingExpectedPolicyBlockCount, 1);
    assert.equal(result.success, false, 'an explicitly expected policy event that did not occur must fail the test');
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('Playwright handles policy console events without URLs without leaking an out-of-scope variable', async () => {
  const fake = makeFakePlaywright({
    consoleEvents: [{ type: 'error', text: 'Cross-Origin Request Blocked: The Same Origin Policy disallows reading the remote resource.' }],
    failedRequests: [{ url: 'https://api.example.test/items', error: 'net::ERR_FAILED' }]
  });
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-playwright-cors-no-url-'));
  try {
    const result = await run({ expected_policy_blocks: [{ type: 'cors', message_contains: 'Cross-Origin Request Blocked' }], actions: [
      { action: 'goto', selector: '', value: 'http://127.0.0.1:4320/' }
    ] }, { playwright: fake.playwright, artifactDirectory: directory });
    assert.equal(result.success, true);
    assert.equal(result.expectedPolicyBlocks.length, 1);
    // The console message did not identify the URL, so the request-failure event
    // cannot safely be attributed to that policy event and remains visible.
    assert.equal(result.failedRequests.length, 1);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('unexpected browser policy blocks fail the run but remain separate from ordinary console and request errors', async () => {
  const corsText = "Access to fetch at 'https://api.example.test/private' from origin 'http://127.0.0.1:4320' has been blocked by CORS policy: No 'Access-Control-Allow-Origin' header is present.";
  const fake = makeFakePlaywright({ consoleEvents: [{ type: 'error', text: corsText }], failedRequests: [{ url: 'https://api.example.test/private', error: 'net::ERR_FAILED' }] });
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-playwright-unexpected-policy-'));
  try {
    const result = await run({ actions: [{ action: 'goto', selector: '', value: 'http://127.0.0.1:4320/' }] }, { playwright: fake.playwright, artifactDirectory: directory });
    assert.equal(result.success, false);
    assert.equal(result.consoleErrors.length, 0);
    assert.equal(result.failedRequests.length, 0);
    assert.equal(result.unexpectedPolicyBlocks.length, 1);
    assert.equal(result.diagnostics.unexpectedPolicyBlockCount, 1);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('Playwright request validation accepts a bounded structured workflow', () => {
  const result = validateRequest({ actions: [
    { action: 'goto', value: 'http://127.0.0.1:3000' },
    { action: 'fill', selector: '#email', value: 'user@example.test' },
    { action: 'expect_text', selector: 'h1', value: 'Welcome' },
    { action: 'screenshot', name: 'landing-page' }
  ] });
  assert.equal(result.browser, 'chromium');
  assert.equal(result.channel, 'auto');
  assert.equal(result.actions.length, 4);
});

test('Playwright selects installed Edge/Chrome before bundled Chromium and supports explicit channels', () => {
  assert.equal(validateRequest({ channel: 'msedge', actions: [{ action: 'inspect_page' }] }).channel, 'msedge');
  assert.equal(validateRequest({ channel: 'chrome', actions: [{ action: 'inspect_page' }] }).channel, 'chrome');
  assert.throws(() => validateRequest({ browser: 'firefox', channel: 'msedge', actions: [{ action: 'inspect_page' }] }), /only with Chromium/);
  assert.throws(() => validateRequest({ channel: 'safari', actions: [{ action: 'inspect_page' }] }), /channel must be/);
});

test('Playwright auto channel falls back from installed Edge to Chrome before bundled Chromium', async () => {
  const fake = makeFakePlaywright({ failChannels: ['msedge'] });
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-playwright-channels-'));
  try {
    const result = await run({ actions: [{ action: 'inspect_page' }] }, { playwright: fake.playwright, artifactDirectory: directory });
    assert.equal(result.success, true);
    assert.deepEqual(fake.calls.filter(call => call[0] === 'launch').map(call => call[1].channel), ['msedge', 'chrome']);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('Playwright JSON input accepts a UTF-8 BOM without weakening JSON validation', () => {
  assert.deepEqual(parseInputJson('\uFEFF{"browser":"chromium","actions":[]}'), { browser: 'chromium', actions: [] });
  assert.throws(() => parseInputJson('\uFEFF{broken'), SyntaxError);
  const root = path.resolve(__dirname, '..');
  const runner = path.join(root, 'Shared', 'PlaywrightRunner.cjs');
  const child = childProcess.spawnSync(process.execPath, [runner], { input: `\uFEFF${JSON.stringify({ actions: [] })}`, encoding: 'utf8', timeout: 5000 });
  assert.equal(child.status, 1);
  assert.match(child.stdout, /between 1 and 30 browser actions/);
  assert.doesNotMatch(child.stdout, /Unexpected token/);
});

test('Playwright request validation rejects non-web schemes and embedded credentials', () => {
  assert.throws(() => validateRequest({ actions: [{ action: 'goto', value: 'javascript:alert(1)' }] }), /HTTP\/HTTPS/);
  assert.throws(() => validateRequest({ actions: [{ action: 'goto', value: 'https://user:secret@example.test/' }] }), /embedded credentials/);
  assert.throws(() => validateRequest({ actions: [{ action: 'goto', value: 'file:///etc/passwd' }] }), /HTTP\/HTTPS/);
});

test('Playwright expected policy block rules must be narrow and supported', () => {
  assert.deepEqual(validateRequest({ expected_policy_blocks: [{ type: 'cors', url_contains: 'api.example.test' }], actions: [{ action: 'inspect_page' }] }).expectedPolicyBlocks,
    [{ type: 'cors', url_contains: 'api.example.test', message_contains: '' }]);
  assert.throws(() => validateRequest({ expected_policy_blocks: [{ type: 'cors' }], actions: [{ action: 'inspect_page' }] }), /must include url_contains or message_contains/);
  assert.throws(() => validateRequest({ expected_policy_blocks: [{ type: 'any' , message_contains: 'blocked' }], actions: [{ action: 'inspect_page' }] }), /unsupported type/);
});

test('Playwright request validation rejects unsupported or malformed actions', () => {
  assert.throws(() => validateRequest({ actions: [{ action: 'evaluate', value: 'process.exit()' }] }), /unsupported action/);
  assert.throws(() => validateRequest({ actions: [{ action: 'click' }] }), /requires a non-empty selector/);
  assert.throws(() => validateRequest({ actions: [{ action: 'fill', selector: '#name' }] }), /requires a non-empty value/);
  assert.throws(() => validateRequest({ actions: [{ action: 'wait_for', selector: '#name', state: 'magic' }] }), /invalid wait state/);
});

test('Playwright selector failures identify the exact action and show a useful correction', () => {
  assert.throws(() => validateRequest({ actions: [
    { action: 'goto', value: 'http://127.0.0.1:4320/' },
    { action: 'inspect_page' },
    { action: 'fill', value: 'RunMind acceptance test' }
  ] }), /Action 3 \('fill'\) requires a non-empty selector.*#request-name/s);
  assert.throws(() => validateRequest({ actions: [
    { action: 'goto', value: 'http://127.0.0.1:4320/' },
    { action: 'select_option', selector: '#request-priority' }
  ] }), /Action 2 \('select_option'\) requires a non-empty value/);
});

test('Playwright fixture preserves submit feedback while explicit reset reports Form cleared', () => {
  const root = path.resolve(__dirname, '..');
  const app = fs.readFileSync(path.join(root, 'test-fixtures', 'playwright-app', 'app.js'), 'utf8');
  assert.match(app, /resetFollowsSubmission = true;\s*form\.reset\(\)/);
  assert.match(app, /if \(resetFollowsSubmission\) \{\s*resetFollowsSubmission = false;\s*return;/);
  assert.match(app, /status\.textContent = 'Form cleared\.';/);
  assert.ok(app.indexOf('Request submitted: ${name}.') < app.indexOf('resetFollowsSubmission = true;'));
});

test('Playwright fixture executes submit/reset behavior without overwriting successful feedback', () => {
  const root = path.resolve(__dirname, '..');
  const source = fs.readFileSync(path.join(root, 'test-fixtures', 'playwright-app', 'app.js'), 'utf8');
  const documentListeners = new Map();
  const formListeners = new Map();
  const nameField = { value: 'RunMind acceptance test', focus() {} };
  const priorityField = { value: 'high' };
  const status = { textContent: '' };
  const listItems = [];
  const list = { append(item) { listItems.push(item); } };
  const emptyState = { removed: false, remove() { this.removed = true; } };
  const form = {
    addEventListener(name, handler) { formListeners.set(name, handler); },
    reset() {
      nameField.value = '';
      priorityField.value = 'normal';
      formListeners.get('reset')?.();
    }
  };
  const elements = new Map([
    ['#request-form', form], ['#request-name', nameField], ['#request-priority', priorityField],
    ['#submission-status', status], ['#request-list', list], ['#empty-state', emptyState]
  ]);
  const document = {
    addEventListener(name, handler) { documentListeners.set(name, handler); },
    querySelector(selector) { return elements.get(selector) || null; },
    createElement(tag) { return { tag, children: [], append(...children) { this.children.push(...children); }, textContent: '' }; }
  };
  vm.runInNewContext(source, { document }, { timeout: 1000 });
  documentListeners.get('DOMContentLoaded')();
  formListeners.get('submit')({ preventDefault() {} });
  assert.equal(status.textContent, 'Request submitted: RunMind acceptance test.');
  assert.equal(nameField.value, '');
  assert.equal(priorityField.value, 'normal');
  assert.equal(listItems.length, 1);
  assert.equal(emptyState.removed, true);
  form.reset();
  assert.equal(status.textContent, 'Form cleared.');
});

test('Playwright request validation enforces action and field bounds', () => {
  assert.equal(MAX_ACTIONS, 30);
  assert.throws(() => validateRequest({ actions: [] }), /between 1 and 30/);
  assert.throws(() => validateRequest({ actions: Array.from({ length: 31 }, () => ({ action: 'goto', value: 'http://localhost' })) }), /between 1 and 30/);
  assert.throws(() => validateRequest({ actions: [{ action: 'click', selector: 'x'.repeat(1001) }] }), /selector exceeds/);
  assert.throws(() => validateRequest({ actions: [{ action: 'fill', selector: '#x', value: 'x'.repeat(16001) }] }), /value exceeds/);
  assert.throws(() => validateRequest({ browser: 'edge', actions: [{ action: 'goto', value: 'http://localhost' }] }), /Browser must be/);
});

test('Playwright runner executes structured navigation, assertions, reads and screenshots', async () => {
  const fake = makeFakePlaywright();
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-playwright-'));
  try {
    const result = await run({ actions: [
      { action: 'goto', value: 'http://127.0.0.1:3000/' },
      { action: 'fill', selector: '#email', value: 'user@example.test' },
      { action: 'click', selector: 'button[type=submit]' },
      { action: 'expect_text', selector: 'h1', value: 'Hello' },
      { action: 'expect_url', value: '127.0.0.1:3000', match: 'contains' },
      { action: 'expect_title', value: 'Test Page', match: 'contains' },
      { action: 'expect_visible', selector: 'main' },
      { action: 'read_text', selector: 'h1' },
      { action: 'count', selector: 'li' },
      { action: 'screenshot', name: 'test-shot' }
    ] }, { playwright: fake.playwright, artifactDirectory: directory });
    assert.equal(result.success, true);
    assert.equal(result.completedSteps, 10);
    assert.equal(result.title, 'RunMind Test Page');
    assert.equal(result.steps[7].result.text, 'Hello Playwright');
    assert.equal(result.steps[8].result.count, 1);
    assert.equal(fs.existsSync(result.steps[9].result.artifact), true);
    assert.equal(fake.closed, true);
    assert.ok(fake.calls.some(call => call[0] === 'context.close'));
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('Playwright page inspection returns bounded landmarks, accessible controls, and an ARIA snapshot', async () => {
  const fake = makeFakePlaywright();
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-playwright-inspect-'));
  try {
    const result = await run({ actions: [{ action: 'inspect_page' }] }, { playwright: fake.playwright, artifactDirectory: directory });
    assert.equal(result.success, true);
    const inspection = result.steps[0].result;
    assert.equal(inspection.document.title, 'Fixture');
    assert.equal(inspection.landmarks[0].tag, 'main');
    assert.equal(inspection.interactiveElements[0].name, 'Submit');
    assert.equal(inspection.accessibility.method, 'Playwright Locator.ariaSnapshot');
    assert.match(inspection.accessibility.snapshot, /heading/);
    assert.equal(fake.closed, true);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('Playwright runner stops at the first failed assertion and closes the browser', async () => {
  const fake = makeFakePlaywright();
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-playwright-'));
  try {
    const result = await run({ actions: [
      { action: 'goto', value: 'http://127.0.0.1:3000/' },
      { action: 'expect_text', selector: 'h1', value: 'Not the page text', match: 'exact' },
      { action: 'click', selector: '#never-run' }
    ] }, { playwright: fake.playwright, artifactDirectory: directory });
    assert.equal(result.success, false);
    assert.equal(result.completedSteps, 1);
    assert.equal(result.steps[1].ok, false);
    assert.equal(result.steps.length, 2);
    assert.equal(fake.closed, true);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('Playwright runner blocks local-file and privileged browser navigation while allowing HTTP(S)', async () => {
  const fake = makeFakePlaywright();
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'runmind-playwright-'));
  try {
    await run({ actions: [{ action: 'goto', value: 'http://localhost/' }] }, { playwright: fake.playwright, artifactDirectory: directory });
    assert.equal(fake.routes.length, 1);
    assert.equal(fake.routes[0].pattern, '**/*');
    const handler = fake.routes[0].handler;
    async function check(url, navigation = true) {
      const state = { continued: false, aborted: false };
      const frame = { parentFrame: () => null };
      await handler({ request: () => ({ url: () => url, isNavigationRequest: () => navigation, frame: () => frame }), continue: async () => { state.continued = true; }, abort: async () => { state.aborted = true; } });
      return state;
    }
    assert.equal((await check('file:///C:/Windows/win.ini')).aborted, true);
    assert.equal((await check('chrome://settings')).aborted, true);
    assert.equal((await check('data:text/html,hello')).aborted, true);
    assert.equal((await check('https://example.test/')).continued, true);
    assert.equal((await check('https://example.test/image.png', false)).continued, true);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('Playwright runner reports a missing project dependency with exact setup guidance', async () => {
  const error = Object.assign(new Error("Cannot find module 'playwright'"), { code: 'MODULE_NOT_FOUND' });
  await assert.rejects(() => run({ actions: [{ action: 'goto', value: 'http://localhost/' }] }, { requireFromWorkspace() { throw error; } }), /npm install --save-dev playwright/);
});

test('Playwright runner reports missing system and bundled browsers with actionable guidance', async () => {
  const attempts = [];
  const playwright = { chromium: { async launch(options) { attempts.push(options.channel || 'bundled'); const error = new Error(`browserType.launch: Executable doesn't exist at C:\\browser\\chrome.exe`); error.code = 'ENOENT'; throw error; } } };
  await assert.rejects(() => run({ actions: [{ action: 'goto', value: 'http://localhost/' }] }, { playwright }), /npx playwright install chromium/);
  assert.deepEqual(attempts, ['msedge','chrome','bundled']);
});

test('Playwright runner uses an ephemeral context with downloads disabled and service workers blocked', async () => {
  const fake = makeFakePlaywright();
  await run({ actions: [{ action: 'goto', value: 'http://localhost/' }] }, { playwright: fake.playwright, artifactDirectory: os.tmpdir() });
  const contextCall = fake.calls.find(call => call[0] === 'newContext');
  assert.deepEqual(contextCall[1], { acceptDownloads: false, serviceWorkers: 'block' });
});

test('all three native hosts deploy the same Playwright runner and dispatch route', () => {
  const root = path.resolve(__dirname, '..');
  const bridge = fs.readFileSync(path.join(root, 'Shared/AgentHostBridge.cs'), 'utf8');
  assert.match(bridge, /"playwright\.run"\s*=>\s*PlaywrightRunAsync/);
  assert.match(bridge, /PlaywrightRunner\.cjs/);
  assert.match(bridge, /StandardInputEncoding = Utf8NoBom/);
  assert.match(bridge, /var channel = ReadString\(args, \"channel\", 30\)/);
  assert.match(bridge, /private static readonly UTF8Encoding Utf8NoBom = new\(encoderShouldEmitUTF8Identifier: false\)/);
  for (const file of [
    'RunMind.Standalone/RunMind.Standalone.csproj',
    'Community.PowerToys.Run.Plugin.RunMind/Community.PowerToys.Run.Plugin.RunMind.csproj',
    'RunMind.CommandPalette/RunMind.CommandPalette.csproj'
  ]) assert.match(fs.readFileSync(path.join(root, file), 'utf8'), /PlaywrightRunner\.cjs/);
});

test('Agent exposes the safe page inspector and stops repeated tool failures', () => {
  const root = path.resolve(__dirname, '..');
  const providers = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/providers.js'), 'utf8');
  const runtime = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/agent/runtime.js'), 'utf8');
  assert.match(providers, /semantic landmarks, interactive controls, accessible names/);
  assert.match(providers, /'inspect_page'/);
  assert.match(runtime, /failedToolCallFingerprints = new Map\(\)/);
  assert.match(runtime, /lastFailureByTool = new Map\(\)/);
  assert.match(runtime, /failedToolCallFingerprints\.clear\(\)/);
  assert.match(runtime, /same tool returns the same failure again/);
  assert.match(runtime, /ensureFinalAgentReport/);
  assert.match(runtime, /buildDeterministicFinalReport/);
  assert.match(runtime, /repeated-tool-failure/);
  assert.match(runtime, /tool-step-limit/);
});

test('Agent exposes Playwright as a structured, approval-gated primary-agent tool', () => {
  const root = path.resolve(__dirname, '..');
  const providers = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/providers.js'), 'utf8');
  const tools = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/agent/tools.js'), 'utf8');
  assert.match(providers, /name: 'playwright_run'/);
  assert.match(providers, /'goto','inspect_page','click','fill','press','check','uncheck','select_option'/);
  assert.match(providers, /auto prefers installed Microsoft Edge, then Google Chrome/);
  assert.match(tools, /name === 'playwright_run'/);
  assert.match(tools, /alwaysAsk: true/);
  assert.match(tools, /subAgentDepth/);
  assert.match(tools, /playwright\.run/);
});
