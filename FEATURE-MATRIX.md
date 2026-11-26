# RunMind 2.6.3 — Feature Matrix (Phase 1 + System Manager hardening work in progress)

| Area | Capability | Status in current 2.6.3 working bundle | Notes |
|---|---|---|---|
| Hosts | Standalone | Preserved | Shared Agent Core |
| Hosts | PowerToys Run | Preserved | Shared Agent Core |
| Hosts | PowerToys Command Palette | Preserved | Shared Agent Core |
| Developer | Project index and search | Implemented with limits | Persistent bounded local index; Acorn AST parsing, lexical reference resolution, read/write classification, syntactic call sites, relative and workspace package export/import-alias resolution, re-export matching, external dependency identification, bounded circular dependency detection; `project_symbol_graph` tool; TypeScript/TSX/JSX and other languages retain heuristic fallback | Call sites include local declaration links where statically available; this is not compiler-grade analysis or complete Node module resolution.
| Developer | Coordinated multi-file editing | Added in integration pass | 2–20 files, one approval, snapshot checks, per-file backups, best-effort rollback; sequential writes are not atomic |
| Developer | Static project validation | Implemented with limits | JSON/XML and selected manifest checks; test discovery; package manager/lockfile detection; no build/test execution |
| Developer | Approved PowerShell/native execution | Implemented | Existing 2.4.0 bridge |
| Developer | Build/test workflows | Implemented with limits | Workflow-guided approved PowerShell/native execution; commands are selected from project evidence and require fresh approval |
| Developer | Static analysis/lint workflow | Implemented | Discovers configured tools; read-only checks first |
| Developer | Playwright test-runner workflow | Implemented | Uses project-installed Playwright CLI; no embedded browser runtime |
| Developer | API smoke-test workflow | Implemented | Uses approved PowerShell; no dedicated API client UI |
| Developer | Dependency audit workflow | Implemented | Ecosystem-specific commands require approval |
| Skills | Local folder install | Implemented | Existing installer retained |
| Skills | Direct HTTPS SKILL.md URL | Implemented | Preview/confirmation; SKILL.md only |
| Skills | GitHub/GitLab repository-folder URL | Implemented | Resolves tree/blob URLs to raw SKILL.md |
| Skills | Pasted SKILL.md auto-detection | Implemented | Preview/confirmation before install |
| Skills | Global/project content-only install | Implemented | Existing scope selector |
| Skills | Repository/archive full resource import | Not included | Follow-up candidate |
| System Manager | Optional master switch | Implemented | Disabled by default; shared native settings |
| System Manager | Group and individual capability switches | Implemented | Applications, processes, diagnostics |
| System Manager | Permission profiles | Implemented | Restricted/Balanced/Custom; per-action launch/open permission |
| System Manager | Startup application inventory | Added | Read-only per-user/machine Run/RunOnce registry and Startup-folder inventory; no enable/disable operation yet |
| System Manager | Application discovery | Implemented | Ranked name/alias/executable matching across App Paths, uninstall registry, Start Menu, bounded common install locations, PATH and Windows Apps folder; bounded refresh on stale cache/search miss |
| System Manager | Application launch | Implemented | Uses stable application IDs; revalidates executable; reuses/activates existing windows by default; distinguishes startup, activation and unverified handoff |
| System Manager | Open files/folders/HTTPS URLs | Implemented | Windows default-handler request accepted; target application/window/focus/navigation are not verified |
| System Manager | Process inspection | Expanded | Parent PID, working/private memory, cumulative CPU time, threads, handles, priority, responsiveness, executable path and start time; detail fields individually gated |
| System Manager | Graceful process close | Added | Identity-checked main-window close request; mandatory confirmation; never force-kills |
| System Manager | Process termination | Implemented | Disabled by default; mandatory fresh confirmation and identity revalidation |
| System Manager | OS/environment/service/network diagnostics | Expanded | Uptime, drive capacity, gateways and DNS added; individual switches; read-only |
| System Manager | Windows service control | Expanded | List/inspect/config/dependents; start/stop/restart with fresh confirmation and final-state verification; requires serviceInformation capability |
| System Manager | Native command timeout | User-configurable | 0 disables automatic timeout; 1–3600 seconds; cancellation remains available |
| System Manager | Application lifecycle | Implemented with limits | Reuse/activate, new-window allowlist, new-process, per-app preferences and graceful close; no force-kill |
| System Manager | Packaged Windows apps | Implemented with limits | AppsFolder/AUMID activation; no generic arguments or visible-window verification |
| System Manager | Application adapters | Implemented | Built-ins plus user JSON manifests with argument templates; no scripts/shell |
| System Manager | RunMind conversation adapter | Added in integration pass | Send prompt, open validated `.runmind` session in Standalone, or start a new conversation window; no attach to already-running WebView sessions |
| Agent | Current tool inventory | Implemented | Lists exact active Agent/MCP/host/workspace tool definitions |
| Skills | Global discovery/loading | Implemented | Works without an authorized project folder |
| System Manager | GUI automation/admin/system changes | Not included | Explicitly out of scope |
| Security | Skill scripts auto-execution | Not allowed | Never executed by installer |
| Security | Native process execution | Approval required | Runs as current Windows user; not sandboxed |


