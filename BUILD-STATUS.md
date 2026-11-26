## Phase 2 — API & Backend Testing — 2026-11-21

- Added `api_test_run` and a shared native HTTP runner backed by Node.js built-in `fetch`; no additional package dependency.
- Supports bounded sequential API requests, JSON/text payloads, status/header/JSON-path/body assertions, response timing, and redacted response previews.
- Environment placeholders (`{{env:NAME}}`) allow credentials to come from the current user's environment without embedding values in the Agent prompt or approval preview.
- Fresh approval per suite; same-origin request and redirect restrictions; 20-request suite cap; 1 MB response cap; five-minute native execution cap; blocked in Deep Research and delegated sub-agents.
- Complete portable suite: **97/97 passed**, including **13/13 API/backend integration tests** against an ephemeral local HTTP server. JavaScript syntax and all three host project XML checks passed.
- Native C# compilation and live Windows host behavior have not been run here. Run `Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64` on Windows. Details: `docs/PHASE2-API-BACKEND-TESTING.md`.

## Phase 2 — Playwright Browser Integration — 2026-11-21

- Added the shared `playwright_run` Agent tool and native bridge operation for all three hosts. The bundled CommonJS runner resolves the project-installed `playwright` dependency and runs structured workflows in ephemeral headless browser contexts.
- Supported actions: HTTP/HTTPS navigation, click/fill/press/check/uncheck/select, locator waits, text/URL/title/visibility assertions, text reads, element counts, and viewport screenshots. Results include per-step status, console errors, page exceptions, failed requests, and HTTP error responses.
- Every run requires fresh approval. Deep Research and delegated sub-agents are blocked. No persistent browser profile, cookies, downloads, service workers, arbitrary page JavaScript evaluation, or automatic dependency installation.
- Complete portable Node suite: **84/84 passed**. The Playwright runner is tested with deterministic test doubles; no real browser was launched here.
- JavaScript syntax checks and all three host project XML checks passed. Native C# compilation and real Windows browser runtime validation have not been run in this environment.
- Setup and limits: `docs/PHASE2-PLAYWRIGHT-BROWSER-INTEGRATION.md`. Run `Validate-Phase1-Windows.ps1` on Windows to compile all three hosts.

## Phase 2 — Runtime Diagnostics & Profiling — 2026-11-21

- Added Node runtime heap diagnostics, V8 CPU profiling with `.cpuprofile` export, streamed heap snapshots, and bounded timestamped heap sampling to the existing shared Node debugger session.
- Complete portable Node test suite: **72/72 passed**; runtime Inspector tests verify heap usage, CPU profile samples, and valid streamed heap snapshot JSON.
- JavaScript syntax checks passed for the changed Agent tool definitions and new integration test.
- Native C# compilation was not run in this environment. The user subsequently confirmed the increment works across all three Windows hosts.
- Details: `docs/PHASE2-RUNTIME-DIAGNOSTICS-PROFILING.md` and `VALIDATION-REPORT-PHASE2-NODE-DEBUGGER.md`.

## Windows Release x64 compiler-error correction — 2026-11-21

- Added the missing `SupportsNewWindowBehavior` helper with a narrow executable allowlist and validated executable resolution.
- Renamed the adapter IP configuration local to `adapterProperties` to remove the `CS0136` local-name collision.
- Added regression coverage for both reported source errors. The portable suite now passes 66/66 tests.
- The corrected Windows solution has **not** been rebuilt in this environment; rerun `Validate-Phase1-Windows.ps1` on Windows. See `VALIDATION-REPORT-WINDOWS-BUILD-FIX.md`.

# RunMind 2.6.3 — Developer Intelligence & System Manager

## Active integrated milestone — Phase 1 + System Manager

- Added package-manager and lockfile signals to the persistent project index (npm, pnpm, Yarn, Bun, uv, Poetry, Pipenv, Cargo, Go, Composer, Bundler). Bun binary lockfiles are detected by filename without reading their binary contents.
- Added Node package script command suggestions based only on the declared package manager and scripts in `package.json`. Static validation does not execute them.
- Added best-effort local import relationship summaries for relative JavaScript/TypeScript/Python imports. These are heuristic leads, not AST/compiler-resolved references.
- Extended project validation with toolchain evidence, declared script commands, and local import graph summary.
- Refactored Docker/Podman CLI actions to use asynchronous waiting, user-configurable timeout, cancellation, process-tree cleanup, and bounded stdout/stderr capture.
- Added coordinated multi-file editing with one approval, stable path locks, snapshot checks, per-file backups, and best-effort rollback (sequential, not atomic).
- Extended the built-in RunMind adapter to send prompts, open bounded/validated `.runmind` session files in Standalone, and open a new conversation window.
- Existing Git bridge exposes repository selection/clone, read operations, stage/unstage/commit, branch/stash/merge/revert/cherry-pick and remote operations; destructive operations require fresh approval.
- Added `docs/PHASE1-INTEGRATED-VALIDATION.md` with repository-level workflows and cross-host acceptance criteria.

### Validation status for this source snapshot

- JavaScript syntax: checked for all 17 shared ChatWindow script files.
- JSON/XML: all 2 JSON files and 7 project/XAML/manifest XML files parsed successfully.
- Project-index helper behavior: pnpm declared-manager detection, Bun `.lockb` filename detection, and relative JS import-edge detection passed targeted Node checks.
- Changed C# files: lexical delimiter scan passed; this is not a C# compilation.
- Git workflow smoke: local init/status/diff/stage/unstage/commit/log/show/branch-switch/stash semantics passed.
- .NET SDK/C# compiler: unavailable in this environment. No Windows build or runtime test is claimed.
- ZIP integrity and SHA-256: verified in the accompanying release report.

