# RunMind Roadmap

## Current source baseline: 3.1.0 — P4 Agent Capabilities

## Completed milestone: P0 — Foundation & Reliability

P0 source implementation is retained in this release. The P0 target-Windows certification gate remains separate: build all three hosts with `Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64` and complete `docs/P0-WINDOWS-ACCEPTANCE.md`. This environment cannot certify Windows/.NET runtime behavior.

## Completed milestone: P1 — Project Intelligence

P1 extends the P0 persistent index and unified workspace. It does not create a second index, a second workspace, or a parallel execution engine.

### P1 scope implemented in source

- **Project Understanding:** evidence-based architecture map of top-level modules/boundaries, package and HTML entry-point candidates, import direction, resolved/unresolved relationships, circular dependencies, high-coupling modules, parser coverage and index truncation. The Agent workflow instructs source verification before treating heuristic edges as proof.
- **Project Diagnostics:** separate read-only operational health assessment for package manifests, package-manager/lockfile ambiguity and conflicts, npm lockfile root metadata consistency, declared scripts, tooling configuration, test discovery, parser errors, unresolved imports, cycles and incomplete indexing. Environment probes and build/test/audit commands remain separately approved actions.
- **Project Mission Control:** structured evidence-backed control center available both as an Agent tool/workflow and in Agent Settings. It reports index freshness/completeness, architecture evidence, detailed diagnostics, static release-readiness, prioritized recommendations and optional explicit changed-path test impact without executing commands.
- **Test Impact Analysis:** accepts explicit workspace-relative changed paths, follows reverse static import edges for JavaScript/TypeScript changes to identify direct and transitive JavaScript/TypeScript tests, labels filename-only suggestions as low confidence, and reports unknown paths. It does not infer a Git diff or execute tests.
- **Release Readiness:** static checks for package version, changelog version reference, README, CI configuration, test discovery, package-manager/lockfile consistency, diagnostics, and declared build/test scripts. It explicitly reports Git status, live dependency audit, actual builds/tests, signing, SBOM, packaging and publication as unverified gates.

### P1 validation boundaries

- Static analysis uses the persistent index and is bounded by its existing file, byte, depth and parser limits.
- Test selection depends on statically indexed imports. Dynamic imports, runtime-generated relationships and non-standard test harnesses can be missed; candidates must be reviewed.
- Project-specific language-aware source analysis/tooling remains limited to JavaScript, TypeScript, JSX/TSX, HTML and CSS. SVG is indexed as a first-class vector asset with local-reference analysis, not as a programming language. Other source files remain generic text.
- No build, test, package install, dependency audit, Git mutation, tag, signing or publication is triggered by these P1 tools.
- The P0 Windows certification checklist remains open until run on the target Windows machine.

## Completed milestone: P2 — System Manager Intelligence

P2 extends the native System Manager across the same three hosts with individually opt-in, permission-aware capabilities. See `docs/SYSTEM-MANAGER-P2.md` for supported operations and safety limits. The Windows acceptance gate is documented separately; source implementation and portable contract tests do not certify live Windows behavior.

- Windows Event Log investigation limited to Application/System/Setup with bounded time ranges and result counts.
- Crash investigation correlates Application Error, Windows Error Reporting, .NET Runtime and Service Control Manager records; grouped signatures are evidence, not proven root causes.
- Resource sampling returns bounded system CPU/process CPU/working-set samples for a finite request of at most 60 seconds.
- Scheduled Task inventory is read-only; exact-identity enable/disable/start/stop are explicitly confirmed and post-action verified. Creation, deletion and definition/trigger editing are not supported.
- Storage health reports physical-disk, reliability-counter and volume data when Windows/controller drivers expose them; unavailable counters are unknown, not healthy.
- Security posture reads Defender, firewall profiles, system-volume BitLocker, Secure Boot, TPM and UAC indicators; it is not a compliance certification or malware scan.
- Targeted network diagnostics resolve one hostname/IP, report Windows-selected route/interface/next-hop when available, and optionally attempt one TCP port with bounded phases and fresh confirmation; no scans/ranges.
- SVG is a first-class vector asset in Project Understanding and Mission Control, including normalized local references from HTML, CSS and SVG `href`/`xlink:href`.


## Completed milestone: P3 — Developer Infrastructure

P3 extends the existing shared Agent Core/native bridge; it does not introduce a separate developer-infrastructure runtime.

