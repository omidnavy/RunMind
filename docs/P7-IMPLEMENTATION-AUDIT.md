# RunMind P7 — Live Resource Intelligence implementation audit

## Requirement-to-implementation map

| Requirement | Implementation | Boundary |
|---|---|---|
| Explicit live session | Start/sample/stop operations in shared native bridge | User starts session from System Manager UI only |
| Bounded duration | Native rejects values outside 30–900 seconds | UI presents 1/5/15 minute presets |
| Controlled sampling | Native allows 1/2/5 second sample intervals and throttles early sample requests | Browser polls sequentially; samples are pull-driven, not background callbacks |
| Host CPU | `GetSystemTimes` interval deltas | Null if Windows API unavailable |
| Physical memory | `GlobalMemoryStatusEx` | Host-wide physical memory; page-file values are in returned sample object |
| Process CPU | Process CPU-time deltas matched by PID and start time | First 1,000 accessible processes only; short-lived/inaccessible processes may be absent |
| Process memory | Working set/private memory and top-memory ranking | Working set includes shared pages and must not be summed as unique physical ownership |
| Network rates | Interface receive/send counter deltas | Active non-loopback/non-tunnel interfaces; virtual/VPN adapters can overlap |
| GPU utilization | Windows PDH `GPU Engine(*)\Utilization Percentage` | Busiest observed physical engine; unsupported drivers yield unavailable |
| GPU memory | Windows PDH `GPU Adapter Memory(*)\Dedicated Usage` and `Shared Usage` | Usage bytes only; no reliable capacity/percentage is inferred |
| Process I/O | `GetProcessIoCounters` read/write transfer deltas | Not physical-disk-only I/O and not a per-process disk attribution guarantee |
| Live charts | Inline SVG path rendering for CPU, memory, GPU utilization/memory, network and process I/O | No chart library or remote assets |
| Process Intelligence list | Searchable, selectable live list built from the top-100 CPU-share and top-100 memory candidates | Up to 100 displayed rows; sort by CPU, working set, private memory, name or PID; separate movable/resizable persistent inspection modal |
| Session export | Explicit browser-generated CSV | User-triggered download only |
| Bounded history | Maximum 900 samples in WebView memory | No raw telemetry persistence |
| Lifecycle cleanup | Keep sampling when tabs change or flyout closes; stop on explicit Stop, duration expiry, pagehide and bridge disposal; native time limit | If host process is force-killed, OS teardown reclaims volatile state |
| Permission enforcement | System Manager master-switch gate for the UI session; finite Agent report retains diagnostics/resourceMonitoring gating; Process Intelligence inspection/actions use their own process capabilities | UI Start click explicitly authorizes sampling; process actions require per-action native confirmation |

## Security and privacy review

- Collection is read-only and does not elevate privileges.
- No executable command, PowerShell script, remote request, or service installation is involved in the live sampling loop.
- Native session IDs are unpredictable GUIDs; sample and stop calls require the exact active session ID.
- One active session is stored per native bridge instance.
- The UI keeps no more than 900 samples and exports only after explicit user action. Closing the flyout does not stop polling; history remains in the same WebView memory.
- The finite Agent resource tool remains capped at 60 seconds; Live Monitor does not silently change its semantics or become Agent-controlled.

## Known limits

- CPU and memory values are host-level Windows measurements. Process attribution is best-effort and bounded.
- Network interface counters may overlap because of VPN, bridge and virtual interface topology.
- Process I/O counters include Windows process I/O transfer operations and are not equivalent to physical disk read/write throughput.
- Physical-disk provider sampling is deliberately excluded from the low-overhead live loop; use the existing finite resource report for supported disk counters.
- Current thresholds (85% CPU and 90% memory) are simple display flags, not anomaly detection.
- No automatic event-log correlation is performed during the live session. Use the System Manager health report for on-demand temporal evidence correlation; time overlap is not causal proof.
- No permanent history, background monitoring or automatic remediation is included.

## Portable validation

Run `node --test --test-concurrency=1 tests/*.test.cjs`. `live-resource-intelligence.test.cjs` asserts native bridge dispatch, strict bounds, exact session identity, capability gating, lifecycle cleanup, no native persistence, telemetry semantics, UI controls, data cap, CSV export, responsive CSS and Agent guidance.

C# compilation and live Windows GPU/PDH counter behavior require .NET 10 target-machine validation. Do not mark P7 Windows-certified until the Windows acceptance checklist passes in the three Full Agent hosts.
