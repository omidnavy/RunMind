# RunMind 2.6.3 — Workspace and WebView2 Startup Fixes

Date: 2026-11-21  
Baseline: RunMind 2.6.3, Phase 2 development-server package with the earlier System Manager UI fixes retained; includes the Windows test-runner timing adjustment.  
Version: unchanged (2.6.3).

## Fixes included

### 0. Windows validation test-runner stability

A Windows validation run reported 112/113 passing tests. The only failure was the real Node Inspector launch integration test timing out after five seconds while waiting for the spawned debug target to write its expected stdout line. The workspace resolver and WebView2 regression tests passed in that same run. No .NET compilation was reached because the validation script deliberately stops when a Node test fails.

The Windows validation script now runs test files serially (`--test-concurrency=1`) to avoid unnecessary process contention between integration suites. The Inspector launch test's final debuggee-output wait is extended from 5 to 15 seconds and reports captured stdout, stderr, child exit code, and observed Inspector events if it still times out. The debugger implementation itself was not changed for this test-only failure.

### 1. Native workspace path resolution

The native bridge previously combined every `working_directory` with the selected native-workspace root. If the user selected `test-fixtures/development-server` itself but the Agent supplied `test-fixtures/development-server` as the relative working directory, the bridge looked for the fixture folder inside itself and returned `The requested working directory does not exist inside the selected native workspace.`

The shared native resolver now handles both supported conventions:

- Repository root selected: `working_directory = "test-fixtures/development-server"`.
- Fixture project selected: leave `working_directory` empty.

For compatibility, the resolver also recognises an already-selected root duplicated at the start of a supplied repository-relative path and resolves it from the selected root. Node debugger `script_path` uses the same normalisation. Workspace containment validation, rooted-path rejection, and reparse-point checks remain in place.

This shared resolver is used by development-server management, API testing, Playwright, native commands/PowerShell, and Node debugger operations.

### 2. WebView2 startup coordination

Standalone and PowerToys Run now acquire an exclusive, cross-process file lock keyed by the canonical WebView2 user-data-folder path. The lock spans `CoreWebView2Environment.CreateAsync` and WPF `EnsureCoreWebView2Async`, then is released. This prevents two RunMind windows/processes from racing their initial controller attachment against the same persistent WebView2 profile.

The profile location and WebView2 user data are unchanged. RunMind remains multi-window/multi-process; this is not a single-instance restriction. The shared MainWindow used by Standalone and Command Palette uses the same lock helper.

### 3. Guidance and regression tests

- Tool schemas now explain that `working_directory` is optional and relative to the selected native workspace.
- Debugger schema explains which `script_path` to use when the fixture folder itself is selected.
- Fixture README and Phase 2 test documentation describe both workspace-selection conventions.
- Existing System Manager timeout-field and four-column tab UI changes remain included.

## Validation performed in this environment

| Check | Result |
|---|---:|
| `node --test --test-concurrency=1 tests/*.test.cjs` | **113/113 passed** |
| JavaScript syntax checks (`Shared`, `tests`, `test-fixtures`) | Passed |
| XML parsing of all three `.csproj` files | Passed |
| Node Inspector launch/attach integration suite (5 consecutive runs) | **4/4 passed on each run** |
| Windows validation runner concurrency contract | Passed |
| Development-server fixture manifest check | Passed |
| New nested-workspace resolver regression contracts | Passed |
| New WebView2 lock wiring regression contracts | Passed |
| Archive integrity | Passed (`unzip -t`) |
| SHA-256 | See accompanying `.sha256` file |

## Not validated here

- Windows/.NET C# compilation of the Standalone, PowerToys Run, and Command Palette hosts. This environment does not have the .NET SDK installed.
- Live Windows WebView2 initialization with two standalone launches sharing the same profile. The new file-lock coordination is source- and contract-tested but still needs the Windows runtime check below.
- Visual rendering of the earlier System Manager settings style fix.

## Windows verification

From the extracted project root, run:

```powershell
.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64
```

Then test the workspace fix:

1. Open RunMind Standalone and choose `test-fixtures\development-server` as the native workspace when prompted.
2. Ask RunMind to discover the development-server scripts. Omit `working_directory` or leave it empty. Expected scripts: `dev`, `dev-alt`, and `fail`.
3. Start `dev` on port `4317` with readiness URL `http://127.0.0.1:4317/health`. It should report ready.
4. Test API/backend calls against `http://127.0.0.1:4317`; omit `working_directory` when the fixture folder is the selected workspace.
5. For the debugger fixture, use `script_path: "server.cjs"` and `working_directory: ""` (or omit it), with argument `4319`.
6. Repeat the relevant native-tool tests in PowerToys Run and Command Palette. Select the same fixture folder as each host's native workspace.

To test the alternate convention, select the repository root instead and use `working_directory: "test-fixtures/development-server"` and debugger `script_path: "test-fixtures/development-server/server.cjs"`.

Finally, leave one Standalone instance open until its chat UI is fully visible, then launch RunMind again from the taskbar context menu. Expected: the additional RunMind window initializes without the `0x80010108 (RPC_E_DISCONNECTED)` failure. Confirm that both windows remain usable. Also test the reverse order (launching the second instance shortly after the first) to exercise overlapping startup.

Do not report Windows build/runtime validation as passed until the checks above have actually been run on Windows.