- **Podman/Docker Development Workbench:** fixed-command runtime/resource inventory, exact container inspect/logs/stats/start/stop/restart and Compose config/ps/up/down/build/logs. Each call is freshly approved; state-changing actions are not represented as independently health-verified. No shell passthrough, prune or volume/image deletion.
- **SQLite workbench:** integrity, table/schema/foreign-key inspection, parameterized read-only query/EXPLAIN and one approved transactional DML/DDL statement on an existing workspace-contained SQLite file. SQLite only; MySQL/PostgreSQL later.
- **OpenAPI contract tests:** bounded OpenAPI 3.0/3.1 JSON/YAML input, explicit base URL, local references, example-driven request generation, status-specific response schema validation, same-origin redirect restrictions and mutation opt-in.
- **Runtime/log correlation:** ephemeral event timeline across selected owned development-server and Node Inspector sessions plus explicitly supplied Playwright/API event slices. IDs are correlation hints, not causal proof.
- **Advanced browser debugging:** Playwright console/page-error/request diagnostics, source locations, resource timings, screenshots and optional trace ZIP. No arbitrary JS evaluation or browser-source breakpoints.
- Five corresponding built-in developer workflows are available. See `docs/DEVELOPER-INFRASTRUCTURE-P3.md`.

### P3 validation boundaries

- Portable tests exercise Node runners, mocked browser behavior and source/native-host contracts. They do not establish that Podman/Docker is installed, that the Windows engine/WSL backend is healthy, or that all real Windows operations work.
- C# compilation and all three Windows-host acceptance remain pending until run on the user's Windows machine with .NET 10 and Node.js 24+.
- SQLite uses Node's built-in `node:sqlite`; no SQLite native package, DB file creator or migration system is introduced. MySQL/PostgreSQL integrations are not included.
- OpenAPI YAML needs an existing `yaml` or `js-yaml` dependency. JSON is built in. The schema validator intentionally supports a bounded subset and does not execute remote `$ref` requests.
- Browser traces may contain page text/screenshots and must be handled as sensitive local artifacts. No arbitrary browser JavaScript execution or live DevTools source stepping is exposed.
- Runtime correlation is an in-memory/response-only view; no persistent log warehouse, clock synchronization or causal inference is implied.

## Completed milestone: P4 — Agent Capabilities

P4 completes the named Agent capability milestone on top of P0–P3. It strengthens MCP discovery, multi-tool execution, checkpoint recovery, tool-result context, Skills/project context, Change Impact Explorer, Development Session Timeline and Artifact Verification. See `docs/AGENT-CAPABILITIES-P4.md` and `docs/P4-IMPLEMENTATION-AUDIT.md`.

- **MCP discovery:** paginated server/tool/resource/resource-template/prompt metadata, discovery completeness and search; exact template expansion; resource/prompt/tool access remains separately approved.
- **Multi-tool orchestration:** 1–4 bounded workers for independent read-only operations; serial barriers for duplicate calls, writes, approval gates, MCP and delegated work; dependency-layer and execution-graph records.
- **Task checkpoints/recovery:** bounded redacted per-chat task/plan/progress/operation records; interruption marks uncertain outcomes; explicit same-workspace Resume and inspect-before-retry policy; no automatic replay.
- **Tool summarization:** bounded/depth-limited, credential-redacted structured summaries; body, HTML and binary/base64 payloads are omitted from Agent context and local previews.
- **Skills/project context:** bounded project instructions, package metadata, project index and ranked Skill metadata; Skill content is explicitly loaded and untrusted; no instruction grants tools or permission.
- **Change Impact Explorer:** explicit changed paths/symbols to JS/TS direct/transitive importers and test candidates, with HTML/CSS/SVG asset consumers and unknown/unanalysed paths.
- **Development Session Timeline:** local chronological checkpoint/tool/dependency/sub-agent records in Workflow UI and a read-only Agent tool.
- **Artifact Verification:** structural checks, exact-byte SHA-256, optional expected digest comparison and verification receipt; digest equality is not provenance/signature verification.

### P4 validation boundaries

- MCP pagination is bounded to 20 pages/2,000 entries per discovered catalogue; remote resource/prompt/tool calls remain approved operations.
- Read-only concurrency is restricted to an explicit allow-list; approval-gated, mutating, duplicate, MCP and delegated calls are barriers.
- Checkpoints are local to the chat's WebView profile, bounded to 20 entries and do not restore raw conversation transcripts or external state.
- Change impact is static/index-based and can miss runtime-generated/dynamic dependencies.
- Session timeline contains only RunMind-recorded events; it is not a log warehouse or causal tracing engine.
- Artifact digest checks establish byte equality only, not trusted provenance.
- Windows/.NET certification remains pending until the P0–P4 acceptance checklists pass on target Windows.

