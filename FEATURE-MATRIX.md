# RunMind 3.1.0 — Feature Matrix (P0 foundation + P1 Project Intelligence + P2 System Manager Intelligence + P3 Developer Infrastructure + P4 Agent Capabilities)

| Area | Capability | Status in current 3.1.0 source bundle | Notes |
|---|---|---|---|
| Hosts | Standalone | Preserved | Shared Agent Core |
| Hosts | PowerToys Run | Preserved | Shared Agent Core |
| Hosts | PowerToys Command Palette | Preserved | Shared Agent Core |
| Agent / MCP | MCP resource discovery | Implemented in P4 | Server metadata/capabilities; paginated tools/resources/resource templates/prompts; metadata search; 20-page/2,000-item bounds; repeated-cursor, error and truncation reporting; exact URI templates; resource/prompt/tool reads remain approval-gated |
| Agent orchestration | Multi-tool execution | Improved in P4 | Independent read-only allow-list, 1–4 bounded workers, duplicate-call serialization, side-effect/approval/MCP/delegation barriers, dependency layers and operation graph |
| Agent reliability | Task checkpoints and recovery | Implemented in P4 | Bounded/redacted per-chat task/plan/progress/operation records; interrupted outcomes unknown; explicit same-workspace resume; inspect before retry; no auto-replay |
| Agent context | Tool-result summarization | Implemented in P4 | Bounded structured summaries, secret redaction, depth/item/property limits and omission of raw body/HTML/binary payloads |
| Agent context | Skills and project context | Improved in P4 | Bounded project instructions/package/index context; relevance-ranked Skill metadata; Skills explicitly loaded and untrusted; never grant permissions |
| Developer | Change Impact Explorer | Implemented in P4 | Explicit JS/TS paths/symbols; direct/transitive import dependents; test candidates; HTML/CSS/SVG asset consumers; unknown/unanalysed paths |
| Agent observability | Development Session Timeline | Implemented in P4 | Chronological local checkpoints, tool operations/dependency groups and sub-agent evidence in Workflow UI and read-only Agent tool |
| Artifact integrity | Artifact Verification | Expanded in P4 | Structural checks, exact-byte SHA-256, optional trusted expected digest comparison, bounded receipt; digest is not provenance proof |
| Developer | Project index and search | Implemented with limits | Persistent incremental index; Acorn JS analysis; TypeScript compiler API for JS/JSX/TS/TSX semantic references; HTML/CSS project understanding; root tsconfig paths; static module graph; `project_symbol_graph` | Language-aware code analysis is intentionally scoped to JavaScript, TypeScript, JSX/TSX, HTML and CSS. SVG is indexed separately as a vector asset with local-reference analysis, not as a programming language. Compiler checker bounded to 500 source files and 12 MB; no default libs/full diagnostic pass; external installed packages use native Node resolver tool.
| Developer workflow | Project Understanding | Expanded | Uses the persistent index, symbol graph, AST/checker references and source tracing to map architecture and execution paths; does not execute commands or modify files |
| Developer workflow | Project Diagnostics | Added | Separate operational readiness workflow for manifests, lockfiles, toolchains, scripts, build/test configuration and optional approval-gated live checks; read-only by default |
| Developer | Project Understanding | Expanded in P1 | Module boundaries, entry-point candidates, dependency direction, high-coupling modules, cycles, unresolved imports and analysis coverage; source verification remains required |
| Developer | Project Diagnostics | Expanded in P1 | Static manifest/lockfile consistency, package scripts, tooling config, test discovery, parser errors and graph health; live probes/commands remain separately approved |
| Developer | Project Mission Control | Implemented in P1; refined in 2.8.1 | Versioned evidence report; stale-index refresh; prioritized recommendations; structured Agent Settings dashboard; architecture, diagnostics, release and optional explicit changed-path impact |
| Developer | Test Impact Analysis | Implemented in P1 | Explicit changed paths, JS/TS reverse import graph and JS/TS test selection with confidence labels; other file types are unanalysed; no automatic test execution |
| Developer | Release Readiness | Implemented in P1 | Static version/changelog/README/CI/test/lockfile/script checks; Git, actual builds/tests, live audit, signing and publishing remain unverified gates |
| Workspace | Unified conversation workspace | Implemented in P0 | Native-host-owned canonical root shared by Agent file access, Git and native project tools; path-contained RPC, per-conversation workspace ID, cross-host revalidation, stale-handle invalidation; browser-only fallback retained outside native hosts |
| Agent reliability | Evidence-based final response | Implemented in P0 | Bounded tool execution ledger and deterministic report fallback after empty model output, tool-step limit, repeated tool failure or generation failure after tool execution |
| Agent reliability | Tool argument correction | Improved in P0 | Action-specific schema requirements, validation hints, retry fingerprints and no repeated identical malformed tool calls |
| Developer | Development-server session recovery | Implemented in P0 | Persisted process identity, start/reconcile/stop locking, conservative re-adoption only on PID/start-time/executable match; stale/unknown records never terminated |
| System Manager | Targeted port-owner lookup | Implemented in P0 | TCP/UDP local port inspection with process identity; available via process or network diagnostics capability; never terminates owners |
| System Manager | Windows Event Log investigation | Implemented in P2; opt-in | Application/System/Setup only; 1–720 hour range; max 200 events; message truncation; no Security/custom log access |
| System Manager | Application crash investigation | Implemented in P2; opt-in | Correlates Application Error 1000, WER 1001, .NET Runtime 1026 and SCM 7031/7034; bounded 1–30 day range; signatures are evidence, not root-cause proof |
| System Manager | Resource sampling | Implemented in P2; opt-in | Finite 1–60 second sampling; system CPU, process CPU deltas and working set; max 1,000 sampled process records and 60 sample records; not persistent monitoring |
| System Manager | Scheduled Tasks | Implemented in P2; opt-in | Read-only inventory; exact task name/path enable/disable/start/stop with fresh native confirmation and state verification; no task creation/deletion/definition editing |
| System Manager | Storage health | Implemented in P2; opt-in | Physical disks, reliability counters and volumes with CIM fallback; unavailable controller/SMART data remains unknown |
| System Manager | Windows security posture | Implemented in P2; opt-in | Defender, firewall profiles, system-volume BitLocker, Secure Boot, TPM and UAC; snapshot only, not malware scan or compliance certification |
| System Manager | Targeted network diagnostics | Implemented in P2; opt-in | One hostname/IP, DNS, best-effort selected local route/interface/next-hop and optional one TCP port; bounded DNS/route/TCP phases, TCP addresses attempted disclosed and fresh confirmation; no scans/ranges |
| Developer | SVG project assets | Implemented in P2 scope refinement | Indexed as vector assets; normalized local refs from HTML/CSS and SVG `href`/`xlink:href`; surfaced in Project Understanding and Mission Control |
| Developer infrastructure | Podman/Docker workbench | Implemented in P3 | Fixed-argument CLI workbench; runtime, container/image/volume/network inventory; exact container inspect/logs/stats/start/stop/restart; Compose config/ps/up/down/build/logs; fresh approval for every action; no prune/remove-volume/image-delete or arbitrary CLI surface |
| Developer infrastructure | SQLite workbench | Implemented in P3 | Existing workspace-contained `.db`, `.sqlite`, `.sqlite3`; integrity/table/schema/foreign-key inspection; parameterized read-only SQL and EXPLAIN; one approved transactional DML/DDL statement; bounded rows/output; no database creation/migration/deletion |
| Developer infrastructure | API contract / OpenAPI testing | Implemented in P3 | OpenAPI 3.0/3.1 JSON built in; optional existing YAML parser; example-based bounded operations; local JSON Pointer refs; status-specific response schemas; explicit base URL, same-origin redirects; mutations excluded by default |
| Developer infrastructure | Runtime/log correlation | Implemented in P3 | Ephemeral timeline from exact RunMind-owned server and Node Inspector sessions plus explicitly supplied browser/API event slices; trace/request/correlation/Journey ID extraction; source-time quality labels and redaction; no persistent log store or causality inference |
| Browser debugging | Advanced Playwright diagnostics | Expanded in P3 | Page/ARIA inspection, console and exception source locations, request/response timing, failed requests/HTTP errors, full-page screenshots and opt-in trace ZIP; ephemeral context, bounded artifacts, no arbitrary page JS evaluation or live source breakpoints |
| Developer workflows | P3 infrastructure workflows | Implemented in P3 | Built-in container workbench, SQLite workbench, OpenAPI contract testing, runtime/log correlation and advanced browser-debugging workflows |
| Browser quality | Expected policy-block classification | Implemented in P0 | CORS/CSP/mixed-content events remain visible; explicitly expected policy events are separated from application errors and correlated transport failures |
| Developer | Coordinated multi-file editing | Added in integration pass | 2–20 files, one approval, snapshot checks, per-file backups, best-effort rollback; sequential writes are not atomic |
| Developer | Node runtime module resolution | Implemented with limits | Fresh-approved fixed script invokes Node ESM `import.meta.resolve` and CommonJS `createRequire().resolve`; no target modules are executed; native workspace must match project workspace |
| Developer | Static project validation | Implemented with limits | JSON/XML and selected manifest checks; test discovery; package manager/lockfile detection; no build/test execution |
| Developer | Approved PowerShell/native execution | Implemented | Existing 2.4.0 bridge |
| Developer | Build/test workflows | Implemented with limits | Workflow-guided approved PowerShell/native execution; commands are selected from project evidence and require fresh approval |
| Developer | Static analysis/lint workflow | Implemented | Discovers configured tools; read-only checks first |
| Developer | Playwright test-runner workflow | Implemented | Workflow-guided project CLI commands remain available |
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
| System Manager | Startup application inventory | Implemented | Per-user/machine Run/RunOnce and user/common Startup-folder inventory; disabled entries remain visible with recovery metadata |
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


