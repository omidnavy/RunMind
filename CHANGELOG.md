## 2.6.4 — Phase 2 reliability and System Manager diagnostics (2026-11-22)

- Fix the Playwright tool-schema/runner contract: every action object now requires explicit `action`, `selector`, and `value` properties. Empty selector/value strings are documented for actions that do not consume them; locator actions still require non-empty selectors during native preflight validation.
- Improve Playwright preflight errors with the exact action index and action name, plus an explanation of the missing field. Invalid action sequences are rejected before browser launch.
- Fix the Playwright fixture's submission status: the programmatic form reset after submission no longer overwrites the success message; a user-initiated Clear form action still reports `Form cleared.`
- Add read-only System Manager `system_manager_find_port_owner` for targeted TCP/UDP local-port ownership lookup, including process identity metadata when Windows permits access.
- Add read-only System Manager `system_manager_process_tree` for a bounded parent chain and child-descendant view with process identity, executable path, start time, and memory information where accessible.
- Route both new diagnostics through the existing process/list-process capability and read-only approval model. They do not terminate or modify processes.
- Add Playwright schema-contract, fixture behavior, and System Manager tool-wiring regression tests.
- Bump all three host project package versions to 2.6.4.
- Unified browser/native workspace selection and persistent development-server orphan reconciliation remain planned follow-up work; they are not claimed as completed in this release.

## 2.6.3 — Phase 2 follow-up refinements (2026-11-22)

- Make native workspace selection idempotent per chat context: if the native execution root is already selected, the Agent receives the existing selection instead of opening the native folder picker again. The separate Agent-browser/native selection remains unresolved pending the host-owned workspace RPC migration.
- Development-server discovery now returns both its absolute resolved path for display and `working_directory_relative` for safe follow-up calls. Starting a server reuses the relative value rather than sending the absolute path back through the workspace resolver.
- Clarify tool guidance so nested-project workflows reuse the relative directory returned by discovery.

## 2.6.3 — Phase 2 workspace and browser follow-up (2026-11-22)

- Added safe selected-folder prefix normalization for Agent file paths, supporting parent-workspace and nested-project path forms while preferring valid direct paths and retaining traversal protection.
- Playwright Chromium now automatically tries installed Microsoft Edge, Google Chrome, and then bundled Chromium; explicit browser channel selection is available and download restrictions produce actionable guidance.
- PowerShell no longer treats native-command stderr warnings as terminating errors by default; cmdlet/script errors remain governed by `$ErrorActionPreference`.
- Added tests for workspace path normalization, browser channel selection/fallback, and PowerShell native stderr handling.
- Added the unified-workspace requirement and migration design. The separate native folder prompt remains a known architectural gap until Agent file operations move behind the native workspace RPC.

## Unreleased — Native workspace and WebView2 startup fixes (2026-11-21)

- Fix native workspace path resolution when the selected native workspace is itself a nested project folder. Development-server management, API tests, Playwright, PowerShell/native commands, and Node debugger now share the same normalisation while retaining workspace containment and reparse-point checks.
- Serialize WebView2 environment/controller initialization per canonical user-data folder across RunMind host processes, reducing concurrent-startup races that can surface as disconnected COM controller errors.
- Clarify the workspace-relative path rules in tool schemas and test documentation.

## Unreleased — Phase 2 API & Backend Testing

- Add the shared `api_test_run` Agent tool and native `api.test.run` bridge operation, backed by Node.js built-in `fetch` with no new package dependency.
- Support bounded sequential GET/POST/PUT/PATCH/DELETE/HEAD/OPTIONS requests, JSON/text bodies, headers, status/header/JSON-path/body assertions, timings, and bounded response previews.
- Add `{{env:NAME}}` placeholders for credentials inherited from the current user process environment; do not expose placeholder values in approval previews or results, and redact common secret fields and response headers.
- Restrict request paths and redirects to the approved HTTP(S) base URL origin; cap suites at 20 requests, each response at 1 MB, request bodies at 64 KB, output at 256 KB, and native execution at five minutes.
- Require fresh approval for every suite and disable the tool in Deep Research and delegated sub-agent tasks.
- Add local HTTP-server integration tests for request execution, assertions, secret handling, redirect restrictions, failure reporting, and three-host deployment wiring.

## Unreleased — Phase 2 Playwright Browser Integration

- Add `playwright_run` to the existing shared Agent Core and native bridge across Standalone, PowerToys Run, and PowerToys Command Palette. The bundled runner resolves the project-installed `playwright` package; it does not add a NuGet dependency, bundle browsers, or install project packages.
- Support bounded structured navigation, locator interactions, waits, text/URL/title/visibility assertions, text reads, element counts, and viewport screenshots. Return per-step results plus console errors, page exceptions, failed requests, and HTTP error responses.
- Require fresh approval for each run and disable the capability in Deep Research and delegated sub-agent tasks. Use a fresh ephemeral context, block service workers and downloads, reject non-HTTP(S) navigation and embedded URL credentials, and expose no arbitrary page JavaScript evaluation.
- Bound workflows to 30 actions and 300 seconds; cap input/output, screenshot size, and local artifact retention. Close the browser/context after every run.
- Add deterministic runner tests and host-wiring contracts. These tests use a Playwright test double and do not claim real-browser or Windows C# compilation validation.

# RunMind Changelog

## Unreleased — Phase 2 Native Node.js Debugger foundation (2026-11-21)

- Add a shared native Node Inspector bridge used by Standalone, PowerToys Run, and PowerToys Command Palette through the existing Agent Core; no separate debugger agent or additional NuGet dependency.
- Add approved launch of workspace-relative `.js`, `.mjs`, and `.cjs` entry points using `node.exe --inspect-brk=127.0.0.1:0`; process arguments use `ProcessStartInfo.ArgumentList`, the target runs as the current Windows user, and a native workspace must be selected.
- Add approved attach to Node Inspector endpoints discovered over `127.0.0.1` only. The discovered target must identify as `node`, and the returned WebSocket URL must use a loopback IP and the requested port.
- Add pause/resume, step over/into/out, exception pause modes, URL breakpoints, breakpoint removal, paused call-frame snapshots, scope property inspection, loaded script metadata, and bounded event/stdout/stderr capture.
- Require a separate fresh approval for expression evaluation; use `Debugger.evaluateOnCallFrame` with `returnByValue`, `awaitPromise: false`, and V8 `throwOnSideEffect`. This is a guardrail, not a sandbox.
- Restrict the debugger to eight concurrent sessions per host, bounded event/output/message buffers, local Inspector connections, and a fixed CDP method allowlist. Deep Research and delegated sub-agent contexts cannot invoke the debugger tools.
- Add lifecycle safeguards: detaching an attached external process never terminates it; RunMind can terminate only a process it launched, with a separate approval; paused sessions cannot be silently detached; host disposal cleans up RunMind-launched child processes. Attaching to `--inspect-brk` does not release its startup pause until the user explicitly resumes.
- Capture child output using bounded chunk reads rather than line-based reads, preventing unbounded buffering from very long output lines.
- Add a real Node Inspector integration test covering break-on-start, URL breakpoint, paused local evaluation, resume, detach, and attach/pause/resume against an already-running Node process.

### Phase 2 status
- Implemented in this increment: Native Node.js Debugger foundation.
- At this initial debugger-only stage, CPU/heap profiling, Playwright browser automation, dedicated API/backend testing, and development-server lifecycle management were still pending; subsequent Phase 2 increments are recorded below.
- Validation available here: Node.js integration and source-contract tests. C# compilation and runtime validation for all three Windows hosts must still be run on Windows.


## 2.6.3 Windows build correction

- Restore the explicit, narrow executable allowlist used by per-application new-window behavior, resolving the missing `SupportsNewWindowBehavior` method referenced by the application behavior and launch paths.
- Rename the per-adapter IP configuration local to `adapterProperties`, avoiding a C# local-name collision with the enclosing `IPGlobalProperties` variable.
- Add regression checks for both compiler failures reported by the Windows Release x64 solution build.
## Unreleased — Phase 1 completion extension (2026-11-21)

- Bundle the TypeScript 5.8.3 compiler API for offline JSX/TSX/TypeScript parsing, loaded lazily when project indexing begins and a bounded project-wide `ts.Program`/checker pass across indexed JavaScript, JSX, TypeScript, and TSX. The checker resolves cross-file imported symbols and TSX component references; root `tsconfig.json`/`tsconfig.*.json` compiler options and `paths` are honored. Analysis is bounded to 500 source files and 12 MB, with no default library loading and no full diagnostic/type-check pass. The bundle includes the Apache-2.0 license.
- Upgrade project-index storage to schema/key v6. Preserve redacted analysis source for JS/TS files, reuse compiler results when the project is unchanged, and expose checker-backed cross-file references through `project_symbol_graph`.
- Add `node_resolve_modules`, a fresh-approval, fixed-script native tool that calls Node's `createRequire().resolve()` and `import.meta.resolve()` for the selected importer. It reports actual installed-package and conditional ESM/CommonJS resolution without importing or executing target modules. The browser-selected project and native execution workspace must be the same folder.
- Extend preview-first AST refactoring with `rename-public-api`: rename a direct named export and update statically resolved importers/re-exports across up to 20 files, preserving consumer-local names through aliases. Apply uses the approved multi-file editor, per-file recovery backups, concurrency checks, and best-effort rollback. Default exports, export-star chains, dynamic/unresolved importers, and more than 20 affected files are rejected rather than guessed.
- Extend static workspace resolution with `.js` to `.ts/.tsx/.d.ts` substitutions and `tsconfig` path aliases. The indexed graph remains bounded and does not scan `node_modules`; use the native Node resolver for actual installed dependency resolution.
- Extend reversible startup management to HKCU/HKLM `Run` and `RunOnce` registry entries and inventoried `.lnk`/`.exe` items in user/common Startup folders. Registry command/type and file contents are preserved; file recovery copies are SHA-256 verified. Disabled entries remain visible in startup inventory for restoration. Every mutation requires fresh native confirmation and never overwrites an existing target.
- Add integration coverage for TypeScript checker bindings, TSX component references, `tsconfig` paths, actual Node ESM/CommonJS conditional exports, cross-file public API rename previews, and startup recovery contracts.


## Phase 1 semantic JavaScript and System Manager increments

- Upgrade the project-index schema/key to v4 and force re-indexing of prior records.
- Resolve JavaScript lexical references, classify reads/writes, and record direct/member/constructor call sites with statically linked local declaration positions where possible.
- Extend the symbol graph with bounded circular relative-module dependency detection.
- Expand process inventory with parent PID and gated CPU/memory/thread/handle/priority/responsiveness details; add identity-checked graceful close without force termination.
- Expand Windows service inspection to configuration/dependents and restart with mandatory confirmation and final-state polling.
- Expand Podman/Docker inventory to stats, images, volumes, networks, and health inspection; mutating actions are not read-only and are followed by state verification.
- Add uptime, disk capacity, network gateway, and DNS information to read-only diagnostics.
- At the time of this increment, TypeScript/TSX/JSX and native Node resolution were outstanding; the 2026-11-21 Phase 1 completion extension below adds compiler-API parsing/checker references and an approved Node ESM/CommonJS resolver.

# RunMind Changelog

## Unreleased — Phase 2 Runtime Diagnostics & Profiling

- Extend the existing shared Node Inspector sessions with V8 heap diagnostics, CPU sampling profiles, streamed heap snapshots, and timestamped memory sampling.
- Export Chrome DevTools-compatible `.cpuprofile` and `.heapsnapshot` artifacts under `%LOCALAPPDATA%\RunMind\RuntimeArtifacts`; bound CPU profiles to 32 MB, heap snapshots to 128 MB, artifact retention to 20 files, and memory sampling to 120 samples.
- Require fresh approval to start CPU profiling and memory monitoring, and always require fresh approval for heap snapshots. Stop/read actions remain available without approval.
- Keep runtime profiling in the existing Agent Core and disable it in Deep Research and delegated sub-agent tasks.
- Add real Node Inspector integration tests for heap usage, CPU profile samples, and streamed heap snapshot chunks.

## Unreleased — Phase 1 Developer Workbench & System Manager hardening

- Upgrade the persistent JavaScript project index to schema/key v4. Record AST-derived direct/member/constructor call sites, local declaration links, and bounded circular module dependency cycles; expose call sites and cycles through `project_symbol_graph`.
- Expand Windows process inspection with parent PID, working/private memory, cumulative CPU time, thread/handle counts, priority, responsiveness, executable path, and start time (detail fields remain capability-gated). Add an identity-checked graceful process-close request that never force-kills.
- Expand Windows service queries with configuration and dependent-service inspection, plus restart. Start/stop/restart always require fresh native confirmation and poll the actual service state; accepted/completed/verified are reported separately.
- Expand Podman/Docker inventory with stats, images, volumes, and networks. State-changing container actions are no longer classified as read-only and are followed by an inspect-based state check.
- Add uptime, logical-drive capacity, network gateway, and DNS information to the individually enabled read-only diagnostics.
- Add read-only Windows startup inventory across per-user/machine Run/RunOnce registry keys and Startup folders; no startup modification is exposed.
- Extend static Node module resolution to relative imports, workspace package `exports`, package `imports` (`#` aliases), common Node built-ins, and workspace re-exports. External dependencies are surfaced separately. TypeScript path mappings, installed dependency resolution, custom loaders, and full Node runtime resolution remain outstanding.
- Bundle Acorn 8.15.0 (MIT) for offline JavaScript AST parsing; TypeScript, TSX, and JSX parsing remain on heuristic fallback.
- Harden JavaScript reference analysis: `var` declarations bind to the nearest function/module scope, shorthand AST nodes are deduplicated by source position, and identifier occurrences report read, write, or read-write access (including compound assignments and updates).

- Extend the persistent project index with package-manager and lockfile detection across Node.js, Python, Rust, Go, PHP, and Ruby ecosystems.
- Detect npm, pnpm, Yarn, and Bun using package metadata/lockfiles; expose only script commands backed by declared `package.json` scripts. Suggestions are informational and are not executed by indexing or static validation.
- Add a bounded local import relationship summary: JavaScript specifiers use Acorn AST extraction when parsing succeeds; TypeScript/JSX/Python remain heuristic, and path resolution remains best-effort.
- Extend project validation results with toolchain evidence, script command suggestions, and import-graph summary while preserving its non-executing contract.
- Harden Docker/Podman CLI execution with asynchronous process waiting, user-configurable timeout, cancellation, process-tree cleanup, and bounded stdout/stderr capture.
- Add coordinated multi-file editing for 2–20 existing workspace files: one approval, path locking, pre-write snapshot checks, per-file recovery backups, and best-effort rollback on write failure. Writes are sequential, not filesystem-atomic.
- Extend the built-in RunMind adapter with `open-session` for validated `.runmind` files and `new-conversation`, alongside `send-prompt`. The Standalone host restores a bounded session file as actual conversation state.
- Retain and smoke-test the native Git workflow surface (status/diff/log/show/branches/remotes, stage/unstage/commit, branch operations, stash, remote operations, and destructive operations with fresh approval).
- Document remaining Phase 1 limits and the integrated repository workflow validation approach.

## 2.6.3 — Developer Intelligence & System Manager

- Replaced the Notepad++ adapter with Windows Notepad.
- Added a File Explorer adapter for opening folders and selecting files.
- Added a native Docker/Podman container action bridge for list, inspect, start, stop, restart, and logs. Runtime selection supports auto, Docker, and Podman; state-changing operations always require native confirmation.
- Added coding, debugging, testing-and-quality, and code-review Agent Skills.
- Added RunMind-native `file-history`, `skill-management`, `create-skill`, and `install-skill` skills, adapted from the supplied Bionic bundle. They use RunMind Git, backup, and skill-manager capabilities; Bionic-only browser/session APIs were not copied.
- Fixed a C# record `with` initializer in shared adapter override loading that prevented all three hosts from compiling.
- Wired System Manager tab navigation and built-in adapter Edit/Reset controls. Built-in Edit now loads the actual effective argument templates, and native adapter inventory exposes those templates for the editor.
- Fixed built-in adapter saving: the editor no longer creates empty operation arrays that native validation rejects; successful saves report whether an override was created.
- Added persistent New adapter and Cancel edit actions so leaving built-in edit mode restores a clean custom-adapter install form.
- Added shared coding execution guidance emphasizing project inspection, root-cause fixes, preserving user changes, proportionate validation, and honest verification reporting.
- Retained the existing system-management, container-management, and runmind-management skills.

# Changelog

## 2026-11-21 — Native workspace and WebView2 startup fixes

- Fixed native workspace path resolution when the selected native workspace is itself a nested project folder; server management, API tests, Playwright, and Node debugger now share the corrected resolver.
- Serialized WebView2 environment/controller initialization per user-data folder across RunMind host processes to avoid overlapping startup against the same profile.
- Clarified tool schema and runtime test documentation for workspace-relative paths.


## 2.6.2 — System Management Skills & Capability Contracts

- Added bundled `system-management`, `container-management`, and `runmind-management` Agent Skills, discoverable and loadable on demand alongside global and project skills.
- Added a concise shared System Management instruction policy; skills guide behavior but never grant permissions or replace native enforcement.
- Enriched adapter inventory with operation parameters, state-change classification, confirmation policy, and expected result fields. Native validation remains authoritative.
- Included bundled skills in Standalone, Command Palette, and PowerToys Run host outputs.


### Reliability and compatibility
- Keep all System Manager policy in the single leading Agent system message. This avoids model chat templates rejecting a second system message while preserving the same action policy.
- Normalize native application launch outcomes so process startup, foreground activation, new-window requests, existing-instance handoff and navigation verification are reported independently.
- Explicitly report that a started process does not prove a visible window or completed URL navigation.

### Application adapters
- Add an Application Adapters manager to the System Manager flyout. It lists built-in/custom adapters, supports refreshing, and provides a JSON manifest installer with native confirmation.
- Add removal of custom adapter manifests with native confirmation; built-in adapters cannot be removed.
- Add a first-party RunMind adapter that starts RunMind with a supplied Agent prompt using its existing `--prompt` startup argument. It uses the normal discovered-application and native launch/permission path; it does not create another Agent runtime or bypass approvals.
- Add the built-in-only `send-prompt` operation for the RunMind adapter; custom adapters remain limited to `open-file`, `open-folder`, and `open-url`. Argument templates remain the only supported execution mechanism.

### UI
- Correct checkbox styling so checkbox/radio inputs are excluded from full-width text-input rules, including the LM Studio native metadata option.

### Preserved boundaries
- Keep generic application discovery and all existing adapters. No model-specific or example-application special cases are added.
- Keep System Manager optional, native confirmation/permission checks, and the no-shell/no-script adapter model.

## [2.6.0] — System Manager & Agent Reliability (2026-11-17)

### Application intelligence and lifecycle
- Replace exact-substring-only discovery with ranked matching across normalized names, executable names, aliases, registered and common install locations, Start Menu shortcuts, App Paths, PATH and packaged Windows Apps.
- Add bounded Windows Apps folder discovery for packaged applications; packaged activation is explicitly reported as requested, not as a verified visible window or completed action.
- Rebuild the local application index when stale or when a search misses; validate candidate executables again before launch.
- Return stable application IDs and ranked match reasons instead of asking the model to echo executable paths back to native code.
- Default to reuse-and-activate for already-running desktop applications. If foreground activation cannot be verified, do not silently create a duplicate process.
- Support explicit new-window behavior for a small allowlist of applications with known command-line support, and new-process requests where the application permits them.
- Add persistent per-application launch behavior preferences (`ask`, `reuse`, `new-window`, `new-process`), protected by native confirmation.
- Add graceful application close requests. RunMind sends normal window-close messages, always confirms, and never force-kills an application through this action.
- Update declarative application adapters to use the same application-ID launch contract.

### Agent introspection and Skills
- Add `list_available_tools`, which reports the exact tool definitions currently exposed to the active Agent session, including connected MCP and host/workspace-dependent tools.
- Expose `list_agent_skills` and `load_agent_skill` without requiring a selected project workspace, so global Skills can be discovered and loaded independently.
- Report skill scope and loaded state; distinguish supported global/project sources from a separate bundled/Bionic catalog, which is not present in this source snapshot.

### Preserved boundaries
- Keep one shared Agent runtime and native bridge across Standalone, PowerToys Run and PowerToys Command Palette.
- Keep System Manager disabled by default and retain native permission checks and confirmation dialogs.
- Do not add arbitrary scripts, shell execution, forced focus, GUI automation, administrator elevation, registry modification or service control.

### Validation limits
- JavaScript syntax, JSON/XML parsing, version synchronization, static tool/native dispatch checks, and C# lexical checks are performed in this environment.
- Windows/.NET compilation and live discovery, packaged activation, window focus, launch, close and multi-host runtime tests require a Windows target machine.

## [2.5.2] — System Manager Action Reliability (2026-11-17)

- Require a fresh application discovery and launch sequence for every new user request; prior conversation turns and tool results cannot satisfy a new action.
- Named-browser URL requests are passed as arguments to the selected browser executable instead of being delegated to the Windows default URL handler.
- Require a non-empty application search query in the tool schema, browser-side executor, and native bridge.
- Expand discovery to Windows uninstall registry entries and executable candidates in registered install locations, in addition to App Paths, Start Menu shortcuts, and PATH.
- Stop treating the continued existence of any old process as proof that a new launch request was handed off. Existing-instance handoff is now explicitly unverified.
- Default-handler opens report request acceptance only; visible application, focus, and navigation are not claimed as verified.
- Retain the dedicated System Manager flyout introduced in 2.5.1 and all three supported hosts.

## [2.5.1] — System Manager Flyout & Verified Application Launch (2026-11-17)

- Moved Windows System Manager into its own toolbar flyout, separate from Agent Settings.
- Improved application discovery: resolve Start Menu shortcuts to executable targets and exclude missing or invalid targets.
- Validate cached application paths before returning them, so stale entries are filtered and search can trigger a fresh catalog scan.
- Launch executables directly with shell execution disabled, then verify that the matching process remains active or handed off to an existing instance.
- Report short-lived/unverified launches as failures rather than claiming success.
- Updated Agent tool guidance to require a verified launch result before reporting that an application opened.

## [2.5.0] — Debugging, Quality, Skills & System Manager (2026-11-17)

