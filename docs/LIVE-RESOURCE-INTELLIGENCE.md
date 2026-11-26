# Live Resource Intelligence

## Purpose

Live Resource Intelligence adds a user-started, read-only Windows telemetry session to the existing System Manager flyout. It complements (and does not replace) the existing finite `system_manager_resource_monitor` Agent report and the on-demand System Manager health report.

## User experience

Open **Windows System Manager → Live Monitor**. Enable the System Manager master switch in Settings. Choose a session duration and sampling interval, then explicitly select **Start monitoring**. The Start action is the explicit user authorization for this read-only session; the separate `resourceMonitoring` capability checkbox is not required for the UI monitor and remains in effect for the finite Agent resource-report tool. Use **Stop** to end early. The dashboard shows current values, rolling charts, process rankings and a CSV export.

Supported UI durations are 1, 5 and 15 minutes. Native validation accepts 30–900 seconds. Supported intervals are 1, 2 and 5 seconds. The browser retains at most 900 accepted samples. The native bridge retains only the current session's previous process/system/network baselines; it does not write telemetry to disk. The Process Intelligence card uses this existing live process inventory and adds on-demand details and guarded process actions; see [Process Intelligence](system/PROCESS-INTELLIGENCE.md).

## Data sources and semantics

| Measurement | Native source | Meaning and limitations |
|---|---|---|
| Host CPU utilization | Windows `GetSystemTimes` deltas | Total host CPU capacity across logical processors; null when unavailable; not a per-process sum |
| Physical memory | `GlobalMemoryStatusEx` | Total/available/used physical RAM and memory-load percent |
| Process CPU | `Process.TotalProcessorTime` deltas matched by PID and process start time | Machine-capacity share, plus single-core-equivalent percentage; inaccessible/new/exited processes may be omitted |
| Process memory | Working set and private-memory process properties | Current values for accessible processes, capped to the first 1,000 enumerated processes |
| Network receive/send | `NetworkInterface` byte counters | Rates summed across operational non-loopback, non-tunnel interfaces; a sample is withheld when the observed active-interface set changes; VPN/bridge/virtual adapters may overlap; not per-process network attribution |
| Process I/O transfer | Windows `GetProcessIoCounters` deltas | Aggregated process read/write transfer counters; may include non-disk I/O and must not be interpreted as physical disk throughput |
| GPU utilization | Windows PDH `GPU Engine(*)\Utilization Percentage` counters | Highest observed physical GPU engine utilization; process-specific engine instances are grouped by adapter/engine and capped at 100%; unsupported counters remain unavailable |
| GPU memory | Windows PDH `GPU Adapter Memory(*)\Dedicated Usage` and `Shared Usage` counters | Dedicated and shared memory usage in bytes, summed across observed adapters; the counters do not provide a reliable total capacity, so no usage percentage is inferred |
| Physical disk | Existing finite resource report | Not sampled in the live session; Windows performance provider remains optional/possibly unavailable |

Missing measurements remain `null`/unavailable and are never substituted with zero. Process identity is matched using both PID and process start time to reduce PID-reuse misattribution. The telemetry sampler's process inventory is capped at 1,000. Its dashboard Process Intelligence list is a bounded union of up to 100 CPU and 100 memory candidates, and the UI displays at most 100 entries at once. Selection requires a process start timestamp; see the dedicated guide for details/actions and safety constraints.

## Thresholds and interpretation

The live view exposes fixed display indicators at 85% host CPU and 90% physical-memory load. These are threshold flags, not learned anomaly detection, diagnosis, or evidence of root cause. A chart showing temporal proximity between resource pressure and another event would not prove causation. No automatic remediation is performed.

## Lifecycle and privacy

- Monitoring requires the System Manager master switch. Clicking **Start monitoring** is the explicit authorization; the Diagnostics group and `resourceMonitoring` capability checkbox are not required for this UI session. The finite Agent `system_manager_resource_monitor` tool remains protected by Diagnostics and `resourceMonitoring`.
- The session is created only after an explicit Start click and is read-only.
- One session is allowed per `AgentHostBridge` instance.
- Native duration is strictly bounded to 30–900 seconds; intervals are strictly 1, 2 or 5 seconds.
- Stop requires the exact active 32-character session ID; repeated stop after an already-ended session is idempotent.
- Switching tabs or closing the System Manager flyout only hides the UI; sampling and chart history continue in the same WebView session. The user can reopen the flyout to see current readings and the accumulated trace. Explicit Stop, duration expiry, page/WebView teardown, RunMind window close or native-host disposal ends the session.
- There is no background service, scheduled collector or silent startup collection.
- Samples are held in WebView memory only, capped at 900, and may be exported by the user to CSV. RunMind does not persist the samples.
- The telemetry session itself is read-only and does not automatically change process state. Process Intelligence actions are separate, explicitly invoked native operations with separate permission checks and fresh confirmations; service-hosted and protected processes are blocked. See [Process Intelligence](system/PROCESS-INTELLIGENCE.md).

## Architecture

- `Shared/SystemManager.cs`: Windows counters, per-session state, rate calculations, bounds and stop/dispose handling.
- `Shared/AgentHostBridge.cs`: native operations `systemManager.liveResources.start`, `.sample` and `.stop`.
- `Shared/ChatWindow.html`: Live Monitor tab, controls, SVG chart surfaces and process table.
- `Shared/ChatWindow/scripts/application.js`: polling lifecycle, capped in-memory history, chart rendering, process ranking and CSV export.
- `Shared/ChatWindow/styles/chat.css`: responsive dashboard styling and reduced-motion behavior.
- `Shared/ChatWindow/scripts/agent/runtime.js`: clarifies that the Agent resource-report tool is still finite and the UI session is manually controlled.

GPU readings use optional Windows PDH counters; availability depends on the installed GPU driver and exposed counters. The monitor displays dedicated and shared memory usage, not a memory-capacity percentage. The shared bridge/UI implementation is used by Standalone, PowerToys Run and Command Palette. The lightweight Runner intentionally remains outside the System Manager/Full Agent capability boundary.

## Validation boundary

Portable source-contract tests and JavaScript syntax checks do not compile the native C# or prove Windows performance-counter and process-handle behavior. Before release, build all four hosts with .NET 10 on Windows and complete the [P7 Windows acceptance checklist](P7-WINDOWS-ACCEPTANCE.md) in Standalone, PowerToys Run and Command Palette. Check actual collection overhead on a machine with a large process inventory and with VPN/virtual network adapters.
