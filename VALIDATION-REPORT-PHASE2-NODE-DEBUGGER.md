# RunMind Phase 2 — Native Node.js Debugger Validation

**Working baseline:** 2.6.3 (no version bump assigned)  
**Increment date:** 2026-11-21  
**Status:** Node.js integration tests passed; Windows native build/runtime validation pending.

## Implemented

- Shared `AgentHostBridge` Node Inspector implementation compiled into the Standalone, PowerToys Run, and PowerToys Command Palette projects.
- Agent Core tools: `node_debugger_launch`, `node_debugger_attach`, `node_debugger_list_sessions`, `node_debugger_events`, `node_debugger_command`, and `node_debugger_stop`.
- Launch workspace-relative `.js`, `.mjs`, and `.cjs` entry points using `node.exe --inspect-brk=127.0.0.1:0`, with argument-list invocation rather than a shell.
- Attach to a Node Inspector port via `127.0.0.1` only; discovery verifies `type: node` and the returned WebSocket URL's loopback address and port.
- Breakpoints, pause/resume, stepping, pause-on-exception settings, paused stack frames, scope property inspection, scripts, exception/console events, and bounded process output.
- Separate fresh approval for launch, attach, expression evaluation, and process termination. Evaluation enables V8 `throwOnSideEffect`; this is not a sandbox.
- Lifecycle handling that does not terminate attached processes, does not silently detach paused targets, tracks detached RunMind-launched processes, and cleans up RunMind-launched child processes when the host is disposed.
- Attach does not release a target paused at `--inspect-brk` until the user explicitly resumes it.
- Chunk-based stdout/stderr capture and bounded queues/buffers.

## Validation performed

Environment: Node.js v22.16.0 (Linux test environment).

| Check | Result |
|---|---:|
| Complete Node test suite | **70/70 passed** |
| Node Inspector launch/protocol integration | Passed |
| URL breakpoint and paused local-variable evaluation | Passed |
| Resume, detach and Node process exit | Passed |
| Attach to an already-running Node process, pause and resume | Passed |
| Shared tool dispatch/approval/loopback source contract | Passed |
| Shared bridge inclusion in all three host project files | Passed |
| JavaScript syntax checks under `Shared/ChatWindow/scripts` | **17/17 passed** |
| JSON files | **2/2 parsed** |
| Project XML files (`.csproj`, `.props`, `.targets`, `.manifest`) | **4/4 parsed** |
| ZIP integrity | Passed after packaging |

The real protocol integration tests use Node's Inspector endpoint and Node's built-in WebSocket client. They validate the underlying Node Inspector behaviors, while the native C# bridge itself was not executable in this environment.

## Not performed / release gate

- No C# compilation: `dotnet`, `csc`, and `msbuild` are unavailable in this environment.
- No Windows runtime tests for the native bridge or its WebView/host interactions.
- No interactive smoke test in Standalone, PowerToys Run, or Command Palette.

On Windows, run from the extracted project root:

```powershell
.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64
```

The script now runs all bundled Node tests (Phase 1 plus the current Phase 2 increment) and builds the complete three-host solution. After a successful build, smoke-test launch, attach, breakpoints, stepping, pause-on-exceptions, scope inspection, separately approved evaluation, detach, and termination of a RunMind-launched debug process in each host.

## Deliberate limitations

- Launch entry points are `.js`, `.mjs`, and `.cjs`; direct TypeScript execution/transpilation is not included.
- Source-map-aware breakpoint remapping, automatic test discovery while debugging, multi-target process orchestration, and debugger UI panes are not included yet.
- Debugger sessions are local to the current host instance (up to eight sessions); they are not shared over IPC between different RunMind hosts.
- Targets run with the current Windows account permissions. Node Inspector access is powerful; approval boundaries do not turn it into a process sandbox.
- Runtime CPU/heap profiling, Playwright automation, dedicated API/backend testing, and development-server lifecycle management remain later Phase 2 increments.