- Added reusable developer workflows for project builds/tests, static analysis/lint, Playwright test-runner checks, API smoke testing, and dependency security audits. They rely on existing explicit PowerShell/native execution approval and report actual command results.
- Added automatic recognition of pasted Agent Skill content, with preview and confirmation before installation.
- Added direct HTTPS `SKILL.md` import and GitHub/GitLab repository-folder URL resolution, with frontmatter validation, duplicate protection, source metadata, and global/project scope.
- Added optional Windows System Manager, disabled by default, with master/group/individual switches and shared native enforcement.
- Added local application catalog discovery from Start Menu shortcuts, registered App Paths and PATH; generic application launch, file/folder/HTTPS URL opening, built-in and user-authored declarative application adapters, process listing/termination, and read-only OS/environment/service/network diagnostics.
- Process termination requires a native Windows confirmation, revalidates PID/name/start time and executable identity when available, and blocks selected critical Windows processes. Application launch/open permissions are enforced by the native bridge.
- Preserved Standalone, PowerToys Run, PowerToys Command Palette, and the shared Agent Core.
- Limitations: URL Skill import currently imports `SKILL.md` only; archive import and recursive download of linked Skill resources are not included. The original Playwright workflow invokes the project's installed CLI; the later Phase 2 browser integration adds structured automation through the shared native runner and project-local Playwright package.

## [2.4.0] — Developer Workbench & Native Automation (2026-11-17)

### Added
- Add approved PowerShell execution and native `.exe` command execution to the shared Agent runtime. Each invocation requires fresh in-chat approval, supports cancellation and a 1–900 second timeout, captures bounded stdout/stderr, and reports the exit code and duration.
- Add native execution workspace selection. The selected directory is scoped to the current conversation context; relative working directories cannot escape it. Native processes run as the current Windows user and are explicitly not a security sandbox.
- Add separate native executable invocation using `ProcessStartInfo.ArgumentList`; shell command-line parsing, batch files, and script launch are not accepted by this tool. PowerShell is the explicit script-capable route.
- Automatically build the persistent project index when a workspace is selected or restored. Existing indexes are incrementally refreshed, reusing files whose size and modification time are unchanged.
- Periodically check the active workspace every 60 seconds while the chat is visible and incrementally refresh changed/deleted/added index records. Manual build/refresh and cancellation remain available.
- Keep PowerShell and process execution in the shared native Agent bridge so Standalone, PowerToys Run, and Command Palette use the same tool contract and approval UI. Native process tools are excluded from Deep Research and delegated sub-agent tasks.

### Security and boundaries
- PowerShell and native executables run with the current Windows user's permissions; approval confirms an individual operation but does not sandbox it or reduce its privileges.
- Native execution requires a separately selected native workspace because browser File System Access handles do not expose absolute paths to the host. Relative working directories are confined to that selected directory; scripts themselves can still access any resource available to the user.
- Native executable tools accept `.exe` names resolved through PATH and pass arguments individually. PowerShell script text is written to the child process standard input; no command text is interpolated into a shell command line.
- Process output is capped at 512 KB per stream, and the process tree is terminated on cancellation or timeout.
- Project indexing remains bounded and local. Periodic refresh is polling-based (60-second interval while visible), not a real-time filesystem watcher; unchanged-file reuse still relies on size and modification-time metadata.

### Preserved
- Shared Agent Core, all three hosts, existing Git operations, Skills, MCP, Deep Research, workflows, Project Profiles, workspace tools, and approval behavior.
- Existing manual project-index controls and local IndexedDB storage.

### Validation
- JavaScript syntax checks, JSON/XML parsing, release-version synchronization, static native-bridge checks, archive integrity, and SHA-256 verification are included in the validation report.
- Windows/.NET compilation and live PowerShell/native process execution, WebView2 folder selection, and periodic index refresh require validation on a Windows target machine.

## [2.3.0] — Project intelligence and reusable developer workflows (2026-11-17)

### Added
- Add an explicit, persistent project index stored locally in the existing per-workspace IndexedDB store. Indexing was user-triggered and supported incremental refresh by file size and modification time.
- Index bounded text source files and common project manifests; capture file paths, language hints, supported import declarations, symbol declarations, and dependencies from common npm, Python, Cargo, Go, NuGet, Maven, Gradle, Composer, and Bundler manifests.
- Add project-index search for paths, indexed text, imports, and supported symbols, plus an index overview and bounded static project-validation tool for Agent tasks.
- Add reusable built-in developer workflows for project understanding, feature implementation, bug investigation, change review, and release preparation. Add workspace-scoped custom workflow storage.

### Preserved
- Standalone, PowerToys Run, and PowerToys Command Palette continue to use the same shared ChatWindow and Agent runtime.
- Existing Project Profiles, Workspace Context, folder grants, Agent Skills, Deep Research templates, workflow operation history, approvals, and Git behavior remain in place.

---

## [2.2.7] — Theme-matched startup and PowerToys Run installer (2026-11-15)

### Fixed
- Read the saved appearance before the WebView's first page paint and resolve the selected light/dark palette background from the runtime-discovered theme catalog.
- Set the initial WPF window and WebView2 surface backgrounds to the selected palette color, so the host's default dark surface no longer flashes before the chat theme appears.
- Keep the page body transparent and hidden until the selected stylesheet is loaded; the saved palette color fills the page behind it. Remove the temporary root background only after the themed page has rendered.
- Keep normal theme-switch transitions unchanged; transition suppression still applies only during initial appearance setup.

### Added
- Add `Install-RunMind-PowerToys-Run.bat` to install the already-built PowerToys Run plugin into `%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\Community.PowerToys.Run.Plugin.RunMind`.
- The installer checks for running PowerToys processes and refuses to copy while PowerToys is open. It mirrors the complete Release x64 plugin output, removes obsolete files from older plugin deployments, and verifies required files after copying.
- Keep PowerToys Run installation separate from `Build-Release-x64.bat`; the build script still builds/packages but does not install either host.

### Validation
- JavaScript syntax, theme metadata/background parsing, project XML, plugin/manifest JSON, installer structure, and source ZIP integrity were checked.
- A Windows/.NET compilation and live WebView2 startup test remain required on the target machine.

## [2.2.6] — Unified runtime themes and first-paint stability (2026-11-15)

### Changed
- Consolidate each theme into one `Shared/ChatWindow/theme/<id>.css` file. Its `RunMind-Theme` metadata declares the ID, display name, and supported modes (`light`, `dark`, or both); the same file can provide both palettes.
- Update native theme discovery to read the metadata, validate each declared palette independently, and ignore malformed, unreadable, oversized, or unexpected CSS files without blocking the remaining themes.
- Update the WebView theme loader to load the selected single-file theme and validate the CSSOM palette before activation. A failed theme is skipped in favor of another valid theme for the requested mode; the previously active stylesheet stays in place during switches.
- Update Command Palette package validation to verify theme metadata and ensure every theme file is included in the MSIX.
- Suppress CSS transitions and animations only during initial theme application. The chat remains hidden until a valid theme is ready; normal theme-switch transitions remain available afterward.

### Preserved
- All existing palette declarations and colors. Paired light/dark palettes were combined without redesign; light-only and dark-only themes remain limited to their declared mode.
- The built-in Warm Graphite base palette remains the final fallback if no valid dark theme can be loaded.
- Existing chat UI, native bridge messages, appearance preferences, and all three host integrations.

### Validation
- CSS consolidation coverage, metadata parsing, per-mode selector/property checks, JavaScript syntax, project XML, manifest/plugin JSON, host resource-copy rules, packaging-script checks, and ZIP integrity were checked.
- Windows/.NET compilation and live WebView2 initialization/theme-switch tests remain to be run on the target Windows machine.

## [2.2.5] — Runtime-discovered theme system (2026-11-14)

### Added
- Split the existing palettes into self-contained `dark-<id>.css` and `light-<id>.css` files under `Shared/ChatWindow/theme/`; retained `base.css` for shared fallback tokens.
- Added `ThemeCatalog` and the internal native `themes.list` bridge operation. The chat UI discovers available theme files at runtime and builds the Light/Dark selectors from the returned catalog; display names are derived from filenames.
- Added `ChatWindow/scripts/theme-loader.js`. Theme switching keeps the current stylesheet active until the requested stylesheet has loaded, then swaps the active sheet without an unstyled intermediate frame.
- Updated the Command Palette MSIX validator to require the dynamic loader and verify packaged theme files. Existing wildcard copy rules deploy new theme files to all three hosts.

### Changed
- Removed the hard-coded theme option lists and CSS theme registry from HTML/JavaScript. Adding or deleting a matching theme CSS file now updates the selectors at chat initialization and whenever the Appearance menu is opened.
- Updated native appearance preference validation to accept currently discovered theme IDs and fall back safely when a stored theme file has been removed.
- Kept the existing palette declarations and visual appearance; no theme redesign was made.

### Validation
- JavaScript syntax checks, CSS extraction checks, theme filename/selector/catalog checks, project XML parsing, package metadata checks, resource-copy rule checks, and ZIP integrity checks passed.
- Windows/.NET compilation and live WebView2 theme-switching tests remain outstanding in this environment.

## [2.2.4] — PowerToys Run chat handoff isolation (2026-11-12)

### Fixed
- Stop passing the PowerToys Run inline provider, model, and endpoint URLs into the full chat window when opening an in-progress query or completed inline response. The chat window now retains its own selected model and its own saved provider connections.
- Keep the inline provider/model as message provenance only, so the original inline response can still be attributed correctly without forcing the chat generation model.
- An in-progress inline prompt now auto-sends using the chat window's currently selected model instead of failing because the inline model is absent from the chat window's connection list.

### Validation
- Static checks cover bootstrap separation, message provenance, project metadata, and archive integrity. Windows/.NET compilation and live PowerToys Run tests remain to be run on the target Windows machine.

## [2.2.3] — PowerToys Run settings polish and Command Palette alias (2026-11-11)

### Changed
- Group the PowerToys Run provider settings in endpoint-first order: Ollama endpoint and its two auth fields, followed by LM Studio URL and its two auth fields. No setting keys or credential behavior changed.
- Add an About section to Command Palette's RunMind extension settings, including the RunMind website and a clickable **Visit omid.navy** action.
- Give **Open chat with empty context** the stable command ID `runmind.open-empty-chat`.
- Extend the separate Command Palette installer to seed the direct `RunMind` alias for that command when Command Palette is closed and its existing alias settings are available. The script creates a timestamped backup before writing, preserves a `RunMind` alias owned by another command, and skips safely when the host is running, its settings are not initialized, or the settings path is ambiguous. The helper can be rerun after closing Command Palette.

### Validation
- Static project, JSON/XML, source-order, stable-ID, About-link, installer-guard, and archive checks were performed.
- Windows/.NET compilation and live PowerToys Run / Command Palette tests remain to be run on the target Windows machine.

## [2.2.1] — PowerToys Run settings and repeatable chat launch (2026-11-11)

### Fixed
- Use numeric combo-box values for the Thinking / reasoning setting so **Off** and the other selections persist correctly in PowerToys Run.
- Make the original PowerToys Run query a reusable launch snapshot. If inline generation is handed to a chat window, the retained query can open additional independent chat windows from the same original prompt; one window's follow-up messages are not shared with its siblings.
- Migrate the previous built-in PowerToys Run system prompt to the requested PowerToys Run-specific RunMind prompt while preserving custom and explicitly empty prompts.

### Added
- Add independent optional API key and custom authentication-header settings for the Ollama and LM Studio endpoints in PowerToys Run.
- Apply provider authentication to model discovery, inline generation, and conversation-title requests. A custom `Authorization` header overrides the optional Bearer API key; other custom headers are sent alongside it.
- Keep PowerToys Run credentials separate from Command Palette credentials in the shared per-user settings file.

### Security note
- PowerToys Run API keys and custom auth headers are stored in the per-user RunMind settings JSON without DPAPI encryption. The PowerToys Run settings text fields are not masked.

### Validation
- Static source, JSON, project XML, package-manifest, JavaScript, and archive checks were performed. Windows/.NET compilation and live PowerToys Run/provider tests still need to be run on the target Windows machine.

## [2.2.0] — Command Palette authentication and empty-chat handling (2026-11-10)

### Added
- Optional per-provider API keys and custom request authentication headers for Ollama and LM Studio in Command Palette settings. Authentication is applied to model discovery and generation. Secret input values are masked in the settings card.

### Fixed
- Opening the separate RunMind chat from an empty inline page now opens a new empty chat instead of raising an empty-transcript error.
- Use distinct command IDs for the search fallback and persistent Dock command to avoid registering the same command as both fallback and Dock pin.
- Validate custom header names and values to prevent header injection and reject reserved transport/content headers.

### Security note
- Command Palette credentials are stored in the per-user RunMind settings JSON and are not DPAPI-encrypted. They are never emitted to RunMind diagnostics.

### Known host limitation
- Enter-to-submit and Shift+Enter-for-newline require a PowerToys Command Palette host change; the extension API does not expose the Adaptive Card TextBox key event.

## [2.2.0] — Dock conversation controls (2026-11-10)

### Added
- Add **Start fresh** to both inline search conversations and the pinned Dock conversation. It clears current/previous messages, cancels in-flight generation, resets the form field, and keeps the current page open.
- Keep the Dock conversation page as its own persistent instance, separate from the search fallback, so Dock history can be continued without contaminating search fallback history.

### Notes
- Command Palette's Adaptive Card host does not currently expose modifier-aware key events to extensions. Multiline input and the explicit Send button remain; exact Enter-to-send / Shift+Enter-newline requires a PowerToys host change.

## [2.2.0] — Command Palette Dock and prompt defaults (2026-11-10)

### Fixed
- Resolve the existing pinned `runmind.inline-conversation` Dock command to a fresh `InlineAnswerPage` instance instead of the long-lived search fallback page. The Dock page now starts with an empty transcript and does not inherit the active search query's conversation.
- Use a neutral **Your message** label for the inline message form and update the empty-page guidance so it works for both fresh Dock chats and search fallback conversations.

### Changed
- Add a Command Palette-specific system prompt with the exact launcher-oriented instructions requested for its root search-box input. The Command Palette inline client now uses `CommandPaletteSystemPrompt` for Ollama, LM Studio native chat, and LM Studio compatibility chat.
- Keep the existing `InlineSystemPrompt` independent for PowerToys Run. PowerToys Run settings writes explicitly preserve `CommandPaletteSystemPrompt`, so editing the Run plugin's own prompt does not reset the Command Palette prompt.
- Expose the Command Palette prompt in its own extension settings page, without changing Standalone or PowerToys Run prompt behavior.

### Keyboard input limitation
- The inline follow-up remains an Adaptive Card multiline `Input.Text`, so Enter inserts a newline and the **Send** action submits. The current Command Palette host's `ContentFormControl` submits Enter only when the focused TextBox is single-line; it does not expose a modifier-aware event to extensions. Therefore RunMind cannot implement Enter-to-send plus Shift+Enter-newline through the current extension API alone. This needs a PowerToys host-side change.

### Validation
- Source-level checks cover Dock page isolation, preservation of the stable pinned ID, separation of the two system prompts, all three Command Palette provider request paths, and preservation by PowerToys Run settings writes. XML/JSON parsing and archive integrity checks are run for the release ZIP. Windows/.NET compilation and live Dock/provider tests still need to be confirmed on the target Windows installation.

## [2.2.0] — Complete Command Palette conversation handoff (2026-11-10)

### Added
- Transfer the complete ordered inline conversation to the Command Palette-hosted chat window, including every completed user/assistant turn, provider/model attribution, timestamps, and failed assistant responses.
- Restore the transferred transcript through the existing RunMind `.runmind` session loader so the messages become the active conversation state and subsequent model requests receive the prior history.
- When inline generation is still running, transfer its current user prompt but omit the unfinished assistant response rather than presenting partial output as a completed turn.
- Bound the conversation payload to 4 MB and the native chat launch request to 32 MB.

### Compatibility
- Reuse the existing `ChatWindow.html` session import path; no changes to the shared chat UI are required. Standalone and PowerToys Run conversation startup paths remain unchanged.

### Validation
- Static checks cover complete role-ordered transcript generation, RunMind v2 session schema, payload bounds, and routing into the existing `MainWindow` initial-conversation loader. Windows/.NET compilation and live chat continuation still require verification on the target Windows installation.

## [2.2.0] — Command Palette form submission correction (2026-11-10)

### Fixed
- Handle the current Command Palette Adaptive Card submission contract in both the inline-model settings form and inline conversation form. The host provides form inputs and `Action.Submit.data` separately; both are now merged before processing the action.
- Persist the selected provider/model when **Save settings** is submitted. Verify the saved model by reading the shared settings file back and emit diagnostics if persistence fails.
- Execute **Send** and **Open chat with context** from the inline conversation form using the separately supplied action data.
- Retain one-argument `SubmitForm` overloads for compatibility with older Command Palette hosts.

### Validation
- Static checks cover both form overloads, separation/merge of inputs and action data, JSON structure, project XML, and ZIP integrity. Windows/.NET compilation and live provider / chat process tests remain unverified in this environment.

## [2.2.0] — PowerToys Command Palette integration (in development)

### Added
- Add an independent PowerToys Command Palette host that launches its own chat process from the packaged extension executable. It does not launch or require the Standalone executable.
- Host the canonical shared `ChatWindow.html`, `ArtifactTools.js`, native bridge, and WPF/WebView2 chat window inside the Command Palette project by linking the shared sources and assets.
- Add the **Open chat with empty context** action and a branded **RunMind** free-text fallback that submits the entered query to the Command Palette-hosted chat.
- Add a native Command Palette extension settings page for inline provider/model and generation configuration. The page does not launch the chat window.
- Keep each host's WebView2 profile isolated to avoid cross-process profile locking. The existing native appearance preference file remains shared across hosts. PowerToys Run and Command Palette inline settings are synchronized separately through a shared configuration file.
- Move the canonical `ChatWindow.html` Agent runtime and `ArtifactTools.js` implementation into `Shared/`; Standalone, PowerToys Run, and Command Palette link those shared files into their existing output locations.


### Command Palette inline settings follow-up
- Remove the top-level **Configure RunMind** command and the old chat/settings-flyout launch path after user testing showed it opened a blank chat window.
- Replace the manual answer-paste form with automatic query/result handoff from the inline Command Palette conversation to the independent chat window.
- Add a provider selector, refreshable model dropdown, custom model ID, endpoints, timeouts, generation controls, system instructions, and Nemotron conversational-mode option to the actual extension settings page.
- Add `Shared/InlineModelSettings.cs` as the common settings store consumed by Command Palette and PowerToys Run.
- Keep inline answer generation/rendering and streaming inside Command Palette explicitly marked as outstanding.

### Fixed
- Resolve CsWinRT Windows metadata from the .NET Windows SDK projection ref-pack for the active target framework instead of relying on the installed SDK's `Platforms\UAP\<version>\Platform.xml`. This avoids the CsWinRT metadata-read failure reported on Windows SDK installations where that platform XML is missing, unreadable, or incomplete.
- Reference `Microsoft.Windows.SDK.BuildTools` explicitly for the Command Palette project. The separate MSIX build-tools package supplies the Windows SDK MSBuild infrastructure (including `UAP.props`) required by MSIX/PRI generation; it is not supplied by `Microsoft.Windows.SDK.BuildTools.MSIX` alone.
- Copy the Command Palette package logos and `Public` extension folder into both build and publish outputs. Loose registration previously depended on files that were not explicitly staged beside the executable.
- Add a BAT-driven deployment workflow that creates a signed x64 MSIX, creates/reuses a publisher-matching local development certificate, trusts its public certificate in Local Machine Trusted People on first install, and installs/removes the package for the current user.
- Validate the generated package's identity, app-extension declaration, COM CLSID consistency, logos, and public extension folder before installing it. The normal workflow now uses `Add-AppxPackage -Path` rather than loose-manifest registration.
- Demote the SDK's `APPX1707` diagnostic to an informational MSBuild message. `Microsoft.CommandPalette.Extensions.winmd` is the Command Palette API contract consumed by the extension; RunMind implements its extension interfaces in the managed COM server registered by the package manifest, not as a separate WinRT component. Assigning an unrelated implementation DLL would be misleading.
- Set the COM server working directory to its packaged executable directory before constructing the extension, ensuring relative package assets resolve consistently during COM activation.
- Add a per-user COM-server startup log with process, activation arguments, working directory, initialization stages, provider requests, and full startup/shutdown exceptions. The log is written to `%LOCALAPPDATA%\RunMind\CommandPalette\Logs\com-server.log`; diagnostics failures are deliberately non-fatal.

### Compatibility and scope
- The Command Palette extension requires PowerToys Command Palette and its supported MSIX/WinRT extension model. It hosts the shared WPF/WebView2 chat UI in a process started from its own packaged executable. It does not depend on the Standalone executable. Inline model-answer rendering inside the Command Palette surface and full settings/data synchronization across the three isolated WebView2 profiles remain incomplete.
- PowerToys Run remains supported. Its existing plugin behavior and settings are retained.
- The local development package is signed with a machine-trusted self-signed certificate matching `CN=Omid Navy`; it is intended only for local testing and is not a production distribution certificate. The existing standalone host and PowerToys Run behavior remain unchanged.

### Validation
- Validate project and package-manifest XML, package identity/publisher alignment, all three Command Palette CLSID declarations, app-extension name and public folder, BAT action routing, deployment-script package checks, shared runtime source/output paths, JavaScript references, full source-archive layout, and ZIP integrity. Windows MSIX generation/signing, certificate elevation, package installation, and live PowerToys discovery/activation still require verification on the target Windows installation.
- Windows MSIX generation/signing, certificate elevation, package installation, and live PowerToys discovery/activation still require verification on the target Windows installation.

## [2.1.6] — Reliability, bounded processing, and workspace safety

### Fixed
- Keep the Workspace and Conversation Branch databases in separate IndexedDB handles. Each database's version-change callback now closes only the connection it owns, so session cleanup and workspace-handle operations cannot overwrite one another's database reference.
- Exclude failed and cancelled assistant generations from completed branch-comparison turns. Cancellation is recorded and retained in saved branch messages; Whole branch display and export identify failed, cancelled, and still-generating responses.
- Normalize trailing DNS dots before private/local hostname classification, including `localhost.` and dotted loopback IPv4 forms; reject redirects from configured remote Reader endpoints so an authenticated Reader request cannot silently follow a redirect.
- Revalidate source snapshots after approval and again immediately before destructive completion in edit, delete, move, and rename flows. Move/rename coordinate source and destination paths; when a source changes during the operation, RunMind preserves it and leaves any already-created destination copy intact.
- Serialize RunMind workspace writes by destination path and perform the final no-overwrite existence check inside that lock. This closes races between concurrent RunMind operations; the browser file API cannot provide atomic exclusion against unrelated external writers.
- Add a bounded directory-entry budget to recursive workspace scans and direct folder listings, with truncation/count diagnostics.
- Add automatic backup retention (20 `.bak` files / 500 MiB), while keeping the newest backups and refusing to prune when the inventory scan itself is truncated.
- Enforce strict CSV quote placement, correct the 50% numeric-summary threshold rounding, bound aggregate merge/join input and output sizes, and check cancellation within join loops.
- Group duplicate-file candidates by size before hashing and enforce a 500 MiB cumulative hashing budget in addition to the 100 MiB per-file limit. Hashing uses the Web Crypto digest API, which requires one file buffer at a time; the per-file and cumulative budgets bound that buffer-based workload.
- Trap Tab/Shift+Tab focus within the AI Branch Comparison modal and restore focus to the actual opener when it closes.