## Future expansion candidates (not part of P4)

- Optional MySQL/PostgreSQL adapters after SQLite proves its long-term interface and Windows runtime integration.
- Signed artifact and provenance verification through trusted signing identities, certificate policy and reproducible build evidence.
- Deeper browser source stepping through a separate Chromium/CDP debugging security model.
- External OpenTelemetry/log integrations only where existing log ownership and user-controlled retention are respected.
- Approval-gated release automation for build/test/package/sign/SBOM, backed by independently verifiable evidence.

Keep the current codebase compact: reuse the shared Agent Core, native bridge, project index, approval system and existing runners. Avoid adding a new engine when an existing subsystem can be extended.

### Compatibility and security principles

- One shared Agent Core across Standalone, PowerToys Run and PowerToys Command Palette.
- One native-owned workspace root per conversation; no absolute path guessing from directory names.
- Native operations are bounded and freshly approved where they change application or system state.
- Never terminate an unknown process because it occupies a port.
- Never claim HTTP readiness without an actual configured probe.
- Browser policy events are not silently discarded; expected and unexpected blocks remain distinguishable.
- System Manager remains optional and disabled by default.
- Native process execution runs as the current Windows user and is not a sandbox.
- Project language-aware source support remains JS/TS/JSX/TSX/HTML/CSS; SVG is a first-class vector asset (not a language). Other language-specific tooling requires an explicit future scope decision.


## 2.6.2 — System Management Skills & Capability Contracts

- Bundled on-demand system-management, container-management, and runmind-management skills.
- Shared Agent policy describes safe discovery, operation selection, permission boundaries, and verification.
- Adapter inventory reports descriptive operation contracts; native enforcement remains authoritative.


## 2.6.3 — Developer Intelligence & System Manager

Implemented in this source snapshot: Windows Notepad adapter, File Explorer folder/file actions, Docker/Podman container lifecycle/read operations, and four coding skills. The adapter editor/override UI and native conversation-session management remain follow-up work; RunMind conversations currently live in WebView IndexedDB and are not exposed as a native session store.


## Phase 2 progress — 2026-11-21

- Node Debugger: implemented and confirmed by the user to build successfully across all three Windows hosts.
- Runtime Diagnostics & Profiling: added V8 heap diagnostics, CPU profile export, streamed heap snapshots, and bounded timestamped memory sampling. The user confirmed the increment works across all three Windows hosts.
- Playwright Browser Integration: added the shared structured Playwright runner, approval-gated browser workflows, assertions, console/network diagnostics, and screenshot artifacts. Portable integration tests pass; Windows compilation/runtime validation is pending.

## Phase 1 completion candidate — 2026-11-21

Implemented in the source increment: JavaScript AST rename/import organization/conservative extract-function previews; cross-file reference edges for resolved static imports; reversible HKCU/HKLM Run startup toggles; hardware/driver and TCP/UDP listener diagnostics; normalized System Manager outcomes; real local Git workflow tests; and a Windows validation script for all three projects. The user subsequently confirmed the three-host Node Debugger build succeeds.


## Phase 2 progress — API & Backend Testing

- Added `api_test_run` to the shared Agent Core and all three native host deployments.
- Added structured HTTP request suites, JSON/text bodies, response assertions, environment-variable secret placeholders, bounded previews, and same-origin redirect enforcement.
- Added local HTTP integration coverage; Windows build/runtime validation remains pending.


## Phase 2 progress — Development Server Management

- Added the shared `development_server_manage` Agent tool and native process-bridge implementation for Node.js package-script discovery/start, multiple owned server sessions, bounded stdout/stderr logs, HTTP readiness/health checks, port-conflict detection, and explicit stop.
- Startup and stop require fresh approval. Startup approval displays the actual `package.json` script command. Termination is restricted to process trees launched and tracked by the current RunMind host.
- Portable lifecycle contract tests: 12/12 passed. Windows C# compilation and live process behavior remain pending.

## Project intelligence workflows

- Keep **Project Understanding** as the shared architecture/code intelligence layer built on the persistent AST/index and symbol graph.
- Maintain **Project Diagnostics** as a distinct, read-only-by-default operational readiness workflow for manifests, lockfiles, toolchains, scripts, build/test configuration, and approved live checks.
- Keep build/test execution and runtime debugging as separate actions that consume Project Understanding and the diagnostics results.
