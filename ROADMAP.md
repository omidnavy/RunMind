# RunMind Roadmap

## Current source baseline: 2.7.0 — P0 Foundation & Reliability

## Active milestone: P0 — Reliability and workspace architecture

P0 source implementation is included in this bundle. The portable Node test suite passes 156/156 tests with Node.js 22.16.0. The three-host Windows Release x64 build and live acceptance must still be run on the target machine using Node.js 24+ and .NET 10; this source snapshot is not described as Windows-certified until that succeeds.

The full source-scope audit is recorded in `docs/P0-IMPLEMENTATION-AUDIT.md`; the live Windows gate is recorded in `docs/P0-WINDOWS-ACCEPTANCE.md`.

### P0 scope implemented in source

- **Unified Workspace Selection:** one native-host-owned, per-conversation canonical root is shared by Agent file operations, Git and native project tools. The WebView uses a bounded native file RPC; workspace IDs invalidate stale handles; persisted state is revalidated across host processes; a second folder picker is not part of the native-host workflow. Browser-only contexts retain a browser directory-picker fallback.
- **Agent tool reliability:** tool outcomes are recorded in a bounded execution ledger; unchanged malformed tool calls are not repeated; Playwright action schemas describe required selector/value fields; validation identifies the exact action and correction hint; empty model output, generation failure, repeated tool failure, and tool-step limits after tool execution produce an evidence-based deterministic final report.
- **Development-server ownership:** session records persist process ID, executable path, process start identity, script, working directory, port, readiness URL and state. Start, reconcile, metadata updates and stop use cross-process locks. Only an exact PID/start-time/executable identity match can be re-adopted or stopped. Stale/unknown records are reported, never killed. No configured HTTP readiness URL means readiness is `not_configured`, not `ready`.
- **Targeted port investigation:** System Manager provides TCP/UDP port-owner lookup under the existing process or network-diagnostics capability. The Agent is instructed to use it for a specific port instead of broad system inventory.
- **Playwright policy reporting:** CORS, CSP and mixed-content blocks are distinct policy events. Explicitly expected events remain visible but are not counted as unexpected application console/network failures when safely correlated. Unexpected and missing expected events remain failures.
- **Project-language scope:** JavaScript, TypeScript, JSX/TSX, HTML and CSS only. No additional project-language parsers, semantic engines, debuggers or language-specific build/test workflows are planned in this milestone.

### P0 Windows acceptance still required

- Build all three hosts with `Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64`.
- Verify one folder selection is sufficient for Agent file access, Git, PowerShell/native execution, Node debugger, API runner, Playwright and development-server tools.
- Verify workspace changes propagate across Standalone, PowerToys Run and Command Palette and invalidate stale handles.
- Verify development-server reconciliation across host restart, exact identity re-adoption, stale PID handling, port conflict safety, and explicit stop.
- Verify empty/repeated model tool failures still yield a non-empty final report.
- Verify Task C in Playwright, including the fixture's submit/reset messages and expected CORS policy classification.

## Next milestone candidates

1. **Project Mission Control:** orchestrate Project Understanding, Project Diagnostics, approved build/test checks, browser/API checks and an evidence-backed readiness report.
2. **Test Impact Analysis:** use the existing JS/TS symbol graph and dependency edges to select likely affected tests and modules.
3. **Development Session Timeline:** correlate server sessions, PIDs, browser requests, API tests, debugger events and captured logs without introducing duplicate event stores.
4. **System Manager diagnostics:** Windows Event Log/crash investigation, bounded resource sampling, Scheduled Task inventory, storage health, security posture and targeted DNS/TCP probes.
5. **Podman Development Workbench:** Compose/Containerfile inspection, build logs, container-health and service/port/volume/network relationships.
6. **Database Development Workbench:** start with PostgreSQL and SQLite; schema inspection, read-only query execution, query plans and migration validation.
7. **API contract testing:** OpenAPI/JSON Schema validation, reusable sequential suites and explicit auth profiles.
8. **Agent/MCP reliability:** better task checkpoints, resource discovery and structured recovery.

These are candidates, not claims of existing implementation. Keep the current codebase compact: reuse the shared Agent Core, native bridge, project index, approval system and existing runners. Avoid adding a new engine when an existing subsystem can be extended.

### Compatibility and security principles

- One shared Agent Core across Standalone, PowerToys Run and PowerToys Command Palette.
- One native-owned workspace root per conversation; no absolute path guessing from directory names.
- Native operations are bounded and freshly approved where they change application or system state.
- Never terminate an unknown process because it occupies a port.
- Never claim HTTP readiness without an actual configured probe.
- Browser policy events are not silently discarded; expected and unexpected blocks remain distinguishable.
- System Manager remains optional and disabled by default.
- Native process execution runs as the current Windows user and is not a sandbox.
- Project language support remains JS/TS/JSX/TSX/HTML/CSS until a future explicit scope decision.


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
