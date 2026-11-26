'use strict';

const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');

const MAX_INPUT_BYTES = 640 * 1024;
const MAX_ACTIONS = 30;
const MAX_TEXT = 16000;
const MAX_SELECTOR = 1000;
const MAX_OUTPUT_TEXT = 12000;
const MAX_SCREENSHOT_BYTES = 5 * 1024 * 1024;

function validateRequest(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) throw new Error('A Playwright request object is required.');
  const browser = String(input.browser || 'chromium').toLowerCase();
  if (!['chromium', 'firefox', 'webkit'].includes(browser)) throw new Error('Browser must be chromium, firefox, or webkit.');
  if (!Array.isArray(input.actions) || input.actions.length < 1 || input.actions.length > MAX_ACTIONS) throw new Error(`Provide between 1 and ${MAX_ACTIONS} browser actions.`);
  const allowed = new Set(['goto','click','fill','press','check','uncheck','select_option','wait_for','expect_text','expect_url','expect_title','expect_visible','expect_hidden','read_text','count','screenshot']);
  const actions = input.actions.map((raw, index) => {
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) throw new Error(`Action ${index + 1} must be an object.`);
    const action = String(raw.action || '').toLowerCase();
    if (!allowed.has(action)) throw new Error(`Action ${index + 1} has unsupported action '${action}'.`);
    const selector = String(raw.selector || '');
    const value = String(raw.value ?? '');
    if (selector.length > MAX_SELECTOR) throw new Error(`Action ${index + 1} selector exceeds ${MAX_SELECTOR} characters.`);
    if (value.length > MAX_TEXT) throw new Error(`Action ${index + 1} value exceeds ${MAX_TEXT} characters.`);
    if (['click','fill','press','check','uncheck','select_option','wait_for','expect_text','expect_visible','expect_hidden','read_text','count'].includes(action) && !selector.trim()) throw new Error(`Action ${index + 1} requires a selector.`);
    if (['goto','fill','press','select_option','expect_text','expect_url','expect_title'].includes(action) && !value) throw new Error(`Action ${index + 1} requires a value.`);
    if (action === 'goto') {
      let url;
      try { url = new URL(value); } catch { throw new Error(`Action ${index + 1} requires an absolute HTTP or HTTPS URL.`); }
      if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password) throw new Error(`Action ${index + 1} URL must use HTTP/HTTPS and cannot contain embedded credentials.`);
    }
    if (action === 'wait_for' && !['visible','hidden','attached','detached'].includes(String(raw.state || 'visible'))) throw new Error(`Action ${index + 1} has an invalid wait state.`);
    if (action === 'expect_text' && !['contains','exact'].includes(String(raw.match || 'contains'))) throw new Error(`Action ${index + 1} has an invalid text match mode.`);
    if (action === 'expect_url' && !['contains','exact'].includes(String(raw.match || 'contains'))) throw new Error(`Action ${index + 1} has an invalid URL match mode.`);
    if (action === 'expect_title' && !['contains','exact'].includes(String(raw.match || 'contains'))) throw new Error(`Action ${index + 1} has an invalid title match mode.`);
    if (action === 'screenshot' && raw.name != null && !/^[a-zA-Z0-9_-]{1,60}$/.test(String(raw.name))) throw new Error(`Action ${index + 1} screenshot name may contain only letters, numbers, underscores, and hyphens.`);
    return { action, selector, value, state: String(raw.state || 'visible'), match: String(raw.match || 'contains'), name: String(raw.name || `step-${index + 1}`) };
  });
  return { browser, actions };
}

function createArtifactDirectory(env = process.env) {
  const base = env.LOCALAPPDATA || path.join(os.homedir(), '.local', 'share');
  const directory = path.join(base, 'RunMind', 'BrowserArtifacts');
  fs.mkdirSync(directory, { recursive: true });
  return directory;
}