### Improved
- Bound Whole branch model input to a 100,000-character dossier using representative first/last history excerpts and explicit omission notices. Full branch histories remain available in the comparison view and Markdown export.
- Bound comparison follow-up context to 16,000 characters / 12 recent messages, each cached discussion to 40 messages / 60,000 characters, and the in-memory comparison-session cache to three recent selections.
- Hash comparison-session identity without retaining a serialized copy of the entire dossier.
- Throttle comparison streaming updates and update only the active assistant message content rather than rebuilding the full discussion for each token.
- Return explicit directory-entry diagnostics from workspace listings and expose the new CSV/hash/scan limits in Agent tool descriptions.

### Compatibility
- Existing `.runmind` version 1 and 2 conversations remain supported. The optional `cancelled` message flag is backward-compatible; older cancelled messages containing the existing stop marker are also recognized. Configured Reader endpoints must return the response directly; HTTP redirects are rejected and the endpoint should be configured at its canonical URL.
- Full original branch history remains unchanged. Only the model request uses bounded excerpts; the UI and Markdown branch dossier retain the complete history. The separate AI follow-up discussion has an explicit bounded retention policy, disclosed in the UI and Markdown export.
- No API credential-storage migration or transcript virtualization is included in this release. No provider routing, Agent permission policy, Git, Skills, or NativePageReader changes are intended.

### Validation
- Validate IndexedDB ownership/source paths, cancelled/error turn classification, bounded dossier and discussion contexts, LRU session eviction, throttled streaming update wiring, modal focus trap, trailing-dot host classification, directory-entry budgets, path-lock serialization, post-approval/post-backup snapshot checks, backup retention, strict CSV parsing, aggregate CSV limits, join cancellation, duplicate-hash budgets, JavaScript syntax, HTML/CSS structure, project XML, plugin manifest JSON, version alignment, changed-files-only overlay packaging, ZIP integrity, and byte-for-byte overlay reapplication against RunMind 2.1.5.
- Windows/.NET compilation and live PowerToys/WebView2/provider/runtime testing require the target Windows environment and are not claimed here.

## [2.1.5] — Perfecting Branch Comparison

### Added
- Add two Branch Comparison modes: **Single response** and **Whole branch**. Single response remains the default and initially selects each branch's latest user question/answer turn.
- Allow the user to choose a specific question/answer turn independently for Branch A and Branch B. Turn options show their own turn number, whether an answer is available, and a short question preview. An unanswered latest turn remains visible and is never silently replaced by an older answer; the user may explicitly select an earlier answered turn.
- Add Whole branch comparison, displaying and sending the complete ordered history of both branches, including all user prompts, assistant responses, per-message provider/model attribution, available text attachments, and recorded model thinking. Recorded thinking is separately labeled in the UI and treated as contextual evidence by the comparison model.
- Include comparison mode and selected turn/history content in the immutable comparison dossier, AI assessment, follow-up discussion context, and Markdown export.

### Improved
- Tailor the prepared AI assessment instruction and re-examination request to the selected mode: one question/answer pair for Single response, or cross-turn consistency, evolving decisions, constraints, unresolved questions, corrections, and recorded thinking for Whole branch.
- Preserve prior AI comparison discussions when the same branch content and selection are reopened; comparison session identity no longer changes solely because a new dossier timestamp is created.
- Keep each branch's turn numbering independent. Branches with different message counts and different numbers of turns can be compared without aligning raw message indices.

### Compatibility
- No branch storage schema, conversation format, provider routing, Agent, Think, Git, Skills, workspace, or NativePageReader behavior changes.
- Existing latest-response comparison is preserved as the default Single response mode. Whole branch is opt-in.
- Whole branch comparison includes recorded `thinking` content only when that content exists in the saved branch messages. It does not synthesize or infer missing reasoning. Large histories can exceed a selected model's context limit; RunMind retains the dossier and displays an approximate context estimate rather than silently dropping turns.

### Validation
- Validate independent turn enumeration across different branch lengths, latest answered/unanswered turns, explicit earlier-turn selection, multiple assistant messages within one turn, Whole branch chronological history, recorded thinking inclusion, mode-aware AI instructions/export, comparison-session identity, inline JavaScript syntax, project XML, plugin manifest JSON, version alignment, changed-files-only overlay packaging, ZIP integrity, and exact overlay reapplication against RunMind 2.1.4.
- Windows/.NET compilation and live PowerToys/WebView2/provider testing require the target Windows environment and are not claimed here.

## [2.1.4] — Branch comparison uses each branch's latest completed turn

### Fixed
- Fix branch comparison selecting an older assistant answer when a branch has a newer user prompt that has not received a response yet. Comparison now requires an assistant response after that branch's own latest user message; it never falls back to an answer from an earlier turn.
- Synchronize the active branch from the live transcript immediately before comparison so the comparison cannot use a stale in-memory branch snapshot.
- Identify each compared response by its own user-turn number and a preview of the prompt it answers, making the selected turn visible in both comparison cards.
- Show a branch-specific explanation when either branch has no answer for its latest prompt.

### Compatibility
- Branches may contain different numbers of messages and turns. Each branch is evaluated independently; message indices are never compared across branches.
- Existing conversation and branch storage formats are unchanged. No provider, Agent, Think, or conversation persistence behavior is changed.

### Validation
- Validate differing branch lengths, answered and unanswered latest prompts, trailing user messages, latest-turn pairing, inline JavaScript syntax, project XML, plugin manifest JSON, version alignment, changed-files-only overlay packaging, ZIP integrity, and exact overlay reapplication against RunMind 2.1.3.
- Windows/.NET compilation and live PowerToys/WebView2 UI testing require the target Windows environment and are not claimed here.

## [2.1.3] — Preserve Think preference when editing prompts

### Fixed
- Preserve the user's Think toggle on each user prompt independently of the assistant's effective `thinkRequested` flag. The effective flag can be suppressed by Agent, MCP, or Deep Research modes, even while the user's Think selection remains enabled.
- Restore that saved per-prompt Think preference when editing and resubmitting the prompt. For older conversations without the new user-message field, infer the choice from the paired assistant response's request flag or stored thinking content.
- Restore the unsent draft's previous Think toggle when cancelling prompt editing.

### Compatibility
- Add optional `thinkEnabled` metadata to user messages in temporary branch archives and `.runmind` files. Existing version-1 and version-2 conversation files remain compatible; missing metadata uses the legacy assistant-response fallback.
- The global Think preference, provider request payload, Agent/MCP/Research behavior, and regeneration behavior are otherwise unchanged.

### Validation
- Validate inline JavaScript syntax, Think preference capture/edit/cancel/serialization paths, legacy conversation fallback, project XML, plugin manifest JSON, version alignment, changed-files-only packaging, ZIP integrity, and exact overlay reapplication against RunMind 2.1.2.
- Windows/.NET compilation and live provider tests require the target Windows environment and are not claimed here.

## [2.1.2] — Agent Settings UX and anchored flyouts

### Improved
- Redesign the Agent Settings flyout into four navigable sections: Agent, Project, Skills, and Security. Existing controls, element IDs, settings handlers, profile behavior, permission semantics, and Skills Manager operations are retained.
- Group related settings into consistent cards with standardized field spacing, full-width inputs/selects, aligned permission rows, wrapping action groups, clearer descriptions, and a focused section-level save action.
- Improve the Skills Manager presentation with separate scope selection, installed-skill list, description area, status feedback, and clearly differentiated install/refresh/remove actions. Preserve the manual installation paths and the Global versus Current Project distinction.

### Fixed
- Fix toolbar flyouts covering their trigger buttons and neighboring controls when a tall panel fits neither entirely above nor below the trigger. Flyouts now remain anchored to the trigger: they open below whenever they fit, flip above when necessary, and when neither side can contain the full panel they use the larger available side as a scrollable viewport. They are no longer centered over the toolbar as a fallback.
- Apply the shared placement correction to toolbar, conversation, appearance, OCR, and other panels using the shared flyout placement helper. User-moved/resized panels remain clamped to the viewport and retain their temporary geometry behavior.

### Compatibility
- No Agent Core, tool, permission, profile, Skills storage, conversation, provider, or NativePageReader behavior changes are intended.
- `Shared/NativePageReader.cs` and the native host bridge are unchanged in this release.

### Validation
- Validate all inline JavaScript syntax, Agent Settings tab/control wiring, unique control IDs, HTML structure, project XML, plugin manifest JSON, version alignment, flyout placement edge cases, changed-files-only packaging, ZIP integrity, and exact overlay reapplication against the reconstructed 2.1.1 tree (including the 2.1.1 build-fix overlay).
- A Windows/.NET build and live WebView2 layout testing at multiple window sizes/DPI scales require the target Windows environment and are not claimed here.

## [2.1.1] — Conversation lifecycle, startup polish, and Agent Skills Manager

### Build correction
- Fully qualify `System.Windows.Application` when dispatching the Global Skill folder picker. The Standalone project enables both WPF and Windows Forms, so the unqualified `Application` type is ambiguous.

### Fixed
- Fix `.runmind` Save As rejecting conversations serialized in format version 2. The shared native handler now accepts versions 1 and 2 while retaining message-array validation and the existing file-size limit.
- Prevent the saved appearance from flashing after the default theme on startup. Both WebView2 hosts now inject the saved appearance before document content is first rendered; the existing JavaScript appearance controls and cross-host synchronization remain in place.
- Replace the project-specific System Atlas example in the Project Profile name field with a neutral placeholder.

### Changed
- Separate temporary conversation recovery from intentional conversation saving. Active conversation branches are stored in a session-keyed IndexedDB archive; `sessionStorage` carries the active window's session identifier. Conversation branches are no longer duplicated into Local Storage.
- Keep the temporary archive isolated per top-level chat window and host profile. Reloads retain the same session; starting a new conversation rotates the session identifier and retires the previous temporary archive.
- Delete a session's temporary conversation archive, Deep Research checkpoints/history, and session-scoped Agent activity on normal window close. Startup cleanup removes expired session records and their associated temporary data; crashed or uncleanly terminated sessions receive a 24-hour lease grace period before cleanup.
- Retire the legacy 2.1.0 Local Storage branch snapshots on first 2.1.1 startup. Legacy IndexedDB branch archives and unowned legacy research checkpoints/history are expired after the 24-hour grace period. Existing Workspace Context folder handles remain in their separate IndexedDB database and are not removed by conversation-session cleanup.
- Restrict Deep Research checkpoint restoration to the owning window session. A fresh chat no longer searches for or claims a paused/interrupted task from another window.

### Added
- Add an Agent Skills Manager to Agent Settings with separate Global Skills and Current Project Skills scopes. It lists installed Skill names and descriptions, supports folder installation, and removes a selected Skill after confirmation.
- Add native global Skill installation/removal under `%LOCALAPPDATA%\RunMind\Skills` and project Skill installation/removal under `.agents/skills`. Installation validates `SKILL.md` metadata and applies bounded file-count, depth, and size limits; global removal rejects symbolic links and reparse points before recursive deletion. Existing files are never overwritten.
- Preserve manual installation support and the open Agent Skills folder layout. Scripts and other Skill assets may be stored, but scripts are never automatically executed and Skills cannot grant themselves permissions.

### Compatibility and limits
- `.runmind` versions 1 and 2 remain supported. Version 2 remains the current save format.
- IndexedDB remains in use for browser folder handles and for the full-size temporary conversation archive. Local Storage remains for application preferences and other persistent settings, not conversation branch transcripts.
- Active temporary conversations and Deep Research checkpoints survive reloads in their originating window. They are not a conversation library and are not shared with other windows. Save a conversation as `.runmind` to retain it intentionally or move it between windows/hosts.
- Global Skills are shared through the native per-user folder. Project Skills require an authorized project workspace in the current WebView profile. The two scopes are managed independently; deleting one installation does not delete the other.

### Validation
- JavaScript syntax, project XML, plugin manifest JSON, version alignment, static session/storage invariants, Save format-version acceptance, pre-paint theme bootstrap registration, NativePageReader source-hash/integration checks, changed-files-only overlay reconstruction, and ZIP integrity are checked against the reconstructed 2.1.0 source tree.
- A Windows/.NET compile and live WebView2, PowerToys Run, theme-flash, skill-folder-picker, and multi-window runtime tests require the target Windows environment and are not claimed here.

## [2.1.0] — Project-aware Agent, Git, Skills, and reliable downloads

### Added
- Add shared Project Profiles for Standalone and PowerToys Run. Profiles capture project instructions, provider/model plus the exact configured connection ID, safe Agent settings, enabled Skill names, MCP server IDs, a workspace folder-name hint, and the conversation permission policy. Credential fields and browser folder handles are not copied into a profile; freeform project instructions should not be used to store secrets.
- Extend Workspace Context snapshots in `.runmind` format version 2 while retaining version 1 import compatibility. Context remains optional and conversation-scoped. Browser directory handles stay in IndexedDB under the context ID; the folder name in a conversation or profile is only a reconnect hint.
- Add a narrow `AgentHostBridge` shared by both Windows hosts for the capabilities that require native Windows access. The shared `ChatWindow.html` remains the Agent runtime and owns tool definitions, policy decisions, approval UI, and orchestration.
- Add everyday Git tools: repository selection, clone, status, diff, log, show, branches, remotes, stage, unstage, commit, create/switch branch, merge, revert, cherry-pick, stash, fetch, pull, push, reset, clean, restore, and branch deletion. Git runs through `ProcessStartInfo.ArgumentList`, never a shell command string.
- Add `download_file` for original HTTP/HTTPS response bytes. The browser path handles smaller CORS-accessible downloads; the native path streams to disk, supports validated HTTP Range resume, ETag/Last-Modified checks, progress and cancellation, computes SHA-256, checks an optional expected digest, stages to a `.runmind.part` file, and finalizes without overwriting an existing destination.
- Add Agent Skills discovery and on-demand loading for the open `SKILL.md` format, from global `%LOCALAPPDATA%\RunMind\Skills\<name>\SKILL.md` and project `.agents/skills/<name>/SKILL.md` locations. Skill instructions and explicitly requested references are bounded; scripts are not automatically executed.
- Add conversation-scoped permission levels for Git reads, Git writes, Git remote operations, and downloads. Destructive Git actions always require a fresh approval, and native repository selection always prompts separately from browser folder access.

### Build corrections
- Fully qualify `Microsoft.Win32.SaveFileDialog` in the shared conversation save handler. Both Windows host projects enable WinForms, so an unqualified `SaveFileDialog` is ambiguous; the WPF owner overload is intentional.
- Fully qualify the WPF `System.Windows.Application` base type in the Standalone app entry point and remove a redundant `ObjectDisposedException` catch already covered by `InvalidOperationException`.

### Changed
- Keep Project Profile metadata in a shared per-user native store so both desktop hosts see the same profile catalog. The shared native adapter supplies policy defaults; conversation overrides travel with that conversation and saved profile. A named mutex serializes profile updates across host processes.
- Applying a Project Profile copies its values into the active conversation. Later profile edits do not mutate existing conversations. Applying a profile creates a new Workspace Context ID and does not transfer a browser folder grant or previously selected native Git repository.
- Bind native Git repository selection to the current conversation context. Switching contexts invalidates the native repository capability until the user selects or clones one for the active context.
- Require explicit remote and branch arguments for Git fetch, pull, and push. Always require fresh approval for destructive Git operations and commits (Git hooks may execute during a commit). Repository selection has its own fresh confirmation.
- Keep browser folder access and native Git selection separate. A cloned repository becomes available to native Git in that conversation; browser-based file tools require a separate `request_folder_access` action and user-selected folder.
- Restore and retain the original `NativePageReader` message handling and lifecycle in Standalone and PowerToys Run. `Shared/NativePageReader.cs` is unchanged from 2.0.9.

### Compatibility and limits
- `.runmind` versions 1 and 2 remain readable. Version 2 stores Workspace Context snapshots, not OS permissions, authentication material, or transferable filesystem grants.
- Project Profiles are shared between the two desktop hosts through `%LOCALAPPDATA%\RunMind\ProjectProfiles`. Browser directory handles remain specific to the WebView profile that stored them in IndexedDB; reselect/re-authorize the folder in another host or device. RunMind never silently substitutes a folder.
- Git requires Git for Windows (`git.exe`) on `PATH`. This release supports ordinary working trees with a local `.git` directory; linked Git worktrees and `.git` files are not supported. Clone accepts credential-free HTTP/HTTPS URLs; configure authentication with Windows Git Credential Manager. There is no unrestricted terminal tool.
- Native downloads are limited to 4 GB and two hours per operation, use a Windows Save dialog, and block private, loopback, link-local, and reserved IP destinations. Resume requires a matching URL fingerprint and a server validator (ETag or Last-Modified); otherwise a fresh transfer starts. Browser downloads are limited to 10 MB and require browser fetch/CORS access plus an authorized workspace folder. Downloads are never automatically extracted or executed.
- Agent Skills are discovered as metadata first and loaded only on demand. `SKILL.md` and individual references are limited to 128 KB; up to 10 references may be requested per load. Skills cannot grant permissions and scripts are not executed by this release.
- No project indexing, semantic search, reusable workflows, Standalone Launcher, or Command Palette UI is included. Those remain later roadmap work.
- No new package dependency is introduced.

### Validation
- JavaScript syntax and static integration checks, project XML parsing, plugin manifest JSON parsing, version alignment, NativePageReader source-hash/integration checks, changed-files-only overlay reconstruction, and ZIP integrity are checked against the supplied 2.0.9 source archive.
- A Windows/.NET build and live WebView2, Git, download, and PowerToys runtime tests are not claimed here: this environment has no .NET SDK or Windows runtime. Use the Windows build instructions and perform the first full runtime test on the target machine.

## [2.0.9] — Configurable PowerToys Run stage timeouts

### Changed
- Add independent PowerToys Run plugin settings for **Model discovery timeout** and **Provider connection timeout**. Both default to the previous hard-coded values (8 and 15 seconds respectively), preserving existing behavior for installations that do not yet have these settings saved.
- Model discovery timeout controls the selected provider's model-list request and response-body read. Provider connection timeout controls the initial inline generation request through receipt of its HTTP response headers; the existing Request timeout remains the overall generation-request limit.
- Validate both new settings to the inclusive range 1–300 seconds. Missing, malformed, or out-of-range stored values fall back to the previous defaults.
- Include the active configured timeout values in generic inline timeout diagnostics; model-discovery timeout errors report the configured discovery limit.

### Compatibility and scope
- Settings apply only to inline PowerToys Run requests. Standalone chat timeout behavior is unchanged.
- Existing settings retain their prior behavior: installations without the two new options continue using 8-second model discovery and 15-second provider connection defaults.
- No provider request format, query lifecycle, async result refresh, global-search behavior, or dependencies changed.

### Validation
- Source checks verify both settings are exposed through `AdditionalOptions`, parsed with backward-compatible defaults and range validation, stored in the immutable settings snapshot, and used at every former hard-coded timeout call site.
- Project XML, plugin manifest JSON, version alignment, changed-files-only overlay reconstruction, and ZIP integrity checked against the reconstructed 2.0.8 source tree.
- Windows/.NET compilation and live PowerToys settings/UI tests require the Windows build environment and excluded PowerToys reference assemblies; they are not claimed here.

## [2.0.8] — Reliable PowerToys result refresh

### Fixed
- Replace asynchronous `IResultUpdated` publication with PowerToys' public `IPublicAPI.ChangeQuery(query, requery: true)` mechanism after inline generation completes. This makes the completed answer flow through the ordinary delayed-query result pipeline, including the host's normal plugin metadata assignment.
- Keep generation asynchronous and release the per-plugin execution gate immediately. Completion triggers exactly one same-query refresh; the delayed query then returns the cached answer without issuing another model request.
- Re-check the active operation on the UI dispatcher immediately before requesting the refresh, preventing a completed older query from replacing newer text typed in the meantime.
- Remove the unused `IResultUpdated` implementation from RunMind; the completed answer no longer depends on PowerToys registering or accepting that event.

### Compatibility and scope
- Preserve global search, the `llm` action keyword, the live 100-character prompt preview, the pending chat-opening result, provider request behavior, and the non-blocking cancellation lifecycle.
- The answer replaces the pending RunMind result after one host-managed requery. The model is not called a second time.
- No debounce, timer, keyboard hook, dependency, or additional network request is introduced.

### Validation
- Source-level checks verify the delayed callback still returns immediately, generation runs once per operation, completion is cached before refresh, and the refresh is guarded against a superseding query.
- Project XML, plugin manifest JSON, version alignment, changed-files-only overlay reconstruction, and ZIP integrity checked against the reconstructed 2.0.7 source tree.
- Windows/.NET compilation and live PowerToys integration tests require the Windows build environment and excluded PowerToys reference assemblies; they are not claimed here.

## [2.0.7] — Live inline query preview

### Changed
- Restore the live prompt preview in the fast **Ask RunMind** result. The subtitle now reflects the latest prompt delivered to RunMind while retaining the **Open in chat** and background-generation status.
- Use a small, bounded whitespace-normalizing preview routine on the fast path. It avoids regular expressions and caps displayed query text at 100 characters, keeping per-keystroke formatting lightweight.
- Preserve the asynchronous delayed-generation flow introduced in 2.0.6: model requests continue outside PowerToys' per-plugin execution gate, and completed results are published against their originating query generation.

### Compatibility and scope
- Preserve global search, the `llm` action keyword, provider behavior, query cancellation, latest-operation click handoff, and asynchronous result publication.
- The displayed preview updates only when PowerToys delivers a new query to RunMind; the plugin cannot display text that the host has not dispatched.
- No debounce, timer, keyboard hook, dependency, or additional model request is introduced.

### Validation
- Source-level checks verify both fast and delayed pending-result creation use the query prompt, preview formatting is bounded, and the asynchronous generation/publication lifecycle is unchanged.
- Project XML, plugin manifest JSON, version alignment, changed-files-only overlay reconstruction, and ZIP integrity checked against the reconstructed 2.0.6 project state.
- Windows/.NET compilation and live PowerToys timing tests require the Windows build environment and PowerToys reference assemblies; they are not claimed here.

## [2.0.6] — Asynchronous PowerToys inline result publication

### Fixed
- Stop blocking PowerToys' per-plugin execution gate while an inline model request is running. The delayed callback now schedules generation in the background and returns immediately, allowing subsequent keystrokes to reach the fast query path and cancel superseded operations.
- Publish completed inline responses through PowerToys' `IResultUpdated` event, carrying the original query generation so the host can reject stale responses.
- Keep the fast pending result available for opening chat while generation runs. Its title and subtitle are now stable and no longer format a changing preview on every keystroke.
- Remove the obsolete synchronous completion waiter from the query-operation lifecycle; cancellation and disposal remain coordinated with asynchronous cancellation callbacks.

