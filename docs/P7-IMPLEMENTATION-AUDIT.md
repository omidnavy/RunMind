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
| Process I/O | `GetProcessIoCounters` read/write transfer deltas | Not physical-disk-only I/O and not a per-process disk attribution guarantee |
| Live charts | Inline SVG path rendering for CPU, memory, network and process I/O | No chart library or remote assets |
| Process table | Toggle ranking by CPU or memory | Up to eight rows per view |
| Session export | Explicit browser-generated CSV | User-triggered download only |
| Bounded history | Maximum 900 samples in WebView memory | No raw telemetry persistence |
| Lifecycle cleanup | Stop on tab departure, panel close, pagehide and bridge disposal; native time limit | If host process is force-killed, OS teardown reclaims volatile state |
| Permission enforcement | Existing System Manager master/group/capability gate | Default capability remains disabled |

## Security and privacy review

- Collection is read-only and does not elevate privileges.
- No executable command, PowerShell script, remote request, or service installation is involved in the live sampling loop.
- Native session IDs are unpredictable GUIDs; sample and stop calls require the exact active session ID.
- One active session is stored per native bridge instance.
- The UI keeps no more than 900 samples and exports only after explicit user action.
- The finite P2 Agent resource tool remains capped at 60 seconds; Live Monitor does not silently change its semantics or become Agent-controlled.

## Known limits

- CPU and memory values are host-level Windows measurements. Process attribution is best-effort and bounded.
- Network interface counters may overlap because of VPN, bridge and virtual interface topology.
- Process I/O counters include Windows process I/O transfer operations and are not equivalent to physical disk read/write throughput.
- Physical-disk provider sampling is deliberately excluded from the low-overhead live loop; use the existing finite resource report for supported disk counters.
- Current thresholds (85% CPU and 90% memory) are simple display flags, not anomaly detection.
- No automatic event-log correlation is performed during the live session. Use the P5 health report for on-demand temporal evidence correlation; time overlap is not causal proof.
- No permanent history, background monitoring or automatic remediation is included.

## Portable validation

Run `node --test --test-concurrency=1 tests/*.test.cjs`. `live-resource-intelligence.test.cjs` asserts native bridge dispatch, strict bounds, exact session identity, capability gating, lifecycle cleanup, no native persistence, telemetry semantics, UI controls, data cap, CSV export, responsive CSS and Agent guidance.

C# compilation and live Windows counter behavior require .NET 10 target-machine validation. Do not mark P7 Windows-certified until the Windows acceptance checklist passes in all three Full Agent hosts.
