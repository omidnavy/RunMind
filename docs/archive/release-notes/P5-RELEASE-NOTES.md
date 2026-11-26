> **Archive:** Historical development record. This file describes the source state at the time it was written; use the current README, build guides, feature matrix, and feature-specific documentation for present behavior.

# RunMind 3.2.0 — P5 System Manager Intelligence

RunMind 3.2.0 connects the existing Windows System Manager capabilities into a deeper, evidence-based diagnostic workflow across RunMind Standalone, PowerToys Run and PowerToys Command Palette. It retains P0–P4 and uses the same shared native bridge, Agent Core, settings and approval model.

## What's new

- **Service configuration intelligence:** inspect service state, PID, startup mode, service account, binary path, error control, declared dependencies, recovery actions, failure flags and trigger configuration. The new `dependencies` action returns a cycle-safe recursive dependency tree bounded to depth 5 and 50 nodes. Load-order groups are shown but not recursively expanded.
- **Service failure recurrence:** include additional Service Control Manager failure event IDs and group recurring service failures by service identity. Repeated failures of the same service are distinguished from unrelated failures.
- **Evidence-based health report:** add the read-only `system_manager_health_report` Agent tool. It combines only individually enabled Windows inventory, Application/System Event Logs, crash/service investigation, finite resource samples and storage health. It returns categorized issues, recurring signatures, supporting events and coverage/warnings.
- **Time-based evidence correlation:** group Windows/Application/service/network events in five-minute evidence windows. The report also shows events near the finite resource-sampling window. Temporal overlap is explicitly not treated as causality or proof of root cause.
- **Opt-in health history:** optionally save a compact local summary and compare the latest report with the previous on-demand snapshot. History is disabled by default; at most 30 summaries and 512 KB are retained. The file stores aggregate metrics, issue keys and selected service states—not raw event messages, paths, endpoints or command lines. Cross-host writes are serialized with a named mutex.
- **Network ownership inventory:** include bounded active TCP connection and UDP endpoint inventories with process IDs and process names. Executable paths are included only when process-detail inspection is enabled.
- **Possible TCP listener conflicts:** identify overlapping local TCP listener bindings as candidates only. The report explains that socket reuse and dual-stack behavior can permit legitimate coexistence.
- **Expanded resource telemetry:** add physical/page-file memory measurements, per-process working set/private memory, CPU interval deltas and supported physical-disk queue/read/write telemetry. The report includes leading CPU/memory process contributors in relevant pressure findings.
- **Targeted network diagnostics:** retain explicit one-host DNS resolution and Windows route selection, with an optional single TCP endpoint probe requiring fresh native confirmation. No scanning or unsolicited remote probes are introduced.

## Safety and operating boundaries

- System Manager remains optional and its groups/capabilities remain individually opt-in.
- The health report is on-demand and read-only. It does not install a monitoring service, run continuously, remediate automatically, or claim compliance/root-cause certainty.
- Service start/stop/restart retains fresh native confirmation, exact service-name validation and post-action state verification.
- DNS/route/TCP checks remain user-directed; the optional TCP probe requires confirmation and is limited to one selected endpoint.
- Counts, process rows, event ranges, dependency traversal, sampling intervals and history are bounded. Unsupported performance counters/sensors are reported unavailable rather than healthy.
- No new package/runtime dependency or project-language support was added. Project language-aware support remains JavaScript, TypeScript, JSX/TSX, HTML and CSS; SVG remains an asset format.

## Validation

- Portable regression suite: **237/237 passed** using Node.js 22.16.0 in the packaging environment.
- P5-focused System Manager suite: **13/13 passed**.
- Repository JavaScript syntax: **24/24 files passed** `node --check`.
- Windows/.NET 10 Release x64 compilation and live acceptance have **not** been run in this environment. Complete `docs/P5-WINDOWS-ACCEPTANCE.md` and the carried-forward P0–P4 acceptance checklists on Windows with Node.js 24+ and .NET 10 before treating this as Windows-certified.
