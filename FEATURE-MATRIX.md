# RunMind 2.6.1 — Feature Matrix

| Area | Capability | Status in 2.6.1 | Notes |
|---|---|---|---|
| Hosts | Standalone | Preserved | Shared Agent Core |
| Hosts | PowerToys Run | Preserved | Shared Agent Core |
| Hosts | PowerToys Command Palette | Preserved | Shared Agent Core |
| Developer | Project index and search | Implemented | Persistent local index |
| Developer | Static project validation | Implemented | Does not execute commands |
| Developer | Approved PowerShell/native execution | Implemented | Existing 2.4.0 bridge |
| Developer | Build/test workflows | Implemented | Workflow-guided approved execution |
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
| System Manager | Application discovery | Implemented | Ranked name/alias/executable matching across App Paths, uninstall registry, Start Menu, bounded common install locations, PATH and Windows Apps folder; bounded refresh on stale cache/search miss |
| System Manager | Application launch | Implemented | Uses stable application IDs; revalidates executable; reuses/activates existing windows by default; distinguishes startup, activation and unverified handoff |
| System Manager | Open files/folders/HTTPS URLs | Implemented | Windows default-handler request accepted; target application/window/focus/navigation are not verified |
| System Manager | Process inspection | Implemented | Read-only; detail fields individually gated |
| System Manager | Process termination | Implemented | Disabled by default; mandatory fresh confirmation and identity revalidation |
| System Manager | OS/environment/service/network diagnostics | Implemented | Individual switches; read-only |
| System Manager | Application lifecycle | Implemented with limits | Reuse/activate, new-window allowlist, new-process, per-app preferences and graceful close; no force-kill |
| System Manager | Packaged Windows apps | Implemented with limits | AppsFolder/AUMID activation; no generic arguments or visible-window verification |
| System Manager | Application adapters | Implemented | Built-ins plus user JSON manifests with argument templates; no scripts/shell |
| Agent | Current tool inventory | Implemented | Lists exact active Agent/MCP/host/workspace tool definitions |
| Skills | Global discovery/loading | Implemented | Works without an authorized project folder |
| System Manager | GUI automation/admin/system changes | Not included | Explicitly out of scope |
| Security | Skill scripts auto-execution | Not allowed | Never executed by installer |
| Security | Native process execution | Approval required | Runs as current Windows user; not sandboxed |


## 2.6.1 changes

| Area | Capability | Status in 2.6.1 | Notes |
|---|---|---|---|
| System Manager | Adapter inventory and custom manifest management | Implemented | Dedicated UI; native confirmation for install/removal; built-ins protected |
| System Manager | First-party RunMind adapter | Implemented | Sends a prompt through the existing `--prompt` startup argument |
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
| Docker/Podman list, inspect, start, stop, restart, logs | Implemented (native CLI; Windows runtime test required) |
| Coding/debugging/testing-and-quality/code-review skills | Implemented |
| Dedicated adapters tab and bounded inner scrolling | Not implemented |
| Built-in adapter edit overrides/reset | Not implemented |
| RunMind open/create/export existing conversation native adapter | Not implemented; conversations remain in WebView IndexedDB |