### Compatibility and scope
- Preserve global plugin search, the `llm` action keyword, the immediate pending chat handoff, existing providers, query validation, and the 2.0.5 cancellation/disposal safeguards.
- The completed inline answer is delivered asynchronously as a separate result while the pending **Open in chat** result remains available. PowerToys controls its own result-list refresh cycle; RunMind no longer holds its plugin execution gate for the duration of generation.
- No artificial debounce, keyboard hooks, dependencies, or class extraction added.

### Validation
- Source-level checks cover immediate return from the delayed callback, one background generation per operation, query-generation-tagged result publication, cancellation of superseded work, and removal of synchronous waiting.
- Project XML, plugin manifest JSON, version alignment, changed-files-only overlay reconstruction, and ZIP integrity checked against the reconstructed 2.0.5 baseline.
- Windows/.NET compilation and live PowerToys integration tests were not available in this environment.

## [2.0.5] — Non-blocking PowerToys inline query cancellation

### Changed
- Replace synchronous `CancellationTokenSource.Cancel()` callback execution in the inline query operation with `CancellationTokenSource.CancelAsync()`. The fast PowerToys query path now marks the previous operation cancelled immediately and returns its new pending result without waiting for registered cancellation callbacks to finish.
- Keep the previous cancellation source reserved until all asynchronous cancellation callbacks complete; only then can the existing operation lifecycle dispose it. Pre-start cancellation, completion signaling, handoff invalidation, and current-operation checks remain intact.
- Add Debug-build timing diagnostics for fast-result preparation and previous-query cancellation callback completion. Diagnostics do not include the user's search text and are omitted from Release builds.

### Compatibility and scope
- Preserve the existing `IPlugin.Query()` / `IDelayedExecutionPlugin.Query()` split, the existing query filters, model/provider request formats, and the pending-result handoff behavior. No artificial debounce, timer, keyboard hook, or additional network request is introduced.
- PowerToys must still dispatch the updated search text to RunMind; Input smoothing can delay that host callback. This change removes RunMind's synchronous wait for cancellation callbacks, not PowerToys' own input-dispatch delay.
- The project targets .NET 10, where `CancellationTokenSource.CancelAsync()` is available. No new package or runtime dependency is added.

### Validation
- Source-level checks verify that the fast replacement path invokes non-blocking cancellation, that cancellation is marked before the callback task is observed, and that the cancellation source remains protected from disposal until asynchronous callbacks finish.
- Check pre-start cancellation signaling, repeated Cancel/Dispose calls, completion/disposal ordering, and DEBUG-only timing diagnostics. Validate C# delimiter/token balance, project XML, plugin manifest JSON, release-version alignment, changed-files-only overlay reconstruction, and ZIP integrity against the supplied 2.0.4 baseline.
- Windows/.NET compilation and live PowerToys timing tests require the Windows build environment and excluded PowerToys reference assemblies; they are not claimed here.

## [2.0.4] — PowerToys inline latest-query handoff

### Fixed
- Resolve clicks on pending inline results against the latest active query RunMind has received, rather than unconditionally using the prompt captured by the result that PowerToys may still be displaying.
- Use the selected operation's own prompt, model, and provider settings together when opening the chat or displaying a completed response. This prevents an older visible result from opening a chat with an earlier prompt after a newer query has already reached the plugin.
- Reject a click if no valid active query remains; do not fall back to an older prompt.

### Compatibility and limitation
- The fast query path and existing query-operation lifecycle are retained. This change does not add an artificial debounce or delay.
- PowerToys must deliver the newer search text to the plugin before RunMind can use it. Text still being typed but not yet dispatched by PowerToys is not observable through the plugin result action.

### Validation
- Source-level checks verify the pending-result action resolves the active query under the query lock and uses a single operation's prompt/model/settings for both handoff and completed-response paths.
- Project XML, plugin manifest JSON, release-version alignment, overlay application, changed-files-only contents, and ZIP integrity checked against the supplied 2.0.3 overlay plus the 2.0.2 baseline.
- Windows/.NET compilation and live PowerToys interaction tests were not available in this environment.

## [2.0.3] — Inline reliability and WebView security hardening

### Fixed
- Replace the ambiguous query-operation start Boolean with explicit **Started**, **AlreadyRunning**, and **Terminal** outcomes. A delayed callback waits only when another callback is actually running the operation; cancelling or handing off an operation before it starts now completes its wait signal and cannot leave a worker waiting forever.
- Coordinate settings publication and active-query invalidation under a consistent settings-then-query lock order. Versioned settings snapshots prevent stale fast/delayed callbacks from starting work after a settings change, without cancelling a new query created from the new settings.
- Use one shared input filter for the fast and delayed PowerToys query paths, including the `llm`, action-keyword, and standalone app-name exclusions.
- Default-deny WebView2 navigation. Only the exact mapped `https://localhost/ChatWindow.html` page on the default HTTPS port is trusted; invalid and unsupported schemes are blocked, while HTTP, HTTPS, and mailto links continue to open externally. Web messages are dispatched only from that trusted page origin.
- Bound accumulated PowerToys Run inline output to 8 Mi characters for both Ollama and LM Studio instead of allowing unlimited response accumulation.
- Require an explicit successful stream-completion signal: Ollama `done: true`, or LM Studio `[DONE]` / a final non-null OpenAI-compatible `finish_reason`. Unexpected EOF is reported as an incomplete response rather than a successful partial answer.
- Report model-discovery timeouts separately from response-generation timeouts and clarify that the configurable Request timeout does not affect model discovery or connection establishment.
- Preserve Ollama endpoint query parameters when deriving the native `/api/tags` model-discovery URL.

### Compatibility and scope
- Keep the existing 55 MiB conversation-window transfer limit and preview normalization unchanged; neither was changed without usage measurements or focused regression coverage.
- Keep `IsValidModelAsync()` and the public compatibility overload; their in-file call-site count alone is not enough to prove they are unused by other assemblies.
- The chat-window Ollama base URL still omits endpoint query strings because the current WebView code appends API paths to that base. Forwarding query strings there requires a path-aware URL composition change in `ChatWindow.html`; they are not appended blindly in this release. Native `/api/tags` discovery does preserve them.
- No class extraction, dependency changes, or broader refactoring is included.

### Validation
- Source-level checks cover query-operation terminal/start branches, pre-start cancellation signalling, settings-snapshot version checks, lock-order consistency, fast/delayed query-filter reuse, WebView2 default-deny navigation and origin gating, response-size enforcement, explicit stream completion, timeout messages, and Ollama discovery query preservation.
- C# delimiter/token balance, C# project XML, plugin manifest JSON, version alignment, and changed-files-only overlay application checked against the supplied 2.0.2 archive.
- The .NET SDK, Windows WPF/WebView2 runtime, and excluded PowerToys reference assemblies were unavailable, so Windows/.NET compilation and live PowerToys integration tests were not run.

## [2.0.2] — Clay appearance palettes

### Added
- Add the paired **Clay** light and dark palettes to the Appearance selectors, inspired by the supplied reference screenshots without copying the original theme name.
- Use the reference's exact base colors: `#F9F9F7` light background, `#2D2D2B` dark background / light foreground, and `#CC7D5E` shared accent. Supporting surfaces, borders, muted text, and message fills are tuned to the same warm-neutral palette for RunMind's controls and panels.
- Preserve independent Light and Dark palette selections, per-user persistence, instant application, and existing defaults. Clay is accepted consistently by the WebView and native preference store for both standalone and PowerToys-hosted windows.

### Validation
- Theme selector counts and IDs, JavaScript syntax, C# project XML, plugin manifest JSON, native preference allow-lists, release-version alignment, and changed-files-only overlay application checked against the supplied 2.0.0 archive plus the 2.0.1 overlay.
- The supplied reference screenshots were inspected for palette colors. Windows/.NET compilation and live WebView2/PowerToys runtime validation were not available in this environment.

## [2.0.1] — New conversation workflow

### Added
- Add **New conversation** at the top of the Conversations flyout.
- Reuse the existing Open Conversation destination chooser for both actions, allowing a new conversation to start in the current window or a separate window.
- Opening a new window leaves the current chat and any in-progress work untouched. The new window inherits the exact selected provider/model connection, current Think setting, and shared appearance/preferences while receiving a fresh conversation identity.
- Starting in the current window asks before replacing existing conversation content: **Save & continue**, **Discard & continue**, or **Cancel**. Save As cancellation or failure leaves the current chat in place.
- Give each new conversation a fresh window-scoped storage identity so previous messages, branch histories, workflow state, research checkpoints, and comparison sessions are not loaded into it. Existing saved files and prior window-scoped records are not deleted.

### Safety and behavior
- Stop active chat generation, research, summarization, and tool-retry work only after the user confirms replacing the current window. Cancelling the confirmation leaves active work running.
- `.runmind` Save & continue preserves the saved message/branch content supported by the existing format. Unsent composer text, pending attachments, and AI Branch Comparison discussion/drafts are not part of that file; the confirmation explains this before replacement.
- Provider/model selection and appearance preferences remain unchanged; the new conversation gets a fresh title and identity.

### Validation
- JavaScript syntax and new-conversation flow checks; C# project XML and plugin manifest validation; PowerToys/standalone host-message and bootstrap-path source checks; release-version alignment; and changed-files-only overlay application against the supplied 2.0.0 archive.
- Windows/.NET compilation and live WebView2/PowerToys integration were not run in this environment because the required Windows build environment and excluded PowerToys reference libraries are unavailable.

## [2.0.0] — Data, documents, and media capabilities

### Added
- Add a local, dependency-free artifact generator for Word DOCX, Excel XLSX, paginated text PDF, SVG QR codes, and multi-image HTML galleries.
- Add structural artifact verification for DOCX/XLSX package parts and XML, PDF structure, SVG/HTML roots, JSON syntax, and CSV/TSV readability. Generated artifacts are verified after saving; verification does not claim visual or semantic correctness.
- Add multi-series bar/line charts plus pie/doughnut charts as self-contained SVG. Add editable Mermaid flowchart source alongside SVG diagrams by default, with selectable TD/TB/LR/RL/BT direction.
- Extend sitemap reading to bounded recursive sitemap-index traversal, URL deduplication, same-host-only child sitemaps, and text search across URL and sitemap metadata.
- Extend JSON transforms with pick/omit fields, recursive key renaming, flatten/unflatten, array filtering, and array-field mapping; optionally save transformed JSON as a new workspace file.
- Add deterministic CSV/TSV dataset validation for required and unique columns and bounded row rules. Improve delimiter inference across dataset analysis, filtering, deduplication, schema inference, joins, and merges.
- Enhance Markdown reports with titles, summaries, table of contents, and generation metadata; render Markdown as safe HTML for HTML reports.

### Safety and limits
- All generated files require the existing in-app user approval and are created as new files; existing files are never overwritten.
- Generators run locally/offline and introduce no runtime package dependencies. DOCX supports a bounded Markdown subset; XLSX is limited to 10 worksheets, 10,000 rows per sheet, 50 columns, and 100,000 cells total; PDF is a text-report format using built-in fonts and does not render images or complex layouts; QR codes support up to 106 UTF-8 bytes; galleries reference workspace images rather than copying or embedding them.
- Sitemap traversal is restricted to the starting hostname and bounded to 20 sitemap documents, 2,000 matching URLs, and a bounded scan budget. Artifact verification checks structure, not visual layout, formula results, factual correctness, or semantic completeness.
- No presentation generator, metadata-preserving document conversion, or website change-monitoring subsystem is included in this release.

### Validation
- Independent package-reader checks for DOCX, XLSX, and PDF; XML parsing for all generated Office package parts; SVG parsing; QR decode checks for multiple payload sizes; JavaScript syntax and tool-dispatch checks; project XML and plugin manifest validation; and overlay application against the reconstructed 1.10.34 baseline.
- Windows/.NET compilation and live WebView2/PowerToys integration require the Windows build environment and excluded reference/resource files and are not claimed here.

## [1.10.34] — Spacious, movable and resizable flyouts

### Changed
- Increase the default working width of toolbar, Appearance, Conversations, OCR, Context Manager, and Deep Research panels while keeping each panel bounded by the current WebView viewport.
- Replace trigger-gap height clamping with viewport-aware placement. If a panel cannot fit above or below its trigger, it opens as a centered floating panel using the full available viewport instead of being squeezed into a narrow scroll area.
- Add a visible top drag grip and a bottom-right resize grip to every toolbar, Conversations, Appearance, and Deep Research flyout. Dragging and resizing are constrained to the visible viewport, with keyboard arrow-key support on both controls.
- Keep moved/resized geometry only while that flyout is open. Closing it, opening another flyout, or pressing Escape returns it to its default size and anchor; no geometry is persisted.
- Widen the Conversations panel and make its Save/Open action buttons wrap safely so **Export copy** remains fully visible when the panel content fits the window.
- Keep internal scrolling for genuinely long content, such as long settings lists and previews; ordinary panels no longer gain an outer scrollbar solely because their trigger is near a viewport edge.

### Validation
- JavaScript syntax, project XML, plugin manifest JSON, version alignment, and changed-files-only overlay application checked.
- Chromium layout and interaction checks cover all 11 flyouts, drag/resize, close-and-reopen reset, and the Conversations export button in a compact 620 × 420 viewport. No runtime errors or unnecessary outer scrolling were observed for panels whose content fits.
- Windows/.NET compilation and live WebView2/PowerToys interaction require the Windows build environment and excluded PowerToys reference libraries; they were not run here.

## [1.10.33] — Reliability and performance hardening

### Fixed
- Reduce full transcript rendering from repeated per-message array scans to one linear pass that computes the latest assistant/editable-user indexes and pending-message state once per render. Streaming continues to patch only the active message.
- Exclude blank CSV cells from numeric statistics, calculate minimum/maximum/mean iteratively without argument spreading, and preserve statistics for duplicate column headings using explicit column indexes.
- Validate CSV delimiters consistently as exactly one character rather than silently truncating multi-character values. CSV export now quotes values containing the selected delimiter. Named column operations report ambiguous duplicate headings; whole-row deduplication still handles duplicate headings correctly.
- Bound workspace traversal independently by result count, scanned-file count, per-file content size, and cumulative content bytes. Skip common dependency, build, cache, and backup directories; avoid returning unrelated directory entries during searches.
- Resolve workspace-handle and conversation-branch IndexedDB actions only after transaction completion, and reset the cached workspace database promise after an open failure.
- Detect duplicate attachment filenames across the entire conversation instead of silently selecting the newest matching attachment.
- Revalidate edit, delete, and restore destination snapshots after user approval. Backups are created from the revalidated file, and operations stop if the file changed while approval was pending.
- Remove the unused `openResearchWorkspace()` wrapper; research panel visibility remains controlled by the existing shared state functions.

### Compatibility and safety notes
- Workspace search now returns scan counts and a truncation reason alongside its results so budget-limited searches are visible to the agent.
- File snapshot revalidation compares metadata and content in bounded chunks. The File System Access API does not provide an atomic cross-process create-if-absent primitive, so simultaneous independent writers can still race between the final existence check and file creation.
- Existing streaming DOM patching, backup creation, and destructive-operation approval remain in place. Backup retention and transcript virtualization are intentionally unchanged.

### Validation
- Source-level regression checks cover rendering complexity, CSV edge cases, traversal budgets, IndexedDB completion semantics, duplicate attachment detection, and post-approval file revalidation.
- JavaScript syntax, project XML, plugin manifest JSON, version alignment, and overlay integrity checked. Windows/.NET compilation and live WebView2/PowerToys workspace operations require the Windows build environment and excluded PowerToys reference libraries.

## [1.10.32] — Curated appearance themes

### Added
- Add a dedicated Appearance menu with System, Light, and Dark modes, plus five curated light palettes and five curated dark palettes.
- Preserve the existing Classic Light and Warm Graphite palettes as the defaults. Light and dark palette choices are stored independently and restored when the effective mode changes.
- Add Soft Ivory, Sage Garden, Rose Clay, and Sandstone light palettes; add Obsidian, Deep Forest, Mocha, and Burgundy dark palettes.
- Save appearance preferences in one shared per-user file under `%LOCALAPPDATA%\RunMind`, so standalone RunMind and PowerToys-hosted chat windows use the same selections despite separate WebView2 profiles.
- Apply palette changes immediately and synchronize standalone and PowerToys chat-window native title bars with the selected effective light/dark mode.
- Add a compact active-palette preview and a reset-to-defaults action.

### Compatibility
- Migrate the previous System/Light/Dark selection when no shared appearance preference exists. The existing light and dark colors are preserved as the starting palettes.

### Validation
- JavaScript syntax, HTML theme-selector counts, project XML, plugin manifest JSON, and overlay contents checked. Full Windows/.NET build and WebView2 runtime validation require the Windows build environment and the excluded PowerToys reference libraries.

## [1.10.31] — Refine AI branch comparison action

- Renamed the comparison-workspace action to **Compare with AI** and kept its label consistent before and after the first assessment; the discussion window remains titled **AI Branch Comparison**.
- Replaced the generic filled-primary button with a purpose-built control using RunMind theme tokens for color, surfaces, borders, focus, hover, and disabled states.
- Updated the supporting hint to describe the independent review without implying that it opens a separate discussion mode.

## [1.10.30] — Fix LM Studio title generation and refine comparison composer

### Fixed
- Align the native LM Studio conversation-title request with RunMind’s regular LM Studio generation behavior: explicitly disable reasoning with `reasoning_effort: none` and `chat_template_kwargs.enable_thinking: false`, while preserving the configured Nemotron conversational-mode setting.
- Add safe retries when an LM Studio server explicitly rejects an optional reasoning/template/token parameter. Parse both string and text-part forms of OpenAI-compatible assistant content, with support for legacy completion text; report a diagnostic when no assistant text is returned instead of silently keeping the fallback title.
- Apply the same reasoning-off settings, compatibility fallback, and response extraction behavior to the standalone WebView title-generation path.

### Changed
- The AI Branch Comparison composer now starts at one line, grows automatically with its content up to five visible lines, and scrolls internally beyond that height. The main chat composer is unchanged.

### Validation
- Source checks verify the LM Studio title request settings and supported response shapes, plus the one-to-five-line comparison composer sizing and auto-resize behavior.
- JavaScript syntax, project XML, plugin manifest JSON, version alignment, ZIP integrity, and clean overlay application were checked. Windows/.NET compilation and live LM Studio/WebView2 interaction require validation on Windows.

## [1.10.29] — Correct generated conversation-title application

### Fixed
- Fixed the remaining PowerToys native-response title issue when WebView2 initialization replaces the original control with a temporary-profile fallback. Title generation and application now resolve the WebView2 instance currently hosted by the window instead of retaining the disposed original control.
- Ignore queued document-title and navigation-completion events from replaced WebView2 controls so an obsolete page cannot overwrite the active window title.
- Apply the generated title to the active chat page first, verify that the page reports the expected document title, retry transient application failures, and only then synchronize the native window title.
- Keep the already-visible fallback title when generation or application fails, and emit a diagnostic instead of silently re-applying the fallback as though generation succeeded.

### Validation
- Source-level checks cover the temporary-profile replacement path, active-WebView resolution during readiness and title application, stale event guards, and confirmation of the page title before updating the native window title.
- JavaScript syntax, project XML, plugin manifest JSON, version alignment, and clean-baseline overlay application were checked. Windows/.NET compilation and live WebView2/provider interaction require validation on Windows.

## [1.10.28] — Expand AI Branch Comparison composer

### Changed
- Increase the AI Branch Comparison composer to show at least five lines from the outset.
- Automatically grow the comparison composer as the user types, up to a comfortable maximum height; only longer messages then scroll inside the text field.
- Keep the change scoped to the AI Branch Comparison modal; the main chat composer is unchanged.

### Validation
- Checked the composer markup, CSS sizing, and auto-growth/reset handlers; JavaScript syntax, project XML, plugin manifest JSON, version alignment, and overlay integrity were checked.
- Windows/.NET compilation and live WebView2 interaction require validation on Windows.

## [1.10.27] — Apply generated conversation titles after chat initialization

### Fixed
- Fixed a race in the PowerToys native-response window where the title-generation request could finish before WebView2 initialized. The page's subsequent fallback-title initialization could then overwrite the generated title.
- Wait for the chat page to finish initializing before starting the title request, and skip the extra request if the window closes or WebView2 initialization fails.
- Preserve the generated title in both the WebView conversation heading and the native window title.

### Validation
- Source-level checks verified that title generation is sequenced after successful WebView2 initialization and that the existing JavaScript title setter and native `DocumentTitleChanged` bridge remain connected.
- JavaScript syntax, project XML, plugin manifest JSON, version alignment, and overlay integrity were checked. Windows/.NET compilation and live provider/WebView2 behavior require validation on Windows.

## [1.10.26] — User-controlled initial AI branch assessment

### Changed
- Opening **AI Branch Comparison** now prepares the discussion without automatically making a model request.
- The initial assessment instruction is pre-filled in the message composer so users can review, edit, extend, or replace it before sending.
- The system prompt and immutable original comparison dossier remain hidden and are still supplied automatically with the user's submitted instruction.
- The edited instruction is recorded as the first user message and included in message copy and Markdown export. The same composer continues to support follow-up discussion after the first assessment.
- The initial assessment is generated only after the user explicitly presses Send; model selection and the main chat model remain independent.

### Validation
- Embedded JavaScript syntax, HTML structure/IDs, project XML, plugin manifest JSON, version alignment, and overlay integrity were checked.
- The initial-send request path was checked to confirm that the submitted composer text is used verbatim as the initial user instruction, with the system prompt and dossier kept as separate hidden messages.
- Windows/.NET compilation and live WebView2/provider interaction require validation on Windows.

## [1.10.25] — Persistent AI branch comparison discussion

### Added
- Expanded the branch comparison workspace to take over the complete area below the application header, including the transcript, branch controls, composer, and chat toolbar. The header remains visible; **Back to chat** restores the normal conversation layout.
- Turned **AI Branch Comparison** into a centered discussion modal. The initial assessment and follow-up conversation remain available when the modal is closed and reopened during the same app session.
- Bound discussion history to an immutable comparison snapshot containing both original requests, complete branch responses, branch names, and available model/provider attribution. Re-comparing unchanged snapshots restores their existing discussion; changed responses or requests create a separate discussion.
- Added follow-up chat using the independently selected comparison model. Model changes do not affect the main chat model, and the discussion uses no tools, agents, or Deep Research.
- Added **Re-examine branches** to request a fresh assessment against the original complete dossier while retaining prior discussion messages.
- Added per-message Copy actions, **Copy discussion**, and a local **Download Markdown** export containing the original requests, both complete branch responses, the initial assessment, and the ordered follow-up discussion with assistant model/provider attribution.
- Added an approximate discussion-context estimate. RunMind retains the complete dossier and discussion history rather than silently dropping older messages.
- Changed the generation Stop control to a red square; the separate X remains the Close control.

