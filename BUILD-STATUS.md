# RunMind 2.4.0 — Developer Workbench & Native Automation

## Release bundle status

- Source baseline: RunMind 2.3.0 Project Intelligence & Workflows (131 files).
- Shared Agent runtime now exposes explicitly approved PowerShell and native `.exe` execution through the common native bridge.
- Native execution workspaces are selected with an OS folder picker and scoped to the active conversation context. Relative working directories are confined to the selected folder.
- Process execution supports cancellation, a maximum 15-minute timeout, bounded stdout/stderr, exit code, and duration reporting. Processes run with current-user privileges and are not sandboxed.
- Project indexing starts automatically after a workspace is selected/restored and performs incremental refreshes every 60 seconds while the chat is visible.
- Standalone, PowerToys Run, and PowerToys Command Palette continue to consume the same shared ChatWindow, Agent tool definitions, and native bridge implementation.
- Project/host metadata synchronized to 2.4.0; Command Palette package version synchronized to 2.4.0.0.

## Validation performed in this environment

See `VALIDATION-REPORT.md` for exact checks and limitations. JavaScript syntax, JSON/XML parsing, version synchronization, static process-bridge checks, and ZIP integrity can be checked here. No Windows/.NET build or live WebView2/process execution is claimed.

## Target-machine validation still required

- Build `RunMind.sln` with the .NET 10 Windows targeting pack and the repository's existing package dependencies.
- Launch all three hosts and confirm the shared native bridge handles process workspace selection and process results.
- Select a native execution workspace; test PowerShell and native `.exe` commands, approval denial, cancellation, timeout, large output truncation, non-zero exit codes, and relative-directory traversal rejection.
- Select/reconnect a browser workspace and verify automatic first index creation, incremental updates, deleted-file handling, periodic refresh, cancellation, and unchanged-file reuse.
- Re-run existing Git, Skills, MCP, Deep Research, conversation, and Project Profile regression checks.
