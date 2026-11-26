# RunMind 2.5.2 — Feature Matrix

| Area | Capability | Status in 2.5.2 | Notes |
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
| System Manager | Application discovery | Implemented | App Paths, uninstall registry, Start Menu shortcuts and PATH; non-empty query required; local cache refreshes on search miss |
| System Manager | Generic application launch | Implemented | Only discovered existing executables; fresh per-request execution; new-process startup verified; existing-instance handoff explicitly unverified |
| System Manager | Open files/folders/HTTPS URLs | Implemented | Windows default-handler request accepted; target application/window/focus/navigation are not verified |
| System Manager | Process inspection | Implemented | Read-only; detail fields individually gated |
| System Manager | Process termination | Implemented | Disabled by default; mandatory fresh confirmation and identity revalidation |
| System Manager | OS/environment/service/network diagnostics | Implemented | Individual switches; read-only |
| System Manager | Application adapters | Implemented | Built-ins plus user JSON manifests with argument templates; no scripts/shell |
| System Manager | GUI automation/admin/system changes | Not included | Explicitly out of scope |
| Security | Skill scripts auto-execution | Not allowed | Never executed by installer |
| Security | Native process execution | Approval required | Runs as current Windows user; not sandboxed |


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
