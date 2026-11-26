# Phase 2 — Native Node.js Debugger foundation

## Scope

The first Phase 2 increment adds a native Node Inspector client to the existing shared `AgentHostBridge` and Agent Core. All three hosts use the same implementation: Standalone, PowerToys Run, and PowerToys Command Palette. It is not a separate agent and introduces no additional NuGet package.

## Available Agent tools

- `node_debugger_launch`: launch a selected-workspace `.js`, `.mjs`, or `.cjs` entry point under `--inspect-brk`.
- `node_debugger_attach`: attach to an existing Node Inspector TCP port on IPv4 loopback.
- `node_debugger_list_sessions`: list the current host's debugger sessions.
- `node_debugger_events`: drain pause/resume, exception, console, script-parsed, and other Inspector events, plus bounded stdout/stderr.
- `node_debugger_command`: pause/resume, step, configure pause-on-exception, set/remove breakpoints, inspect stack/scope, evaluate, and list scripts.
- `node_debugger_stop`: detach, or terminate a process only when RunMind launched it and the user separately approves termination.

## Launch behavior

1. Select the native workspace for the current chat.
2. Launch a workspace-relative `.js`, `.mjs`, or `.cjs` path.
3. RunMind starts `node.exe --inspect-brk=127.0.0.1:0` with `ProcessStartInfo.ArgumentList`, never through a shell.
4. RunMind reads the Inspector endpoint from stderr and connects over loopback.
5. Node remains paused at startup until the debugger attaches and reports the first paused frame.

The target runs with the current Windows account permissions; it is not sandboxed. `node.exe` and project dependencies are resolved using the environment inherited by the RunMind host.

## Attach behavior

RunMind queries `http://127.0.0.1:<port>/json/list`, accepts only targets with `type: node`, then validates that the returned WebSocket URL uses a loopback IP and the requested port. It does not accept remote hosts, proxy resolution, or non-Node targets. Attaching does not automatically release a target waiting at `--inspect-brk`; the user must issue Resume.

## Safety and limits

- Launch and attach require fresh native approval.
- Evaluate always requires a separate fresh approval. V8 `throwOnSideEffect` is enabled, but is not a security sandbox.
- RunMind binds launch Inspector to `127.0.0.1`, never `0.0.0.0`.
- A fixed CDP method allowlist is used. Arbitrary CDP methods are not exposed.
- The current host allows at most eight sessions. Events, scripts, messages and process output have fixed bounds.
- RunMind never terminates an externally attached process. A process it launched can be terminated only after fresh approval.
- A paused target cannot be silently detached. Resume it first, or explicitly terminate a RunMind-launched target.
- When the host exits, RunMind-launched debug processes are cleaned up. An explicit detach while a launched process is running leaves it running and keeps its process handle tracked until it exits or the user terminates it.
- Current launch entry files are limited to `.js`, `.mjs`, and `.cjs`; TypeScript execution/transpilation and source-map breakpoint mapping are not included. Use a project runtime script or prebuilt JavaScript entry file for now.
- This increment does not yet implement CPU/heap profiling, Playwright automation, API testing, or development-server lifecycle management.

## Validation

`tests/node-debugger.integration.test.cjs` starts real Node.js Inspector processes and verifies loopback discovery, break-on-start, breakpoint handling, paused-frame evaluation with V8 side-effect checking, resume/detach, and attach/pause/resume against an already-running Node process. It also checks that all three `.csproj` files include the shared bridge and that the tool schemas/dispatch contracts remain present.

The JavaScript tests do not compile the C# native bridge. Build all three Windows hosts using the repository's Windows validation script before treating this increment as runtime-certified.
