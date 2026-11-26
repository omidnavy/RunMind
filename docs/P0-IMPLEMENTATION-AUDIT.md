# RunMind 2.7.0 — P0 implementation audit

## Result

The agreed P0 source implementation is present in this release tree. The remaining release gate is target-Windows validation: a successful .NET 10 Release x64 build and live acceptance in Standalone, PowerToys Run, and PowerToys Command Palette. Portable tests and source inspection cannot certify Windows file-picker behavior, cross-process process adoption, or real Edge/Chromium policy events.

## Agreed P0 scope audit

| P0 requirement | Source implementation | Portable coverage | Target-Windows acceptance |
|---|---|---|---|
| One canonical workspace across native hosts and tools | Native host owns the conversation workspace; Agent file RPC, Git, approved native execution, debugger, API runner, Playwright, and development servers resolve through it. | Workspace dispatch, workspace identity, path containment, and cross-host metadata contracts. | Required: picker restoration, all tool families, cross-host refresh, stale-handle rejection, and reparse-point behavior. |
| Agent tool selection and argument correction | Action-specific Playwright schema guidance; exact action-index validation errors; selector hints from page inspection; retry fingerprinting blocks unchanged failed calls. | Provider schema and native preflight contract tests. | Required with local and stronger models; verify malformed actions are corrected rather than repeated unchanged. |
| No empty final response after tool execution | Bounded tool-outcome ledger, completion audit, deterministic report fallback for empty output, generation failure, repeated failures, and step limit. | P0 ledger/fallback contracts and retry behavior tests. | Required with actual model providers; test the low-step-limit and empty-output scenarios. |
| Development-server ownership and orphan recovery | Persisted metadata, cross-process locks, PID + start-time + executable verification, conservative reconciliation, bounded logs, loopback readiness, and approval-gated stop. Unknown/stale processes are never adopted or terminated. | Discovery/start/stop/readiness/metadata and identity-reconciliation contracts. | Required across separate host processes, including port conflict and unknown PID cases. |
| Targeted port-owner diagnostics | Read-only TCP/UDP port-owner tool, gated by existing process-list or network-diagnostics capability. Agent guidance prefers it to broad diagnostics for a single port. | Tool exposure, native dispatch, and capability contract tests. | Required for TCP and, where practical, UDP on the target machine. |
| Playwright browser-policy classification | CORS, CSP, and mixed-content events remain in dedicated policy result collections. Narrow explicit expected rules classify matching events; unexpected or missing expected events fail the run. | Deterministic runner tests for expected/unexpected/missing events, transport correlation, and separate console/page/HTTP diagnostics. | Required with installed Edge/Chromium against the supplied fixture. |
| JS/TS/JSX/HTML/CSS language boundary | Language-aware analysis and build/test discovery are restricted to JavaScript, TypeScript, JSX/TSX, HTML, and CSS. Other source files are generic text only. | Regression test covers supported language map, import graph, dependency inventory, and toolchain boundary. | No special Windows step beyond normal acceptance. |

## Corrections made during this audit

- Removed leftover Python module candidates from the JavaScript/TypeScript relative-import resolver. Python files remain available only as generic text; they cannot become JS/TS import-graph targets.
- Added a regression assertion that prevents Python module names from re-entering the JS/TS import resolver.
- Corrected stale release metrics in README, ROADMAP, and release notes to 156 portable tests and six parsed project/property/XAML/AppxManifest XML files.

## Explicitly outside this P0 release gate

The separate System Manager enhancement candidates—dedicated process-tree presentation, time-series resource monitor, Windows Event Log/crash investigation, Scheduled Task inventory, storage-health inventory, security-posture inventory, and targeted DNS/network probes—were backlog candidates, not requirements in the selected P0 scope. Existing P0 capabilities include process inventory with parent PID, broad read-only diagnostics, and the targeted TCP/UDP port-owner lookup. These additional tools should be scoped and accepted as a separate increment rather than silently expanding P0.

## Verification boundary

Portable validation is repeatable with:

```powershell
node --test --test-concurrency=1
```

The Windows gate is:

```powershell
.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64
```

That script requires Node.js 24 or later and the .NET 10 SDK, runs the complete portable suite, and builds `RunMind.sln` for the selected configuration/platform. After it succeeds, complete `docs/P0-WINDOWS-ACCEPTANCE.md` separately in all three hosts. Until both steps pass, describe 2.7.0 as **P0 source-complete / Windows acceptance pending**, not Windows-certified.
