'use strict';

const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');

const MAX_INPUT_BYTES = 640 * 1024;

function parseInputJson(input) {
  const normalized = String(input || '').replace(/^\uFEFF/, '');
  return JSON.parse(normalized || '{}');
}
const MAX_ACTIONS = 30;
const MAX_TEXT = 16000;
const MAX_SELECTOR = 1000;
const MAX_OUTPUT_TEXT = 12000;
const MAX_SCREENSHOT_BYTES = 5 * 1024 * 1024;
const MAX_INSPECTED_ELEMENTS = 80;

function validateRequest(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) throw new Error('A Playwright request object is required.');
  const browser = String(input.browser || 'chromium').toLowerCase();
  if (!['chromium', 'firefox', 'webkit'].includes(browser)) throw new Error('Browser must be chromium, firefox, or webkit.');
  const channel = String(input.channel || 'auto').toLowerCase();
  if (!['auto', 'bundled', 'msedge', 'chrome'].includes(channel)) throw new Error('Browser channel must be auto, bundled, msedge, or chrome.');
  if (browser !== 'chromium' && !['auto', 'bundled'].includes(channel)) throw new Error('Installed Edge/Chrome channels are available only with Chromium.');
  const expectedCorsOrigins = [];
  if (input.expected_cors_origins != null) {
    if (!Array.isArray(input.expected_cors_origins) || input.expected_cors_origins.length > 20) throw new Error('expected_cors_origins must be an array containing at most 20 HTTP(S) origins.');
    for (const item of input.expected_cors_origins) {
      let url;
      try { url = new URL(String(item)); } catch { throw new Error(`Invalid expected CORS origin: '${item}'.`); }
      if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.pathname !== '/' || url.search || url.hash)
        throw new Error('Expected CORS entries must be exact credential-free HTTP(S) origins, without a path, query, or fragment.');
      expectedCorsOrigins.push(url.origin);
    }
  }
  if (!Array.isArray(input.actions) || input.actions.length < 1 || input.actions.length > MAX_ACTIONS) throw new Error(`Provide between 1 and ${MAX_ACTIONS} browser actions.`);
  const allowed = new Set(['goto','inspect_page','click','fill','press','check','uncheck','select_option','wait_for','expect_text','expect_url','expect_title','expect_visible','expect_hidden','read_text','count','screenshot']);
  const actions = input.actions.map((raw, index) => {
    if (!raw || typeof raw !== 'object' || Array.isArray(raw)) throw new Error(`Action ${index + 1} must be an object.`);
    const action = String(raw.action || '').toLowerCase();
    if (!allowed.has(action)) throw new Error(`Action ${index + 1} has unsupported action '${action}'.`);
    const selector = String(raw.selector || '');
    const value = String(raw.value ?? '');
    if (selector.length > MAX_SELECTOR) throw new Error(`Action ${index + 1} selector exceeds ${MAX_SELECTOR} characters.`);
    if (value.length > MAX_TEXT) throw new Error(`Action ${index + 1} value exceeds ${MAX_TEXT} characters.`);
    if (['click','fill','press','check','uncheck','select_option','wait_for','expect_text','expect_visible','expect_hidden','read_text','count'].includes(action) && !selector.trim()) throw new Error(`Action ${index + 1} ('${action}') requires a non-empty selector. Supply a workspace page selector such as '#request-name', 'form button[type=submit]', or '[role=status]'.`);
    if (['goto','fill','press','select_option','expect_text','expect_url','expect_title'].includes(action) && !value) throw new Error(`Action ${index + 1} ('${action}') requires a non-empty value. For goto use an absolute HTTP/HTTPS URL; for locator actions use the text or key to enter/select/assert.`);
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
  return { browser, channel, expectedCorsOrigins: [...new Set(expectedCorsOrigins)], actions };
}

function isCorsPolicyMessage(message) {
  const text = String(message || '');
  return /blocked by CORS policy|cross-origin request blocked|CORS request did not succeed|access-control-allow-origin|preflight request.*(?:CORS|cross.origin)|CORS.*preflight/i.test(text);
}

function extractCorsTargetUrl(message) {
  const text = String(message || '');
  const patterns = [/(?:Access to (?:fetch|XMLHttpRequest) at|request to)\s+['"](https?:\/\/[^'"]+)['"]/i, /https?:\/\/[^\s'"<>]+/i];
  for (const pattern of patterns) {
    const match = text.match(pattern);
    if (match) { try { return new URL(match[1] || match[0]).href; } catch { } }
  }
  return '';
}

function normalizeCorsUrl(value) {
  try {
    const url = new URL(String(value || ''));
    url.hash = '';
    return { exact: url.href, pathKey: `${url.origin}${url.pathname}`, hasQuery: !!url.search };
  } catch { return null; }
}

function matchesCorsTarget(candidate, target) {
  const left = normalizeCorsUrl(candidate);
  const right = normalizeCorsUrl(target);
  if (!left || !right) return false;
  if (left.exact === right.exact) return true;
  // Browser messages often omit the query string. Only use origin+pathname fallback
  // when the CORS target itself contains no query, avoiding over-classification.
  return !right.hasQuery && left.pathKey === right.pathKey;
}

function classifyCorsPolicyBlocks(failedRequests, httpErrors, browserPolicyBlocks) {
  const corsTargets = browserPolicyBlocks.filter(item => item.kind === 'cors' && item.url);
  for (const request of failedRequests) {
    const match = corsTargets.find(target => matchesCorsTarget(request.url, target.url));
    if (match) {
      request.classification = 'cors_policy_block';
      request.browserPolicyBlock = true;
      request.applicationFailure = false;
      request.expectedCorsPolicyBlock = match.expected === true;
      request.corsOrigin = match.origin || '';
      request.note = 'The browser blocked this request under CORS policy; this is not classified as an application JavaScript exception or an unexpected transport failure.';
    }
  }
  for (const item of httpErrors) {
    const match = item.method === 'OPTIONS' ? corsTargets.find(target => matchesCorsTarget(item.url, target.url)) : null;
    if (match) {
      item.classification = 'cors_preflight_response';
      item.browserPolicyBlock = true;
      item.applicationFailure = false;
      item.expectedCorsPolicyBlock = match.expected === true;
      item.corsOrigin = match.origin || '';
      item.note = 'This OPTIONS response is associated with a browser-enforced CORS preflight block, not an application HTTP failure for the requested operation.';
    }
  }
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

async function inspectPage(page) {
  const body = page.locator('body');
  const dom = await body.evaluate(element => {
    const compact = value => String(value || '').replace(/\s+/g, ' ').trim();
    const safeText = (element, maximum = 240) => compact(element?.innerText || element?.textContent || '').slice(0, maximum);
    const namedBy = element => {
      const ids = String(element.getAttribute('aria-labelledby') || '').split(/\s+/).filter(Boolean);
      return ids.map(id => safeText(document.getElementById(id), 240)).filter(Boolean).join(' ');
    };
    const accessibleName = element => compact(
      element.getAttribute('aria-label') || namedBy(element) ||
      (element.labels ? Array.from(element.labels).map(label => safeText(label, 240)).join(' ') : '') ||
      element.getAttribute('alt') || element.getAttribute('title') ||
      element.getAttribute('placeholder') || safeText(element, 240)
    ).slice(0, 240);
    const selectorHint = element => {
      if (element.id) return `#${element.id}`;
      const name = element.getAttribute('name');
      if (name) return `${element.tagName.toLowerCase()}[name=${JSON.stringify(name)}]`;
      const ariaLabel = element.getAttribute('aria-label');
      if (ariaLabel) return `${element.tagName.toLowerCase()}[aria-label=${JSON.stringify(ariaLabel)}]`;
      return element.tagName.toLowerCase();
    };
    const landmarkSelector = 'header,nav,main,aside,footer,form,[role="banner"],[role="navigation"],[role="main"],[role="complementary"],[role="contentinfo"],h1,h2,h3,h4';
    const landmarks = Array.from(document.querySelectorAll(landmarkSelector)).slice(0, 60).map(node => ({
      tag: node.tagName.toLowerCase(),
      role: node.getAttribute('role') || '',
      name: accessibleName(node)
    }));
    const interactiveSelector = 'button,a[href],input:not([type="hidden"]),select,textarea,[role="button"],[role="link"],[role="checkbox"],[role="radio"],[role="switch"],[role="tab"],[contenteditable="true"],[tabindex]:not([tabindex="-1"])';
    const candidates = Array.from(document.querySelectorAll(interactiveSelector));
    const interactiveElements = candidates.slice(0, 80).map(node => ({
      tag: node.tagName.toLowerCase(),
      role: node.getAttribute('role') || ({ A: 'link', BUTTON: 'button', SELECT: 'combobox', TEXTAREA: 'textbox' }[node.tagName] || (node.tagName === 'INPUT' ? ({ checkbox: 'checkbox', radio: 'radio', button: 'button', submit: 'button' }[String(node.type || '').toLowerCase()] || 'textbox') : '')),
      name: accessibleName(node),
      selectorHint: selectorHint(node),
      type: node.getAttribute('type') || '',
      disabled: !!node.disabled,
      required: !!node.required,
      checked: typeof node.checked === 'boolean' ? node.checked : null,
      visible: node.getAttribute('aria-hidden') !== 'true' && node.getClientRects().length > 0
    }));
    return {
      document: { title: document.title, language: document.documentElement.lang || '', url: location.href },
      bodyText: safeText(element, 12000),
      landmarks,
      landmarksTruncated: document.querySelectorAll(landmarkSelector).length > landmarks.length,
      interactiveElements,
      interactiveElementsTruncated: candidates.length > interactiveElements.length
    };
  });

  let accessibilitySnapshot = '';
  let accessibilityMethod = 'semantic DOM fallback';
  let accessibilityNote = 'This Playwright version does not expose Locator.ariaSnapshot(); landmarks and accessible names were derived from the DOM.';
  if (typeof body.ariaSnapshot === 'function') {
    try {
      accessibilitySnapshot = String(await body.ariaSnapshot() || '').slice(0, MAX_OUTPUT_TEXT);
      accessibilityMethod = 'Playwright Locator.ariaSnapshot';
      accessibilityNote = '';
    } catch (error) {
      accessibilityNote = `Playwright accessibility snapshot failed: ${String(error?.message || error).slice(0, 500)}`;
    }
  }

  return {
    document: dom.document,
    bodyText: dom.bodyText,
    landmarks: dom.landmarks,
    landmarksTruncated: dom.landmarksTruncated,
    interactiveElements: dom.interactiveElements.slice(0, MAX_INSPECTED_ELEMENTS),
    interactiveElementsTruncated: dom.interactiveElementsTruncated,
    accessibility: {
      method: accessibilityMethod,
      snapshot: accessibilitySnapshot,
      note: accessibilityNote,
      truncated: accessibilitySnapshot.length >= MAX_OUTPUT_TEXT
    }
  };
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
  const channels = request.browser !== 'chromium' || request.channel === 'bundled'
    ? [null]
    : request.channel === 'auto' ? ['msedge', 'chrome', null] : [request.channel];
  const launchErrors = [];
  for (const channel of channels) {
    try {
      browser = await browserType.launch({ headless: true, ...(channel ? { channel } : {}) });
      break;
    } catch (error) {
      launchErrors.push({ channel: channel || 'bundled', error });
      const message = String(error?.message || error);
      const missingBrowser = error?.code === 'ENOENT' || /Executable doesn't exist|browser executable.*not found|could not find.*(?:browser|chrome|msedge)|browser has not been found/i.test(message);
      if (!missingBrowser || channel === channels.at(-1)) {
        if (missingBrowser) {
          const attempted = channels.map(item => item || 'Playwright bundled browser').join(', ');
          const hint = request.browser === 'chromium'
            ? 'Install Microsoft Edge or Google Chrome and use channel "auto", or install the bundled browser with `npx playwright install chromium`. A CDN HTTP 403 is a browser-download restriction; using an installed browser avoids that download.'
            : `Install the browser with ` + '`npx playwright install ${request.browser}`' + `.`;
          throw new Error(`No usable ${request.browser} browser was found. Tried: ${attempted}. ${hint} ${message.slice(0, 700)}`);
        }
        throw error;
      }
    }
  }
  if (!browser) throw new Error(`Could not launch ${request.browser}. ${launchErrors.map(item => `${item.channel}: ${String(item.error?.message || item.error).slice(0, 300)}`).join(' | ')}`);
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
    const consoleEvents = [];
    const pageErrors = [];
    const failedRequests = [];
    const httpErrors = [];
    const browserPolicyBlocks = [];
    const expectedCorsOrigins = new Set(request.expectedCorsOrigins);
    page.on('console', message => {
      if (message.type() !== 'error') return;
      const text = message.text().slice(0, 1000);
      if (consoleEvents.length < 50) consoleEvents.push({ type: message.type(), text });
      if (isCorsPolicyMessage(text)) {
        const url = extractCorsTargetUrl(text);
        let origin = '';
        try { origin = url ? new URL(url).origin : ''; } catch { }
        if (browserPolicyBlocks.length < 50) browserPolicyBlocks.push({ kind: 'cors', source: 'console', url: String(url || '').slice(0, 2000), origin,
          expected: !!origin && expectedCorsOrigins.has(origin), classification: 'browser_security_policy', applicationFailure: false,
          message: text, note: 'The browser enforced CORS. The raw console event is preserved in consoleEvents/browserPolicyBlocks; it is not counted as an application console error or unexpected transport failure.' });
        return;
      }
      if (consoleErrors.length < 50) consoleErrors.push(text);
    });
    page.on('pageerror', error => { if (pageErrors.length < 50) pageErrors.push(String(error.message || error).slice(0, 1000)); });
    page.on('requestfailed', request => {
      if (failedRequests.length >= 50) return;
      failedRequests.push({ url: request.url().slice(0, 2000), method: request.method(), error: String(request.failure()?.errorText || 'request failed').slice(0, 500), classification: 'network_failure', browserPolicyBlock: false, applicationFailure: false });
    });
    page.on('response', response => {
      if (response.status() >= 400 && httpErrors.length < 50) httpErrors.push({ status: response.status(), url: response.url().slice(0, 2000), method: response.request().method(), classification: 'http_error', browserPolicyBlock: false, applicationFailure: true });
    });
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
          case 'inspect_page': result = await inspectPage(page); break;
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
    classifyCorsPolicyBlocks(failedRequests, httpErrors, browserPolicyBlocks);
    const expectedCorsBlocks = browserPolicyBlocks.filter(item => item.expected).length;
    const unexpectedPolicyBlocks = browserPolicyBlocks.filter(item => !item.expected).length;
    const unexpectedNetworkFailures = failedRequests.filter(item => !item.browserPolicyBlock).length;
    const applicationHttpErrors = httpErrors.filter(item => !item.browserPolicyBlock).length;
    const corsPreflightResponses = httpErrors.filter(item => item.classification === 'cors_preflight_response').length;
    const policyBlockedRequests = failedRequests.filter(item => item.browserPolicyBlock).length;
    const networkSummary = {
      // Counts distinguish raw browser events from actionable application/transport failures.
      consoleErrorCount: consoleErrors.length,
      rawConsoleErrorEventCount: consoleEvents.length,
      pageErrorCount: pageErrors.length,
      failedRequestCount: failedRequests.length,
      unexpectedNetworkFailureCount: unexpectedNetworkFailures,
      httpErrorCount: httpErrors.length,
      applicationHttpErrorCount: applicationHttpErrors,
      corsPolicyBlockCount: browserPolicyBlocks.length,
      expectedCorsBlockCount: expectedCorsBlocks,
      undeclaredCorsBlockCount: unexpectedPolicyBlocks,
      policyBlockedRequestCount: policyBlockedRequests,
      corsPreflightResponseCount: corsPreflightResponses
    };
    return { success: !failed, browser: request.browser, url: page.url().slice(0, 4000), title: (await page.title().catch(() => '')).slice(0, 1000), steps: results, completedSteps: results.filter(item => item.ok).length, totalSteps: request.actions.length, consoleErrors, consoleEvents, pageErrors, failedRequests, httpErrors, browserPolicyBlocks, expectedCorsOrigins: request.expectedCorsOrigins, networkSummary, artifactDirectory };
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
      if (Buffer.byteLength(input, 'utf8') > MAX_INPUT_BYTES) throw new Error('Playwright request exceeds the 640 KB input limit.');
    }
    const parsed = parseInputJson(input);
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
module.exports = { validateRequest, parseInputJson, inspectPage, run, isCorsPolicyMessage, extractCorsTargetUrl, classifyCorsPolicyBlocks, normalizeCorsUrl, matchesCorsTarget, MAX_ACTIONS, MAX_INPUT_BYTES };
