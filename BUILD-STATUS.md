# RunMind 3.2.0 — P5 System Manager Intelligence Build Status

This source release contains P0 Foundation & Reliability, P1 Project Intelligence/Mission Control, P2 System Manager, P3 Developer Infrastructure, P4 Agent Capabilities and P5 System Manager Health Intelligence for all three Windows hosts: RunMind Standalone, PowerToys Run and PowerToys Command Palette. All hosts share the Agent Core, native bridge, workspace, project index, approval model and capability enforcement.

## P5 additions

- Structured Windows service inspection: startup mode, service identity/configuration, dependencies, recovery actions, failure flags and trigger information.
- Cycle-safe dependency trees with a five-level / 50-node ceiling.
- Expanded SCM service-failure analysis and same-signature recurrence groups.
- Read-only, on-demand evidence-based system health report across individually enabled inventory, Event Log, crash, resource and storage categories.
- Five-minute event evidence windows and a bounded resource-sample time window, explicitly without causal inference.
- Optional, local health history disabled by default; up to 30 compact summaries / 512 KB, with cross-process write serialization.
- Active TCP/UDP ownership inventory, bounded possible TCP listener-conflict candidates and selected-target DNS/route/one-port TCP diagnostics.
- Expanded CPU, physical/page-file memory, per-process CPU/working-set/private-memory and supported physical-disk I/O telemetry.
- All shared features are available through the common native bridge to Standalone, PowerToys Run and Command Palette.

## Validation performed in the packaging environment

- Full portable suite: **237/237 passed** with `node --test --test-concurrency=1 tests/*.test.cjs`.
- P5 focused suite: **13/13 passed**.
- Repository JavaScript syntax: **24/24 files passed** with `node --check`.
- Release metadata contract: all three host package versions and Command Palette Appx identity are tested as `3.2.0` / `3.2.0.0`.
- JSON validation: **4/4 passed**. XML/project/manifest/XAML validation: **7/7 passed**. ZIP integrity is checked on the final release archive.
- The packaging environment runs Node.js **22.16.0**, not the target Node.js 24 runtime.

## Windows build status

**Not yet certified on Windows.** The packaging environment does not contain the .NET SDK, Windows APIs, Event Log, PowerShell/CIM performance provider or the three Windows host runtimes. Therefore no claim is made that the three .NET 10 projects have compiled or that live Windows service/network/resource operations have passed.

On the target Windows machine, use Node.js 24+ and .NET 10, then run:

```powershell
.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64
```

Complete `docs/P5-WINDOWS-ACCEPTANCE.md` and the carried-forward `docs/P0-WINDOWS-ACCEPTANCE.md` through `docs/P4-WINDOWS-ACCEPTANCE.md` in Standalone, PowerToys Run and PowerToys Command Palette. Do not call this release Windows-certified until those gates pass.

## Operational limits

- Health reports are on-demand; no continuous monitor or background agent is installed.
- Historical comparison is optional and stores aggregate summary data only.
- Windows service state changes and selected TCP endpoint probes retain mandatory fresh native confirmation.
- Missing Windows counters/sensors are reported unavailable, not healthy.
- The health report describes evidence and temporal correlations, not causal root cause, compliance or remediation.
