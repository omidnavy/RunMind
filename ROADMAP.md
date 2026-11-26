# RunMind Roadmap

## Current source baseline: 2.6.3 — Developer Intelligence & System Manager

## Active milestone: Phase 2 — Debugging & Quality

The 2.6.3 baseline includes the Phase 1 Developer Workbench and System Manager capabilities. The Node Debugger increment has been built successfully by the user on Windows across all three hosts. Runtime Diagnostics & Profiling is the active increment and is awaiting the next three-host Windows build/runtime validation.

### Milestone scope and implementation direction

- Extend the existing Node Inspector sessions with V8 heap diagnostics, CPU sampling profiles, streamed heap snapshots, and bounded timestamped memory sampling.
- Keep profiling tools in the shared Agent Core across Standalone, PowerToys Run, and PowerToys Command Palette; do not introduce a second agent or duplicate session manager.
- Require fresh approval for profiling starts and heap snapshots, bound all retained samples/artifacts, and keep runtime profiling unavailable to Deep Research and delegated sub-agent tasks.
- Complete the remaining Phase 2 increments in sequence: Playwright browser integration, dedicated API/backend testing, and development-server lifecycle management.
- Preserve the existing Phase 1 Developer Workbench, Git, Skills, Profiles, MCP, workflows, and System Manager capabilities without broad rewrites.

### Included in this source bundle

- Existing project indexing, workspace tools, Git, native PowerShell/.exe execution, MCP, Deep Research, workflows, task recovery, Project Profiles and Skills Manager are preserved.
- System Manager remains optional and disabled by default. Native master/group/capability checks remain authoritative. Startup Run/RunOnce and Startup-folder changes require fresh native confirmation and preserve a recovery command/file copy.
- Application discovery uses a bounded local index with refresh on stale data or search miss. Sources include App Paths, Windows uninstall registration, Start Menu shortcuts, bounded common install locations, PATH and the Windows Apps folder. Matching ranks normalized names, aliases, executable names, prefixes, substrings and tokens.
- Packaged Windows applications are represented by validated AppUserModelIDs and activated through `shell:AppsFolder`. This confirms only that Windows accepted an activation request; it does not verify a visible window or action completion.
- Launch requests use a stable application ID from current discovery. Executable paths are revalidated natively. Existing desktop windows are reused and brought forward by default; foreground activation is reported as verified only when Windows reports the target window as foreground.
- If an existing process cannot be activated, RunMind does not silently create a duplicate. The user can request a new process explicitly.
- New-window requests are supported only for a bounded allowlist of applications with known command-line support. New-process behavior is best-effort because some applications redirect to an existing instance.
- Per-application launch behavior preferences (`ask`, `reuse`, `new-window`, `new-process`) are stored in the existing System Manager settings file. Changing a preference always requires native confirmation.
- Application close uses graceful main-window close requests and mandatory native confirmation. It never force-terminates a process; packaged applications are not force-closed. Startup inventory includes Run/RunOnce and Startup folders; reversible enable/disable applies to exact registry values and inventoried `.lnk`/`.exe` Startup-folder files, with recovery copies.
- Declarative application adapters use the same stable application-ID launch path. The System Manager flyout now lists adapters and supports custom manifest installation/removal with native confirmation. The built-in RunMind adapter can start RunMind with a supplied prompt through its existing startup argument. No scripts or shell commands are introduced.
- System Manager policy is appended to the existing leading Agent system message instead of adding a second system message, improving compatibility with stricter model chat templates.
- Native launch results distinguish verified process startup, verified foreground activation, new-window request, existing-instance handoff and unverified navigation. Tool-layer results consistently expose success, accepted, completed, and verified dimensions.
- `list_available_tools` reports the exact tool definitions exposed in the current Agent session. `list_agent_skills` and `load_agent_skill` work without requiring a project folder; project-scoped skills still require an authorized workspace.

### Explicit limitations

- Public API rename is limited to direct named exports and statically resolved import/re-export sites (20-file cap); default exports, export-star chains, dynamic imports and unresolved references are not changed.
- The current source bundle does not contain the Bionic bundled-skill payload. The skill inventory therefore reports installed Global and authorized Project skills only; it does not invent a Built-in/Bionic scope.
- Packaged-app activation does not currently pass arbitrary command-line arguments and cannot verify visible windows, focus, or URL navigation.
- New-window support is limited to applications with known command-line flags. Other applications can use reuse or new-process behavior.
- Graceful close applies to accessible desktop main windows. It does not force-close background-only or packaged applications.
- Windows foreground activation is subject to Windows security restrictions; RunMind never uses focus-stealing workarounds.
- The TypeScript compiler pass is bounded to 500 indexed source files/12 MB, uses no default library bundle, and does not run full semantic diagnostics. Actual Node resolution requires Node on PATH and the native workspace to match the selected project folder. Playwright remains a project-installed CLI workflow, not an embedded browser runtime. API smoke testing remains workflow-guided through approved PowerShell.

### Next candidates

- Complete Windows build/runtime validation for Runtime Diagnostics & Profiling across all three hosts.
- Phase 2 Playwright browser integration, followed by API/backend testing and development-server lifecycle management.
- Windows-targeted automated integration tests for discovery, launch, activation, close, and all three hosts.
- Better app-specific adapters where a real user workflow justifies them.
- Full Skill repository/archive import with referenced resources and update checks, if needed.
- Dedicated Playwright runtime integration and richer browser diagnostics, if justified by actual usage.

### Compatibility and security principles

- One shared Agent Core across all three hosts.
- No duplicated conversation, project-index, Skills, workflow, or permission engines.
- Native operations are explicit, bounded, and approval-gated where they change application state.
- System Manager is optional and disabled by default.
- No shell execution is hidden behind application discovery or ordinary read-only diagnostics. Fixed diagnostic utilities use fixed arguments; no user-provided command strings are accepted by these diagnostic paths.
- No arbitrary scripts, forced focus, GUI automation, or automatic administrator elevation. Registry changes are limited to the explicit, reversible startup Run-entry tool and always require native confirmation. Windows service start/stop is available only through the explicit service capability and mandatory confirmation.


## 2.6.2 — System Management Skills & Capability Contracts

- Bundled on-demand system-management, container-management, and runmind-management skills.
- Shared Agent policy describes safe discovery, operation selection, permission boundaries, and verification.
- Adapter inventory reports descriptive operation contracts; native enforcement remains authoritative.


## 2.6.3 — Developer Intelligence & System Manager

Implemented in this source snapshot: Windows Notepad adapter, File Explorer folder/file actions, Docker/Podman container lifecycle/read operations, and four coding skills. The adapter editor/override UI and native conversation-session management remain follow-up work; RunMind conversations currently live in WebView IndexedDB and are not exposed as a native session store.


## Phase 2 progress — 2026-11-21

- Node Debugger: implemented and confirmed by the user to build successfully across all three Windows hosts.
- Runtime Diagnostics & Profiling: added V8 heap diagnostics, CPU profile export, streamed heap snapshots, and bounded timestamped memory sampling. Node Inspector integration tests pass; Windows compilation/runtime validation is pending.

## Phase 1 completion candidate — 2026-11-21

Implemented in the source increment: JavaScript AST rename/import organization/conservative extract-function previews; cross-file reference edges for resolved static imports; reversible HKCU/HKLM Run startup toggles; hardware/driver and TCP/UDP listener diagnostics; normalized System Manager outcomes; real local Git workflow tests; and a Windows validation script for all three projects. The user subsequently confirmed the three-host Node Debugger build succeeds.