## Phase 2 — Debugging & Quality (initial increment)

| Area | Capability | Status | Notes |
|---|---|---|---|
| Debugger | Shared Node Inspector launch | Implemented with limits | Workspace-relative `.js`/`.mjs`/`.cjs`; `--inspect-brk` on IPv4 loopback; fresh approval; current-user execution, not sandboxed |
| Debugger | Attach to Node Inspector | Implemented with limits | Loopback-only target discovery; verifies `type: node`; attach does not release `--inspect-brk` until explicit Resume |
| Debugger | Execution control | Implemented | Pause, resume, step over/into/out, and pause-on-exception modes |
| Debugger | Breakpoints | Implemented with limits | Workspace-relative URL breakpoints and removal; source maps / generated-source mapping not included |
| Debugger | Stack and scope inspection | Implemented with limits | Paused call-frame snapshots and bounded Runtime property inspection |
| Debugger | Expression evaluation | Implemented with limits | Separate fresh approval; V8 side-effect check; not a sandbox |
| Debugger | Events and process output | Implemented with limits | Exceptions, console, script and pause events; bounded 256 events, 64 KB stdout/stderr each |
| Debugger | Multiple sessions | Implemented with limits | Maximum eight sessions per host; attached processes are never terminated by RunMind |
| Runtime quality | Runtime diagnostics | Implemented; Windows build/runtime verified by user | V8 heap usage and session state through the shared Node Inspector session |
| Runtime quality | CPU profiling | Implemented; Windows build/runtime verified by user | V8 sampling profile; `.cpuprofile` export; top sampled self-time frames; 32 MB artifact cap |
| Runtime quality | Heap snapshots | Implemented; Windows build/runtime verified by user | Streamed `.heapsnapshot` output; 128 MB cap; fresh approval; incomplete snapshots discarded |
| Runtime quality | Memory-over-time sampling | Implemented; Windows build/runtime verified by user | Timestamped V8 heap samples; 1–60 second interval; 5–600 second duration; 120-sample ring |
| Runtime quality | Heap dominator/leak UI | Not included | Open exported V8 snapshots in Chrome/Edge DevTools; native analysis UI is a later candidate |
| Browser quality | Structured Playwright automation | Implemented in working bundle; Windows build pending | Shared native runner; Chromium/Firefox/WebKit; 1–30 structured actions; fresh approval per run; ephemeral contexts; assertions, console/network diagnostics, semantic page inspection and screenshots; expected/unexpected CORS/CSP/mixed-content policy events reported separately; project-installed package required |
| API quality | Structured HTTP/API test runner | Implemented in working bundle; Windows build pending | Shared native Node fetch runner; 1–20 sequential requests; status/header/JSON/body assertions; env-backed secrets; same-origin redirects; fresh approval per suite |
| Development servers | Start/readiness/log/health/stop lifecycle | Implemented in working bundle; Windows build pending | Shared native process bridge; package.json script discovery; up to eight owned servers; persisted process identity; guarded re-adoption and per-session stop locks; stale/unknown processes are never terminated; bounded logs; loopback readiness/health; no HTTP-ready claim without a configured probe; fresh approval for start/stop; npm CLI required |