## Release bundle status

- Full-source baseline: RunMind 2.6.0 source bundle, updated in place for this release.
- Preserves Standalone, PowerToys Run, PowerToys Command Palette, the shared ChatWindow, Agent Core, native bridge, Skills, MCP, Deep Research, Git, workflows, Project Profiles and existing permissions.
- System Manager discovery now ranks flexible application matches across executable and packaged Windows applications, with bounded live refresh on stale index/search miss.
- Launches use a stable application ID from current discovery, not a model-supplied executable path. Existing desktop windows are reused/activated by default; failed foreground activation does not silently start a duplicate.
- Adds explicit new-window/new-process choices, persistent ask/reuse/new-window/new-process per-application preferences, and graceful close requests with mandatory native confirmation.
- Adds current-session tool inventory and makes Global skill discovery/loading available without a project folder while retaining workspace authorization for Project skills.
- Adds the adapter manager UI, confirmed custom adapter removal, and the first-party RunMind prompt adapter.
- Merges System Manager policy into the leading system message for compatibility with strict model chat templates; normalizes action outcome reporting.
- Fixes checkbox dimensions in settings fields.
- Synchronizes host/package metadata to 2.6.1 / 2.6.1.0.

## Validation performed in this environment

See `VALIDATION-REPORT-PHASE1-INTEGRATION.md` for exact checks and limitations for the current integration pass; `VALIDATION-REPORT.md` remains historical baseline information. Windows/.NET compilation and live Windows behavior have not been verified in this environment. JavaScript syntax, HTML ID consistency, JSON/XML parsing, tool-to-native-dispatch wiring, C# lexical delimiter balance, release metadata, ZIP integrity, and SHA-256 are checked here.

## Target-machine validation still required

- Build the full solution with the .NET 10 Windows targeting pack and repository package dependencies.
- Test Calculator discovery through Windows Apps folder, Firefox/Firefox Developer Edition matching, Steam discovery, aliases, stale catalog refresh and ambiguous results.
- Test existing-window activation, Windows foreground restrictions, named-browser URL handoff, new-window/new-process behavior, packaged activation, graceful close, and per-app preferences.
- Verify tool inventory against actual tool definitions with/without workspace access and connected MCP servers.
- Verify global and project skill listing/loading with and without a project workspace; confirm unsupported bundled/Bionic skills are not falsely reported.
- Run all existing Standalone, PowerToys Run and PowerToys Command Palette smoke/regression tests.
- Re-run existing Git, workspace, indexing, conversation, Project Profile, Skills, MCP, Deep Research, workflow, and PowerShell/native execution checks.

## Historical release notes



## 2.5.1 update

- System Manager has its own toolbar flyout, separate from Agent Settings.
- Application discovery resolves Start Menu shortcut targets and filters missing executables.
- Application launch now uses direct executable startup and checks for a matching running process before returning success.
- Windows/.NET build and live application-launch behavior still require validation on a Windows target.


## 2.5.2 update

- Added a conditional Agent system instruction requiring fresh System Manager discovery and launch for every new user request; prior conversation results cannot satisfy a new action.
- Named-browser URL requests are directed through the selected executable with the URL as an argument, not through the Windows default URL handler.
- Application search requires a non-empty query at the tool schema, JavaScript executor, and native bridge boundaries.
- Discovery now includes Windows uninstall registry entries and executable candidates under registered install locations, improving coverage for apps such as Steam.
- Existing process PIDs are snapshotted before launch. A surviving old PID alone is no longer treated as a verified launch; launcher exit/handoff and process-running states are reported separately.
- Default-handler opens report request acceptance only; they do not claim an application, visible window, focus, or navigation was verified.


## 2.6.2 validation status

Source updates include three bundled Agent Skills, native skill discovery/loading, shared System Management prompt guidance, adapter operation metadata, and content packaging for all three hosts. Static validation is performed in the accompanying report. Windows/.NET compilation and runtime behavior require Windows testing.


## 2.6.3 implementation status

- Added Notepad and File Explorer built-in adapter definitions.
- Added native Docker/Podman CLI operation with strict action/runtime/container validation and mandatory confirmation for start/stop/restart.
- Added four coding skills and shared coding execution policy.
- Added four additional RunMind-native skills adapted from the supplied Bionic bundle: file-history, skill-management, create-skill, and install-skill.
- Fixed the shared adapter override record initializer and connected the adapter tab, Edit, and Reset UI actions.
- System Manager includes separate Settings and Application Adapters tabs, bounded panel/list scrolling, built-in adapter override storage, Edit/Reset actions, and custom adapter management. Full RunMind conversation management through the native adapter remains unimplemented; the existing `.runmind` save/open UI is still available.
- Windows/.NET compilation and runtime validation are still required.


## Phase 1 completion candidate — 2026-11-21

The current source includes preview-first JavaScript AST rename/import organization, reversible HKCU/HKLM Run startup toggles, hardware/driver and TCP/UDP listener diagnostics, normalized System Manager tool results, real local Git workflow coverage, and multi-file recovery contract checks. Portable Node tests pass in the development environment.

**Windows release gate remains open.** This environment does not contain the .NET SDK or a Windows runtime, so no current C# compilation or Windows operation is claimed. Run `Validate-Phase1-Windows.ps1` on Windows with the .NET 10 SDK, Node.js, and restored NuGet dependencies to compile all three host projects and execute the portable tests. Then perform the manual three-host runtime checklist in `docs/PHASE1-INTEGRATED-VALIDATION.md`.

Portable validation on 2026-11-21: 47/47 Node tests, 18/18 JavaScript syntax checks, 2/2 JSON files, 7/7 XML files, and C# delimiter sanity passed. This does not replace .NET compilation or Windows runtime validation.
