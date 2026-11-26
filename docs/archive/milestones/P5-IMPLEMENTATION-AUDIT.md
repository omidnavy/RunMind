> **Archive:** Historical development record. This file describes the source state at the time it was written; use the current README, build guides, feature matrix, and feature-specific documentation for present behavior.

# RunMind 3.2.0 — P5 System Manager Intelligence implementation audit

## Scope

P5 bundles Windows Services intelligence, Reliability & Health History, network ownership/correlation, and expanded hardware/resource telemetry. All native functionality is in the shared System Manager bridge and is compiled/deployed by the three existing hosts. It does not add another background service, monitoring daemon, database, or agent executor.

## Requirement-to-implementation map

| Requirement | Implementation | Boundaries |
|---|---|---|
| Inspect Windows services | Existing service list/inspect/config extended with structured service configuration and `sc.exe` recovery/trigger queries | Read-only queries; exact service names |
| Inspect startup mode | Parses boot/system/automatic/manual/disabled startup types, binary path, service account, type, error control and load-order group | Unknown fields remain unavailable |
| Inspect dependencies | `dependencies` builds a recursive tree using declared service dependencies | Cycle-safe; max depth 5; max 50 nodes; load-order groups are not expanded |
| Inspect failure configuration | `qfailure`, `qfailureflag`, `qtriggerinfo` | Each query may be independently unavailable; warnings are preserved |
| Identify recurring service failures | Expanded SCM event IDs and event grouping by actual service identity | Recurrence is an observed event signature, not root-cause proof |
| Approved service start/stop/restart | Existing action path preserved | Always fresh native confirmation; final state is queried and verified |
| Correlate crashes, service failures and Windows events | Shared evidence collection from crash investigation and Application/System logs; five-minute event buckets | Time proximity is not causality |
| Identify recurring problems | Groups same-service failures, app/exception/module crash signatures and network provider/event signatures | At least three occurrences are highlighted; unknown crash signatures are not grouped |
| Compare previous observations | Optional on-demand snapshot comparison | Compares last saved report only; no continuous interval history |
| Evidence-based health report | New `system_manager_health_report` tool and `systemManager.healthReport` native dispatcher route | Read-only, on-demand, individually capability-gated; status is not a health certification |
| Inspect active connections and owners | Bounded `netstat -ano` TCP/UDP inventory with PID/process-name mapping; listener rows are separated from active TCP connections | Executable identity is separately capability-gated; row counts capped |
| Diagnose DNS/routing/connections | Existing selected-target DNS resolution, Windows route lookup and optional one-port TCP check | One explicit hostname/IP and optional port; TCP requires fresh confirmation; no scans |
| Identify local port conflicts | Compare overlapping local TCP listener bindings, including possible IPv4/IPv6 wildcard overlap | Reported as possible candidates; socket reuse/dual-stack can be legitimate |
| Correlate network with app/service events | Include DNS/network event signatures in shared five-minute event windows | Temporal association only; no inferred causality |
| CPU/memory/disk telemetry | Native system CPU times, GlobalMemoryStatusEx, process CPU/memory samples and Windows physical-disk performance provider | Finite sampling; provider/counter gaps are unavailable, not zero |
| Resource pressure and process context | Pressure issues include top sampled CPU or memory process contributors; nearby Event Log evidence is returned against the sample interval | Observational correlation only; per-process disk attribution is not claimed |

## Shared integration

- `Shared/SystemManager.cs` owns native service, network, resource, event and report logic.
- `Shared/AgentHostBridge.cs` dispatches `systemManager.healthReport`.
- `Shared/ChatWindow/scripts/agent/tools.js` routes the Agent tool and keeps the report read-only.
- `Shared/ChatWindow/scripts/providers.js` exposes the report only when System Manager and System Diagnostics are enabled and at least one relevant read capability is enabled.
- `Shared/ChatWindow/scripts/agent/runtime.js` provides operating guidance: only enabled categories, bounded dependency traversal, temporal evidence without causal claims, user-directed networking, opt-in history and no background monitoring.
- `Shared/ChatWindow.html`, `core.js` and `application.js` expose and persist the independent history opt-in setting.
- The same shared sources are deployed to Standalone, PowerToys Run and PowerToys Command Palette.

## History format and privacy

- Path: `%LOCALAPPDATA%\RunMind\SystemManager\health-history.json`.
- Default: disabled.
- Retention: latest 30 summaries; maximum serialized file size 512 KB.
- Data: timestamp, status, aggregate CPU/memory/disk metrics, event/issue counts, issue keys and selected service state values.
- Excluded: raw Event Log messages, executable paths, connection endpoints, process command lines and raw resource process inventories.
- Reads/writes reject reparse-point paths; writes use an atomic temporary file and a named cross-process mutex with a bounded wait.
- Comparison is between successive user-requested reports and does not imply that the system was observed between them.

## Security and resource limits

- Existing master switch, group switch and individual capability checks remain authoritative.
- Service state changes always ask for fresh native confirmation and verify final state.
- TCP endpoint testing requires the targeted-network capability, a user-selected destination and fresh native confirmation.
- Dependency tree: depth 5 / 50 service nodes.
- Active TCP/UDP network rows: max 200 per collection; netstat TCP parsing capped at 500 input rows; possible conflict candidates capped.
- Resource sampling: at most 60 seconds for the direct tool; health report uses 1–15 seconds. Samples/process lists are bounded.
- Health report: at most 30 days of event history, bounded log result counts, up to 15 seconds sampling, and summary history as described above.
- Missing performance provider/counter or inaccessible sensor is unknown/unavailable; it is never silently substituted with a healthy zero.

## Validation evidence and limitations

- Full portable suite: 237/237 passed (Node.js 22.16.0 in this environment).
- P5 focused suite: 13/13 passed.
- Repository JS syntax checks: 24/24 passed.
- Windows-specific .NET 10 compilation, Event Log provider behavior, performance-counter availability, service control and all three host UI flows remain target-Windows acceptance items. This audit is a source-level implementation map, not a Windows certification.


## Later source extension: P7 Live Resource Intelligence

P5 remains an on-demand health/report milestone. P7 separately adds a user-started bounded Live Monitor UI session; it does not change the P5 report into a continuous collector. See `LIVE-RESOURCE-INTELLIGENCE.md` and `P7-IMPLEMENTATION-AUDIT.md`.
