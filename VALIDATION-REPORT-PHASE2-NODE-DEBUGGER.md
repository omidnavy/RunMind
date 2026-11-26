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
## Windows build-log correction (2026-11-21)

- Fixed C# anonymous-object initializer at `Shared/NodeDebuggerBridge.cs:430`: escaped the reserved identifier as `@params`, preserving the serialized JSON property name `params` required by the Chrome DevTools Protocol.
- Added/retained regression coverage for the source contract.
- Re-ran the complete bundled Node test suite after the correction: **70/70 passed**.
- Windows/.NET compilation still requires execution on the user's Windows machine.


## Second Windows build-log correction (2026-11-21)

- Fixed C# compiler error CS0136 in `Shared/NodeDebuggerBridge.cs` within `SendCommandAsync`: renamed the error-response local from `message` to `errorMessage` to avoid colliding with the enclosing request-payload local.
- This is a local identifier-only change; the Inspector request/response behavior and emitted error text are unchanged.
- Re-ran the complete bundled Node test suite after the correction: **70/70 passed**.
- Windows/.NET compilation remains unverified in this environment and must be rerun on Windows.


## Runtime Diagnostics & Profiling increment — 2026-11-21

- Added shared Agent Core tools for runtime heap diagnostics, V8 CPU profile start/stop, streamed heap snapshot capture, and bounded timestamped memory sampling.
- Added explicit fresh approvals for CPU profiling start, memory monitor start, and every heap snapshot. Runtime tools are disabled in Deep Research and delegated sub-agent tasks.
- CPU profiles are exported as `.cpuprofile` files with sampled self-time hotspots; heap snapshots stream to `.heapsnapshot` files without entering the bounded debugger event queue.
- Artifact limits: 32 MB CPU profile, 128 MB heap snapshot, 20 retained artifacts. Memory monitor: 1–60 second interval, 5–600 second duration, maximum 120 retained samples.
- Added `tests/node-runtime-profiling.integration.test.cjs`, which uses a real Node Inspector process to verify V8 heap usage, CPU profile nodes/samples, and complete JSON reconstruction from streamed heap snapshot chunks.

| Check | Result |
|---|---:|
| Complete Node test suite | **72/72 passed** |
| Runtime profiling Inspector integration | Passed |
| CPU profile data | Nodes, samples, and time deltas present |
| Heap snapshot stream | Reassembled and parsed as valid V8 snapshot JSON |
| Runtime tool schemas and native dispatch contracts | Passed |
| JavaScript syntax (`Shared/ChatWindow/scripts`) | **17/17 passed** |
| JSON files | **2/2 parsed** |
| XML project/manifest/XAML files | **6/6 parsed** |
| C# lexical delimiter balance | Passed (not compilation) |
| C# compilation | **Not performed** — no .NET SDK/compiler in this environment |
| Windows three-host build/runtime | **Pending user validation** |

Run `node --test tests/*.test.cjs` and then `.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64` from the extracted project root on Windows. The Node integration test validates the actual Node Inspector protocol, not the native C# implementation.
