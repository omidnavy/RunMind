# RunMind 2.5.1 — Feature Matrix

| Area | Capability | Status in 2.5.1 | Notes |
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
| System Manager | Application discovery | Implemented | Start Menu shortcut targets resolved to existing executables; App Paths + PATH, local cache, on-demand refresh |
| System Manager | Generic application launch | Implemented | Only discovered existing executables; direct launch plus matching-process verification |
| System Manager | Open files/folders/HTTPS URLs | Implemented | Native default handlers |
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
- Launch success is reported only when the matching process remains active or the request is handed off to an existing instance.