## 2.6.1 changes

| Area | Capability | Status in current 2.6.3 working bundle | Notes |
|---|---|---|---|
| System Manager | Adapter inventory and custom manifest management | Implemented | Dedicated UI; native confirmation for install/removal; built-ins protected |
| System Manager | First-party RunMind adapter | Extended | Sends prompts, opens a validated `.runmind` session, or starts a fresh Standalone conversation |
| Agent | Single leading system message | Implemented | System Manager policy merged into first message for model-template compatibility |
| Agent | Structured application action outcomes | Implemented | Process startup, activation, handoff, window and navigation reported separately |

| System Manager | Flexible application discovery | Implemented | Ranked name/alias/executable/token matching; bounded registry, App Paths, Start Menu, bounded common install locations, PATH and Windows Apps folder sources |
| System Manager | Packaged Windows application discovery | Implemented | Uses Shell AppsFolder/AUMID discovery; activation request is not presented as verified UI launch |
| System Manager | Stable application selection | Implemented | Launch and preferences use a catalog application ID, validated natively |
| System Manager | Reuse and activate | Implemented | Existing visible windows are restored/foregrounded; success requires foreground-window verification |
| System Manager | New window | Implemented with limits | Known command-line support allowlist only; no generic promise for every application |
| System Manager | New process | Implemented with limits | Application may still hand off to an existing instance; handoff remains unverified |
| System Manager | Per-application launch behavior | Implemented | Persistent ask/reuse/new-window/new-process preference; native confirmation required to change |
| System Manager | Graceful application close | Implemented | Sends normal window-close requests; mandatory confirmation; never force-kills; packaged apps excluded |
| Agent | Available tool inventory | Implemented | Reports the exact active tool definitions for the current session |
| Skills | Global skill discovery/loading | Implemented | No project folder required; metadata and scope are reported |
| Skills | Project skill discovery/loading | Preserved | Requires an authorized project folder |
| Skills | Bundled/Bionic skill catalog | Not included | No bundled Bionic skill payload is present in this source snapshot |

## 2.5.1 changes

- System Manager configuration is exposed through its own toolbar flyout.
- Application discovery resolves Start Menu shortcut targets and filters missing executable paths.
- Launch success checks whether a matching process remains active.


## 2.5.2 changes

| Area | Capability | Status in 2.5.2 | Notes |
|---|---|---|---|
| System Manager | Fresh action per user request | Implemented | Runtime instructions prohibit reusing earlier tool results as completion of a new launch request. |
| System Manager | Installed application discovery | Implemented | Includes App Paths, uninstall registry entries, Start Menu shortcuts and PATH; search query is mandatory. |
| System Manager | Launch verification | Implemented | New process startup and unverified existing-instance handoff are distinct outcomes. A running process does not prove visible focus. |
| System Manager | Default-handler open | Implemented | Reports Windows request acceptance without claiming application/window/navigation verification. |


| Capability | 2.6.2 |
|---|---|
| Bundled on-demand System Management skills | Yes |
| Adapter operation metadata (arguments, state-change, confirmation, result fields) | Yes |
| Native permission enforcement independent of skill instructions | Yes |


## 2.6.3 additions

