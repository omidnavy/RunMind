# RunMind 2.7.0 — P0 Foundation Acceptance

This document is the acceptance guide for the P0 reliability/workspace foundation. It complements the automated portable test suite. Windows/.NET validation must still be run on a Windows machine.

## Scope

P0 includes:

1. One native-host-owned workspace selection per conversation, shared with Agent file tools, project indexing, Skills, Git, PowerShell, native commands, development servers, API tests, Node debugging and Playwright.
2. A single shared workspace file bridge with relative-path containment, reparse-point checks, bounded chunk transfers and stale-selection protection.
3. Improved tool-choice/argument guidance, action-specific Playwright schema validation, repeated-call protection and a deterministic completion audit when a model omits its final narrative.
4. Persisted development-server ownership with PID, process start-time and executable identity verification; safe session reconciliation after host restart.
5. Targeted port-owner/process-tree diagnostics instead of broad system inventory for a narrow listener question.
6. Playwright CORS classification that preserves raw browser policy events while separating them from JavaScript errors, HTTP failures and unexpected transport failures.

## Language boundary

Language-aware project understanding/diagnostics currently targets **JavaScript, TypeScript, JSX/TSX, HTML and CSS**. Node package-manager discovery is limited to npm, pnpm, Yarn and Bun. Other file types may still be listed/read/searched as generic text or configuration, but no additional language parser, compiler integration, package ecosystem, debugger, build or test support is being added in this milestone.

## Automated validation performed in the build environment

- Node.js portable suite: 158/158 tests passed from the final release source tree before packaging.
- JavaScript syntax checks: providers, Agent workspace/runtime tools, MCP workspace flow, project index, Playwright runner and acceptance fixture.
- XML parsing: all three `.csproj` files and the Command Palette package manifest.
- JSON parsing: PowerToys Run plugin metadata and fixture package manifests.
- ZIP integrity and SHA-256 verification.

The build environment does not contain the .NET SDK and cannot execute the Windows desktop hosts. Do not interpret source-contract tests or XML parsing as a C# compile or Windows runtime test.

## Windows build gate

From the repository root in PowerShell, with Node.js 24 and .NET 10 installed:

```powershell
.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64
```

This is the complete portable Node test suite followed by the solution build for Standalone, PowerToys Run and Command Palette. Keep the full output. A successful Node section alone is not a successful Windows build.

## Manual P0 acceptance

### A. Unified workspace selection

1. Start RunMind Standalone and open a new conversation.
2. Ask for a project overview or another operation that requires workspace access.
3. Select a project folder once through the native folder picker.
4. In the same conversation, run file search/read, project overview/index/search, Git status (if it is a repository), and a native read-only command.
5. Run Playwright/API/development-server discovery in the same project.
6. Confirm no second folder picker appears. `select_native_workspace` should return `alreadySelected: true` when invoked again.
7. Change the workspace deliberately. Confirm the selection identity changes, old handles are rejected, and the project index is rebuilt for the new selection.
8. Repeat using the same conversation in another RunMind host where the host supports opening that conversation. Confirm the latest persisted selection identity is used; a stale cached handle must not continue to read/write the previous root.
9. Verify traversal (`../`), absolute paths, alternate data streams and reparse-point traversal are rejected. Confirm file writes use a fresh approval, a backup/snapshot, bounded chunks and the selection identity check at commit.

### B. Development-server ownership and recovery

1. Select a workspace containing `test-fixtures/development-server` or the `playwright-app` fixture.
2. Start a declared Node package script with a loopback readiness URL and approve the exact script/lifecycle preview.
3. Confirm the server record is persisted and the health endpoint responds.
4. Close/restart the originating RunMind host without stopping the server.
5. Reopen the same conversation and same selected workspace. List servers; the exact matching PID, process start time and executable should be recovered.
6. Confirm recovered sessions state that historical stdout/stderr is unavailable; the host cannot reattach old process pipes.
7. Request stop and approve it. Confirm the exact verified process tree terminates.
8. Start an unrelated external process on a test port. Confirm RunMind reports the conflict and never adopts or terminates it.
9. Change workspace identity and confirm old session metadata is not silently treated as a session owned by the new workspace.

### C. Targeted System Manager diagnostics

Ask: `Which process owns TCP port 4321?`

Expected: `system_manager_find_port_owner` with `{port:4321,protocol:"tcp"}`. It must not call broad `system_manager_diagnostics`. The result should show endpoint/state/PID and optional process identity details. It must not stop or modify the process.

For a known PID, ask for its parent/child process relationships. Expected tool: `system_manager_process_tree`.

### D. Agent argument discipline and final-response recovery

1. Run a multi-step Playwright request with navigation, `inspect_page`, form fill/select/click, assertions and screenshot in one call.
2. Confirm the model supplies action-specific selectors/values; missing fields are rejected before browser startup with the exact action number/name and remediation hint.
3. Cause a controlled tool validation error. Confirm the Agent corrects the failing action rather than resending an identical payload.
4. Run a multi-step task using a small local model. Confirm that after tool execution the Agent either gives a substantive completion summary or RunMind supplies a deterministic audit listing confirmed, failed/blocked and unverified actions. The final assistant message must not be empty.
5. Confirm the Agent chooses targeted diagnostics for a port question and does not call a broad inventory tool.

### E. CORS and browser error reporting

1. Load an app that deliberately requests an origin which does not allow CORS.
2. Run Playwright with that origin in `expected_cors_origins` when the block is intentionally expected.
3. Confirm the raw console event remains visible in `consoleEvents` / `browserPolicyBlocks`.
4. Confirm the event is classified as a browser security-policy block, not a JavaScript/application console error or unexpected network transport failure.
5. Confirm failed requests matching the CORS event and OPTIONS preflight responses are separately classified.
6. Introduce an unrelated `ERR_CONNECTION_RESET`, JavaScript exception and HTTP 500; verify those remain separate failures.
7. Repeat without declaring the origin. The CORS event remains a policy block, but is reported as undeclared/unspecified rather than assumed expected.

## Recovery limitations

- Development-server recovery can reattach ownership metadata only when conversation ID, current workspace selection identity and exact process identity match. It never adopts an unknown PID based only on a port.
- Recovered server logs are unavailable because stdout/stderr pipes cannot be reattached after the original host exits.
- Processes from an old workspace-selection identity are not silently recovered into a newly selected workspace.
- Native workspace selection is per conversation context. Different conversations have independent authorized roots.
- CORS is a browser policy signal, not proof that the remote service is healthy or that the application intended the request to succeed. Expected CORS must be explicitly declared; raw evidence is retained.

## Sign-off

P0 implementation and portable tests are complete in source. P0 is not Windows-certified until the build gate and manual acceptance checks above pass on the target Windows installation across all three hosts.
