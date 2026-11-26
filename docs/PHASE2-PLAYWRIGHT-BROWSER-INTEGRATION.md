# Phase 2 — Playwright Browser Integration

## Scope

RunMind exposes one structured `playwright_run` Agent tool through the existing Agent Core and native host bridge. It executes one bounded browser workflow per call. Standalone, PowerToys Run, and PowerToys Command Palette all use the same implementation.

The runner is a small bundled CommonJS file (`Shared/PlaywrightRunner.cjs`). It loads the `playwright` package from the selected native workspace at runtime. RunMind does not bundle Playwright, download browsers, edit the project's package manifest, or install dependencies.

## Project setup

From the project directory selected as the native execution workspace, install the Playwright package using the project's normal package manager. For npm:

```powershell
npm install --save-dev playwright
```

By default, RunMind tries an installed Microsoft Edge channel, then Google Chrome, then the Playwright-bundled Chromium binary. This avoids a browser download when a compatible system browser is already installed. To select one explicitly, set the Playwright tool's `channel` to `msedge`, `chrome`, or `bundled`. If no compatible system browser is available, install the bundled browser:

```powershell
npx playwright install chromium
```

Firefox and WebKit can be installed separately when needed:

```powershell
npx playwright install firefox
npx playwright install webkit
```

RunMind 2.7.0 uses the selected conversation workspace as the canonical native root. The user does not select the same folder again for Playwright. The optional `working_directory` argument is relative to that root and is useful for monorepos where Playwright is installed in a subproject. Browser-only contexts without a native host retain the browser File System Access API fallback. See `UNIFIED-WORKSPACE-SELECTION.md`.

## Supported structured actions

A single run accepts 1–30 actions and stops on the first failed action or assertion:

- `goto`: navigate to an absolute HTTP/HTTPS URL.
- `inspect_page`: return a bounded semantic DOM summary, landmarks, interactive controls, accessible names, and a Playwright ARIA snapshot when the installed version supports it. This is a fixed inspection operation, not arbitrary page JavaScript evaluation.
- `click`, `fill`, `press`, `check`, `uncheck`, `select_option`: locator interactions.
- `wait_for`: wait for a locator to become visible, hidden, attached, or detached.
- `expect_text`: assert locator text using contains/exact matching.
- `expect_url`, `expect_title`: assert the current URL or page title.
- `expect_visible`, `expect_hidden`: visibility assertions.
- `read_text`, `count`: return bounded locator text or element count.
- `screenshot`: save a viewport screenshot to the local artifact directory.

The tool returns per-step outcomes and durations, final URL/title, browser console errors, page exceptions, failed network requests, and HTTP responses with status 400 or higher. Its native stdin protocol uses UTF-8 without a BOM; both Node runners also tolerate one leading BOM for compatibility. Action schemas describe selector/value requirements, and native preflight errors identify the exact action index and type. Browser policy events (CORS, CSP and mixed-content) are reported separately from ordinary console and transport failures; expected events can be declared explicitly and remain visible in the report.

Example Agent action plan:

```json
{
  "browser": "chromium",
  "actions": [
    { "action": "goto", "value": "http://127.0.0.1:3000/" },
    { "action": "fill", "selector": "#email", "value": "test@example.test" },
    { "action": "click", "selector": "button[type=submit]" },
    { "action": "expect_text", "selector": "main", "value": "Welcome" },
    { "action": "screenshot", "name": "signed-in" }
  ]
}
```

## Execution and security boundaries

- Every browser run requires fresh user approval. The approval prompt shows the selected browser, working directory, timeout, and requested actions.
- Browser automation is unavailable in Deep Research and delegated sub-agent tasks.
- The runner uses a new headless browser process and a fresh ephemeral context for every call. It does not reuse the user's browser profile, cookies, credentials, local storage, or login state.
- Downloads are disabled and service workers are blocked. No arbitrary JavaScript evaluation, arbitrary browser launch flags, persistent contexts, or browser profile access are exposed.
- Only HTTP and HTTPS navigation URLs are accepted; embedded URL credentials are rejected. A browser-context route guard also blocks `file:`, `chrome:`, `chrome-extension:`, `devtools:`, and non-HTTP(S) main-frame navigations, including redirects initiated by page code.
- Browser pages execute their own JavaScript and may cause network or application-side effects. Fresh approval is a user-intent gate, not a sandbox. Node and browser processes run with the current Windows user's permissions.
- A run is limited to 30 actions, 300 seconds, 640 KB request input, and 512 KB captured output. Default locator timeout is 10 seconds; navigation timeout is 30 seconds.
- Screenshots are limited to 5 MB each and stored under `%LOCALAPPDATA%\RunMind\BrowserArtifacts`. The runner retains at most 20 recent artifacts and targets a 100 MB total artifact budget.
- A browser process and context are closed after each run, including when an assertion fails.

## Dependencies and limitations

- Node.js must be on PATH; the existing native Node toolchain requirement applies.
- The project must have a locally resolvable `playwright` package. `playwright-core` alone, a globally installed package, remote browser services, and package-manager Plug'n'Play layouts are not currently supported.
- Chromium is the default. The `auto` channel tries installed Microsoft Edge, installed Google Chrome, then bundled Chromium. Explicit Edge/Chrome channels require the corresponding browser to be installed. Firefox and WebKit work when the project package and matching browser binaries are installed.
- If a browser CDN returns HTTP 403, this is an external browser-download restriction; use an installed Edge/Chrome channel instead of repeatedly retrying the same download.
- The current increment is a bounded workflow runner, not a persistent interactive browser session. Each call starts from a clean context.
- Authentication flows requiring an existing user profile, persistent cookies, downloads, file uploads, arbitrary `page.evaluate`, browser extensions, and GUI-visible headed mode are outside this increment.
- API/backend testing and development-server lifecycle management use the same canonical conversation workspace as Agent file operations. Development-server ownership records are persisted and re-adopted only after process identity verification.

## Validation

`tests/playwright-browser.integration.test.cjs` tests request validation, BOM-safe JSON input, scheme/credential rejection, action bounds, semantic/ARIA page inspection against a deterministic Playwright test double, structured workflow execution, assertion failure behavior, ephemeral context settings, tool approval wiring, and deployment through all three host projects. `test-fixtures/playwright-app` provides a local HTML app for manual Windows browser validation; it has no runtime dependencies, but the selected fixture workspace must have Playwright and Chromium installed. It does not claim a real browser was launched in this environment. The user must run the Windows Release x64 validation script to compile the native C# bridge in all three hosts.
