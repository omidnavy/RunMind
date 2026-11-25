# RunMind 2.5.0 validation report

## Release

**RunMind 2.5.0 — Debugging, Quality, Skills & System Manager**

Source baseline: verified `RunMind-2.4.0-Developer-Workbench-Native-Automation.zip`.

## Checks performed

| Check | Result | Notes |
|---|---|---|
| JavaScript syntax | Passed | `node --check` run for every JavaScript file in `Shared/ChatWindow/scripts`. |
| HTML structure and IDs | Passed | Parsed `ChatWindow.html`; no duplicate IDs; new Settings page and Skill URL controls exist. |
| Project JSON | Passed | All `.json` files parsed successfully. |
| Project XML | Passed | All `.csproj` files and the Command Palette MSIX manifest parsed successfully. |
| Version synchronization | Passed | Standalone, Command Palette, PowerToys Run plugin, plugin metadata, and MSIX manifest report 2.5.0 / 2.5.0.0 consistently. |
| System Manager tool wiring | Passed | Verified conditional tool definitions, JS executor routes, native dispatch entries, shared source inclusion in all three host projects, and settings gating. |
| Skill installer wiring | Passed | Verified URL preview/content install dispatch, URL and pasted-content UI flows, frontmatter validation route, and Global/Project scope handlers. |
| C# lexical delimiter check | Passed | Braces, parentheses, and brackets balance in `AgentHostBridge.cs` and `SystemManager.cs` after ignoring comments and string literals. This is not a compiler check. |
| Workflow definitions | Passed | Build, test, static analysis, Playwright CLI, API smoke-test, and dependency audit workflows are present in the shared developer workflow catalog. |
| Source count | Passed | 133 files in the release source tree; all three host projects and shared source directories retained. |
| Windows/.NET compilation | Not run | `dotnet`, `csc`, and `mcs` are unavailable in this environment. No compile success is claimed. |
| Live WebView2 / Windows testing | Not run | Requires Windows. Native confirmation dialogs, registry/Start Menu/PATH discovery, process enumeration/termination, `sc.exe`, network diagnostics, PowerShell and all three host integrations require target-machine testing. |

## Implementation boundaries

- System Manager is disabled by default. The native bridge independently checks the master switch, group, and individual capability for every System Manager operation. The Agent tool definitions are refreshed before each user turn so another host's settings changes are reflected.
- Application discovery is bounded to registered App Paths, Start Menu shortcuts, and top-level executable entries in a bounded set of PATH directories. A local catalog is reused for five minutes and refreshed on a search miss. Launch requires a catalogued executable/shortcut.
- Application adapters use argument arrays only. Built-in adapters are provided for VS Code, Windows Terminal, Firefox, Notepad++, and Blender. User adapters are declarative JSON manifests; they cannot contain scripts, shell commands, executable paths, or arbitrary code. Adapter installation requires native confirmation.
- Launch/open operations use native confirmation unless the user explicitly chooses Allow automatically under Balanced or Custom. Restricted always asks. Process termination always shows a native Windows confirmation, revalidates PID/name/start time and executable path when available, and refuses selected protected process names.
- System diagnostics are read-only. Service status is collected through the Windows `sc.exe query` command; no service control is implemented. Network information is read-only.
- Skill URL imports require HTTPS, reject local/private network destinations, limit `SKILL.md` to 128 KB, validate `name` and `description` frontmatter, and require a preview/confirmation before install. GitHub/GitLab folder URLs are resolved to a raw `SKILL.md`. Direct URL and pasted-content imports currently install `SKILL.md` only; linked resources and archive imports are not included.
- Pasted valid Skill content is detected before normal message submission. Accepting the preview installs it; declining leaves the text to be sent as a normal chat message. Skill scripts are never executed.
- Playwright support is a workflow that invokes a project's already-installed Playwright CLI through approved PowerShell. It is not an embedded browser automation runtime. API checks are workflow-guided PowerShell requests, not a dedicated API-client UI.
- PowerShell/native process execution is inherited from 2.4.0. Processes run with current-user privileges and are not sandboxed. Every native process invocation still requires the existing explicit approval path.

## Remaining validation

A Windows/.NET build and live tests are required before treating the release as production-verified. In particular, verify WPF confirmation behavior in each host, Windows App Paths/Start Menu discovery, service/network diagnostics, and end-to-end Skill installation with representative public sources.
