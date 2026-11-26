# Process Intelligence

Process Intelligence extends the **Live Monitor** in Windows System Manager. It combines a selectable live process list, on-demand inspection and confirmed process actions in one place. It is not a separate background monitor or a replacement for Windows Task Manager.

## Process list

During an explicitly started Live Resource Intelligence session, the process list contains a bounded union of the 100 highest observed CPU-share processes and the 100 largest working-set processes. The UI displays up to 100 entries at a time in a bounded, independently scrollable list and supports filtering by process name or PID. The sort selector supports CPU share, working set, private memory, process name and PID; numeric resource sorts are descending, while name and PID sorts are ascending. CPU share is normalized against total machine capacity. The list reuses the live session's existing process samples; it does not start another collector.

Process selection is enabled only when the process sample includes a verifiable start time and the user has enabled the System Manager master switch, Process Management group and `inspectProcesses` capability.

## On-demand inspection

Selecting a process opens a separate inspection modal and asks the shared native Windows bridge for details. The modal can be moved by its header and resized from its corner. It remains open while Live Monitor samples update, when the System Manager flyout closes, and when tabs change; only its explicit Close button dismisses it. A verified process exit does not dismiss the modal. The inspection revalidates process name, PID and start time before returning information. Where Windows permits access, the modal shows:

- Process identity, start time, parent PID/name and observed child processes.
- Executable path, original command line/arguments and file metadata (version, product, company, size and timestamp).
- Process owner, session, token elevation and integrity level.
- Authenticode signature status and signer subject.
- Thread/handle counts, working set, private memory and the latest live CPU/memory sample.
- Windows services hosted by the selected process, where the service inventory query succeeds.

Inspection is on demand. Command lines can contain passwords, tokens and other secrets. RunMind displays them only in the selected process detail panel and does not persist the command line or inspection response as telemetry. Process details may be incomplete when Windows access is restricted or a provider is unavailable.

### Data that Windows does not provide here

The supported process inventory query does not expose the live working directory, so the UI states that it is unavailable rather than inferring one. The current GPU counters provide host/adapter totals, not reliable per-process GPU utilization or GPU-memory attribution. Parent/child relationships are observations from a process snapshot, not a durable process tree.

## Actions

All actions are routed through the shared native bridge and require the relevant System Manager permission. A new native confirmation is shown for every action, even when a permission profile would otherwise allow ordinary operations.

| Action | Behavior |
|---|---|
| Open file location | Opens Windows Explorer with the executable selected. It does not execute the file. |
| Copy path / command line | Copies the selected value to the clipboard. |
| Close gracefully | Sends a normal main-window close request. It never force-terminates. A process with no accessible main window is left running. |
| Force terminate | Immediately terminates only the selected process after a separate confirmation. Child processes are not automatically terminated. Unsaved work may be lost. |
| Restart | Revalidates the selected process, requests a graceful close first, and asks separately before force termination if it remains running. It starts a replacement only after the old process has exited. |

### Restart constraints

Restart is intentionally conservative. It is offered only when the executable path and original command line are available, the executable still exists, and the process is owned by the current RunMind Windows account in the current interactive session. RunMind parses the original command line with the Windows command-line parser and reuses its arguments.

The original process token/elevation cannot be reproduced: the replacement runs with the current RunMind token (which may itself be elevated). The original working directory is not available, so the executable's folder is used as the replacement working directory. Environment variables, inherited handles, job membership, console attachment, startup context and other process-specific state are not reconstructed. A successful process start is not proof that the application restored its prior state.

Processes that host Windows services are blocked from process-level close, termination and restart. Use the dedicated System Manager service controls instead. If Windows cannot verify the service inventory, the UI disables process actions and the native action paths fail closed. Protected Windows process names are also blocked. RunMind does not silently elevate, terminate process trees, or offer an automatic retry after an action fails.

## Permissions

Enable System Manager, the **Process Management** group, and `inspectProcesses` to inspect details. Enable `terminateProcesses` separately to unlock graceful close, force termination and restart. Opening the executable location requires inspection permission. The native bridge checks permissions again for every operation; UI visibility is not a security boundary.

The shared implementation is available in Standalone, PowerToys Run and Command Palette. Runner remains a restricted quick launcher and does not expose System Manager.

## Implementation map

- `Shared/SystemManager.cs`: process identity verification, process metadata, token integrity/elevation, service-host checks, file signature lookup and process actions.
- `Shared/AgentHostBridge.cs`: shared dispatch for inspect, reveal, close, terminate and restart.
- `Shared/ChatWindow.html`: Process Intelligence list and separate inspection modal.
- `Shared/ChatWindow/scripts/application.js`: filtering, selectable sort subjects, bounded-list rendering, modal drag behavior, on-demand inspection, live metric updates and action status.
- `Shared/ChatWindow/styles/chat.css`: responsive charts, bounded process list, and movable/resizable modal styling.
- `tests/live-resource-intelligence.test.cjs`: source-contract coverage for process inspection, bounded live list, permissions and action safeguards.

## Windows acceptance

See [Process Intelligence Windows acceptance](PROCESS-INTELLIGENCE-WINDOWS-ACCEPTANCE.md). Static source tests do not prove Windows process-handle, token, service-inventory or restart behavior; those checks must be completed on Windows before release.