| Capability | 2.6.3 |
|---|---|
| Windows Notepad adapter | Implemented |
| File Explorer open-folder/select-file adapter | Implemented |
| Docker/Podman list, inspect, start, stop, restart, logs, stats, images, volumes, networks, health | Expanded (native CLI; Windows runtime test required) |
| Coding/debugging/testing-and-quality/code-review skills | Implemented |
| File-history and skill lifecycle skills (skill-management/create-skill/install-skill) | Implemented |
| Dedicated adapters tab and bounded inner scrolling | Implemented |
| Built-in adapter edit overrides/reset | Implemented |
| RunMind open/create/export existing conversation native adapter | Not implemented; conversations remain in WebView IndexedDB |


## Phase 1 + System Manager milestone — implementation notes

| Area | Capability | Status | Notes |
|---|---|---|---|
| Developer | Package-manager and lockfile detection | Implemented in project index | Detects npm, pnpm, Yarn, Bun, uv, Poetry, Pipenv, Cargo, Go, Composer and Bundler lockfiles; Node packageManager field takes precedence when present |
| Developer | Declared script command suggestions | Implemented in static validation | Suggestions are derived only from detected Node package manager and scripts declared in package.json; never executed by validation |
| Developer | Local import relationships | Static/partial | JavaScript AST metadata resolves relative imports, selected workspace package exports/import aliases, re-exports, cycles, and cross-file references for statically resolved imports; TypeScript/JSX/Python remain heuristic; runtime conditional resolution is incomplete |
| System Manager | Container command output and timeout | Hardened | Asynchronous process wait, user-configurable timeout (0 = no automatic timeout), cancellation, process-tree termination, bounded stdout/stderr |
| System Manager | Native confirmation and permissions | Preserved | System Manager remains disabled by default; destructive process/container operations still require native confirmation |

### Additional System Manager integration pass

- The previous fixed 30-second container timeout is removed. `commandTimeoutSeconds` is stored in System Manager settings; 0 means no automatic timeout, and 1–3600 sets the requested limit. Cancellation remains available.
- Added a Windows service tool for listing, inspecting, starting and stopping services. Start/stop always require a fresh native confirmation; successful command acceptance is not represented as verified final service state.

### Explicit Phase 1 gaps

- Acorn provides syntax-tree parsing for JavaScript (.js/.mjs/.cjs), including declaration and identifier-occurrence extraction. TypeScript, JSX, and other languages still use heuristic extraction. Cross-file use sites are mapped for statically resolved import bindings; complete compiler-grade binding/reference resolution and dynamic runtime resolution are not included.
- Runtime build, test, lint, Playwright and dependency-audit commands are not executed by static validation. They require an explicitly selected native workspace and fresh approval.
- Native process execution runs as the current Windows user and is not a security sandbox.
- Multi-host Windows compilation and end-to-end execution must be validated on Windows; this environment cannot establish those results.
- RunMind's built-in adapter can submit a startup prompt, but does not manage an already-running conversation/session store.


## Phase 1 completion increment (Unreleased)

| Capability | Status | Scope / limitation |
|---|---|---|
| JavaScript AST refactoring | Implemented | Preview-first rename/import organization and conservative statement extraction; one file at a time; applies through approved edit with backup; cross-file API rename is not automatic |
| JavaScript import organization | Implemented | Sorts independent contiguous top-level import groups; preserves comment-pinned groups |
| Startup entry management | Implemented | Enable/disable exact HKCU/HKLM Run values with recovery; RunOnce and Startup-folder entries are inventory-only |
| Hardware and driver diagnostics | Implemented | Fixed PowerShell CIM query and bounded `driverquery` inventory; requires Windows runtime validation |
| TCP listener ownership | Implemented | `netstat -ano` listener mapping to PID/process name where accessible |
| System Manager result contract | Implemented | Tool-layer normalized success/accepted/completed/verified fields |
| Git integration test | Implemented | Real local temporary-repository smoke test; native bridge still needs Windows integration validation |
| Multi-file recovery tests | Implemented | Source-level ordering/rollback contract tests; browser filesystem failure injection remains outstanding |

| Cross-file symbol references | Implemented (static) | Maps resolved import bindings to lexical use sites in importing modules; dynamic/unresolved references remain outside the graph |
