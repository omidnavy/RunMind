'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { validateRequest, run, MAX_ACTIONS } = require('../Shared/PlaywrightRunner.cjs');

function makeFakePlaywright() {
  const calls = [];
  const handlers = new Map();
  const page = {
    setDefaultTimeout(value) { calls.push(['setDefaultTimeout', value]); },
    setDefaultNavigationTimeout(value) { calls.push(['setDefaultNavigationTimeout', value]); },
    on(name, callback) { handlers.set(name, callback); },
    async goto(url) { calls.push(['goto', url]); page.currentUrl = url; },
    url() { return page.currentUrl || 'about:blank'; },
    async title() { return 'RunMind Test Page'; },
    locator(selector) {
      const record = (name, ...args) => { calls.push([name, selector, ...args]); };
      return {
        async click() { record('click'); }, async fill(value) { record('fill', value); },
        async press(value) { record('press', value); }, async check() { record('check'); },
        async uncheck() { record('uncheck'); }, async selectOption(value) { record('selectOption', value); },
        async waitFor(options) { record('waitFor', options); }, async innerText() { record('innerText'); return 'Hello Playwright'; },
        async count() { record('count'); return 1; }
      };
    },
    async screenshot(options) { calls.push(['screenshot', options]); fs.writeFileSync(options.path, Buffer.from('fake-png')); }
  };
  let closed = false;
  const routes = [];
  const context = { async route(pattern, handler) { routes.push({ pattern, handler }); }, async newPage() { return page; }, async close() { calls.push(['context.close']); } };
  const browser = { async newContext(options) { calls.push(['newContext', options]); return context; }, async close() { closed = true; calls.push(['browser.close']); } };
  return { calls, handlers, routes, browser, page, get closed() { return closed; }, playwright: { chromium: { async launch(options) { calls.push(['launch', options]); return browser; } }, firefox: {}, webkit: {} } };
}

test('Playwright request validation accepts a bounded structured workflow', () => {
  const result = validateRequest({ actions: [
    { action: 'goto', value: 'http://127.0.0.1:3000' },
    { action: 'fill', selector: '#email', value: 'user@example.test' },
    { action: 'expect_text', selector: 'h1', value: 'Welcome' },
    { action: 'screenshot', name: 'landing-page' }
  ] });
  assert.equal(result.browser, 'chromium');
  assert.equal(result.actions.length, 4);
});

test('Playwright request validation rejects non-web schemes and embedded credentials', () => {
  assert.throws(() => validateRequest({ actions: [{ action: 'goto', value: 'javascript:alert(1)' }] }), /HTTP\/HTTPS/);
  assert.throws(() => validateRequest({ actions: [{ action: 'goto', value: 'https://user:secret@example.test/' }] }), /embedded credentials/);
  assert.throws(() => validateRequest({ actions: [{ action: 'goto', value: 'file:///etc/passwd' }] }), /HTTP\/HTTPS/);
});

test('Playwright request validation rejects unsupported or malformed actions', () => {
  assert.throws(() => validateRequest({ actions: [{ action: 'evaluate', value: 'process.exit()' }] }), /unsupported action/);
  assert.throws(() => validateRequest({ actions: [{ action: 'click' }] }), /requires a selector/);
  assert.throws(() => validateRequest({ actions: [{ action: 'fill', selector: '#name' }] }), /requires a value/);
  assert.throws(() => validateRequest({ actions: [{ action: 'wait_for', selector: '#name', state: 'magic' }] }), /invalid wait state/);
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

test('Playwright runner reports a missing browser binary with install guidance', async () => {
  const playwright = { chromium: { async launch() { throw new Error(`browserType.launch: Executable doesn't exist at C:\\browser\\chrome.exe`); } } };
  await assert.rejects(() => run({ actions: [{ action: 'goto', value: 'http://localhost/' }] }, { playwright }), /npx playwright install chromium/);
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
  for (const file of [
    'RunMind.Standalone/RunMind.Standalone.csproj',
    'Community.PowerToys.Run.Plugin.RunMind/Community.PowerToys.Run.Plugin.RunMind.csproj',
    'RunMind.CommandPalette/RunMind.CommandPalette.csproj'
  ]) assert.match(fs.readFileSync(path.join(root, file), 'utf8'), /PlaywrightRunner\.cjs/);
});

test('Agent exposes Playwright as a structured, approval-gated primary-agent tool', () => {
  const root = path.resolve(__dirname, '..');
  const providers = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/providers.js'), 'utf8');
  const tools = fs.readFileSync(path.join(root, 'Shared/ChatWindow/scripts/agent/tools.js'), 'utf8');
  assert.match(providers, /name: 'playwright_run'/);
  assert.match(providers, /'goto','click','fill','press','check','uncheck','select_option'/);
  assert.match(tools, /name === 'playwright_run'/);
  assert.match(tools, /alwaysAsk: true/);
  assert.match(tools, /subAgentDepth/);
  assert.match(tools, /playwright\.run/);
});
