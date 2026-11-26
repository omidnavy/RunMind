# RunMind 2.5.2 — Validation Report

**Release:** 2.5.2 — System Manager Action Reliability  
**Base:** Complete RunMind 2.5.1 source bundle  
**Validation date:** 2026-11-17

## Changes in this bundle

- Added a conditional Agent system instruction requiring fresh System Manager discovery and execution for each new user request. Earlier tool results, process IDs, and assistant claims cannot satisfy a later request.
- Named-browser URL requests are directed through the selected executable with the URL passed as an argument. The default URL handler is no longer presented as a substitute for launching a named browser.
- Application search now requires a non-empty query in the model tool schema, browser-side executor, and native bridge. Native results echo the query and describe the discovery sources.
- Added Windows uninstall registry discovery using registered DisplayIcon executable paths or a matching executable name under InstallLocation, complementing App Paths, Start Menu shortcuts and PATH.
- Launch verification snapshots pre-existing process IDs. A surviving old process is no longer sufficient to report a successful launch. A launcher exit with an old instance still alive is reported as an unverified handoff; a newly running process is reported separately.
- Default-handler open results report request acceptance only. They do not claim the target application, visible window, focus, or URL navigation was verified.
- Retained the dedicated System Manager flyout and all three supported hosts.

## Static validation

| Check | Result | Notes |
|---|---|---|
| JavaScript syntax | Passed | `node --check` passed for all 17 ChatWindow JavaScript files. |
| JSON parsing | Passed | All 2 JSON files parsed. |
| XML parsing | Passed | All 4 project/manifest XML files parsed. |
| HTML ID uniqueness | Passed | 341 IDs; no duplicates. |
| Host/package version synchronization | Passed | Standalone, PowerToys Run, Command Palette: 2.5.2; MSIX: 2.5.2.0. |
| Shared native source inclusion | Passed | `Shared/SystemManager.cs` is included by all three host projects. |
| C# lexical delimiter scan | Passed | Braces, parentheses and brackets balance in `SystemManager.cs` and `AgentHostBridge.cs`; this is not a compiler check. |
| Fresh-request policy wiring | Passed | Runtime conditional system instruction and launch tool descriptions are present. |
| Required application query | Passed | Tool schema, JS executor and native bridge reject empty search queries. |
| Registry discovery wiring | Passed | Uninstall registry scan validates executable paths before catalog insertion. |
| Launch result semantics | Passed | Old process survival alone cannot produce `launched=true` / `verified=true`. |
| Default-handler result semantics | Passed | Native result marks the request accepted but the target open unverified. |
| ZIP archive integrity | Passed | Final archive passes `unzip -t`. |
| SHA-256 checksum | Passed | Generated for the final ZIP; companion `.sha256` file included. |

## Not performed

- No Windows/.NET compilation was performed in this environment; `dotnet`, `csc`, and `mcs` are unavailable.
- No live Windows launch, URL navigation, process enumeration, registry discovery, or UI-focus tests were performed.
- Steam discovery and Firefox Developer Edition repeated-launch behavior still require runtime validation on the target Windows machine.

This is a complete source bundle with static validation, not a compiled or runtime-verified Windows release. Build the complete Release x64 solution on Windows and test the same repeated-prompt cases from the 2.5.0.2/2.5.1 review before relying on the behavior.
