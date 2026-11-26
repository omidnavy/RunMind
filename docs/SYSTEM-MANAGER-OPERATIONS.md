# System Manager operations

System Manager uses the existing native bridge; it does not add a separate service, agent process or privileged broker. It does not add a separate service, agent process, privileged broker or always-on monitor. The shared Agent Core calls the native bridge in Standalone, PowerToys Run and PowerToys Command Palette.

## Enablement and enforcement

Each System Manager capability defaults to `false` in native settings. Users must enable System Manager, enable the System Diagnostics group, and independently enable the relevant capability. The Agent tool catalog hides capabilities that are not enabled; native dispatch rechecks settings and remains authoritative. A browser-side tool schema is not a security boundary.

## Operations

| Capability | Agent operation | Scope and bounds | Change/approval behavior |
|---|---|---|---|
| Event Logs | `system_manager_read_event_log` | Application/System/Setup only; 1–720 hours; 1–200 events; 1,200-character message preview | Read-only; Security/custom logs excluded |
| Crash investigation | `system_manager_investigate_crashes` | 1–30 days; 1–200 records; event IDs 1000, 1001, 1026, 7031, 7034 | Read-only correlation; no root-cause claim |
| Resource sampling | `system_manager_resource_monitor` | 1–60 seconds; interval 1–10 seconds; at most 60 samples; at most 1,000 process records per snapshot | Read-only finite call; no background service |
| Scheduled Tasks | `system_manager_scheduled_tasks` | Inventory max 200 results; scans at most 5,000 tasks; exact task name/path for mutations | `list` is read-only; enable/disable/start/stop always ask natively and verify state; no create/delete/definition/trigger editing |
| Storage health | `system_manager_storage_health` | Physical disk/reliability counters; max 64 volumes; fallback basic disk inventory max 32 | Read-only; missing metrics remain unavailable |
| Security posture | `system_manager_security_posture` | Defender, Firewall profiles, system-volume BitLocker, Secure Boot, TPM and UAC | Read-only snapshot; not a compliance or malware verdict |
| Targeted network | `system_manager_targeted_network_diagnostics` | One hostname/IP; DNS; optional one TCP port; timeout 1–10 seconds per phase | DNS read-only; TCP always asks natively; no scanning/ranges |

## Event Log semantics

Only the three whitelisted channels can be queried. Query text is matched against provider/message text after a bounded recent event read. Events outside the selected time window, beyond the bounded read window, or without retained Windows records may be missed. Access errors are not converted into empty successful results. Message previews are truncated.

## Crash correlation semantics

The tool reports event source, event ID, time, application/module/exception fields when parseable, and recurring signatures. Windows event descriptions can vary by OS version and locale; fields may be absent. The output is evidence for follow-up, not a definitive diagnosis or proof that correlated events share one cause.

## Resource sampling semantics

The sampler takes finite snapshots during one native call, calculates system CPU from `GetSystemTimes`, computes per-process CPU deltas only when PID and process start identity match, and reports working set. Process start times inaccessible to the current account are excluded from CPU aggregation to avoid PID-reuse attribution. Each snapshot is capped at 1,000 processes and indicates truncation. A process that starts/exits between snapshots may not have a CPU delta. CPU data is never fabricated as zero when the system counter is unavailable.

## Scheduled Task mutation contract

The mutation target must be one exact task name and path; wildcard or traversal input is rejected. The only supported actions are enable, disable, start and stop. Before any state change, RunMind displays the target/action in native confirmation with an always-ask policy, independent of saved permission profiles. After the Windows cmdlet returns, RunMind queries the task again and checks the requested state. A successful command invocation without a matching resulting state is not reported as verified. Task action command-line arguments are deliberately omitted from inventory because they may contain credentials or tokens. Creation, deletion, action editing and trigger editing are not implemented.

## Storage health semantics

Disk and volume readings depend on Windows Storage cmdlets, controller firmware and driver support. Reliability counters such as temperature, wear and power-on hours may be absent. A missing value means unavailable/not reported; it is never converted to a healthy value. The adapter does not run CHKDSK, surface scans, repairs or firmware operations.

## Security posture semantics

Each posture provider is queried independently; partial results include warnings/availability flags. Secure Boot may be unavailable on unsupported firmware or under insufficient context; BitLocker is queried for the system volume. The output reports observed configuration values and does not assert compliance against a regulatory baseline, perform a malware scan, or remediate settings.

## Targeted network semantics

The host input must be one hostname or IP, not a URL/path/credential string. DNS resolution may return multiple addresses; the optional one-port probe tries a bounded subset under one total TCP timeout. TCP probes require a fresh native confirmation. No port sweep, address range, subnet scan or arbitrary packet payload exists. DNS/TCP success is only a connectivity observation from the local machine at that time.