function trimArtifacts(directory) {
  try {
    const files = fs.readdirSync(directory).map(name => {
      const full = path.join(directory, name);
      try { const stat = fs.statSync(full); return stat.isFile() ? { full, mtime: stat.mtimeMs, size: stat.size } : null; } catch { return null; }
    }).filter(Boolean).sort((a, b) => b.mtime - a.mtime);
    let total = 0;
    for (let index = 0; index < files.length; index++) {
      total += files[index].size;
      if (index >= 20 || total > 100 * 1024 * 1024) fs.rmSync(files[index].full, { force: true });
    }
  } catch { }
}

async function run(input, dependencies = {}) {
  const request = validateRequest(input);
  const cwd = path.resolve(String(input.working_directory || process.cwd()));
  const requireFromWorkspace = dependencies.requireFromWorkspace || require('node:module').createRequire(path.join(cwd, '__runmind_playwright__.cjs'));
  let playwright;
  try { playwright = dependencies.playwright || requireFromWorkspace('playwright'); }
  catch (error) {
    if (error?.code === 'MODULE_NOT_FOUND' && /playwright/.test(String(error.message || error)))
      throw new Error('Playwright is not installed in the selected project workspace. Run `npm install --save-dev playwright` there, then `npx playwright install chromium`.');
    throw error;
  }
  const browserType = playwright[request.browser];
  if (!browserType) throw new Error(`The installed Playwright package does not provide ${request.browser}.`);
  const artifactDirectory = dependencies.artifactDirectory || createArtifactDirectory();
  let browser;
  try { browser = await browserType.launch({ headless: true }); }
  catch (error) {
    if (/Executable doesn't exist|browser executable.*not found/i.test(String(error.message || error)))
      throw new Error(`The ${request.browser} browser binary is not installed. Run \`npx playwright install ${request.browser}\` in the selected project. ${String(error.message || error).slice(0, 1000)}`);
    throw error;
  }
  let context;
  try {
    context = await browser.newContext({ acceptDownloads: false, serviceWorkers: 'block' });
    await context.route('**/*', async route => {
      const request = route.request();
      let url;
      try { url = new URL(request.url()); } catch { return route.abort(); }
      if (['file:', 'chrome:', 'chrome-extension:', 'devtools:'].includes(url.protocol)) return route.abort();
      let navigationRequest = false;
      try { navigationRequest = request.isNavigationRequest(); } catch { }
      let mainFrameNavigation = navigationRequest;
      try { if (navigationRequest) mainFrameNavigation = request.frame().parentFrame() === null; } catch { }
      if (mainFrameNavigation && !['http:', 'https:'].includes(url.protocol)) return route.abort();
      return route.continue();
    });
    const page = await context.newPage();
    page.setDefaultTimeout(10000);
    page.setDefaultNavigationTimeout(30000);
    const consoleErrors = [];
    const pageErrors = [];
    const failedRequests = [];
    const httpErrors = [];
    page.on('console', message => { if (message.type() === 'error' && consoleErrors.length < 50) consoleErrors.push(message.text().slice(0, 1000)); });
    page.on('pageerror', error => { if (pageErrors.length < 50) pageErrors.push(String(error.message || error).slice(0, 1000)); });
    page.on('requestfailed', request => { if (failedRequests.length < 50) failedRequests.push({ url: request.url().slice(0, 2000), error: String(request.failure()?.errorText || 'request failed').slice(0, 500) }); });
    page.on('response', response => { if (response.status() >= 400 && httpErrors.length < 50) httpErrors.push({ status: response.status(), url: response.url().slice(0, 2000) }); });
    const results = [];
    let failed = false;
    for (let index = 0; index < request.actions.length; index++) {
      const step = request.actions[index];
      const started = Date.now();
      try {
        let result;
        const locator = step.selector ? page.locator(step.selector) : null;
        switch (step.action) {
          case 'goto': await page.goto(step.value, { waitUntil: 'domcontentloaded' }); result = { url: page.url().slice(0, 4000) }; break;
          case 'click': await locator.click(); result = { clicked: step.selector }; break;
          case 'fill': await locator.fill(step.value); result = { filled: step.selector }; break;
          case 'press': await locator.press(step.value); result = { pressed: step.value }; break;
          case 'check': await locator.check(); result = { checked: step.selector }; break;
          case 'uncheck': await locator.uncheck(); result = { unchecked: step.selector }; break;
          case 'select_option': await locator.selectOption(step.value); result = { selected: step.value }; break;
          case 'wait_for': await locator.waitFor({ state: step.state }); result = { state: step.state }; break;
          case 'expect_text': {
            const actual = (await locator.innerText()).slice(0, MAX_OUTPUT_TEXT);
            const passed = step.match === 'exact' ? actual.trim() === step.value : actual.includes(step.value);
            result = { passed, actual };
            if (!passed) throw new Error(`Expected text ${step.match} '${step.value}', received '${actual.slice(0, 500)}'.`);
            break;
          }
          case 'expect_url': {
            const actual = page.url(); const passed = step.match === 'exact' ? actual === step.value : actual.includes(step.value);
            result = { passed, actual: actual.slice(0, 4000) }; if (!passed) throw new Error(`Expected URL ${step.match} '${step.value}', received '${actual}'.`); break;
          }
          case 'expect_title': {
            const actual = await page.title(); const passed = step.match === 'exact' ? actual === step.value : actual.includes(step.value);
            result = { passed, actual: actual.slice(0, 1000) }; if (!passed) throw new Error(`Expected title ${step.match} '${step.value}', received '${actual}'.`); break;
          }
          case 'expect_visible': await locator.waitFor({ state: 'visible' }); result = { visible: true }; break;
          case 'expect_hidden': await locator.waitFor({ state: 'hidden' }); result = { hidden: true }; break;
          case 'read_text': result = { text: (await locator.innerText()).slice(0, MAX_OUTPUT_TEXT) }; break;
          case 'count': result = { count: await locator.count() }; break;
          case 'screenshot': {
            const file = path.join(artifactDirectory, `${Date.now()}-${process.pid}-${index + 1}-${step.name}.png`);
            await page.screenshot({ path: file, fullPage: false, animations: 'disabled' });
            const stat = fs.statSync(file);
            if (stat.size > MAX_SCREENSHOT_BYTES) { fs.rmSync(file, { force: true }); throw new Error('Screenshot exceeded the 5 MB artifact limit.'); }
            trimArtifacts(artifactDirectory);
            result = { artifact: file, sizeBytes: stat.size };
            break;
          }
        }
        results.push({ index: index + 1, action: step.action, ok: true, durationMs: Date.now() - started, result });
      } catch (error) {
        failed = true;
        results.push({ index: index + 1, action: step.action, ok: false, durationMs: Date.now() - started, error: String(error.message || error).slice(0, 2000) });
        break;
      }
    }
    return { success: !failed, browser: request.browser, url: page.url().slice(0, 4000), title: (await page.title().catch(() => '')).slice(0, 1000), steps: results, completedSteps: results.filter(item => item.ok).length, totalSteps: request.actions.length, consoleErrors, pageErrors, failedRequests, httpErrors, artifactDirectory };
  } finally {
    if (context) await context.close().catch(() => {});
    await browser.close().catch(() => {});
  }
}

async function main() {
  try {
    let input = '';
    for await (const chunk of process.stdin) {
      input += chunk;
      if (Buffer.byteLength(input, 'utf8') > MAX_INPUT_BYTES) throw new Error('Playwright request exceeds the 128 KB input limit.');
    }
    const parsed = JSON.parse(input || '{}');
    parsed.working_directory = process.cwd();
    const result = await run(parsed);
    process.stdout.write(`${JSON.stringify(result)}\n`);
    process.exitCode = result.success ? 0 : 2;
  } catch (error) {
    process.stdout.write(`${JSON.stringify({ success: false, error: String(error.message || error).slice(0, 3000) })}\n`);
    process.exitCode = 1;
  }
}

if (require.main === module) main();
module.exports = { validateRequest, run, MAX_ACTIONS, MAX_INPUT_BYTES };