### Unchanged behavior
- The comparison-model selector defaults to the current chat model, remains independent of the main model selection, and continues to use the existing Ollama and OpenAI-compatible provider adapters.
- Comparison sessions are in-memory and last for the lifetime of the current RunMind page session. They are not added to branch transcripts or conversation saves.

### Validation
- JavaScript syntax, comparison workspace/modal markup and event wiring, snapshot/session restoration paths, copy/export formatting, version alignment, project XML, plugin JSON, and clean-baseline overlay application checked.
- Full Windows/.NET compilation and live WebView2/provider visual/integration testing still require Windows.

## [1.10.24] — Reimagined branch comparison workspace

### Changed
- Reworked branch comparison into a dedicated workspace that temporarily uses the conversation viewport instead of squeezing the transcript. The chat composer remains anchored in its normal position.
- Expanded the two selected branch responses into equal-width, independently scrollable panels with formatted Markdown, using the available workspace height.
- Kept the AI result in a separate **AI Branch Comparison** modal, styled with RunMind's existing theme colors, surfaces, borders, typography, and controls rather than an embedded-page appearance.

### Added
- Added a Copy control to copy the generated AI branch comparison as Markdown/plain text.
- Replaced text-based Stop and Close controls with compact X icon buttons; both have accessible labels and tooltips. Stop cancels generation without dismissing the modal; Close, backdrop click, and Escape dismiss it and cancel an active request.
- Added modal status styling for running, completed, and failed comparisons, while keeping the result area independently scrollable.

### Unchanged behavior
- The independent comparison-model selector still defaults to the selected chat model. Choosing another model affects only the comparison request.
- The fixed RunMind comparison prompt, provider adapters, streaming, and branch-history isolation remain unchanged.
- Only each branch's latest user request and latest assistant response are sent. The result is not inserted into either branch or the saved transcript.

### Validation
- JavaScript syntax and HTML structure, unique element IDs, comparison-workspace/modal transitions, Copy/Stop/Close wiring, project XML, plugin JSON, version alignment, and clean-baseline overlay application checked.
- Full Windows/.NET build and live WebView2/provider testing still require Windows.

## [1.10.23] — Contained AI branch comparison

### Fixed
- Kept the side-by-side branch preview bounded to its own scrollable area, with shorter independently scrollable response panes. Opening a comparison no longer consumes the chat transcript's available height.
- Moved the model-generated assessment into a fixed modal with a dedicated scrollable result region. Long or streaming assessments no longer expand the page layout or push the message composer out of view.
- Added Close, backdrop-click, and Escape dismissal for the assessment modal. Closing it stops an in-flight comparison; the Stop control remains available inside the modal.

### Unchanged behavior
- The independent comparison-model selector, fixed RunMind assessment template, provider adapters, selected branch inputs, streaming, and non-persistence behavior are unchanged.
- The assessment still uses only each branch's latest user request and latest assistant response. It does not alter either branch or the main chat model.

### Validation
- JavaScript syntax, modal and comparison event wiring, bounded layout CSS, project XML, plugin JSON, version alignment, ZIP integrity, and clean-baseline overlay application checked.
- Full Windows/.NET build and live WebView2 visual testing still require Windows.

## [1.10.22] — AI-assisted conversation branch comparison

### Added
- Added **Ask model to compare** to the existing side-by-side branch comparison view. The assessment uses the latest assistant response in each branch and includes the latest user request preceding each response for context.
- Added an independent model selector for branch comparison. It defaults to the currently selected chat model, lists discovered models across configured Ollama and OpenAI-compatible connections, and lets the user choose a different model without changing the main chat selection.
- Added a fixed RunMind comparison template that evaluates relevance, correctness where assessable, completeness, reasoning care, clarity, usefulness, and actionable detail. The result streams into the comparison panel, supports cancellation, and is rendered as Markdown.

### Behavior and limits
- AI comparison is a separate one-off provider generation request with no Agent/Deep Research tools, no custom conversation-history injection, and no writes to either branch. Its result is not appended to the transcript or persisted as a conversation message.
- The comparison sends only the latest associated user request and latest assistant response from each branch, with bounded text lengths; it does not submit complete branch histories or model reasoning/thinking output.
- The independent model choice does not modify the globally selected chat model or saved chat settings. Ollama and OpenAI-compatible provider adapters are reused.

### Validation
- JavaScript syntax, branch-comparison UI wiring, independent-model selection, provider-adapter request shape, cancellation paths, version alignment, project XML, plugin JSON, and overlay ZIP integrity checked. Windows/.NET 10 compilation and live WebView2/provider testing still require Windows.

## [1.10.21] — Reader fallback on public-host DNS filtering

### Fixed
- Corrected Native Reader handling when a syntactically public hostname (for example, `github.com`) resolves locally only to non-public IP addresses. This condition is now reported as a Native retrieval failure, not `NATIVE_DESTINATION_BLOCKED`, so the application continues through the configured **Custom Reader → Jina Reader** fallback chain (or directly to Jina when no custom gateway is configured).
- The Native Reader still never connects to non-public DNS answers. It filters all candidate socket addresses and makes no local connection when no globally routable answer exists. The original hostname is passed to the configured remote Reader, which performs its own resolution.
- Preserved hard security stops for explicit private/loopback/link-local/reserved IP literals, local and special-use hostnames, credential-bearing URLs, disallowed research domains, and unsafe redirects. These are not forwarded to remote Readers.
- Kept fallback orchestration inside RunMind's shared webpage retrieval pipeline. The model receives the retrieved content after a Reader succeeds, or one consolidated failure after all configured Readers fail; it is not asked to orchestrate retries itself.

### Diagnostics
- Native DNS-only-private failures retain a useful hostname-level reason without exposing resolved IP addresses. Existing console diagnostics record each remote fallback attempt and its outcome.

### Validation
- Source regression checks cover DNS-only-private answers taking the recoverable Native-failure path, no connection to non-public candidates, explicit private/local URL hard stops, and Custom → Jina / direct-to-Jina ordering.
- Project/plugin version alignment, JavaScript syntax, project XML, and ZIP integrity checked. Windows/.NET compilation and live WebView2/network integration remain to be tested on Windows.

## [1.10.20] — Connection capabilities and message identity

### Added
- Custom OpenAI-compatible connections can optionally enable LM Studio native model metadata discovery (`/api/v1/models`, with `/api/v0/models` legacy fallback).
- Message metadata preserves the selected connection name so headers identify the actual server rather than only its protocol/provider type.

### Changed
- Built-in LM Studio retains native metadata discovery. Generic custom OpenAI-compatible connections do not probe LM Studio-specific endpoints unless explicitly enabled.
- Connection names are preserved in saved conversations, branch archives, and JSONL exports. Older messages without a saved connection name retain provider-label fallback.

### Compatibility
- Existing custom connections default to LM Studio metadata disabled. The built-in LM Studio connection remains enabled.
- No changes to OpenAI-compatible model discovery (`/v1/models`) or chat-completions routes.

## [1.10.19] — Native Reader DNS handling and Nemotron settings layout

### Fixed
- Fixed the Nemotron Conversational Mode checkbox layout in the generation settings flyout. The control now uses a compact native checkbox, its label is aligned beside it, and the explanatory note spans the full settings-grid width.
- Fixed Native Reader handling of mixed DNS responses. A hostname is no longer rejected merely because one A/AAAA answer is non-public; the connection callback filters out every non-public address and connects only to globally routable candidates. Hosts resolving exclusively to private, local, link-local, or reserved addresses remain blocked.
- Narrowed the IPv4 special-purpose check for `192.0.0.0/16` to the actual `192.0.0.0/24` assignment, while continuing to block `192.168.0.0/16` and the documented special-use ranges.

### Diagnostics
- When DNS has no globally routable result, the Native Reader error now identifies the hostname and suggests checking split-horizon DNS, VPN, or DNS filtering without exposing resolved private IP addresses.

### Validation
- JavaScript syntax and Nemotron checkbox structure/style checks passed.
- Native destination filtering regression cases passed for public-only, mixed public/private, private-only, loopback, link-local, and reserved DNS answers; socket candidates are always filtered before connect.
- Project XML, plugin JSON, version alignment, and ZIP integrity checks passed.
- Windows/.NET compilation and live WebView2/network integration remain to be tested on Windows.

## [1.10.18] — Reader routing, Nemotron conversational mode, and branch-aware saves

### Changed
- Changed the desktop Reader fallback order to **Native Reader → configured Custom Reader → Jina Reader**. A configured custom gateway is attempted first after native retrieval/extraction fails; Jina is attempted when no custom gateway is configured or when the configured custom gateway also fails. Private/local/security-blocked destinations remain hard stops and are never forwarded to remote Readers.
- Reordered the Agent Reader settings to present the custom gateway before Jina and labelled each service's fallback priority.

### Added
- Added a model-specific **Nemotron Conversational Mode** control to Chat Generation Settings. It is shown only for recognized NVIDIA Nemotron 3.5 Bolt E4B model IDs; it is enabled only for an OpenAI-compatible connection. RunMind sends `chat_template_kwargs.conversational_mode` as an explicit boolean alongside the existing `enable_thinking` template argument, so users can request either `true` or `false`.
- Added the equivalent optional checkbox to PowerToys Run settings. It is applied only when the selected provider is LM Studio/OpenAI-compatible and the effective model ID matches Nemotron 3.5 Bolt E4B. Ollama's native API and LM Studio Native REST mode do not expose this chat-template request argument, so the chat control explains why it is unavailable and PowerToys does not send it to Ollama.
- Kept conversational mode disabled by default. The chat setting is persisted with generation preferences and conversation settings; the PowerToys setting is independent.
- Added a save-scope choice when a conversation has multiple branches: **All branches** is preselected; **Active branch only** saves just the selected history as a standalone conversation. The modal can be cancelled with Escape or the existing Cancel/outside-click controls.
- Full `.runmind` workspace saves preserve every branch history, branch IDs/names, active branch selection, creation/update timestamps, and fork-origin metadata (`parentId` and `forkedFromMessageIndex`) when known. The active branch transcript remains in the legacy top-level `messages` field for compatibility; non-active transcripts are stored in `branchWorkspace`. Old `.runmind` files without `branchWorkspace` still load as a single Original branch.
- Full branch histories are hydrated from a per-WebView IndexedDB archive before saving; localStorage remains a bounded recovery fallback. The portable `.runmind` file is the complete transferable archive, subject to the 50 MiB file cap, eight-branch cap, and 20,000-message validation cap.
- Hardened document reads so PDF/image/audio/video content types are treated as non-text and handed to the configured Reader fallback instead of being parsed as HTML. Native Reader failure details are retained in source metadata, compacted tool results, and Markdown research exports; stale native-failure labels are cleared after a later successful native read. Successful native reads log only the hostname, content type, and elapsed time to the application console.

### Validation
- Static tests cover Native → custom → Jina order, no-custom direct-to-Jina behavior, custom failure fallback to Jina, hard-stop handling for blocked destinations, Nemotron model-ID matching/exclusions, request-field placement, provider gating, preference persistence, and version consistency.
- JavaScript syntax, project XML/XAML, plugin JSON, version alignment, and ZIP integrity checked. Windows/.NET 10 compilation and live provider/WebView2 testing still require Windows.

## [1.10.17] — Native-first webpage reading

### Added
- Added a shared C# Native Reader used by both the PowerToys Run chat windows and the standalone WPF app. It retrieves public HTTP/HTTPS pages through `HttpClient`, returning bounded raw text/HTML to the existing shared JavaScript HTML-to-Markdown and metadata extraction pipeline.
- Added an origin-checked, request-ID-based WebView2 message bridge for native page reads, cancellation, and bounded result delivery. No WebView2 host objects or general-purpose HTTP proxy are exposed.
- Added redirect-by-redirect validation, public-IP-only DNS/connect checks, no cookies or ambient credentials, a 5-redirect limit, a 25-second timeout, and a 1.5 MiB response cap.

### Changed
- Desktop webpage reads now use this order: **Native C# Reader → configured Jina Reader → optional custom Reader gateway**. The browser no longer attempts a CORS-limited fetch of external webpages in either desktop host. Browser-only previews retain their direct-fetch behavior.
- Sitemap, RSS/XML, and other document reads use the Native Reader in desktop hosts as well. Binary downloads remain on their original direct-byte path and are not converted into Markdown.
- Updated Agent settings to clarify that external Reader services are fallback providers, not the primary fetch route.

### Limits
- The Native Reader retrieves public HTTP/HTTPS resources and does not execute page JavaScript. JavaScript-heavy pages, bot challenges, unsupported page formats, and native retrieval failures continue to the configured remote Reader. Private/local/reserved destinations and DNS results containing non-public addresses are blocked.
- Direct outbound native HTTP currently bypasses configured system proxies to preserve destination validation. Remote Reader fallback remains available through the existing WebView fetch path.

### Validation
- Static checks cover bridge origin/type validation, bounded request/response sizes, cancellation, DNS/IP and redirect safeguards, native-first/external-fallback routing, project XML, manifest versions, JavaScript syntax, and ZIP integrity. Windows/.NET 10 compilation and live WebView2/network testing still require Windows.

## [1.10.16] — Preserve underscores and asterisks inside identifiers

### Fixed
- Corrected inline Markdown emphasis parsing so underscores and asterisks inside alphanumeric identifiers are treated as literal characters, not formatting delimiters. Names such as `UD-Q3_K_XL` and `UD-Q3*K*XL` now render exactly as written in chat messages and assistant output.
- Preserved ordinary standalone Markdown emphasis such as `_italic_` and `*italic*` when delimiters are separated from surrounding words.
- Confirmed the send/context pipeline continues to pass the original user message text unchanged; this fix addresses the rendering interpretation rather than rewriting submitted content.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.16`.

### Validation
- Source regression checks cover literal underscore/asterisk identifiers, punctuation-adjacent identifiers, Unicode text, normal Markdown emphasis, inline-code protection, and unchanged provider message content. JavaScript syntax, project XML, plugin JSON, version alignment, and ZIP integrity checked. Windows/.NET 10 compilation and live WebView2/provider testing still require Windows.

## [1.10.15] — Use Branch labels for generated conversation branches

### Changed
- Replaced generic `Alternative N` child-branch labels with `Branch N`. Branches created from meaningful prompts retain their descriptive prompt-based names; short or generic prompts now receive straightforward `Branch 1`, `Branch 2`, etc. labels.
- Migrated existing auto-generated `Alternative N` labels back to `Branch N` when loading saved branches. Existing `Branch N` labels remain in the requested format, descriptive names are preserved, and collisions are resolved without duplicate names.
- Preserved the exact editable QuickAI default prompt introduced in 1.10.14.

### Validation
- Source regression checks cover generated branch labels, migration of stored `Alternative N` labels, preservation of descriptive names, numbering collisions, and prompt-based branch titles. JavaScript syntax, project XML, plugin JSON, version alignment, and ZIP integrity checked. Windows/.NET 10 compilation and live WebView2 testing still require Windows.

## [1.10.14] — QuickAI prompt refinement and branch-name migration

### Changed
- Replaced the pre-filled PowerToys Run QuickAI starter prompt with the user's exact concise two-sentence version: it retains QuickAI's identity and clarifies that the input is the current PowerToys search-box text. The prompt remains editable and can still be cleared.
- Migrated only the previous built-in QuickAI starter prompt to the new default when loading PowerToys settings. User-authored prompt content and intentionally empty settings remain unchanged.
- This release briefly migrated generated `Branch N` labels to `Alternative N`; RunMind 1.10.15 reverses that naming choice to the user's requested `Branch N` labels.

### Validation
- Source tests cover exact default prompt text, migration of the previous built-in value, preservation of custom/empty prompts, and legacy branch-name migration with collision handling. JavaScript syntax, project XML, plugin JSON, version alignment, and ZIP integrity checked. Windows/.NET 10 compilation and live PowerToys Settings/WebView2 tests still require Windows.

## [1.10.13] — Copy sent prompts

### Added
- Added a **Copy** action to every text-bearing user message in the chat transcript, alongside the existing branch/edit actions. It copies the exact original prompt text (not rendered Markdown) and provides the same brief success feedback as assistant-response copying.
- Prompt copying remains available while a newer assistant response is streaming; it does not alter the conversation or interrupt generation. Edit and branch actions retain their existing availability rules.

### Validation
- Source regression checks cover user and assistant copy actions, prompt text preservation, copy availability during generation, and unchanged edit/branch gating. JavaScript syntax, project XML, plugin JSON, version alignment, and ZIP integrity checked. Windows/.NET 10 compilation and live WebView2 clipboard testing still require Windows.

## [1.10.12] — PowerToys QuickAI defaults and native window sizing

### Added
- Pre-filled the PowerToys Run multiline system-instructions setting with the editable QuickAI starter prompt. Users can freely edit it or clear it; clearing the field disables the additional prompt. The setting key and 12,000-character limit are unchanged.
- Added a shared DPI-aware WPF window-placement helper for the PowerToys and standalone hosts. New windows are centered within the working area of the monitor under the pointer, with safe margins and size clamping for smaller work areas.

### Fixed
- Reduced the default new-chat window height from 860 to 836 DIPs (24 DIPs) in both hosts, helping keep the Windows title bar and window controls accessible on shorter displays.
- Ensured the initial work-area adjustment runs only when a native window is created. It does not continually override subsequent user resizing.
- Preserved the 1.10.11 PowerToys delayed-query response path: completed LLM results are returned for their original query generation without calling `ChangeQuery` or rewriting the search-box text.

### Validation
- Verified QuickAI prompt prefill, explicit-empty opt-out behavior, multiline setting type, PowerToys provider request inclusion, and query-generation ownership by source regression checks.
- Verified all three PowerToys-created windows and the standalone main window use the shared initial placement helper; checked the 836-DIP default and work-area clamping logic with geometry cases.
- Project XML, plugin JSON, version alignment, JavaScript syntax, and ZIP integrity checked. Windows/.NET 10 compilation and live PowerToys/WebView2 testing still require Windows.

## [1.10.11] — PowerToys Run live-input preservation

### Fixed
- Fixed fast inline LLM responses replacing characters typed after the query that launched the request. RunMind no longer calls `IPublicAPI.ChangeQuery` to refresh completed answers; that API changes the actual PowerToys Run search-box text before re-querying.
- The fast `IPlugin.Query` path still returns the immediate pending result and cancels the previous request as soon as a newer query arrives. The delayed `IDelayedExecutionPlugin.Query` phase now performs the provider request on PowerToys' background query worker and returns the completed result directly to that same host query generation.
- PowerToys owns publication of the delayed result and checks its query-session cancellation/generation before updating the result list. Superseded queries are cancelled and their delayed results are discarded; the user's text and caret are never programmatically rewritten.
- The provider request remains asynchronous internally. The synchronous wait exists only inside the host's delayed background phase, not in the fast keystroke callback.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.11`.

### Validation
- Source regression checks confirm there are no `ChangeQuery` calls in the PowerToys plugin path, the fast query remains network-free, and the delayed path returns completed results only for the currently owned operation. Project XML, plugin JSON, version alignment, JavaScript syntax, and ZIP integrity checked. Windows/.NET 10 compilation and live PowerToys typing tests still require Windows.

## [1.10.10] — Multiline PowerToys system prompt

### Fixed
- Changed the PowerToys Run **Custom system instructions** option from a single-line `Textbox` to PowerToys' native `MultilineTextbox` setting control. Users can now enter, paste, and edit real line breaks directly, with the standard multiline editor and vertical scrolling.
- Retained the existing `InlineSystemPrompt` setting key and 12,000-character limit. Existing prompt content and provider request behavior remain compatible; normalization accepts real CR/LF line endings and legacy literal `\n` sequences.
- Added a native placeholder and settings-side maximum length so the editor and runtime limit stay aligned.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.10`.

### Compatibility
- The native `MultilineTextbox` additional-option type is supported by PowerToys Run 0.80.0 and later. Older PowerToys versions do not render this setting type correctly and should be upgraded.

### Validation
- Source checks confirm the existing setting key and runtime normalization remain intact, and the PowerToys option is declared as `MultilineTextbox` with the 12,000-character limit. Project XML, plugin JSON, version alignment, and ZIP integrity checked. Windows/.NET 10 compilation and live PowerToys Settings UI testing still require Windows with the complete `lib` directory.

## [1.10.9] — Flyout scrolling hotfix

### Fixed
- Fixed every toolbar flyout jumping back to the top while scrolling. The document-level capture listener now ignores scroll events originating inside an open flyout, including nested scroll containers.
- Fixed scroll-position loss during legitimate flyout repositioning. The shared placement routine no longer removes `max-height` while measuring; it maintains a viewport-sized measurement cap, computes the full content height from `scrollHeight`, and restores existing outer and nested scroll offsets after layout changes.
- Added contained overscroll behavior to flyouts so reaching the edge of a menu does not unexpectedly scroll the content behind it.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.9`.

### Validation
- JavaScript syntax and Chromium headless interaction checks cover outer flyout scrolling, nested scroll regions, viewport-triggered repositioning, and resize while scrolled. Package metadata and ZIP integrity checked. Windows/.NET 10 compilation and live WebView2 integration still require validation on Windows.

## [1.10.8] — Custom system instructions and consistent generation controls

### Added
- Added an independent optional **Custom system instructions** PowerToys Run setting. It applies only to inline PowerToys queries; it does not leak into the standalone chat app when a pending query is handed off. The Additional Options text field supports explicit `\n` sequences for line breaks and is bounded to 12,000 characters.
- Added independent PowerToys inline generation defaults: Temperature (0–2), Top P (0–1), Max output tokens (1–1,000,000), and Seed (0–2,147,483,647). Empty or invalid values use provider defaults. Ollama receives these under `options` (`temperature`, `top_p`, `num_predict`, `seed`); LM Studio receives the corresponding OpenAI-compatible chat-completion fields.
- Added a multiline **Custom system instructions** editor to the chat application's existing Generation settings flyout, with a 16,000-character counter, live application preview, and a separate Clear action. It is saved in the chat WebView's local preferences and remains independent from PowerToys settings.
- Chat custom instructions apply to normal chat, Agent Mode, Deep Research, and bounded specialist sub-agents. They are included in context budgeting and snapshotted into active agent/research task contexts. RunMind's built-in operational instructions, tool permissions, safety requirements, and task-specific workflows remain in place.

