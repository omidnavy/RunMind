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

## Known limits to keep visible

- Current symbol/import extraction is regex-based. The local import graph is best-effort for relative JavaScript/TypeScript/Python imports; it is not an AST, compiler, or complete cross-language reference graph.
- Static project validation does not execute builds, tests, linters, Playwright, or audits.
- Native PowerShell/.exe execution runs as the current Windows user and is not sandboxed.
- Packaged Windows application activation and default-handler opens do not prove a visible window, focus, or completed navigation.
- The built-in RunMind adapter can pass a startup prompt but cannot operate on the WebView conversation database or control an existing conversation session.
- Windows compilation and live integration across all three hosts must be completed on a Windows machine.
