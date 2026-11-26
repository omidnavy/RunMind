# Process Intelligence — Windows acceptance

Complete this checklist on Windows 10/11 with the .NET 10 SDK, using the same source package for Standalone, PowerToys Run and Command Palette.

## Build and host coverage

- [ ] Run the repository's complete Windows validation/build script and confirm the three Full Agent hosts and the restricted Runner project compile.
- [ ] Open Live Monitor in Standalone and confirm the Process Intelligence card appears.
- [ ] Repeat in PowerToys Run and Command Palette; confirm the shared UI and native bridge behave the same.
- [ ] Confirm Runner does not expose System Manager or Process Intelligence.

## Permissions

- [ ] With System Manager disabled, process inspection and actions are rejected by the native bridge.
- [ ] Enable System Manager and Process Management, but disable `inspectProcesses`; confirm process rows cannot be selected and native inspect is rejected.
- [ ] Enable `inspectProcesses`; select a normal user process and confirm inspection works.
- [ ] Keep `terminateProcesses` disabled; confirm close, terminate and restart remain disabled and native calls are rejected.
- [ ] Enable `terminateProcesses`; confirm every action still shows a fresh native confirmation.

## Inspection

- [ ] Start a bounded Live Monitor session and confirm the process list updates without starting an additional collector.
- [ ] Filter by process name and PID; sort by CPU, working set, private memory, process name and PID. Confirm the process list scrolls inside its bounded panel without extending the Live Monitor flyout.
- [ ] Select a normal user process. Compare PID, name, executable path, command line, owner, session and start time with Windows Task Manager / Process Explorer.
- [ ] Verify file version/company and Authenticode signer/status where present.
- [ ] Compare parent and child relationships with a known process tree; note that this is a point-in-time snapshot.
- [ ] Confirm CPU and working-set values update while the process remains in the current list.
- [ ] Confirm the working-directory field explicitly says unavailable rather than displaying the executable folder as though it were the live current directory.
- [ ] Confirm command-line secrets are warned about and are not written to disk or included in exported telemetry CSV.
- [ ] Confirm inaccessible/unsupported fields show unavailable rather than fabricated values.

- [ ] Drag the inspection modal by its header and resize it from the bottom-right corner. Click outside it and switch System Manager tabs; confirm it remains open until the explicit Close button is pressed.

## Actions

Use a disposable test process that does not host a Windows service. Do not test destructive actions against production applications or system processes.

- [ ] Open file location and confirm Explorer selects the executable without launching it.
- [ ] Copy path and command line; verify clipboard content.
- [ ] For a GUI test process, request graceful close. Confirm the native dialog states that it will not force-terminate. Cancel the process's own unsaved-work prompt when testing that a close can remain pending.
- [ ] For a disposable process with no main window, choose Force terminate. Confirm the native dialog names the target and that exit is verified after approval.
- [ ] Decline the confirmation and verify the process remains running.
- [ ] Restart a disposable same-user, same-session process with a known command line. Confirm graceful close happens first where possible, a second confirmation appears before force termination when needed, and the replacement PID differs from the original.
- [ ] Decline the second force-termination prompt during restart; verify the old process remains running and no duplicate is started.
- [ ] Test a process whose original executable path or command line is inaccessible; restart must remain blocked.
- [ ] Confirm the replacement uses the current RunMind token and executable-folder working directory; do not assume original elevation, working directory or inherited process state is restored.

## Service and protected-process safeguards

- [ ] Inspect a known `svchost.exe` PID that hosts one or more services. Confirm the hosted service inventory is shown and process-level close, force terminate and restart are disabled.
- [ ] Confirm corresponding native operations reject service-hosted PIDs even if invoked outside the UI.
- [ ] Confirm service inventory failure is reported as unavailable and process-level actions remain disabled/blocked.
- [ ] Confirm protected Windows process identities are blocked. Do not attempt to terminate them.
- [ ] Verify child processes are not automatically terminated by an action on the selected process.

## Regression

- [ ] Confirm Live Resource Intelligence still honors explicit Start/Stop, 1/2/5-second sampling and 1/5/15-minute UI durations.
- [ ] Confirm monitoring continues when the flyout closes or tabs change, and stops on explicit Stop, session expiry or RunMind window closure.
- [ ] Confirm CPU, memory, network, process I/O, GPU utilization and dedicated/shared GPU-memory charts still render; unsupported GPU counters remain unavailable.
- [ ] Confirm CSV export still contains bounded session telemetry and does not include on-demand command lines or process inspection payloads.

Record the Windows version, GPU/driver, host surface, build result, failures and relevant sanitized evidence in the release report. Redact user names, command-line secrets and personal paths before sharing logs or screenshots.

### Inspection modal and Live Monitor flyout interaction

- Open Process Intelligence from Live Monitor, then drag the inspection modal by its header. The inspection modal must move while the Live Monitor flyout remains open.
- Resize the inspection modal from its lower-right corner. The Live Monitor flyout must remain open.
- Click controls and text inside the inspection modal; these interactions must not be treated as outside clicks for the toolbar flyout.
- Press Escape while focus is inside the inspection modal. It remains open, and the Live Monitor flyout remains open. Close the inspection modal only with its Close button.