### Changed
- Applied the chat application's configured generation parameters consistently to Agent Mode, Deep Research, and sub-agent provider turns: Ollama uses its native `options` fields; LM Studio OpenAI-compatible requests receive Temperature, Top P, Max output tokens, and Seed; LM Studio Native REST receives its supported native fields including Seed.
- Added conservative parameter fallback for LM Studio: optional parameters are cached as unsupported only after an explicit HTTP 400/422 validation response identifies the rejected field. Generic server, model, and network errors are not retried as parameter fallbacks.
- A change to the chat custom system prompt clears LM Studio native response-chain IDs so the next request starts a new chain and cannot silently omit the changed instructions.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.8`.

### Notes and limits
- PowerToys Run's `AdditionalOptions` supports a single-line text field, so `\n` is the documented way to enter line breaks there. The chat application provides a true multiline editor.
- Sampling controls are passed only where the selected provider API supports them. Invalid/out-of-range optional values are ignored; an explicitly rejected optional LM Studio parameter is omitted for subsequent requests to that model/endpoint combination.
- Custom user instructions are not applied to internal title generation, context summarization, connection diagnostics, or capability probes; these use dedicated narrow-purpose prompts.

### Validation
- JavaScript syntax, custom-prompt composition and persistence paths, context-budget inclusion, Ollama/OpenAI generation-parameter mapping, active-task prompt snapshots, PowerToys request-body mappings, version alignment, and ZIP integrity checked.
- Windows/.NET 10 compilation and live PowerToys/WebView2/Ollama/LM Studio integration were not available in this environment and still require validation on Windows.

## [1.10.7] — Shared viewport-aware flyout positioning

### Fixed
- Replaced the horizontal-transform-only adjustment from 1.10.6 with one shared placement routine for every top-level flyout: model connections, generation settings, context manager, model details, Deep Research, agent history/settings, OCR languages, MCP servers, and Conversations.
- Flyouts are positioned against the WebView viewport rather than their toolbar item's containing block. This handles toolbar wrapping and narrow windows, and prevents parent-relative positioning from pushing panels off-screen.
- Placement now clamps both horizontal edges and vertical bounds. Panels open below their trigger when there is room, otherwise above it; tall panels receive a viewport-bounded height and internal scrolling.
- Repositions all open panels after window/visual-viewport resize and document scrolling. The Conversations panel is included in the same placement logic.
- Hardened the geometry fallback for extremely narrow/short viewports and anchors outside the visible viewport; the result remains fully bounded and scrollable.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.7`.

### Not included
- The requested small reduction to the native chat window's initial height is not part of this overlay. It is controlled by WPF host window creation code, which is absent from the trimmed source input; changing HTML/CSS would not resize the Windows title bar or outer window. It remains a host-source follow-up for both standalone and PowerToys windows.

### Validation
- JavaScript syntax, shared flyout geometry bounds, viewport edge cases, version alignment, and ZIP integrity checked. Live Windows/WebView2 rendering and .NET 10 compilation still require validation on Windows.

# Changelog

## [1.10.6] — Toolbar flyout viewport positioning hotfix

### Fixed
- Fixed toolbar flyouts opening beyond the right edge of the application window. Every standard toolbar panel now measures its trigger and rendered width, then shifts horizontally just enough to remain within the visible WebView viewport with a consistent margin.
- Applied the same positioning rule to all standard toolbar flyouts rather than special-casing only the MCP menu, so other right-side menus remain usable as the toolbar layout changes.
- Recalculates open flyout positions when the window is resized.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.6`.

### Validation
- JavaScript syntax, flyout positioning bounds, version alignment, and ZIP integrity checked. Live Windows/WebView2 rendering still requires validation on Windows.

## [1.10.5] — Deep Research mode and warm dark theme hotfix

### Fixed
- Fixed event bubbling from the composer **Deep Research** toggle: enabling research mode now opens the Research workspace flyout and the same click is no longer misinterpreted as an outside click that immediately closes it. Other toolbar flyouts still close as expected.
- Replaced the blue/indigo dark palette with **Warm Graphite**: neutral charcoal surfaces, warm ivory text, subdued amber highlights, warm user-message surfaces, and softer neutral borders. Light and System-Light palettes remain unchanged.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.5`.

### Notes
- Warm Graphite reduces blue-toned interface colors; it is not a hardware/display blue-light filter and does not change monitor brightness or white-point settings.

### Validation
- Browser regression checks cover composer-toggle opening, exclusive flyout behavior, outside-click closing, active/ARIA state, and the warm dark palette tokens. JavaScript syntax, version alignment, and ZIP integrity are checked. Windows/.NET 10 compilation and live WebView2 visual validation still require Windows.

## [1.10.4] — Toolbar workspace and approval management hotfix

### Fixed
- Converted the Deep Research workspace from a standalone right-side drawer into a standard toolbar flyout anchored directly below its toolbar trigger, matching the placement and sizing behavior of the other toolbar sections.
- Made the Deep Research toolbar trigger use the same open/active visual state as other toolbar menus, with `aria-expanded` kept synchronized for mouse, keyboard, and programmatic open/close actions.
- Added shared mutual exclusion for toolbar and conversation flyouts: opening any section closes the previously open section, and opening Deep Research closes other toolbar sections. Outside-click and Escape handling now cover every top-level flyout, including menus previously omitted from the hard-coded lists.
- Added per-tool **Remove** actions to the Always-approved tools list. Removing one approval updates the persisted permission set immediately; **Clear approvals** remains available.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.4`.

### Validation
- Static checks cover the toolbar-details structure, anchored flyout CSS, active state and `aria-expanded` synchronization, exclusive menu handling, per-tool permission removal, and version alignment.
- JavaScript syntax, HTML ID uniqueness, metadata parsing, and ZIP integrity are checked in the release package. Windows/.NET 10 compilation and live WebView2 interaction still require validation on Windows.

## [1.10.3] — Branch title and spacing hotfix

### Fixed
- Kept the conversation title independent from branch selection. Switching to a branch no longer appends the branch label to, or overwrites, the parent conversation title.
- Renamed the immutable `main` branch's display label from `Main` to `Original`, including migration of existing per-window branch state. The stable internal branch ID remains `main`.
- Generated new branch labels from the user prompt nearest the fork point instead of the first prompt in the conversation. Short greetings and generic prompts receive neutral labels such as `Alternative 1`; duplicate labels receive a numeric suffix.
- Removed the redundant **Branches** toggle row while the branch manager is open. The manager now sits directly below the header and uses its own close control, eliminating the extra vertical gap. The collapsed toggle row is also more compact.

### Limits
- Branch labels are deterministic prompt-derived suggestions, not AI-generated summaries. Users can rename non-original branches at any time.
- Branch names are labels within the current conversation; the parent conversation keeps one stable title across all branches.


## [1.10.2] — Conversation branch layout hotfix

### Fixed
- Fixed the branch manager's `hidden` state being overridden by `.conversation-branchbar { display:flex }`. With only the Main branch, the supposedly hidden manager was still rendered because the author CSS won over the browser's default hidden rule.
- Fixed the transcript frame's default horizontal flex direction. The branch manager and comparison view now occupy a full-width row above the transcript instead of becoming a side-by-side column that squeezed the conversation to roughly half the window.
- Added a compact **Branches** toggle that appears only when multiple branches exist. Branch management is collapsed by default, opens after the user explicitly creates a branch, and has an explicit close button. The comparison panel retains its own Close control.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.2`.

### Validation
- Static regression checks confirm hidden branch controls are force-hidden, the transcript frame uses a vertical layout, branch management starts collapsed, create-branch opens it, and both branch manager and comparison views have close controls.
- JavaScript syntax, HTML ID uniqueness, metadata alignment, and ZIP integrity checks pass. Windows/.NET 10 and live WebView2 rendering still require validation on Windows.

## [1.10.1] — Context metrics hotfix

