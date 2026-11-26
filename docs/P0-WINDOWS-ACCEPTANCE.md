# RunMind 2.7.0 — P0 Windows Acceptance

This checklist is for the target Windows machine. Complete it separately for Standalone, PowerToys Run and PowerToys Command Palette. Passing portable Node tests does not replace this acceptance run.

## 1. Build baseline

Requirements: Node.js 24 or later, .NET 10 SDK, Windows Desktop targeting components, and repository NuGet dependencies.

From the repository root:

```powershell
node --version
dotnet --version
.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64
```

Expected: sequential Node suite completes without failures, then `RunMind.sln` builds Release x64 and all three host projects compile. Keep the transcript under `artifacts\windows-validation`.

## 2. One workspace selection across tools

For each host:

1. Start a new conversation and select a project folder once through Agent folder access.
2. Ask `project_overview` and `read_file` to inspect a known relative file. Confirm there is no second folder picker.
3. Run a read-only `powershell_execute` such as `Get-Location` and confirm it starts at the selected root after approval.
4. Run `run_native_command` with a fixed executable and a relative working directory. Confirm the displayed working directory is inside the selected root.
5. Run `git_status` on a normal repository in the workspace without another Git folder selection.
6. Launch a Node debugger with a workspace-relative script path.
7. Run API testing and Playwright from the same selected project.
8. Discover and start a package.json development server from the selected root or a relative package subdirectory.

Expected: all tools refer to the same canonical root; only a relative subdirectory can narrow the working directory. No tool opens a second native folder picker.

## 3. Workspace path and identity safety

Use a disposable test folder. Verify:

- a normal nested path is readable;
- `..`, rooted paths, drive syntax, alternate-stream syntax, reserved Windows names and empty segments are refused;
- symbolic links/junctions/reparse-point paths are not followed;
- changing the selected folder creates a new workspace identity and old handles fail as stale;
- a file write in progress is aborted/rejected if the workspace identity changes;
- a change made by one RunMind host is observed by another host using the same conversation context;
- reads/writes larger than the supported transfer bound are rejected; chunked writes commit only after completion.

Do not use an important project directory for destructive path tests.

## 4. Agent tool reliability and final response

Run these prompts using a smaller local model and then a stronger model if available:

**Malformed selector recovery**

```text
Use Playwright to open the local fixture. Fill #request-name with `P0 check`, select `high` in #request-priority, and verify the submission status using #submission-status. Every action must include the required selector/value. Report every assertion.
```

**Repeated tool error**

```text
Attempt a bounded read of a deliberately missing relative file. Do not repeat identical calls. Explain the failure and list the operations attempted.
```

**Step-limit/empty-response fallback**

Trigger a multi-step task that performs at least one read-only tool call, then configure a deliberately low tool step limit if available. Confirm the assistant still emits a non-empty report based only on recorded tool outcomes.

Expected: corrected calls use tool error hints; unchanged malformed calls are not repeated; after tool execution the final response is not empty. The deterministic fallback must not claim unperformed work succeeded.

## 5. Development-server persistence and ownership

Use the included `test-fixtures\development-server` project.

1. Select the fixture as the one Agent workspace.
2. Discover `dev` and `dev-alt`.
3. Start `dev` on 4317 with `http://127.0.0.1:4317/health` readiness URL. Confirm HTTP readiness is actually `ready`.
4. Start a second instance on 4318 and verify independent IDs/logs.
5. Start a server without a readiness URL. Expected: `process_running=true`, `readiness_status=not_configured`, and `ready=null`—not a false HTTP-ready claim.
6. Open another RunMind host with the same conversation context and call `list` or `reconcile`. Confirm exact identity sessions are re-adopted.
7. Start an unrelated process on another test port, then reconcile. Confirm it is reported as unknown/stale and is never adopted or terminated.
8. Occupy 4317 with an unrelated process and attempt `dev`. Confirm port conflict and no termination. Use `system_manager_find_port_owner` rather than broad diagnostics to identify it.
9. Stop only a server returned as an identity-verified RunMind session, after approval. Confirm the exact process tree exits.

Expected: ownership metadata persists; PID, executable path/name and process start time are rechecked; stale or unknown PIDs are never terminated.

## 6. Targeted port-owner System Manager tool

Enable either the Process Management/list-process capability or the Network Diagnostics capability. Occupy a test TCP port, call `system_manager_find_port_owner` for it, and confirm the output contains local endpoint, state, PID and process name. Confirm the operation does not stop the process. Repeat for UDP if a test listener is available.

Expected: targeted output only; the agent does not call broad `system_manager_diagnostics` for a single port question.

## 7. Playwright browser-policy classification

Use `test-fixtures\playwright-app` and install Playwright/browser according to the fixture README if needed. The app deliberately issues a cross-origin request to a fixture endpoint that does not return `Access-Control-Allow-Origin`.

1. Run the normal exploration test without declaring CORS expected. Confirm the policy event appears under unexpected policy events and the run is not reported as fully clean.
2. Repeat with an explicit expected CORS rule matching the probe endpoint. Confirm the browser policy event remains in `policyBlocks` and `expectedPolicyBlocks`, while a safely correlated transport failure is not counted as an ordinary application request failure.
3. Confirm an unrelated network failure (for example connection refused to another local test endpoint) remains in `failedRequests`.
4. Declare an expected CORS block that does not occur; the run must fail with a missing-expected-policy report.
5. Verify console exceptions, page errors and HTTP error responses remain separate from policy events.

Expected: no CORS event is silently dropped. Only explicitly matched expected policy events are treated as expected.

## 8. Three-host final smoke test

Repeat sections 2, 4, 5 and 6 in all three hosts. Confirm the shared workspace and server ownership behavior is consistent. Confirm the latest executable/shortcut is used for Standalone and both PowerToys extensions are built from this source tree.

## Sign-off

Record: Windows version, Node.js version, .NET SDK version, commit/archive SHA-256, full build log, per-host results, and any unpassed checks. Do not mark P0 target-machine certified until all required build and acceptance checks pass.
