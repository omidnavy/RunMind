# P7 Windows Acceptance — Live Resource Intelligence

Run on Windows 11 with Node.js 24+ and .NET 10. Complete independently in RunMind Standalone, PowerToys Run and PowerToys Command Palette. Use a normal non-elevated user session first; separately note which process measurements are unavailable due to normal access boundaries.

## Build and host integration

- [ ] Verify source ZIP SHA-256 against its sidecar before extraction.
- [ ] Run `Build-Release-x64.bat` and confirm all four host projects compile/package/publish successfully.
- [ ] Confirm Live Monitor tab appears in System Manager in Standalone, PowerToys Run and Command Palette.
- [ ] Confirm Runner remains a restricted launcher and does not expose System Manager or Full Agent functionality.
- [ ] Confirm existing finite `system_manager_resource_monitor` Agent report still rejects durations above 60 seconds and behaves as before.

## Permission behavior

- [ ] With System Manager disabled, Start reports that System Manager must be enabled and native start is rejected.
- [ ] With System Manager enabled, confirm Start works even when the Diagnostics group and `resourceMonitoring` capability are disabled.
- [ ] Confirm the `resourceMonitoring` capability still gates the finite Agent `system_manager_resource_monitor` report.
- [ ] Confirm the UI session is not auto-started; clicking Start is the explicit authorization.
- [ ] Disable the System Manager master switch during an active session; the next sample fails closed and the UI stops the session.
- [ ] Confirm default System Manager master/capability settings remain disabled.

## Session bounds and lifecycle

- [ ] Confirm UI presets are 60, 300 and 900 seconds and intervals are 1, 2 and 5 seconds.
- [ ] Through the native bridge contract/test harness, confirm durations below 30 or above 900 seconds are rejected.
- [ ] Confirm intervals outside 1/2/5 seconds are rejected.
- [ ] Confirm a second simultaneous session in the same host is rejected.
- [ ] Confirm a sample with the wrong session ID is rejected and does not alter the active session.
- [ ] Confirm a stop call with a missing/incorrect session ID cannot stop another active session.
- [ ] Confirm exact session stop ends sampling and repeated stop is idempotent.
- [ ] Confirm the 15-minute cap ends the session without further samples.
- [ ] Switch to Settings or Application Adapters, then close the System Manager flyout; confirm samples continue while hidden and the history is visible when reopened.
- [ ] Confirm explicit Stop ends collection and retains the current samples for CSV export.
- [ ] Close the WebView/RunMind window and close the host; verify the session is stopped/disposed.

## Measurement correctness

- [ ] Compare host CPU chart with Windows Task Manager over a quiet interval and a deliberate CPU load.
- [ ] Compare physical-memory total/used/load with Task Manager; note expected timing differences.
- [ ] Start/stop a CPU workload and confirm top-process CPU sorting changes; check PID and start time prevent PID-reuse carry-over.
- [ ] Toggle process ranking between CPU and Memory and verify rows reorder correctly.
- [ ] Generate network traffic and confirm receive/send chart changes on active interfaces.
- [ ] Test with VPN/virtual adapters and confirm the UI/disclaimer explains possible interface overlap.
- [ ] Connect/disconnect or enable/disable an adapter during monitoring; confirm the changed-interface sample is reported unavailable and rates resume only after a stable follow-up baseline.
- [ ] Generate file and non-file process I/O and confirm the Process I/O chart label does not describe it as physical disk throughput.
- [ ] Compare physical-disk counters only in the existing finite resource report; unavailable providers remain unavailable.
- [ ] Confirm missing native counters are displayed as unavailable, never as zero.
- [ ] On supported hardware, compare GPU utilization and dedicated/shared GPU memory against Task Manager during an idle period and a GPU workload.
- [ ] Confirm GPU utilization represents the busiest observed engine, not a sum of all engines.
- [ ] Confirm dedicated/shared GPU memory is displayed as usage in bytes and no memory-capacity percentage is fabricated when total capacity is unavailable.
- [ ] On unsupported drivers, confirm GPU values display Unavailable without affecting CPU/memory/network monitoring.
- [ ] Confirm the 85% CPU and 90% memory indicators are described as fixed display thresholds, not diagnoses.

## UI, history and export

- [ ] Confirm charts update at the selected cadence without overlapping sample requests.
- [ ] Confirm a new session clears prior chart traces before the first new sample.
- [ ] Confirm no more than 900 samples are retained by the UI.
- [ ] Confirm Stop retains current session history in memory and CSV export downloads only after the user clicks Export CSV.
- [ ] Inspect exported CSV headers and values for timestamps, CPU, memory, GPU utilization/dedicated/shared memory, network, process I/O and process inventory status.
- [ ] Confirm closing the panel/host does not leave a persisted telemetry file or background collector.
- [ ] Verify responsive panel layout at 100%, 125%, 150% and 200% Windows display scaling.
- [ ] Verify normal and high-contrast/reduced-motion behavior as applicable.

## Acceptance notes

- Process inventory is capped at 1,000 accessible processes; short-lived/inaccessible processes may be absent.
- Process I/O transfer counters include non-disk I/O and do not represent physical-disk-only throughput.
- Network interface counters can overlap with VPN/bridge/virtual adapters.
- The P5 health report remains an on-demand evidence report and does not become a continuous monitor.
