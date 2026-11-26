# RunMind 2.6.1 — Validation Report

**Release:** 2.6.1 — System Manager UI & Model Compatibility  
**Base:** Complete RunMind 2.6.0 source bundle  
**Validation date:** 2026-11-18

## Changes in this bundle

### Model compatibility and action policy
- All system-role messages from the request context are consolidated into the single leading system message. Conversation context retains its original non-system message order.
- System Manager action policy is appended to that leading message only when System Manager launch tools are active. No second system message is inserted after the beginning of the conversation.
- Native launch results now expose a structured `outcome` and separate process-start, activation, window and navigation verification fields. A process start is not represented as proof of a visible window or completed navigation.

### Application adapters
- Added an Application Adapters section to the System Manager flyout.
- The UI lists built-in and custom adapters, refreshes the list, installs JSON manifests, and removes custom manifests.
- Install and removal use existing native confirmation/permission enforcement. Built-in adapters cannot be removed. Custom adapters remain declarative argument templates only; scripts, shell commands and executable paths are not supported.
- Added a first-party RunMind adapter with `send-prompt`, using the existing RunMind `--prompt` startup argument and the normal application discovery/application-ID launch/permission path. A running Standalone `RunMind.exe` is added to the local application catalog during a live catalog rebuild, so portable/current-host use can be discovered as well.
- `send-prompt` is reserved for the built-in RunMind adapter; custom adapters continue to support only `open-file`, `open-folder`, and `open-url`.

### UI
- Checkbox and radio controls are excluded from full-width text-input styling. Their native dimensions and accent color are applied independently.

## Static validation performed

| Check | Result | Notes |
|---|---|---|
| JavaScript syntax | Passed | `node --check` passed for all 17 JavaScript files under `Shared/ChatWindow`. |
| JSON parsing | Passed | Both source JSON files parsed. |
| XML parsing | Passed | All 3 project files and the MSIX manifest parsed. |
| HTML ID uniqueness | Passed | 347 IDs; no duplicates. |
| Prompt message ordering | Passed | Static assertions confirm all system-role context messages are consolidated into the first message and the old second-system-message insertion is absent. |
| Checkbox styling | Passed | Static assertions confirm checkbox/radio exclusion from full-width input rules and dedicated checkbox styling. |
| Adapter UI wiring | Passed | Static assertions confirm list/install/remove UI, native remove dispatch, and refresh wiring. |
| RunMind adapter | Passed | Static assertions confirm built-in `send-prompt` adapter and use of existing `--prompt` argument. |
| Custom adapter safety | Passed | Custom adapter parser remains restricted to `open-file`, `open-folder`, and `open-url`; templates only. |
| Action outcome contract | Passed | Static assertions confirm explicit outcomes and separate verification fields for process startup, activation, window and navigation. |
| Version synchronization | Passed | Standalone, PowerToys Run and Command Palette: 2.6.1; MSIX: 2.6.1.0. |
| Native dispatch wiring | Passed | Adapter removal operation is routed through the shared native bridge. |
| C# lexical scan | Passed | Delimiters and string/comment termination checked in `SystemManager.cs` and `AgentHostBridge.cs`. This is not a compiler check. |
| ZIP archive integrity | Passed | Final source archive passes `unzip -t`. |
| SHA-256 sidecar | Passed | Sidecar checksum verified against the final archive. |

## Not performed — Windows validation required

- No Windows/.NET compilation was performed; the .NET SDK/compiler is unavailable in this environment.
- No live WebView2 rendering test was performed for the checkbox or adapter manager UI.
- No live Windows adapter install/removal confirmation test was performed.
- No live RunMind adapter discovery/launch/`--prompt` handoff test was performed.
- No live Qwen, Nemotron, LM Studio, Ollama, or other model-template request was performed. The prompt fix is based on the observed Jinja error and source-level message ordering; test against multiple model families on Windows.
- No live foreground activation, URL handoff, new-window/new-process, or navigation verification test was performed.
- No Standalone, PowerToys Run, or PowerToys Command Palette runtime test was performed.

## Recommended target-machine test sequence

1. Build all three hosts using `Build-Release-x64.bat` on Windows with the .NET 10 SDK and required dependencies.
2. Inspect the LM Studio native metadata checkbox at normal and narrow window sizes; confirm it renders as a native checkbox and remains clickable.
3. Enable System Manager and Application Management; open Application Adapters and verify all built-ins, including RunMind, appear.
4. Install a harmless custom adapter manifest, refresh the list, remove it, and confirm native confirmation is required for both operations. Verify built-ins cannot be removed.
5. Use the RunMind adapter `send-prompt` operation to start RunMind with a short prompt. Verify it uses the installed/current RunMind executable and normal launch permission flow.
6. Test System Manager actions with multiple model families, including a strict Jinja chat template that previously rejected a second system message. Confirm only one system message is sent at the beginning.
7. Test already-running activation, new-window requests, new-process requests, and applications that hand off to an existing process. Check the `outcome`, `processStartedVerified`, `activationVerified`, `windowVerified`, and `navigationVerified` fields against actual behavior.
8. Run Standalone, PowerToys Run and PowerToys Command Palette regression tests, including the existing discovery, Skills, MCP, Git, workspace and workflow checks.
