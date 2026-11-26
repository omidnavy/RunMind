# Phase 1 + System Manager — integrated validation scenarios

These scenarios validate complete model-driven workflows against real repositories and Windows applications. They are not a substitute for native unit tests; use them after installing the same build in each supported host.

## 1. Repository onboarding and architecture

1. Select a representative repository with multiple source folders, manifests, tests, and a README.
2. Ask RunMind to explain its architecture, entry points, major modules, dependency boundaries, and test strategy.
3. Ask it to identify the package manager and lockfile, list declared build/test/lint scripts, and identify the most-imported local files.
4. Ask it to trace one feature from entry point to implementation and tests.

Acceptance: findings cite actual paths and source evidence; package-manager/script suggestions match the repository; partial index limits are disclosed; no build or tests are claimed as run.

## 2. Controlled feature implementation

1. Ask for a small feature spanning at least two files.
2. Require a concise plan and inspection of existing conventions before edits.
3. Approve only the proposed file writes.
4. Ask RunMind to review the complete diff and identify regression risks.
5. Approve the exact documented test/build command only after RunMind states the command, working directory, expected side effects, and timeout.

Acceptance: unrelated files remain unchanged; no dependencies are installed automatically; writes and native execution use their respective approval paths; reported command output and exit codes match the actual results.

## 3. Bug investigation and regression fix

1. Choose a real defect with a reproducible failing test or clear reproduction steps.
2. Ask RunMind to trace the failing path and establish a root cause before changing code.
3. Approve a minimal fix and any test changes.
4. Run the targeted test, then the relevant broader suite with fresh approval.

Acceptance: the explanation is supported by source/test evidence; the fix addresses the root cause; failures are not hidden; the final report distinguishes static inspection from executed checks.

## 4. Dependency and release review

1. Ask for a dependency inventory and security audit plan.
2. Check whether the detected package manager, lockfiles, and declared scripts agree.
3. Ask for release readiness: version metadata, changelog, build scripts, tests, and Git status.
4. Approve any network-backed audit command separately.

Acceptance: no package manager is guessed when signals conflict; audit findings are identified as live or static; no dependency updates, tags, commits, or publication happen without explicit instruction.

## 5. System Manager application and adapter lifecycle

1. Search for a known application and inspect the ranked candidates before launch.
2. Launch the selected candidate and verify whether RunMind reports process startup, foreground activation, or unverified handoff accurately.
3. Edit a built-in adapter override, save it, verify the override status, then reset it to shipped defaults.
4. Install a simple custom declarative adapter using only a supported operation and argument templates; execute it against a harmless test target; remove it.

Acceptance: stable application IDs are used; ambiguity is surfaced; native confirmation remains authoritative; built-ins are not overwritten; custom adapters cannot execute scripts or arbitrary shell commands.

## 6. Process and container lifecycle

1. List processes with System Manager process capabilities enabled; inspect the identity fields for a harmless test process.
2. If testing termination, confirm the exact PID/name/start timestamp/path and approve the native confirmation. Never use a critical or unsaved-work process.
3. With a disposable container, list and inspect it, then separately approve start/stop/restart actions and verify state with inspect/list.
4. Test a deliberately slow or unavailable runtime command and confirm timeout/error reporting is bounded and truthful.

Acceptance: PID reuse or identity mismatch blocks termination; protected Windows processes remain blocked; state-changing container operations require fresh confirmation; timeout cancels the process tree; stdout/stderr truncation is reported.

## 7. Cross-host parity

Repeat the same repository onboarding, one approved file edit, one native test command, application discovery, and adapter inventory in:

- RunMind Standalone
- PowerToys Run plugin
- PowerToys Command Palette extension

Acceptance: the shared Agent tools, project index behavior, permissions, adapters, and results are materially consistent. Host-specific packaging/launch differences are documented rather than silently treated as parity.

## 8. JavaScript semantic analysis and refactoring

1. Build the project index for a representative `.js/.mjs/.cjs` repository.
2. Inspect declarations, lexical references, read/write classification, call sites, module edges, cycles, and cross-file references from resolved imports.
3. Request a symbol rename using the exact declaration offset from `project_symbol_graph`; inspect the preview and approve application through the normal edit/backup flow.
4. Request import organization and verify comment-pinned groups, blank-line groups, and side-effect-only imports are preserved.
5. Request extract-function using exact statement offsets; verify unsupported control-flow and binding-sensitive selections are rejected.

Acceptance: every proposed file change is previewed and applied through approved file editing with recovery backup; malformed code, ambiguous bindings, collisions, and unsupported extraction semantics are rejected.

## 9. Startup management and diagnostics

1. Enable System Manager, Application Management, discovery and launch capabilities.
2. List startup entries and select a disposable HKCU Run test entry.
3. Disable it with fresh native confirmation; verify the value is absent and the recovery entry exists.
4. Re-enable it with fresh native confirmation; verify the exact original command is restored.
5. Enable System Information and Network Information; inspect hardware/driver inventory, TCP/UDP listener ownership, addresses, gateways, and DNS.

Acceptance: RunOnce and Startup-folder entries remain inventory-only; HKLM permission failures are reported without escalation; startup commands are preserved for recovery; diagnostics remain read-only and bounded.

## Known limits to keep visible

- Acorn semantic analysis and AST refactoring currently support `.js`, `.mjs`, and `.cjs`. JSX/TS/TSX and other languages remain heuristic; full TypeScript/compiler semantics are not included.
- Cross-file references are derived from statically resolved import bindings. Dynamic imports, unresolved modules, custom loaders, full Node conditional resolution, and all package-manager install layouts are not modeled.
- Extract-function is intentionally conservative: it accepts complete consecutive statements in a Program/block and rejects declarations, return/break/continue, await/yield, `this`, `arguments`, direct eval, and other control-flow-sensitive constructs.
- Rename is currently single-file; it does not automatically rename public APIs and every importer in the workspace.
- Static project validation does not execute builds, tests, linters, Playwright, or audits. Approved native commands are separate operations.
- Native PowerShell/.exe execution runs as the current Windows user and is not sandboxed.
- Packaged Windows application activation and default-handler opens do not prove a visible window, focus, or completed navigation.
- The built-in RunMind adapter can pass a startup prompt but cannot operate on the WebView conversation database or control an existing conversation session.
- Windows compilation and live integration across all three hosts must be completed on a Windows machine.


## Remaining Phase 1 release gates (updated)

The current unreleased increment adds preview-first AST rename/import organization/conservative extract-function, static cross-file import reference mapping, reversible HKCU/HKLM Run startup toggles, hardware/driver and TCP/UDP listener diagnostics, normalized System Manager outcomes, and a real local Git repository integration test. The automated tests validate the JS refactor logic and native source contracts. They do not substitute for Windows execution.

Before declaring Phase 1 complete, build and run Standalone, PowerToys Run, and Command Palette on Windows; exercise registry startup changes with a disposable test entry; verify hardware/driver/network diagnostics; test service/process/container operations; and run the same agent-driven repository workflow through all three hosts.