## 2.6.1 changes

| Area | Capability | Status in current 3.1.0 source bundle | Notes |
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
| Developer | Local import relationships | Implemented with limits | Acorn static graph plus bounded TypeScript checker; tsconfig paths; relative/package exports/import aliases/re-exports/cycles; Node runtime resolver tool covers installed dependencies under actual Node ESM/CommonJS rules |
| System Manager | Container command output and timeout | Hardened | Asynchronous process wait, user-configurable timeout (0 = no automatic timeout), cancellation, process-tree termination, bounded stdout/stderr |
| System Manager | Native confirmation and permissions | Preserved | System Manager remains disabled by default; destructive process/container operations still require native confirmation |

### Additional System Manager integration pass

- The previous fixed 30-second container timeout is removed. `commandTimeoutSeconds` is stored in System Manager settings; 0 means no automatic timeout, and 1–3600 sets the requested limit. Cancellation remains available.
- Added a Windows service tool for listing, inspecting, starting and stopping services. Start/stop always require a fresh native confirmation; successful command acceptance is not represented as verified final service state.

### Explicit Phase 1 gaps

- JavaScript (.js/.mjs/.cjs) uses Acorn; JSX/TSX/TypeScript use the bundled TypeScript compiler API. A bounded project Program/checker resolves workspace symbols, but it does not load default libraries or perform a complete diagnostic/type-check pass. Other languages retain heuristic extraction.
- Runtime build, test, lint, Playwright and dependency-audit commands are not executed by static validation. They require an explicitly selected native workspace and fresh approval.
- Native process execution runs as the current Windows user and is not a security sandbox.
- Multi-host Windows compilation and end-to-end execution must be validated on Windows; this environment cannot establish those results.
- RunMind's built-in adapter can submit a startup prompt, but does not manage an already-running conversation/session store.


