'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const childProcess = require('node:child_process');
const { validateRequest, parseInputJson, run, MAX_ACTIONS } = require('../Shared/PlaywrightRunner.cjs');

function makeFakePlaywright(options = {}) {
  const failChannels = new Set(options.failChannels || []);
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
  assert.match(app, /if \(event\.isTrusted\) status\.textContent = 'Form cleared\.'/);
  assert.match(server, /pathname === '\/health'/);
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

test('Playwright action schema requires selector and value fields on every action object', () => {
  const providers = fs.readFileSync(path.join(__dirname, '..', 'Shared', 'ChatWindow', 'scripts', 'providers.js'), 'utf8');
  assert.match(providers, /required: \['action','selector','value'\], additionalProperties: false \} \} \}, required: \['actions'\]/);
  assert.match(providers, /use an empty string only for goto, inspect_page, expect_url, expect_title, and screenshot/i);
  assert.match(providers, /use an empty string for actions that do not consume a value/i);
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

test('Playwright request validation rejects unsupported or malformed actions', () => {
  assert.throws(() => validateRequest({ actions: [{ action: 'evaluate', value: 'process.exit()' }] }), /unsupported action/);
  assert.throws(() => validateRequest({ actions: [{ action: 'click' }] }), /action 1 \('click'\) requires a non-empty selector/);
  assert.throws(() => validateRequest({ actions: [{ action: 'fill', selector: '#name' }] }), /action 1 \('fill'\) requires a non-empty value/);
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
  assert.match(runtime, /RunMind stopped this task because the same tool failure was returned twice/);
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