### Fixed
- Fixed a `ReferenceError` in `compactResearchConversation` when the selected provider exposes an active context limit. The context metric now stores `estimatedPromptTokens: estimatedTokens` instead of referencing an undeclared `estimatedPromptTokens` variable. This prevented Deep Research from sending its first chat request for affected LM Studio models.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.1`.

### Validation
- Added a regression check for the active-context-limit branch and confirmed it records the estimated prompt token count without throwing.
- JavaScript syntax, metadata alignment, and ZIP integrity checks pass. Windows/.NET 10 and live LM Studio/WebView2 integration still require validation on Windows.

## [1.10.0] — Agent orchestration and workflow intelligence

### Added
- Added one shared bounded tool-batch scheduler for Agent Mode, Deep Research, and sub-agents. Independent operations in a model tool-call layer can run concurrently when their tool is on the explicit read-only allowlist, up to a user-configurable limit of four workers. Result messages are returned to the model in the original call order. Mutating tools, approval-gated reads, MCP calls, duplicate identical calls, and delegated-agent calls remain serialized.
- Added workflow operation records with dependency-layer IDs, sanitized inputs, bounded output previews, lifecycle states, duration, retry controls for safe read-only Agent operations, and durable local persistence before execution. Interrupted in-flight read-only operations require an explicit user confirmation before retry. The shared Stop control aborts the active run, marks undispatched queued operations cancelled, and prevents later model-turn layers from being sent.
- Added read-only tool start-priority preferences to Workflow. Dragging a parallel read-only operation card onto another changes the preferred start order for future independent groups. Existing executions, provider result ordering, and dependency layers are not reordered. Priorities are stored in the current WebView profile.
- Expanded bounded one-level sub-agent workflows for Researcher, Coder, Security Analyst, Reviewer, and Summarizer roles. Child agents receive only the approved read-only tool subset, cannot delegate further, and return a normalized structured result (summary, findings, sources, uncertainties, and limitations). Deep Research child retrieval consumes the parent task budgets and domain policy.
- Added inspectable sub-agent assignments/results, including bounded tool-call counts and elapsed time.
- Added conversation branching from completed messages, independent in-window histories, branch selector, rename/delete, return-to-Main, and latest-answer side-by-side comparison. Local snapshots are bounded to eight branches and the latest 16 messages per branch; binary attachment bytes are not duplicated into branch snapshots.
- Expanded local Workflow analytics with per-tool call counts, success rate, average execution duration, provider/model request counts, request success/failure, average latency, and provider-reported token usage when available.

### Changed
- Agent Mode and Deep Research use the same tool-batch scheduler. Each model tool-call response is treated as a dependency layer; the next model turn waits for every result in that layer. Dependencies between calls generated together are not inferred because the model has not seen any of those results yet. Cancellation is propagated through the active AbortSignal; an adapter or external service may take time to stop or may not be able to undo a side effect already accepted.
- Added the **Workflow** view alongside the existing Research, Review, and Recovery views in the same workspace drawer. It shows recent dependency layers, operation status, inputs/outputs, sub-agent work, and local analytics.
- Persisted operation intent before a tool starts. On reload, operations left in `running` state become `interrupted`; pending undispatched operations become `not_started`. RunMind does not silently replay them.
- Provider request telemetry now records failed requests as well as successful ones. Token counts remain provider-reported only; providers that omit usage metadata are shown as not reporting tokens.
- Updated plugin, standalone, and MCP client version identifiers to `1.10.0`.

### Limits and behavior
- Parallel execution is deliberately limited to a reviewed allowlist of known read-only built-in tools. It is not arbitrary dependency inference, arbitrary code concurrency, or unrestricted MCP parallelism. MCP tools remain serialized because the current approval UI and route metadata do not provide a trusted concurrent read-only authorization path.
- Priority drag-and-drop changes the start preference of tool types in future independent read-only groups only. It cannot reorder operations already running or override serialized side-effect/approval barriers. The model's result transcript remains in original tool-call order.
- Sub-agents use the selected model/provider, at most four model/tool turns per delegation, one delegation level, and a read-only child-tool subset. They are not separate models, isolated processes, or autonomous background workers. Structured JSON is normalized when returned; if a model ignores the JSON instruction, its text is retained as an unstructured summary.
- Agent workflow operation history is locally persisted and bounded; a crash during an external operation can leave its outcome ambiguous. Only allowlisted read-only operations have an in-app retry action. This is not an exactly-once guarantee for arbitrary external services. Deep Research additionally retains its existing per-task operation ledger and checkpoint recovery.
- Conversation branches are scoped to the WebView profile/window instance. Local branch snapshots keep at most eight branches and 16 recent messages per branch, trim long text, and omit binary attachment bytes. Use a `.runmind` save for a complete conversation archive. Research checkpoints remain attached to their original research task and are not cloned into branches. Branch comparison is a side-by-side comparison of the latest assistant answers, not a semantic diff.
- Workflow analytics remain local to the current WebView profile and are best-effort. Tool duration is measured by RunMind; provider latency is client-observed. Token usage appears only when a provider reports usage metadata. Clearing workflow history also clears its local analytics, but does not clear conversation branches or research checkpoints.

### Validation
- Static JavaScript syntax, shared scheduler behavior, bounded concurrency, deterministic output order, safe-tool gating, priority ordering, branch snapshot limits, metadata/version alignment, and overlay ZIP integrity are checked in this environment.
- A Windows/.NET 10 build and live WebView2/PowerToys/provider/MCP integration tests still require validation on Windows.

## [1.9.0] — Research intelligence and agent reliability

### Added
- Added an integrated Research / Review / Recovery tab set inside the existing Deep Research drawer. No second panel or parallel research UI was introduced.
- Added claim-level model assessments with supported, partially supported, contradicted, and insufficient-evidence verdicts; explanations; uncertainty notes; and 1–8 exact source passages classified as supporting, contradicting, or contextual. The application checks normalized exact-quote presence against the bounded source text it stored.
- Added a Review view with claim filters, source passage links, hostname-independence cautions, user Accept/Reject decisions, and targeted “Investigate further” actions that continue the same research task.
- Added qualitative source assessments covering primary/secondary/reference type, observable strengths and limitations, author/date visibility, and freshness. No numeric trust score is generated.
- Added checkpoint history in IndexedDB (up to eight snapshots per research task), checkpoint summaries, restore/rollback confirmation, and a Recovery view.
- Added a tool-operation ledger that records intent before a tool executes and saves bounded model results. Exact matching completed operations in an uncommitted batch can reuse their saved result; ambiguous interrupted operations require explicit user acknowledgement before retry.
- Added selected-model diagnostics for discovery, a small text-generation probe, and a harmless function-calling probe. The diagnostic function is never executed.
- Added research context telemetry with an approximate token estimate, active context-limit source, utilization, and compaction counts.
- Added adaptive compaction of older research tool messages at safe checkpoints when an active context limit is known and utilization exceeds the threshold. Full bounded source text remains in the research source register.
- Added seven reusable research templates: General, Technical Investigation, Product & Technology Comparison, Security Assessment, Academic Research, Website Investigation, and Troubleshooting Investigation.
- Expanded Markdown report export with claim assessments, user review decisions, evidence passages, qualitative source assessments, and tool-operation recovery state.

### Changed
- The existing Deep Research workspace now has Research, Review, and Recovery views. Existing source preferences, plan editor, budgets, sources, evidence, activity, pause/resume/cancel, copy, and export remain in the Research view.
- Research instructions ask the model to assess material claims and important sources, cross-check disagreements, disclose uncertainty, and explain possible source dependencies. The UI explicitly distinguishes model assessments from local exact-quote checks.
- Checkpoint history is scoped by research task ID rather than WebView instance ID. The existing primary checkpoint and session fallback remain intact.
- Updated standalone, PowerToys plugin, plugin manifest, and MCP client version identifiers to `1.9.0`.

### Limits and behavior
- Semantic claim verdicts and source-quality explanations are model-assisted assessments. RunMind does not independently prove logical entailment, source authority, factual truth, or editorial independence. Exact quote matching only confirms text presence in the bounded stored excerpt.
- Hostname counts are a visual caution, not an independence test. Different hostnames do not prove independent reporting; common underlying sources can exist across domains.
- Tool-operation keys use the tool name and canonicalized arguments. They protect identical operations within an uncommitted batch, not semantically equivalent actions with different arguments. If a process stops after a side effect but before its result is committed, RunMind labels the operation ambiguous and asks before retrying. Exactly-once execution cannot be guaranteed for arbitrary local or MCP tools.
- Checkpoint history retains at most eight snapshots for the current task. Restoring replaces the current task state after confirmation; newer snapshots remain in history. Recovery storage is WebView-profile local, so PowerToys and standalone histories do not synchronize.
- Context-token counts are estimates based on serialized conversation length divided by four, not provider tokenization. Adaptive compaction is limited to older tool messages and runs only when RunMind knows an active context limit; it does not guarantee that every model prompt will fit.
- Context compaction and operation history are research-task features. Parallel tool scheduling, subagents, conversation branching, scheduled tasks, persistent knowledge indexing, resumable downloads, and advanced MCP protocol features remain outside 1.9.0.
- The LM Studio Native REST mode does not receive the generation/tool-call diagnostics in this release; those probes require a supported chat-completions-compatible route.

### Validation
- Static validation covers inline JavaScript syntax, unique HTML IDs, required element references, project XML, plugin JSON, version alignment, checkpoint/operation-ledger invariants, and overlay archive integrity.
- Windows/.NET 10 compilation and live WebView2 IndexedDB, SearXNG, Reader, provider, local workspace, and MCP side-effect recovery tests still require execution on Windows.

## [1.8.0] — Deep Research evolution and Agent runtime

### Added
- Extended the existing Deep Research workspace with a live operation indicator, source collection details, an evidence register, and a timestamped activity feed. Existing depth presets, page/tool budgets, plan editing, pause/resume/cancel, task history, copy actions, and report export remain.
- Added bounded multi-query discovery with `research_topic` (up to six queries and eight results per query, deduplicated by normalized URL). Search is available only when SearXNG is configured.
- Added `page_to_markdown`, converting directly retrieved HTML into readable Markdown and using the configured Reader fallback when direct retrieval is blocked.
- Added bounded same-origin breadth-first crawling with `crawl_website` (up to 20 pages and depth 3), HTTP link reachability checks with `check_links` (up to 30 links), and exact line-based `compare_webpages`.
- Added research source metadata: URL/final URL, title, domain, author, publication/modified dates when available, retrieval route/time, excerpt, read state, and fetch failures. Sources are deduplicated by normalized URL.
- Added preferred, allowed, and blocked domain controls for Deep Research. Blocked domains take precedence; an allowlist restricts retrieval only when populated; preferred domains affect discovery ordering.
- Added `research_record_evidence`, which records a claim and exact quotation and reports whether the quoted text occurs in the stored source excerpt.
- Added streaming tool-call progress for Ollama NDJSON and OpenAI-compatible SSE responses, including partial tool names and argument previews. Non-streaming JSON responses remain supported.
- Added IndexedDB-backed research checkpoints with bounded snapshots, periodic heartbeat persistence, stale/paused/interrupted task recovery in another window using the same WebView2 profile, and a session-storage fallback if durable storage is unavailable.
- Expanded Markdown research report export with task/model/budget metadata, sources, evidence register, and activity history.

### Changed
- Research now records live status while the model plans, streams tool calls, executes retrieval, and synthesizes findings. Large research tool results are compacted as valid JSON before being appended to the model transcript; full fetched text remains in the source register rather than being cut mid-JSON. Multi-query discovery preserves results across subquestions when compacting.
- Direct HTML retrieval produces Markdown with headings, paragraphs, links, lists, blockquotes, tables, and code blocks; title/author/publication/modified metadata is extracted where available, including supported JSON-LD metadata.
- Domain policy and page/tool/time budgets are enforced by the Deep Research retrieval paths. Time limits are a soft boundary: an in-flight operation is allowed to finish before the task stops retrieving and synthesizes the collected material.
- Updated standalone, PowerToys plugin, plugin manifest, and MCP client version identifiers to `1.8.0`.

### Limits and behavior
- `research_topic` requires a configured SearXNG endpoint. Search snippets are discovery leads and should be opened before use as evidence.
- Direct webpage retrieval depends on CORS and site behavior. If enabled, Jina Reader or a custom Reader gateway may receive the public URL and page content needed for extraction. Reader fallback is not used for private/local or credential-bearing URLs.
- `crawl_website` stays on the starting origin, with a maximum depth of 3 and 20 pages. It is not a whole-web crawler or scheduler.
- `check_links` uses bounded HTTP HEAD requests. CORS/network failures and servers that reject HEAD are reported as unverified, not broken. Private/local destinations are skipped.
- `compare_webpages` compares trimmed exact text lines; it does not infer semantic equivalence or explain meaning-level changes.
- Evidence verification checks exact normalized quote presence in the bounded text stored for a source. It does not independently prove that a source is authoritative or that the quote supports the associated claim. Citation correctness remains model-assisted, not semantically verified by the application.
- Domain preferences apply to Deep Research, not ordinary chat. Preferred domains are ranking hints; they do not prove source quality.
- Pause and plan edits take effect at safe complete tool-call-batch checkpoints. If a process closes during an in-flight batch, the last committed transcript is recovered; operations may need review and are not guaranteed exactly-once. Checkpoints do not synchronize between separate PowerToys and standalone WebView2 profiles.
- Research operations are executed serially. Parallel tool scheduling, generalized idempotent replay/rollback, resumable downloads, subagents, scheduled tasks, conversation branching, persistent knowledge indexing, and advanced MCP protocol features remain outside 1.8.0.

### Validation
- Static checks cover inline JavaScript syntax, Ollama NDJSON and OpenAI-compatible SSE/JSON tool-call parsing, project XML, plugin JSON, version consistency, and overlay archive integrity.
- A Windows/.NET 10 build, WebView2 IndexedDB recovery behavior, live SearXNG/Reader/provider tests, and end-to-end research runs still require validation on Windows.

## [1.7.4] — Transcript text selection polish

### Changed
- Kept the empty composer placeholder (`Message the selected local model…`) out of text selection while leaving text entered by the user selectable.
- Restored text selection for message attribution (author, timestamp, provider, and model) and generation statistics (time to first token, elapsed time, token count, and tokens per second). Application controls and action buttons remain excluded from Ctrl+A selections.
- Synchronized standalone, PowerToys plugin, and plugin manifest version metadata to `1.7.4`.

### Validation
- Static checks cover JavaScript syntax, CSS selection rules, project XML, plugin manifest JSON, release-version consistency, and overlay ZIP integrity. WebView2 selection behavior and a Windows/.NET 10 build still need verification on Windows.

## [1.7.3] — Responsiveness and interface polish

### Changed
- Added an optional Ollama CPU thread cap for PowerToys Run inline requests. Leave the setting blank or use `0` for Ollama's automatic thread selection; enter `1`–`128` to set the maximum worker-thread count. No automatic CPU policy is applied. The setting does not affect standalone chat or LM Studio.
- Throttled live chat Markdown rendering to at most one update every 90 ms. The final transcript is still rendered immediately when generation ends; the limit reduces repeated parsing of an ever-growing response.
- Made application controls, status labels, message metadata, action buttons, and other interface chrome non-selectable. User and assistant message content remains selectable for normal copying.

### Validation
- Static checks in this environment cover JavaScript syntax, project XML, plugin manifest JSON, the optional CPU-thread setting path, the non-selectable chrome selectors, the 90 ms render scheduler, and ZIP integrity.
- A Windows/.NET 10 build, live Ollama comparison with automatic versus capped CPU threads, PowerToys Run responsiveness under load, and WebView2 selection/rendering behavior still need verification on Windows.

## [1.7.2] — PDF.js and offline multilingual OCR

### Added
- Replaced the hand-written PDF stream/text scanner with PDF.js for page-aware text extraction, standard font resources, and CMap support.
- Added automatic per-page OCR fallback using Tesseract.js when extracted text is absent or fails basic text-quality checks. The `read_pdf` tool preserves page order and reports which pages used PDF.js or OCR, selected OCR languages, and pages with low or unavailable confidence.
- Added the **PDF OCR languages** toolbar menu. English (`eng`) and Persian (`fas`, locale `fa_IR`) are the initial bundled language choices; users can select multiple languages for a document.
- Added individually downloadable offline language packs for French, Spanish, Arabic, Simplified/Traditional Chinese, Russian, Finnish, German, Italian, Portuguese, Japanese, Korean, Turkish, and Urdu. Successfully downloaded packs are cached in the current WebView2 profile.
- Added `Prepare-RunMind-OCR.ps1` to retrieve pinned PDF.js `6.5.438`, Tesseract.js `7.0.0`, matching `tesseract.js-core` `7.0.0`, and the English/Persian `tessdata_fast` files on first build. The build script skips downloads after the required local resources are present.

### Changed
- Updated the Agent tool description and system guidance to describe automatic OCR fallback and report OCR confidence limitations rather than claiming PDF OCR is unsupported.
- Updated both desktop project files to copy local PDF.js, Tesseract.js, core, and language model resources into their build outputs.
- Synchronized standalone, PowerToys plugin, and plugin manifest version metadata to `1.7.2`.

### Limitations
- OCR runs only on pages whose extracted text is missing or suspicious; language identification is not automatic. Select the expected language(s) in the OCR menu before reading a mixed-language or non-English scan.
- The first build needs internet access to download pinned third-party runtimes and English/Persian traineddata. After preparation, app runtime and later builds can work offline. Optional language packs are downloaded on demand and cached separately in the PowerToys and standalone WebView2 profiles.
- Encrypted PDFs are not supported. Handwriting, low-resolution scans, unusual scripts/fonts, tables, columns, and reading order can still produce OCR errors. Review low-confidence pages against the original.
- PDF input is limited to 25 MB and 500 pages; extracted text is capped at 90,000 characters per tool result. PDF rendering is bounded to 12 million pixels per page for OCR.

### Validation
- JavaScript syntax, project XML, plugin JSON, build-script structure, and overlay archive integrity are checked in this environment. The PowerShell bootstrap cannot be executed here because this environment has no Windows PowerShell/tar.exe and outbound package downloads are unavailable.
- A Windows/.NET 10 build, PDF.js worker/CMap loading, real English/Persian PDF corpus, scanned-page OCR, optional language download/cache, and WebView2 end-to-end tests still need to be run on Windows.

## [1.7.1] — Conversation attachment support for Agent file tools

### Fixed
- Fixed Agent file tools treating attached filenames as workspace paths. Text-based readers and JSON validation now resolve exact filenames from conversation attachments before falling back to the selected workspace.
- Added PDF and ZIP attachments with MIME/signature checks and bounded per-file/combined sizes. `read_pdf` and `inspect_archive` can read attached files directly; `extract_archive` can use an attached ZIP while still requiring a selected workspace and explicit extraction approval.
- Updated conversation prompt context and tool descriptions so models can identify attached PDF/ZIP filenames and pass the exact filename to the corresponding tool.
- Preserved supported PDF/ZIP attachment bytes when saving and reopening `.runmind` conversations, with validation of their MIME types, signatures, and size limits.

### Changed
- Synchronized standalone, PowerToys plugin, plugin manifest, and MCP client version metadata to `1.7.1`.

### Limitations
- PDF extraction remains best-effort selectable-text extraction; OCR, encrypted PDFs, and unsupported PDF structures/encodings remain unsupported.
- ZIP64 and multi-disk ZIP archives remain unsupported. Extraction continues to require a selected workspace, explicit approval, a new destination folder, and CRC-32 verification.

### Validation
- JavaScript syntax validation passed. Targeted checks passed for PDF/ZIP upload and signature checks, text/PDF/ZIP attachment resolution, PDF text extraction, ZIP inspection/deflate decompression/CRC-32 verification, missing-file handling, workspace fallback, and PDF/ZIP payload validation when reopening `.runmind` sessions. Windows/.NET build and WebView2 end-to-end attachment tests must be run on Windows.

## [1.7.0] — Expanded Agent data and workspace tools

### Added
- Added `validate_json_schema` for workspace JSON data with local JSON Pointer `$ref`, type checks, object/array constraints, string/number bounds, common formats, and logical schema composition (`allOf`, `anyOf`, `oneOf`, `not`, and conditional schemas). Validation returns bounded, path-specific errors. Schema regular expressions run in the isolated worker with a hard timeout. Unsupported advanced keywords are reported as warnings rather than silently claimed as validated.
- Added `read_pdf` for best-effort extraction of selectable PDF text from unencrypted PDFs, including plain, Flate-compressed, and ASCII85/Flate streams where supported. Extraction is bounded to 25 MB per source file and 80 MB of decoded streams.
- Added `inspect_archive` and approval-gated `extract_archive` for standard ZIP files. Extraction rejects path traversal, symbolic links, encrypted entries, duplicate/case-colliding paths, unsupported compression, and oversized archives; every extracted file is checked against its ZIP CRC-32 before it is written. Extraction uses a new destination folder and removes partial output after a failure.
- Added `diagnose_connection` to classify common model endpoint failures and report connection/model-discovery status without sending a generation request or exposing API key values.
- Added CSV schema inference (`infer_dataset_schema`) and key-based CSV joins (`join_datasets`) supporting inner, left, and full joins, with approval and no-overwrite behavior.

### Changed
- Updated Agent tool descriptions and documentation for the new data, PDF, ZIP, and connection-diagnostic capabilities.
- Synchronized standalone, PowerToys plugin, plugin manifest, and MCP client version metadata to `1.7.0`.

### Limitations
- PDF extraction is not a complete PDF renderer: scanned-page OCR, encrypted PDFs, ZIP/object streams, custom font CMaps, and some complex encodings are not supported. Extracted text should be checked against the original for layout-sensitive documents.
- Archive support in this release is standard ZIP only; ZIP64, multi-disk ZIPs, TAR, and other archive formats are not included.
- JSON Schema validation is a bounded in-app implementation, not a claim of full conformance to every draft/keyword. Local `$ref` is supported; dynamic/unevaluated and selected content keywords are reported as unimplemented warnings.
- CSV inference is heuristic (up to 10,000 rows); joins are capped at 100,000 output rows. Empty join keys do not match.
- Connection diagnosis checks model discovery only. It cannot distinguish every network failure from a WebView2 CORS rejection and does not test generation/tool-call capability.

### Validation
- JavaScript syntax validation passed with Node.js 22.
- Targeted helper tests passed for valid/invalid JSON Schema checks, local `$ref`, basic PDF text extraction, deflated ZIP reading and CRC verification, CSV type inference, and full CSV joins.
- Full Windows/.NET build, WebView2 integration, real-world PDF corpus testing, and destructive ZIP-extraction UI testing remain to be run on Windows.

## [1.6.13] — Agent safety and multimodal workspace tools

### Added
- Added `regex_test`, which evaluates JavaScript regular expressions in a disposable Web Worker with a 750 ms hard timeout, bounded pattern/input sizes, and bounded match output.
- Added `read_image` for workspace PNG, JPEG, WebP, and GIF files. The image is attached to the next model turn for visual analysis; a vision-capable model is required.
- Added automatic workspace backups for file edits and deletions. Backups are stored under `.runmind-backups` and can be restored through the approved `restore_file_backup` tool.

### Changed
- File edit/delete approval cards now explain that a recovery backup is created before the operation.
- Restoring over an existing file first saves its current contents as another recovery point.
- Synchronized standalone, PowerToys plugin, plugin manifest, and MCP client version metadata to `1.6.13`.

### Validation
- The shared JavaScript application passes `node --check`.
- The complete Windows/.NET build, WebView2 host integration, live regex timeout behavior, and vision-provider compatibility still require testing on Windows with suitable local model servers.

All notable changes to RunMind are documented here, with the newest entries first. Historical product and file names are retained where they describe behavior at the time of that release.

## [1.6.12] — PowerToys plugin build fix

### Fixed
- Fixed the PowerToys plugin compilation error in `Main.CreateSettingPanel`. A WPF `Grid` derives from `Panel`, not `Control`, and cannot be returned from the `ISettingProvider` method.
- Restored the standard PowerToys pattern used by Microsoft's own plugins: `CreateSettingPanel` throws `NotImplementedException` because RunMind provides its settings through `AdditionalOptions` and does not implement a custom WPF settings panel.

### Changed
- Kept the standalone and PowerToys projects on `net10.0-windows10.0.22621.0`. The third-party linter's hardcoded `net9.0` check is not a reason to downgrade the projects or their .NET 10 PowerToys references.
- Synchronized project, plugin, and MCP client version metadata to `1.6.12`.

### Validation
- Static source checks passed. A full Windows/.NET build and live host tests still need to be run on a Windows development machine.

## [1.6.11] — Conversation UX and reliability

### Changed
- Replaced the ambiguous **Export** toolbar entry with **Conversations**, separating resumable Save/Open actions from one-way export formats.
- Enforced the `.runmind` extension and RunMind conversation JSON format. Empty conversations are rejected consistently by the page and native Save As host.
- Removed live-app message action buttons from standalone HTML transcript exports; code-copy controls remain available.
- Made native conversation saves safer by writing to a temporary file beside the destination and replacing the destination only after the complete JSON file has been written.
- Corrected build and setup documentation, added a static validation report, fixed a broken documentation link, removed obsolete branding screenshots from the README, and corrected the MIT copyright placeholder.
- Updated the unused PowerToys settings-panel hook. Its implementation was corrected in `1.6.12` to use the standard no-custom-panel pattern.

### Fixed
- Cleared a cached WebView2 bootstrapper after a failed launch or installation so an incomplete or invalid cached download does not block later startup attempts.

### Validation
- Static JavaScript, HTML, metadata, XML/XAML, and archive checks were performed. A Windows/.NET compile and live host tests remain necessary.

## [1.6.10] — Build warning cleanup and WebView2 clarification

### Fixed
- Enabled nullable annotations explicitly in the shared native conversation Save As helper. This removes compiler warning `CS8632` when the helper is compiled by the PowerToys project without enabling nullable analysis across the entire plugin project.

### Documentation
- Clarified that the WebView2 Evergreen bootstrapper checks for an installed runtime and downloads/runs Microsoft's installer only when the runtime is missing.
- Clarified that first launch can take longer on a machine without WebView2; subsequent launches reuse the installed shared runtime.

## [1.6.9] — WebView2 provisioning and conversation format

### Added
- Added automatic WebView2 Evergreen Runtime provisioning to both standalone and PowerToys hosts. RunMind checks for an existing runtime and, only when it is missing, downloads Microsoft's official bootstrapper and silently installs it for the current Windows user. An internet connection is needed on first launch only when WebView2 is not already installed.

### Changed
- Removed support for `.llmchat`, legacy LocalLLM JSON, and other portable JSON files as resumable conversations. The native conversation picker and readers now accept only `.runmind` files containing `format: "runmind"`.
- Both hosts use the same WebView2 runtime check and `.runmind` conversation validation.

## [1.6.8] — Native conversation Save As

### Added
- Added a native Windows Save As dialog to both desktop hosts. The current conversation title is pre-filled, `.runmind` is the default extension, and the chosen filename is written back to the conversation metadata and in-app/native window title. Browser-only previews retain a download fallback.
- Centralized the native save-dialog bridge in one shared helper linked into both WPF projects. The bridge accepts only validated messages from the local chat page; WebView2 host objects remain disabled.

### Retained
- Carried forward the non-blocking PowerToys delayed-query implementation, RunMind project/folder/assembly naming, conversation-title behavior, and open-in-current-window/open-in-new-window choices from `1.6.7`.

## [1.6.7] — RunMind project identity and non-blocking PowerToys queries

### Changed
- Renamed the source tree and build identities to `RunMind.Standalone`, `Community.PowerToys.Run.Plugin.RunMind`, `RunMind.sln`, `RunMind.exe`, and `Community.PowerToys.Run.Plugin.RunMind.dll`.
- Kept the PowerToys plugin GUID and action keyword stable. Existing `%LOCALAPPDATA%\LocalLLM` browser profiles and storage keys remain unchanged to preserve user data.
- New conversation files use the `.runmind` extension and JSON format marker `format: "runmind"`. At this release, opening also accepted `.llmchat` and legacy LocalLLM JSON; that compatibility was removed in `1.6.9`.

### Added
- Added **Open in this window** and **Open in new window** choices when opening a saved conversation. The new-window option leaves the current chat untouched and opens the selected conversation in a separate window in both standalone and PowerToys hosts.
- Added a narrowly scoped WebView2 message for opening saved conversations in new windows. Messages are accepted only from the app's local chat document and are checked for format, version, message count, and size. WebView2 host objects remain disabled.

### Fixed
- Removed synchronous `GetAwaiter().GetResult()` calls from the delayed PowerToys query callback. The callback starts an asynchronous worker and returns immediately; model discovery and generation run outside the PowerToys search callback, and results are refreshed after completion.

## [1.6.6] — RunMind branding and standalone conversation titles

### Changed
- Renamed the visible product to **RunMind**. The standalone executable became `RunMind.exe`, and the PowerToys plugin display name became RunMind.
- Retained the existing plugin ID/DLL name, internal namespaces/bootstrap API, action keyword, WebView2 data directories, and `.llmchat` file-format identifier at this stage to preserve compatibility. The legacy conversation format was later removed in `1.6.9`.

### Added
- Enabled the shared browser-side conversation-title generator for standalone chats. A prompt-derived fallback appears when the first message is sent; after the first successful assistant response, it is replaced by a model-generated title using the active provider/model and that connection's configured authorization headers.
- Updated the in-app heading, browser document title, and native Windows caption when the conversation title changes.
- Kept title generation independent for each standalone and PowerToys-launched window.

## [1.6.5] — Native title-bar theme

### Changed
- Made the standalone WPF title bar follow the Windows app appearance preference (`AppsUseLightTheme`) using the native DWM immersive dark-mode attribute.
- Re-applied the title-bar appearance when Windows broadcasts `WM_SETTINGCHANGE` or `WM_THEMECHANGED`, so an already-open window updates when Windows switches between light and dark modes.
- Retained the standard Windows title bar and controls; no custom window chrome or taskbar grouping changes were introduced.

## [1.6.4] — Window icons and multiple standalone instances

### Changed
- Set the LocalLLM ICO on both PowerToys-hosted chat window types so the title bar and taskbar display the application icon.
- Removed standalone single-instance activation so each launch opens a separate chat window in its own process.

## [1.6.3] — Branding, PowerToys query handling, and CORS diagnostics

### Added
- Integrated the supplied branding exports: colorful `Icon.ico` and `Icon.png`, raster sizes, light/dark monochrome PNG variants, and the theme-aware `Icon.svg`.
- Added an explicit WebView2 favicon reference and deployed the ICO beside the virtual-hosted chat page, avoiding the implicit missing `/favicon.ico` request.
- Added actionable model-discovery diagnostics for browser network/CORS failures.

### Changed
- Allowed one-character prompts in global PowerToys mode instead of suppressing them before delayed execution.
- Documented that WebView2 fetches originate from `https://localhost` and provider servers must allow that origin. Rewriting the provider hostname to `127.0.0.1` avoids the virtual-host collision but does not bypass server-side CORS.

## [1.6.1] — PowerToys two-stage query workflow

### Changed
- Finalized the global two-stage PowerToys Run workflow and explicit `llm` action keyword. Standalone launch remains available through normal application search.
- Tightened stale-query protection and cancellation ownership across fast queries, delayed execution, and pending-result hand-off.
- Hardened WPF dispatcher checks and WebView2 initialization/disposal diagnostics for the PowerToys host; retained separate WebView2 profiles for standalone and PowerToys.
- Updated build, setup, feature, implementation, and validation documentation for the `1.6.1` source package.

### Validation
- JavaScript and metadata checks passed. A Windows/.NET build and live PowerToys/WebView2 tests remained necessary.

## [1.6.0] — Global two-stage PowerToys Run results

### Added
- Added a global two-stage PowerToys Run result: an immediate `Ask LocalLLM` placeholder followed by a completed response result.
- Added query-operation cancellation. New fast queries cancel previous requests; delayed execution uses linked cancellation tokens for model discovery, response headers, and streamed response bodies, and suppresses stale results.

### Changed
- Enabled global plugin matching while retaining the `llm` action keyword. `llm` by itself no longer opens a chat window; the standalone executable is launched through normal application search.
- Selecting a pending result hands the request to the chat window and cancels the PowerToys request first. Selecting a completed result opens the chat with the existing prompt and answer.
- Marshalled PowerToys window creation, WebView2 initialization entry points, clipboard actions, and teardown to the WPF dispatcher. Added safe disposal and stage/HRESULT diagnostics.
- Separated PowerToys and standalone WebView2 user-data folders, added a standalone single-instance activation signal (removed in `1.6.4`), and integrated the supplied ICO/PNG/SVG branding assets.
- Kept the shared `ChatWindow.html` implementation and existing provider, Agent, Deep Research, workspace, and MCP code intact.

## [1.5.1] — Agent and Deep Research reliability

### Added
- Added immediate PowerToys Run live-chat hand-off as the default inline behavior: selecting the result opens the shared chat and starts the prompt automatically. The previous blocking inline behavior remains configurable. In-place token mutation of PowerToys results is not supported by the plugin result contract.
- Added task-scoped snapshots of model connection, generation settings, and tool definitions for Agent and Deep Research runs.
- Added collision-resistant MCP tool names and safe expired-session recovery for idempotent reads/lists.

### Changed
- Bounded direct page/document and Agent tool-turn response bodies.
- Improved Deep Research source provenance with discovered/read distinction, retrieval route, final URL, and failed-fetch records.
- Tightened LM Studio base URL validation and cancelled conversation-title generation when the owning window closes.
- Retained the one-product/two-host architecture without an installer, startup registration, tray, or service.

## [1.5.0] — Standalone Windows chat application

### Added
- Added a directly launched Windows desktop host using WebView2 and the existing shared chat application.
- Added explicit WebView2 disposal and window-lifetime checks to the PowerToys chat host.
- Added bounded MCP response reads, request deadlines, and parsing for multiple SSE data events.
- Applied bounded response reads to direct webpage and Reader retrieval.

### Changed
- Kept the PowerToys Run plugin as the second entry point to the same product.
- At this release, both hosts shared a WebView2 profile and therefore application settings and local browser storage. They were separated in `1.6.0`.
- Pressing Enter on the `llm` action keyword without a prompt opens a clean chat window.
- Removed global provider/base-URL mutation during model discovery; each discovery request uses its own connection snapshot and credentials.
- Serialized concurrent model refresh operations.
- Did not add an installer, startup registration, tray, service, Electron, C# Agent bridge, or MCP process bridge.

## [1.4.0-preview.7] — Persistent connections, workspace access, and title generation

### Added
- Added conversation-title generation when the user explicitly opens the standalone Chat window. The request uses the initial PowerToys Run provider/model and combines the initial prompt and response. The native caption starts as `LocalLLM`; a generated title updates the native caption and in-app heading. A prompt-derived fallback is used on failure. Normal inline PowerToys Run invocations do not trigger title generation.
- Added a unique native-generated window ID to the WebView bootstrap. Recent Agent Tasks and Deep Research checkpoints are isolated using this ID before history/checkpoints are loaded, rather than relying on `window.name` as the primary namespace.
- Added persistent model-server API-key authentication per connection, including a configurable authentication-header name. Saved keys are used for discovery, metadata, chat, and tool-call requests.
- Added persistent Jina Reader API key and custom gateway token/endpoint configuration, per-provider connection tests, and explicit credential clearing.
- Added persistent MCP server URLs, enabled state, and bearer tokens. Enabled saved servers attempt to reconnect at chat startup; individual saved tokens can be cleared.
- Added persistent workspace directory handles in IndexedDB, permission rechecks on new chats, reconnection of existing handles, and notifications to other open windows after selection, reconnection, change, or clear.

### Changed
- Kept direct website fetch as the first retrieval strategy. Jina Reader (`https://r.jina.ai`) is the second fallback; an optional user-configured Reader-compatible gateway is the third. No Cloudflare dependency is included.
- Improved Agent and Deep Research evidence instructions to follow links to version-matched manuals, release notes, and source-tree build instructions rather than treating landing/index pages as substantive documentation or inventing unverified build details.
- Kept Deep Research drawer visibility separate from Deep Research execution mode. Opening the drawer does not enable research; enabling the composer mode opens it.
- Cleaned Agent settings text and clarified persistent credential state.

### Validation
- Embedded JavaScript syntax, HTML IDs/selectors, project metadata, static storage/title/tool-flow checks, and ZIP integrity were checked. A .NET compile and live Windows/WebView2 test were not possible in that environment.
- Fixed two C# compile errors in the conversation-title WebView2 update path by awaiting the WPF `DispatcherOperation<Task<string>>` and then awaiting the returned WebView2 script task. No behavior or public API changes were made beyond restoring compilation of the preview.7 title-update implementation.

## [1.4.0-preview.6] — Reader endpoint and gateway configuration

### Fixed
- Corrected the Jina Reader endpoint and request shape. The default is `https://r.jina.ai`; requests use the documented `GET https://r.jina.ai/{target-url}` format instead of the unavailable EU hostname and `?url=` query parameter.

### Added
- Added a configurable Reader base URL, optional Jina Bearer API key, and a connection-test button in Agent settings. Anonymous Reader access remains supported; an API key raises the documented rate limits.
- Added an optional custom Reader-compatible HTTP gateway with its own optional Bearer token and connection test. It is attempted only after Jina fails.
- Added response handling for either a Jina JSON envelope (`data.content`) or readable plain-text/Markdown responses. Retrieval diagnostics identify the provider and HTTP status/detail where available.

### Changed
- Kept Jina and custom gateway tokens in per-window `sessionStorage`; they are not written to `localStorage`, exported conversation files, or task history.
- Updated Reader documentation and settings text. Public URLs may be sent to the selected Reader provider; private/local URLs, embedded credentials, and likely secret-bearing query parameters are blocked from remote forwarding.
- Kept `Main.cs` unchanged. The native window caption was still created by C# from the initial model name; dynamically changing/removing it required a C# host change and was not included in this preview.

### Validation
- Embedded JavaScript syntax, DOM ID uniqueness/references, Reader URL construction and mock response parsing, project metadata, and ZIP integrity were checked. Live CORS behavior and the .NET build still required Windows testing.

