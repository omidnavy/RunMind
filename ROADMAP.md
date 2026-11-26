# RunMind Roadmap

## Current source baseline: 2.7.0 — P0 Reliability & Unified Workspace Foundation

### P0 status

P0 implementation is present in the 2.7.0 source candidate. The portable Node suite and source-level contract tests pass; the three-host Windows/.NET build and manual acceptance scenarios remain a required user-side release gate.

- **Unified Workspace Selection:** native host owns one canonical, persisted workspace root per conversation. Agent file operations use the native file bridge; Git and native execution use the same identity. No browser folder-name path inference is used. The native picker is shown once for first selection and only again for an explicit change or a legacy conversation without native workspace state.
- **Phase 2 acceptance:** Node debugger/runtime profiling have prior user-confirmed Windows validation. Playwright, API and development-server workflows are implemented; run the full validation script and acceptance guide against 2.7.0 before sign-off.
- **Agent reliability:** action schema and native preflight validation, targeted tool selection guidance, identical-call retry protection, task completion audit and deterministic non-empty fallback are implemented.
- **Development-server ownership:** durable server records are scoped to conversation and workspace selection identity. PID, process start time and executable path must match before recovery. Historical logs cannot be reattached after host restart and are reported unavailable.
- **Playwright CORS classification:** raw browser policy events are retained; expected and undeclared CORS blocks are distinct from application/JavaScript errors and unexpected transport failures. User-declared expected CORS origins are classified as expected policy blocks.

### Supported project-language scope

Language-aware developer functionality is intentionally limited to **JavaScript, TypeScript, JSX/TSX, HTML and CSS**. Node package managers are npm, pnpm, Yarn and Bun. Other file types can still be read/searched as generic text or configuration, but no additional language parsers, compiler integrations, debuggers, build/test ecosystems or language-specific package managers are planned for the current scope.

### P0 validation gate

Run `Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64` using Node.js 24 and .NET 10. Then complete `docs/P0-FOUNDATION-ACCEPTANCE.md` across Standalone, PowerToys Run and Command Palette. Do not mark P0 Windows-certified until all three hosts build and the manual workspace, server recovery, tool selection/final response and CORS checks pass.

## Next roadmap after P0

### P1 — Project Mission Control and change impact

- Project onboarding/readiness workflow coordinating Project Understanding, Project Diagnostics and existing build/test tools.
- Test Impact Analysis based on the existing JavaScript/TypeScript symbol and dependency graph.
- Release Readiness report combining tests, browser/API checks, dependency audit, Git status and build artifacts.

### P2 — System Manager intelligence

- Windows Event Log reader and correlated crash investigation.
- Bounded CPU/memory/disk/network sampling.
- Scheduled Task inspection and controlled reversible operations.
- Storage health and targeted DNS/TCP/proxy diagnostics.
- Windows security posture inventory (Defender, Firewall, BitLocker, Secure Boot, TPM and UAC), read-only first.

### P3 — Developer infrastructure

- Podman-first development workflows: Compose discovery, Containerfile inspection/build diagnostics, service/port/network/volume relationships.
- PostgreSQL and SQLite tooling, beginning with schema inspection, read-only SQL and migration review.
- OpenAPI/JSON Schema contract tests and richer API test suites.
- Log/session timeline correlation across development servers, Node debugger, API calls and Playwright.

### P4 — Agent workflow quality

- Better MCP resource discovery and health reporting.
- More robust multi-tool orchestration, checkpoints and recovery.
- Test Impact Analysis and release reports integrated into agent task completion.

No new project-language support is planned in these milestones unless the user explicitly changes the scope. The focus is to deepen the JavaScript/TypeScript/JSX/HTML/CSS toolchain and add cross-cutting capabilities.

### Architecture principles

- One shared Agent Core across Standalone, PowerToys Run and Command Palette.
- One native-host-owned workspace identity per conversation; no second folder picker for the same selection.
- No duplicate project indexes, permission engines, Skills engines, Git adapters or process managers.
- Native process execution is bounded, user-approved and runs with the current Windows user's permissions; workspace path containment is not a process sandbox.
- Unknown processes and port owners are never terminated. Development-server recovery is identity-verified and approval-gated.
- Read-only diagnostics first; mutations require explicit approval and verifiable outcomes.
- Keep implementations proportionate: every abstraction must remove real duplication or satisfy a concrete security/runtime requirement.

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