## Phase 1 completion increment (Unreleased)

| Capability | Status | Scope / limitation |
|---|---|---|
| JavaScript AST refactoring | Implemented with limits | Preview-first local rename/import organization/extract-function plus cross-file named public API rename for statically resolved importers (up to 20 files); approved edit/backup/rollback path; default exports and dynamic unresolved importers rejected |
| JavaScript import organization | Implemented | Sorts independent contiguous top-level import groups; preserves comment-pinned groups |
| Startup entry management | Implemented with limits | Enable/disable exact HKCU/HKLM Run/RunOnce values and inventoried user/common Startup `.lnk`/`.exe` items; command/type or SHA-256-verified file recovery; all changes freshly confirmed |
| Hardware and driver diagnostics | Implemented | Fixed PowerShell CIM query and bounded `driverquery` inventory; requires Windows runtime validation |
| TCP listener ownership | Implemented | `netstat -ano` listener mapping to PID/process name where accessible |
| System Manager result contract | Implemented | Tool-layer normalized success/accepted/completed/verified fields |
| Git integration test | Implemented | Real local temporary-repository smoke test; native bridge still needs Windows integration validation |
| Multi-file recovery tests | Implemented | Source-level ordering/rollback contract tests; browser filesystem failure injection remains outstanding |

| Cross-file symbol references | Implemented (static) | Maps resolved import bindings to lexical use sites in importing modules; dynamic/unresolved references remain outside the graph |

## Developer language scope

RunMind project understanding and project-specific developer workflows are intentionally focused on JavaScript, TypeScript, JSX/TSX, HTML, CSS and SVG. SVG is indexed as a vector asset with local references, not a programming language. No additional language parsers, semantic engines, debuggers or language-specific build/test workflows are introduced in 3.1.0. Existing generic package-manifest/lockfile recognition may observe other ecosystem metadata but does not imply language support.
