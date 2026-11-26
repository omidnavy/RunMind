# RunMind 2.6.0 — Validation Report

**Release:** 2.6.0 — System Manager & Agent Reliability  
**Base:** Complete RunMind 2.5.2 source snapshot  
**Validation date:** 2026-11-17

## Changes in this bundle

### Application discovery

- Replaced strict substring-only filtering with ranked matching over normalized application names, aliases, executable names, path terms, prefixes, substrings, token matches and bounded typo tolerance.
- Normalized common request wording such as `open the Calculator app` before searching.
- Expanded registered application discovery through App Paths and uninstall registration. Registered install locations are searched only at their top level; common Program Files and per-user Programs roots are searched one directory deep with bounded executable counts. There is no recursive drive scan.
- Retained bounded PATH and Start Menu discovery.
- Added bounded `Shell.Application` enumeration of `shell:AppsFolder` and validated AppUserModelID records for packaged Windows applications.
- Refreshes stale catalogs and retries a search miss. Fresh discoveries take priority, while still-valid cached records are retained only as a bounded fallback.
- Returns ranked candidates, match reasons, stable `applicationId` values, source information and saved per-app behavior.

### Application lifecycle and action contract

- Launch now accepts a catalog `applicationId`, not a model-supplied executable path. The native bridge resolves and validates the catalog record and revalidates executable paths before launch.
- Defaults to reuse-and-activate. Existing desktop windows are restored/foregrounded where possible; success is reported only when Windows confirms the target window is foreground.
- If an existing process cannot be activated, RunMind reports that state and does not silently start a duplicate process.
- For URL/file arguments, RunMind invokes the selected application so it can receive the arguments; an exited launcher with an older process still alive is reported as an unverified handoff.
- Supports explicit `new-window` only for a bounded list of applications with known command-line flags, and `new-process` where the application permits it.
- Adds persistent per-application preferences: `ask`, `reuse`, `new-window`, and `new-process`. Preference changes always require native confirmation. An `ask` preference stops before launch and returns choices for the Agent to present to the user.
- Adds graceful application close. It captures process identity, sends normal main-window close requests after mandatory native confirmation, waits a bounded interval, and never force-terminates the process. Packaged applications and background-only processes are not force-closed.
- Updates built-in/custom adapter execution to use the same stable application-ID launch contract.

### Agent tools and Skills

- Adds `list_available_tools`, which reads the active tool definitions supplied to the current Agent task, including connected MCP and host/workspace-dependent tools.
- Makes `list_agent_skills` and `load_agent_skill` available without requiring a selected project workspace.
- Reports skill scope and whether a skill is loaded. Global Skills remain under `%LOCALAPPDATA%\\RunMind\\Skills`; Project Skills remain under `.agents/skills` in an authorized workspace.
- The supplied source snapshot does not contain a Bionic/bundled skill payload. This release does not invent a Built-in skill source or copy unverified skill contents into the product.

## Static validation performed

| Check | Result | Notes |
|---|---|---|
| JavaScript syntax | Passed | `node --check` passed for every JavaScript file under `Shared/ChatWindow`. |
| Tool inventory smoke test | Passed | Executed `list_available_tools` with a stubbed active tool set; verified paginated retrieval, names, and MCP/System Manager categorization across 7 tools. |
| Skills inventory smoke test | Passed | Executed `list_agent_skills` with a stubbed global skill; verified scope and loaded-state reporting. |
| JSON parsing | Passed | All source JSON manifests parsed. |
| XML parsing | Passed | All project files and the MSIX manifest parsed. |
| HTML ID uniqueness | Passed | 341 IDs; no duplicates. |
| Host/package version synchronization | Passed | Standalone, PowerToys Run and Command Palette: 2.6.0; MSIX: 2.6.0.0. |
| Tool/native dispatch wiring | Passed | New Agent tools route to native operations; bridge dispatch includes launch, behavior-save and close operations. |
| Application-ID contract | Passed | Search returns `applicationId`; launch and adapters submit that ID to native code. |
| Graceful-close safety assertion | Passed | Close path uses `CloseMainWindow`; no process-kill call is present in that method. |
| C# lexical delimiter scan | Passed | Braces, parentheses, brackets and string/comment termination checked across 10 shared C# files. This is not a compiler check. |
| ZIP archive integrity | Passed | Final source archive passes `unzip -t`. |
| SHA-256 sidecar | Passed | Sidecar checksum is verified against the final archive with `sha256sum -c`. |

## Not performed — Windows validation required

- No Windows/.NET compilation was performed in this environment. The .NET SDK/compiler is unavailable here.
- No live Windows registry, Start Menu, PATH, Shell AppsFolder, AppUserModelID or Calculator discovery was performed.
- No live Firefox/Firefox Developer Edition/Steam discovery or launch was performed.
- No live foreground activation, browser URL handoff, new-window/new-process, packaged activation, graceful close, or per-app preference test was performed.
- No Standalone, PowerToys Run or PowerToys Command Palette runtime test was performed.

## Recommended target-machine test sequence

1. Build the full Release x64 solution using `Build-Release-x64.bat` on Windows with the .NET 10 SDK and required packages.
2. In each host, enable System Manager and search for `Calculator`, `Firefox`, `Firefox Developer Edition`, `Steam`, `Notepad++`, and a deliberately misspelled query such as `Firefoxx`.
3. Confirm that multiple Firefox installations are presented as candidates and the Agent asks before selecting an ambiguous result.
4. Launch an app when closed; repeat the request when its window is already open; verify activation is reported only when foreground focus is confirmed.
5. Launch a named browser with a URL when it is already running; verify the URL is passed to the selected executable and that an unverified handoff is never described as confirmed navigation.
6. Test new-window and new-process requests on Firefox and VS Code. Test `ask` and each saved per-app behavior, including native confirmation when changing a preference.
7. Test graceful close with a normal app window, multiple app windows, an unsaved-document prompt, and a background-only process. Verify RunMind never force-kills.
8. Test Calculator/package activation and verify the Agent reports request acceptance without claiming a visible window or completed action.
9. Run `list_available_tools` with no workspace, with a workspace, and with an enabled MCP server. Compare output with the tools actually supplied to the model.
10. Run `list_agent_skills` and `load_agent_skill` with no project folder and with a project folder. Confirm Global and Project scope; confirm no absent Bionic/bundled skills are claimed.
11. Repeat smoke/regression tests for all three hosts and existing Git, workspace, indexing, conversations, Project Profiles, Skills, MCP, Deep Research, workflows and native execution.

This bundle has passed static checks only. It must not be treated as a compiled or runtime-verified Windows release until the target-machine steps above pass.
