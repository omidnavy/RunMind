> **Archive:** Historical development record. This file describes the source state at the time it was written; use the current README, build guides, feature matrix, and feature-specific documentation for present behavior.

# RunMind 3.2.0 — P5 Windows acceptance checklist

Run this checklist on the target Windows machine with Node.js 24+ and .NET 10. Repeat interactive checks in RunMind Standalone, PowerToys Run and PowerToys Command Palette.

## Build and deployment

- [ ] Confirm `node --version` reports Node.js 24 or later and `dotnet --version` reports .NET 10.
- [ ] Run `.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64` from the extracted source root.
- [ ] Confirm Standalone and PowerToys Run package versions are `3.2.0` and Command Palette package/MSIX version is `3.2.0.0`.
- [ ] Build and launch all three hosts; verify WebView2 startup and shared Agent Settings.
- [ ] Confirm System Manager remains disabled until explicitly enabled, and the Diagnostics group and individual capabilities are independent switches.

## Service intelligence

- [ ] Enable service inspection and inspect a known non-critical service.
- [ ] Confirm current state/PID (when available), startup mode, service account, executable path, dependencies, failure actions, failure flag and trigger configuration are shown or individually marked unavailable.
- [ ] Run `dependencies` on a service with dependencies; verify the tree is bounded, cycle-safe, and marks load-order groups without expanding them.
- [ ] Run `dependents` and verify the result is scoped to the exact service name.
- [ ] On a disposable/test service only, try a state change. Confirm RunMind always asks, then query the actual state and verify the operation result. Decline once and confirm no action occurs.
- [ ] Do not use Windows-critical boot, security, storage, networking or logon services as test targets.

## Health report, event recurrence and history

- [ ] Enable Application/System Event Log, crash/service investigation and at least one other wanted read capability.
- [ ] Run `system_manager_health_report` with default arguments; confirm it is on-demand and lists its coverage/warnings.
- [ ] Set `days` to 1 and `duration_seconds` to 1; verify both limits are accepted. Confirm values outside 1–30 days or 1–15 seconds are rejected.
- [ ] Confirm the report groups actual repeated service/application/network signatures and does not classify unrelated events as one recurrence.
- [ ] Confirm five-minute evidence windows and the nearby-resource-event window explicitly state that time proximity is not causality.
- [ ] Verify CPU/memory pressure issues include leading sampled process names/PIDs when process attribution is available.
- [ ] With history disabled, confirm no history is saved. Enable the opt-in setting and run two reports; confirm comparison of prior/current summary values and service-state changes.
- [ ] Confirm history is bounded to 30 summaries / 512 KB and contains summary data only. Verify the report still works when optional history writing fails.

## Network ownership and diagnostics

- [ ] Enable network inventory and inspect active TCP connections, TCP listeners and UDP endpoints with PID/process-name owners.
- [ ] Enable process-detail inspection and confirm executable paths appear only with that capability enabled.
- [ ] Confirm possible listener conflicts are labeled as candidates and not definitive bind failures.
- [ ] Enable targeted network diagnostics and test a hostname you selected. Verify DNS and Windows route/interface/next-hop evidence.
- [ ] For one selected TCP port, confirm a fresh native confirmation is required. Decline and verify that no connection attempt is made.
- [ ] Confirm no range scans, port sweeps or unsolicited remote probes occur.

## Resource and storage telemetry

- [ ] Enable resource monitoring and run a finite sample (1–60 seconds). Confirm average/peak CPU, physical/page-file memory where readable, process CPU deltas, working set/private memory and top-process lists.
- [ ] Confirm the health report limits its own resource sample to 1–15 seconds.
- [ ] Confirm supported physical-disk queue/read/write counters are present when the Windows CIM provider supports them; otherwise they are explicitly unavailable.
- [ ] Confirm storage health reports missing reliability counters/sensors as unavailable, not healthy.
- [ ] Verify no background service, scheduled monitor or permanent sampler is installed or left running after the request.

## Three-host integration

- [ ] Repeat the report and read-only diagnostics from Standalone, PowerToys Run and Command Palette.
- [ ] Confirm each host uses the same System Manager settings, history path and native approval surface for state-changing operations.
- [ ] Confirm all three hosts render unavailable/permission warnings without claiming the corresponding capability is healthy.
- [ ] Record any Windows build, service, CIM provider, sensor or permissions-specific limitation in the release acceptance notes.

## Certification boundary

Portable Node tests and source-level contracts are not a substitute for this checklist. Do not mark RunMind 3.2.0 Windows-certified until all required x64 builds and live acceptance items pass on the target Windows installation.
