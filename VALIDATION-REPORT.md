# RunMind 2.4.0 validation report

## Release

**RunMind 2.4.0 — Developer Workbench & Native Automation**

Source baseline: `RunMind-2.3.0-Project-Intelligence-Workflows.zip` (131 files).

## Checks performed

| Check | Result | Notes |
|---|---|---|
| JavaScript syntax | Passed | `node --check` run for every `.js` file in the source tree. |
| Project JSON | Passed | All `.json` files parsed successfully. |
| Project XML | Passed | All `.csproj` files and the Command Palette MSIX manifest parsed successfully. |
| Version synchronization | Passed | PowerToys Run plugin, Standalone, Command Palette project, plugin metadata, and MSIX manifest report 2.4.0 / 2.4.0.0 consistently. |
| Native bridge static integration | Passed | Verified shared bridge dispatch entries, native tool definitions, per-call approval routes, workspace-context checks, process-tree termination, bounded output, and argument-list execution. |
| C# lexical delimiter check | Passed | Braces, brackets, and parentheses in `Shared/AgentHostBridge.cs` passed a lightweight lexical balance check. This is not a compiler check. |
| Project index runtime checks | Passed | Node VM harness verified initial index creation, unchanged-file reuse, changed-file reread, deleted-file removal, and automatic refresh scheduling. |
| Source file count | Passed | 131 source files retained; no host or shared source directories removed. |
| Windows/.NET compilation | Not run | `dotnet`, `csc`, and `mcs` are unavailable in this environment. |
| Live WebView2 / native process testing | Not run | Requires Windows. PowerShell discovery, native folder picker, process cancellation/timeouts, and all three host integrations still need target-machine testing. |

## Implementation notes and boundaries

- PowerShell scripts are written to the child process standard input; they are not interpolated into a shell command line.
- Native executable calls use `ProcessStartInfo.ArgumentList` and accept `.exe` names resolved through PATH. Batch files and arbitrary shell command strings are not accepted by that tool.
- Every PowerShell/native process invocation requires fresh in-chat approval. Processes inherit the current Windows user's permissions; approval is not a sandbox or privilege boundary.
- The native execution workspace is selected separately with an OS folder picker and is scoped to the current conversation context. Relative working directories are checked to remain inside that selected directory, but PowerShell scripts can access other resources available to the current user.
- Process execution is cancellable, has a 1–900 second timeout, and captures at most 512 KB of stdout and 512 KB of stderr.
- The project index is automatically built on workspace selection/restoration and periodically refreshed every 60 seconds while the chat is visible. This is polling, not a real-time filesystem watcher. Unchanged-file reuse depends on size and modification-time metadata.
- Native process tools are excluded from Deep Research and delegated sub-agent tasks.

## Required target-machine regression tests

1. Build `RunMind.sln` with the .NET 10 Windows targeting pack and existing package dependencies.
2. Launch Standalone, PowerToys Run, and PowerToys Command Palette; confirm each host exposes the same native process tools.
3. Select a native execution workspace. Verify approval denial, PowerShell output/error/exit code, native `.exe` argument handling, cancellation, timeout, large-output truncation, and rejection of absolute or traversal working directories.
4. Select and reconnect browser workspaces. Verify first-time automatic indexing, incremental refresh, deleted-file removal, 60-second periodic checks, cancellation, and unchanged-file reuse.
5. Re-run existing Git, Skills, MCP, Deep Research, conversation, Project Profile, and workspace file-operation regression tests.