## [1.4.0-preview.5] — Agent task isolation and Reader fallback

### Fixed
- Fixed Recent Agent Tasks leaking between independent chat windows. Task-history storage now uses a versioned key scoped to the unique top-level chat-window ID. The old shared v1 history is not migrated because its entries cannot be reliably attributed to one window.
- Replaced the GitHub-specific API branch in `fetch_webpage` with one generic page-reader path. GitHub and HAProxy are ordinary URL targets, not special workflows.

### Added
- Added a configurable EU Reader fallback for `fetch_webpage`. Direct browser fetch remains first; if browser CORS, anti-bot, or HTTP access restrictions block it, the app can send a public URL to Jina AI's EU Reader and return extracted page text.

### Changed
- Enabled the Reader fallback by default, with an option to disable it in Agent settings. The UI discloses that public URLs are sent to the remote reader. Local/private IPs, localhost, credential-bearing URLs, and URLs with likely secret-bearing query parameters are not forwarded.
- Updated Recent Agent Tasks help text to explain that each chat window has a separate task list. Clearing history clears only the current window's list.
- Improved Deep Research activity output to show fetched character count and retrieval route. Errors preserve both direct-fetch and Reader-fallback failures, and research instructions tell the model to try an alternate official page rather than abandoning a task after one failed read.

### Validation
- Embedded JavaScript, fetch-fallback mocks, private-URL blocking, opt-out behavior, HTML IDs/selectors, and ZIP integrity were checked. Live WebView2/CORS behavior and .NET compilation still required Windows testing. `Main.cs` remained unchanged.

## [1.4.0-preview.4] — Deep Research drawer and task-history isolation

### Added
- Replaced the full-width research section above the transcript with an optional right-side overlay drawer.
- Added a header toggle to open/reopen the drawer, a visible close control, and Escape-to-close behavior.

### Changed
- Separated Deep Research mode from drawer visibility. Closing the drawer does not stop research, delete its checkpoint, or disable the mode. Switching to ordinary chat/Agent Mode hides the drawer while preserving saved research results.
- Scoped research checkpoint keys with a unique per-window identifier stored in `window.name`, while retaining `sessionStorage` for reload/resume in that window. A new chat window does not read another window's checkpoint, even when WebView2 shares a browser profile.
- Cleaned Recent Agent Tasks so only conversations with evidence of an actual tool invocation are retained. Ordinary chat and greetings are excluded from the old history list. The history popup is height-limited and scrollable.
- Kept C# unchanged.

### Validation
- Embedded JavaScript passed `node --check`. Actual .NET compilation and live WebView2 behavior still required Windows testing.

## [1.4.0-preview.3] — General Agent workflow and cross-window isolation

### Added
- Added compatibility parsing for a model that emits a complete function-style tool call as text (for example, `researchupdateplan(...)`) instead of native structured `tool_calls`. Only a response consisting of a recognized tool invocation is promoted to an action.
- Added generic file-intent preflight and refreshed the tool registry after folder-access changes so file tools become available immediately after the user selects a folder.
- Added one general completion-audit turn for sequential multi-step Agent requests after tool execution, discouraging the model from stopping after the first convenient action.

### Changed
- Removed the request-specific GitHub URL → HTML → read-back → summary shortcut. Agent Mode now relies on its general tool loop to plan and execute user-requested multi-step tasks; GitHub is not a hardcoded workflow.
- Stored Deep Research checkpoints in per-WebView `sessionStorage` instead of shared profile `localStorage`, preventing cross-window leakage. Opening a different conversation clears the current workspace association.
- Made Agent Mode and Deep Research mutually exclusive. Enabling one turns off the other; Deep Research retains access to the shared agent and enabled MCP tools.
- Moved the clearly labelled Close workspace control into the visible research toolbar.
- Canonicalized tool-name aliases in native tool-call responses, including aliases missing underscores.
- Kept `Main.cs` unchanged.

### Validation
- JavaScript syntax, parser smoke tests, static HTML checks, and ZIP integrity checks passed. Live Windows/WebView2 behavior and .NET compilation remained unverified.

## [1.4.0-preview.2] — Research controls and tool approvals

### Added
- Added Copy plan and Copy results controls for live research work and partial/final reports.
- Added persistent, explicitly opted-in per-tool **Always allow this tool** approvals, with a revocation list and clear-all control in Agent settings.

### Changed
- Improved GitHub repository fetching to return README text, repository metadata, and a bounded root-file listing in the readable content field.
- Strengthened Agent instructions to complete dependent multi-step requests rather than stopping after an initial fetch.
- Clarified that native WebView2/Windows file-access prompts are browser-controlled and cannot be restyled by the HTML application.
- Kept `Main.cs` unchanged.

### Fixed
- Fixed the Deep Research workspace visibility bug: Close/Escape hides the panel without cancelling or deleting the active/saved research task.

### Validation
- JavaScript syntax and static HTML checks passed. Windows/WebView2 and .NET runtime tests remained unverified.

## [1.4.0-preview.1] — Deep Research and expanded Agent tools

### Added
- Added standalone Deep Research mode alongside ordinary chat and Agent Mode.
- Added editable research plans with checkpoint-applied revisions, page/tool budgets, source collection, and Markdown report export.
- Added pause/resume/cancel lifecycle controls and `localStorage` checkpoints.
- Added direct file downloads, bounded web extraction (links, metadata, tables, sitemap, RSS/Atom), and GitHub repository README retrieval.
- Expanded selected-folder tools for editing, deleting, copying, moving, renaming, metadata, checksums, comparisons, project overview, and duplicate detection.
- Added CSV analysis/transforms/comparison and JSON validation/formatting tools.
- Added Markdown/HTML report creation, SVG chart/flow-diagram generation, and browser-canvas image operations.

### Changed
- Preserved the existing C# source; only JavaScript, version metadata, and documentation were updated.

### Validation
- This remained a preview. Full .NET compilation and live WebView2/provider/MCP testing were not possible in the build environment.

## [1.3.0] — Agent, connections, MCP, and workspace foundation

### Starting point and preservation
- Based on the user's `1.2.3` source and the `1.3.0-preview.4` development snapshot.
- Preserved the compact PowerToys Run UI, inline provider settings, standalone WebView2 chat, exports, context manager, and normal streaming chat path.
- Kept `Main.cs` unchanged. No C# MCP bridge, native process launcher, or STDIO transport was added.
- Kept `RequestTimeoutSeconds` behavior for inline PowerToys Run results unchanged; standalone chat remained governed by user cancellation and provider completion/errors.

### Added
- Added inline approval cards for Agent/MCP tool execution, replacing browser-native confirmation dialogs. Cards show the requested tool, server, path/URL, and arguments; potentially mutating actions receive a warning treatment.
- Added **Allow once**, **Deny**, and (for read-only Agent tools) **Allow for this task** actions. Task-scoped read approvals reset when a new Agent run starts. MCP tool calls/resources/prompts and file creation remain explicit per-call approvals.
- Made Stop/cancel dismiss a pending approval.
- Added a browser-side connection manager for multiple Ollama and OpenAI-compatible endpoints. Enabled servers are discovered sequentially and models are merged into one dropdown, grouped by alias and endpoint host. Connections can be added, enabled, disabled, and removed; the two built-in defaults can be disabled but not removed.
- Made the selected connection determine the endpoint used for discovery, metadata, ordinary streaming chat, and Agent tool-call requests. Endpoints and aliases are stored in WebView `localStorage`; conversation exports record the selected connection ID so duplicate model names can be restored to the same configured server when available.
- Added `update_plan` for concise Agent Mode plans, with pending/in-progress/completed statuses rendered in the transcript.
- Added local recent task history (up to 20 snapshots) for Agent Mode and MCP-enabled chats. History can reopen a snapshot or be cleared. Snapshots are stored in the WebView profile; attachment bytes are excluded.
- Retained the per-run maximum tool-step setting as the tool-call limit. Read-only approvals can be allowed once or for the current run; the automatic read-only setting remains available. Tool errors are returned to the model as tool results so it can attempt recovery within the configured step limit.
- Added a browser-only Streamable HTTP MCP client: initialize/initialized, tool discovery/calls, resource listing/reading, prompt listing/retrieval, pagination, session IDs, protocol-version headers, optional bearer authentication, disconnect, enable/disable, remove, and reconnect.
- Added MCP access in Agent Mode and optionally in ordinary chat. MCP tool/resource/prompt actions use the inline approval card.
- Added explicit-URL routing to `fetch_webpage`; public GitHub repository URLs use GitHub's public API to retrieve metadata and README.
- Added `web_search`, exposed only when a SearXNG URL is configured.
- Added selected-folder file tools to list, read UTF-8 text, and create new files without overwriting existing files.

### Changed
- Kept a user-selected folder available across prompts and Agent Mode toggles for the current WebView session. The user can clear it in Agent settings. The model instructions tell the model not to ask for the folder again when file tools are already available.
- Kept folder access session-scoped in this release: it is not persisted across WebView restarts, and file tools remain unavailable until a folder is selected.
- Kept MCP bearer tokens session-only. Server URLs are saved locally; HTTP/HTTPS only. STDIO is unsupported because no native process bridge was added.
- Kept MCP resources discoverable/readable; this build does not create a persistent vector index or automatically embed/index all remote resources.

### Limitations at this release
- Model-server API-key authentication was not implemented. OpenAI-compatible endpoints requiring bearer/API-key authentication did not work in this build. MCP bearer authentication was supported but session-only.
- Remote model and MCP endpoints had to allow WebView2 CORS/network access. Some browser-only MCP implementations and authentication flows might not work.
- LM Studio Native REST mode did not support Agent tool calls; OpenAI-compatible mode was required.
- Persistent knowledge-base embedding/indexing, MCP STDIO, file edit/delete, task retry UI, and a secure credential vault were not included.
- JavaScript syntax, HTML IDs/selectors, and ZIP integrity were checked. A full .NET build and live WebView2/provider/MCP tests could not be run because the .NET SDK and configured live test servers were unavailable.

### Web search validation
- `web_search` is included in the model's Agent tool definitions only when Agent settings contain a syntactically valid HTTP(S) SearXNG instance URL.
- Saving a non-empty malformed URL is rejected with an explanatory toast; an empty URL saves normally and explicitly leaves web search disabled.
- Tool execution independently rechecks the normalized configured endpoint and fails closed if the setting is missing or invalid, protecting against stale tool calls or settings changed after tool discovery.
- The configured URL is normalized by removing a trailing slash; credentials embedded in URLs are rejected.
- `fetch_webpage` remains available independently, so direct URL fetching does not require a search engine.

## [1.2.3] — Regenerate thinking-toggle behavior

### Fixed
- Regenerate no longer restores the previous assistant message's `thinkRequested` value. It preserves the current thinking toggle, so enabling thinking before regenerating with a different model/provider is respected.

### Unchanged
- Standalone provider/model selection behavior, `Main.cs`, and the solution were unchanged. Only `ChatWindow.html` and version metadata in the project and plugin manifests changed.

## [1.2.2] — Inline PowerToys provider selection

### Added
- Added a **PowerToys Run LLM provider** setting with **Ollama** and **LM Studio** choices.

### Changed
- The selected provider controls inline PowerToys Run model discovery and generation.
- Ollama uses the configured `/api/tags` and `/api/generate` endpoints, retaining its `keep_alive` and `think` request options.
- LM Studio uses the configured OpenAI-compatible base URL, `GET /v1/models` for model discovery, and streaming `POST /v1/chat/completions` for generation.
- The default-model field is shared; its description refers to the model name/ID from the selected provider.
- The standalone WebView2 chat retains its own provider selector and behavior; the PowerToys Run setting does not force the chat window to use the same provider.
- `RequestTimeoutSeconds` applies only to inline PowerToys Run generation for either provider. `KeepAliveSeconds` and `Thinking` are Ollama-specific and are not sent to LM Studio.

## [1.2.1] — Streaming, context management, and reliability

### Fixed
- Fixed the live thinking disclosure interaction. Streaming updates now patch the existing assistant message in place and preserve the `<details>` node, allowing the user to expand/collapse thinking while generation is active.
- Made the LM Studio API mode setting provider-specific. It is hidden for Ollama selections and shown only when an LM Studio model is selected; the saved preference is retained when switching providers.

### Added
- Added an explicit `[hidden]` style so provider-specific setting fields are reliably removed from the CSS grid.
- Added a dedicated **Context** toolbar panel to replace context controls that were difficult to find inside Generation Settings.
- Added estimated input-token usage and a utilization bar based on a manual per-request context override or detected active/model context metadata.
- Added separate counts for included, summarized, and unsummarized/omitted messages.
- Added an optional hard recent-turn cap, an optional stricter estimated history-token budget, and provider-aware model context length.
- Added a reviewable/editable summary, explicit save/clear actions, and a preview of the selected context. The preview is limited to the most recent 30 included messages for UI responsiveness; this does not change the request.

### Changed
- Summary and history-token-budget settings are preserved in portable conversation files. Recent-turn and token-budget preferences are also remembered locally.
- Context estimates are explicitly approximate. They use serialized text length and do not precisely represent model tokenization, image costs, system prompt overhead, provider formatting, or reserved output tokens.
- Changing context selection invalidates LM Studio native response continuation IDs to prevent a later request from continuing with a stale server-side context chain.
- Scoped the PowerToys Run request-timeout setting to inline PowerToys Run generation. It is not included in the WebView2 bootstrap or applied to standalone chat. Chat generation uses the user-triggered Stop cancellation signal; discovery and metadata lookups retain short independent deadlines.
- Added context detection through Ollama `/api/ps` for active loaded context and `/api/show` for model maximum metadata. LM Studio uses `/api/v1/models` for loaded instance context and model maximum, with `/api/v0/models` as a compatibility fallback. Metadata failures do not block chat generation.
- Distinguished manual request overrides, active loaded context, and model maximum (not currently loaded) in the Context UI.
- Removed the implicit 12-user-turn cap. Blank now means no hard turn cap; old persisted default-12 preferences are migrated to no cap. When context capacity is detected, an adaptive history budget retains space for output/provider overhead. An optional user-set history-token cap can make that budget stricter.
- Updated Ollama and both LM Studio stream readers to cancel unfinished response bodies and release reader locks on errors, Stop, and early completion.
- Normalized LM Studio object-shaped capability metadata (vision, tool-use, reasoning) into model-info capability labels.

### Validation
- Embedded JavaScript syntax, HTML ID uniqueness, and static assertions for timeout separation, context metadata, and history defaults were checked.
- Context-plan tests covered full history, recent-turn limiting, token-budget trimming, newest-turn preservation, and summary/omission boundaries.
- Stream readers were exercised with mocked completion/error streams to verify cancellation and reader-lock cleanup. WebView2/provider runtime behavior and C# compilation still required testing on Windows.

### Historical follow-up notes (release version not recorded)
The original changelog recorded the following reliability follow-ups without assigning them to a specific release. The dated 2026-10-30 thinking-disclosure and LM Studio setting fixes are already consolidated under this version above, so they are not repeated here.
- Scoped the PowerToys Run request timeout to inline generation only. Standalone chat uses user-triggered Stop cancellation, while discovery and metadata lookups retain independent deadlines.
- Added provider-aware context detection and UI labels for manual override, active loaded context, and model maximum.
- Removed the implicit 12-user-turn cap and migrated the old persisted default of 12 to no hard cap.
- Updated Ollama and LM Studio stream readers to cancel unfinished response bodies and release reader locks on errors, Stop, and early completion.
- Normalized LM Studio object-shaped capability metadata for vision, tool use, and reasoning.

## [1.2.0] — Streaming, provider, context, and PowerToys result improvements

### Changed
- Batched streaming content updates with `requestAnimationFrame` and patched only the active message; completed transcript messages remain in place.
- Changed the one-second metrics refresh to update only the metrics element, avoiding transcript rebuilds and Markdown reparsing.
- Handled thinking disclosure state through one delegated listener instead of reattaching listeners after each full render.
- Made preferences reject `null`, arrays, and non-object JSON values.
- Added a 15-second connection-establishment ceiling, a separate short model-discovery deadline, and a longer generation deadline. The PowerToys Run Ollama model list is cached for five seconds. WebView2 request deadlines remain active while response bodies are consumed, including streamed reads.
- Improved Ollama NDJSON and LM Studio SSE parsers to report malformed records/events and fail clearly when a stream is unusable.
- Added time-to-first-token metrics alongside elapsed time, token count, and throughput.

### Added
- Added a small provider registry with normalized callbacks for chat model discovery and generation.
- Added opt-in LM Studio native REST v1 support for native reasoning events, context-length configuration, server response IDs, and stateful continuation.
- Added conversation context summarization. Older turns can be summarized on demand; the summary is included in subsequent requests and exports, while original messages are retained.

### Changed — provider and context behavior
- LM Studio OpenAI-compatible requests retry only for recognized optional-parameter validation errors. Rejected capabilities are cached per endpoint/model for the current WebView session.
- The OpenAI-compatible LM Studio adapter remains available for broad compatibility.
- If an LM Studio native conversation is imported, edited, summarized, or exceeds the configured history window, the next request starts a fresh native thread seeded with the managed local transcript.
- The full conversation remains local and available for export/editing. Requests can be limited to a configurable number of recent user turns (default 12 at this release). Older turns can be summarized on demand; the summary is included in subsequent requests and exports without deleting the original messages.
- Context estimates are labelled approximate and are based on text length rather than actual tokenizer counts. Editing/regenerating invalidates a potentially stale summary.

### Changed — PowerToys Run results
- Preserved the compact multi-result presentation.
- Updated section splitting to preserve all characters exactly (concatenating sections reproduces the original response) and avoid splitting UTF-16 surrogate pairs.
- Kept the context-menu copy action copying the complete response rather than only the selected section.

### Validation and limitations at this release
- Checked imported attachment count, declared/decoded image size, allowed image MIME types, and per-message aggregate size.
- JavaScript endpoint normalization rejects invalid schemes and embedded credentials.
- C# model discovery has its own 8-second timeout; C# generation keeps the configured timeout through stream reading.
- PowerToys Run's plugin query contract is synchronous and exposes no documented in-place result mutation callback. Inline generation therefore returns completed results; live token-by-token launcher result updates were intentionally not implemented.
- The original 1.2.0 archive did not include PowerToys reference assemblies under `lib\x64`, and the editing environment did not have the .NET SDK. The C# project could not be compiled there. Embedded JavaScript passed `node --check`, but WebView2/provider integration was not tested.

## [1.1.0] — WebView2 chat window

### Added
- Replaced the response window's legacy WPF `WebBrowser` renderer with WebView2.
- Seeded each response window with its original prompt and initial Ollama response. Request-error windows also retain the prompt when available.
- Added per-window in-memory conversation history. At this release, closing the window ended the session unless the user exported it.
- Added concurrent Ollama and LM Studio model discovery at chat startup. A single model selector groups models by provider and includes a refresh button; models from the available endpoint remain usable if the other endpoint is unavailable.
- Added the `LMStudioBaseUrl` PowerToys setting (default `http://localhost:1234/v1`). At this release it was used for chat model discovery and follow-up turns; inline PowerToys Run queries remained on the configured Ollama `/api/generate` endpoint.
- Added Ollama follow-up streaming through `/api/chat` and LM Studio follow-up streaming through the OpenAI-compatible `/v1/chat/completions` API.
- Added System, Light, and Dark themes. CSS color, font, radius, and shadow variables were centralized in the `:root` rules in `Main.Chat.cs` for developer customization.
- Added exports for standalone HTML, Open WebUI import JSON, portable conversation JSON, OpenAI-compatible request JSON, Markdown, CSV, TXT, JSONL, and Print/Save as PDF. CSV uses UTF-8 with BOM and guards against common spreadsheet formula-injection prefixes.

### Export behavior
- Standalone HTML exports contain the rendered transcript, embedded CSS, and a small local code-copy helper. They do not contain the WebView2 messaging bridge or Ollama/LM Studio endpoint logic and do not connect to the plugin after export.
- Open WebUI export produces a JSON array containing a conversation `chat` object with a linear message tree (`parentId`, `childrenIds`, and `currentId`). Import it through Open WebUI's **Settings → Data Controls → Import Chats**. The format followed the documented import shape but was not tested against a live Open WebUI instance in the original environment.

### Build requirements and validation at this release
- Required Windows and the .NET SDK targeted by the included project file, Microsoft Edge WebView2 Runtime, and NuGet packages including `Microsoft.Web.WebView2`.
- The original source archive omitted the PowerToys common-library DLLs expected under `lib\x64`; those assemblies had to be supplied separately for the PowerToys build.
- The editing environment did not have the .NET SDK or the omitted PowerToys common libraries, so a full Windows build/runtime test could not be performed. Embedded JavaScript syntax and project XML were checked.

## [1.0.3.1] — Initial Ollama PowerToys Run plugin

### Dependencies
- Uses `Markdig` to render Markdown and `HtmlAgilityPack` to sanitize generated HTML. Keep both package references in the project file.
- The PowerToys reference assemblies and target framework must align with the PowerToys version installed on the Windows machine.

### Behavior and security
- Sends the user's query unchanged as Ollama's `prompt`; no hidden prompt or temperature is added.
- Keeps the configured default model unchanged when a one-off model override is used.
- Supports `> model-name > your prompt` for per-query model selection.
- Makes `keep_alive` and request timeout configurable in PowerToys Run settings.
- Uses Ollama `/api/tags` to distinguish a missing model from an endpoint/network failure.
- Parses newline-delimited `/api/generate` streaming JSON and reports API errors instead of displaying them as model output.
- Provides a clean, responsive, theme-aware HTML response page with browser scrolling, selectable text, clickable HTTP/HTTPS/mailto links, a Copy Markdown button, and per-code-block copy buttons.
- Disables model-supplied raw HTML, strips images and active elements, restricts link schemes, and uses a restrictive Content Security Policy. The response page makes no external requests on its own.
- Keeps the PowerToys result subtitle short; Enter opens the full response, and the result context menu copies the complete Markdown response.


### Command Palette activation diagnostics and SDK assembly resolution

- Register a package-local resolver for `Microsoft.Windows.SDK.NET.dll` before Command Palette WinRT extension types are accessed.
- Isolate COM-server startup in a non-inlined method so the resolver is installed before the extension class is JIT-compiled.
- Log requested and packaged SDK assembly identities and the package path used for resolution.
- Add startup diagnostics around CsWinRT wrapper initialization and extension construction.


## Unreleased — Phase 2 Development Server Management

- Added shared Agent tooling for package-script discovery and development-server start/list/logs/readiness/health/stop across all three hosts.
- Added bounded multi-server ownership tracking, port-conflict preflight, loopback-only HTTP readiness/health checks, and bounded stdout/stderr capture.
- Added fresh approval for startup and termination; termination targets only processes launched by the current RunMind host.
