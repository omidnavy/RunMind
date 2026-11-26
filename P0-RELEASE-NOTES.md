# RunMind 2.7.0 — P0 Foundation & Reliability

## Included

- One native-host-owned workspace per conversation, shared by Agent file operations, Git, approved PowerShell/native execution, Node debugging, API testing, Playwright and development-server management.
- A path-contained native file bridge with relative-path validation, workspace identity validation, reparse-point rejection, bounded chunk transfers, atomic writes and cleanup of interrupted writes.
- Cross-host workspace metadata locking and workspace-ID revalidation so Standalone, PowerToys Run and Command Palette observe root changes.
- Agent tool reliability improvements: schema-aware Playwright instructions, unchanged-failure retry blocking, bounded tool outcome ledger, completion audit and evidence-based final-report fallback.
- Persistent development-server ownership metadata and conservative reconciliation across host restarts. Adoption and stop require PID, process start time and executable identity verification. Unknown/stale processes are never terminated.
- Targeted TCP/UDP port-owner inspection and Agent guidance that prefers it over broad diagnostics for a port-conflict question.
- Separate CORS, CSP and mixed-content policy diagnostics. Expected events remain visible and are classified as expected only when an explicit rule matches; correlated transport failures are treated carefully.
- Removed the last non-JavaScript fallback candidates from the JS/TS import graph; other source files remain generic text only.
- Explicit JavaScript, TypeScript, JSX/TSX, HTML and CSS project-language scope. Other source files remain generic text only; no non-JavaScript parsers, dependency inventory, toolchain discovery, build/test runners or debuggers are exposed.

Requirement-by-requirement audit: `docs/P0-IMPLEMENTATION-AUDIT.md`. Remaining live target-machine checks: `docs/P0-WINDOWS-ACCEPTANCE.md`.

## Validation performed

- Node.js v22.16.0: **156/156 portable tests passed** (`node --test --test-concurrency=1`).
- **40/40** JavaScript/CJS/MJS files outside dependency directories passed `node --check`.
- **4** repository JSON files parsed successfully.
- **6** project/property/XAML/AppxManifest XML files parsed successfully.
- Standalone, PowerToys Run and Command Palette package versions, plus Command Palette Appx manifest version, all agree on **2.7.0**.

## Target Windows validation still required

This environment does not have the .NET SDK or PowerShell, so the following have not been executed here:

- .NET 10 solution compilation for Release x64.
- Live Windows workspace-picker, shared-file-bridge and cross-host refresh tests.
- Development-server re-adoption and stop tests across separate host processes.
- Windows Chromium/Edge Playwright policy-event classification.
- Final acceptance in Standalone, PowerToys Run and Command Palette.

On the target Windows machine, run `Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64`, then complete `docs/P0-WINDOWS-ACCEPTANCE.md`. Do not mark P0 target-machine certified until those checks pass.

## Scope decisions

Project-specific analysis and tooling remains focused on JavaScript, TypeScript, JSX/TSX, HTML and CSS. Other language parsers, debuggers, build systems and test runners are intentionally out of scope.
