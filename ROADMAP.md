# RunMind Roadmap

## Current source baseline: 2.8.1 — P1 Mission Control refinement

## Completed milestone: P0 — Foundation & Reliability

P0 source implementation is retained in this release. The P0 target-Windows certification gate remains separate: build all three hosts with `Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64` and complete `docs/P0-WINDOWS-ACCEPTANCE.md`. This environment cannot certify Windows/.NET runtime behavior.

## Active milestone: P1 — Project Intelligence

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
- Project-specific language-aware analysis and tooling remain limited to JavaScript, TypeScript, JSX/TSX, HTML and CSS. Other source files remain generic text.
- No build, test, package install, dependency audit, Git mutation, tag, signing or publication is triggered by these P1 tools.
- The P0 Windows certification checklist remains open until run on the target Windows machine.

## Next milestone candidates

1. **Development Session Timeline:** correlate owned server sessions, Node debugger events, API tests, Playwright requests and logs without adding duplicate event stores.
2. **System Manager diagnostics:** Windows Event Log/crash investigation, bounded resource sampling, Scheduled Task inventory, storage health, security posture and targeted DNS/TCP probes.
3. **Podman Development Workbench:** Compose/Containerfile inspection, build logs, container-health and service/port/volume/network relationships.
4. **Database Development Workbench:** start with PostgreSQL and SQLite; schema inspection, read-only query execution, query plans and migration validation.
5. **Advanced Agent/MCP reliability:** stronger checkpoints, resource discovery and structured recovery.

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
