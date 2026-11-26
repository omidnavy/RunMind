# Live Resource Intelligence

## Purpose

Live Resource Intelligence adds a user-started, read-only Windows telemetry session to the existing System Manager flyout. It complements (and does not replace) the existing finite `system_manager_resource_monitor` Agent report and the P5 on-demand health report.

## User experience

Open **Windows System Manager → Live Monitor**. Enable System Manager, the System Diagnostics group and the Resource Monitoring capability in Settings. Choose a session duration and sampling interval, then explicitly select **Start monitoring**. Use **Stop** to end early. The dashboard shows current values, rolling charts, process rankings and a CSV export.

Supported UI durations are 1, 5 and 15 minutes. Native validation accepts 30–900 seconds. Supported intervals are 1, 2 and 5 seconds. The browser retains at most 900 accepted samples. The native bridge retains only the current session's previous process/system/network baselines; it does not write telemetry to disk.

## Data sources and semantics

| Measurement | Native source | Meaning and limitations |
|---|---|---|
| Host CPU utilization | Windows `GetSystemTimes` deltas | Total host CPU capacity across logical processors; null when unavailable; not a per-process sum |
| Physical memory | `GlobalMemoryStatusEx` | Total/available/used physical RAM and memory-load percent |
| Process CPU | `Process.TotalProcessorTime` deltas matched by PID and process start time | Machine-capacity share, plus single-core-equivalent percentage; inaccessible/new/exited processes may be omitted |
| Process memory | Working set and private-memory process properties | Current values for accessible processes, capped to the first 1,000 enumerated processes |
| Network receive/send | `NetworkInterface` byte counters | Rates summed across operational non-loopback, non-tunnel interfaces; a sample is withheld when the observed active-interface set changes; VPN/bridge/virtual adapters may overlap; not per-process network attribution |
| Process I/O transfer | Windows `GetProcessIoCounters` deltas | Aggregated process read/write transfer counters; may include non-disk I/O and must not be interpreted as physical disk throughput |
| Physical disk | Existing finite resource report | Not sampled in the live session; Windows performance provider remains optional/possibly unavailable |

Missing measurements remain `null`/unavailable and are never substituted with zero. Process identity is matched using both PID and process start time to reduce PID-reuse misattribution. The process inventory is capped at 1,000. Top process lists are capped at eight and can be ranked by CPU or memory.

## Thresholds and interpretation

The live view exposes fixed display indicators at 85% host CPU and 90% physical-memory load. These are threshold flags, not learned anomaly detection, diagnosis, or evidence of root cause. A chart showing temporal proximity between resource pressure and another event would not prove causation. No automatic remediation is performed.

## Lifecycle and privacy

- Monitoring requires the existing master System Manager switch, Diagnostics group and `resourceMonitoring` capability.
- The session is created only after an explicit Start click and is read-only.
- One session is allowed per `AgentHostBridge` instance.
- Native duration is strictly bounded to 30–900 seconds; intervals are strictly 1, 2 or 5 seconds.
- Stop requires the exact active 32-character session ID; repeated stop after an already-ended session is idempotent.
- Leaving the Live Monitor tab, closing the System Manager panel, navigating away/closing the WebView, or disposing the native host ends the session.
- There is no background service, scheduled collector or silent startup collection.
- Samples are held in WebView memory only, capped at 900, and may be exported by the user to CSV. RunMind does not persist the samples.
- No remote endpoint, port scan, process termination or state-changing operation is part of this feature.

## Architecture

- `Shared/SystemManager.cs`: Windows counters, per-session state, rate calculations, bounds and stop/dispose handling.
- `Shared/AgentHostBridge.cs`: native operations `systemManager.liveResources.start`, `.sample` and `.stop`.
- `Shared/ChatWindow.html`: Live Monitor tab, controls, SVG chart surfaces and process table.
- `Shared/ChatWindow/scripts/application.js`: polling lifecycle, capped in-memory history, chart rendering, process ranking and CSV export.
- `Shared/ChatWindow/styles/chat.css`: responsive dashboard styling and reduced-motion behavior.
- `Shared/ChatWindow/scripts/agent/runtime.js`: clarifies that the Agent resource-report tool is still finite and the UI session is manually controlled.

The shared bridge/UI implementation is used by Standalone, PowerToys Run and Command Palette. The lightweight Runner intentionally remains outside the System Manager/Full Agent capability boundary.

## Validation boundary

Portable source-contract tests and JavaScript syntax checks do not compile the native C# or prove Windows performance-counter and process-handle behavior. Before release, build all four hosts with .NET 10 on Windows and complete `P7-WINDOWS-ACCEPTANCE.md` in Standalone, PowerToys Run and Command Palette. Check actual collection overhead on a machine with a large process inventory and with VPN/virtual network adapters.
