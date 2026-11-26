# RunMind 2.9.0 — P2 System Manager Intelligence Build Status

## Current source bundle

This source tree contains the P0 foundation, complete P1 Project Intelligence/Mission Control implementation, and P2 System Manager Intelligence source for all three Windows hosts: Standalone, PowerToys Run and PowerToys Command Palette. P1 reuses the persistent project index and canonical workspace. P2 adds seven separately opt-in native Windows investigations: Event Logs, crash correlation, finite resource sampling, Scheduled Task inventory/confirmed state control, storage health, security posture, and targeted DNS/route/TCP diagnostics.

Language-aware project analysis and project-specific tooling remain limited to JavaScript, TypeScript, JSX/TSX, HTML and CSS. SVG is indexed as a first-class vector asset with bounded local-reference analysis; it is not a programming language. Other source files may be read/searched as generic text only.

## Validation actually performed

- Node.js version used for portable validation: **v22.16.0** (the project target is Node.js 24+; run the Windows acceptance script with Node.js 24 or later).
- Full portable suite: **192/192 passed** with `node --test --test-concurrency=1`.
- JavaScript/CJS/MJS syntax: **44/44 passed** with `node --check`.
- Repository JSON: **4/4 parsed**.
- Project/property/XAML/AppxManifest XML-family files: **6/6 parsed**.
- Standalone, PowerToys Run, Command Palette and Appx package versions agree on **2.9.0 / 2.9.0.0**.
- ZIP CRC/integrity and SHA-256 are verified in the final release bundle.
- C# compilation and Windows runtime behavior were not run in this environment because the .NET SDK, C# compiler and PowerShell are unavailable. P0/P1/P2 Windows acceptance remains pending.

## P1 capability boundaries

- Project Understanding is static/index-based; important call paths and architectural claims require source inspection.
- Project Diagnostics is read-only static analysis. Environment probes, builds, tests and dependency audits require separate explicit approval and actual execution.
- Project Mission Control is a structured evidence report over the existing index; it incrementally refreshes stale index data when possible, prioritizes findings and displays supporting paths. It does not maintain a separate database or run project commands. See `docs/PROJECT-MISSION-CONTROL.md` for report fields, status semantics, freshness behavior and safety boundaries.
- Test Impact Analysis accepts explicit changed paths, follows static reverse imports, and reports confidence. It does not infer Git changes or execute tests.
- Release Readiness is static only. Git cleanliness, live vulnerability results, actual builds/tests, artifact signatures, SBOM, package creation and publication remain unverified until separately checked.

## P2 implementation and safety boundaries

- Each new P2 capability is default-off, persisted through native settings, and checked again by the native dispatcher. Enabling the global System Manager and Diagnostics group is also required.
- Event Log reads are restricted to Application/System/Setup, 1–720 hours, and at most 200 returned events; message text is truncated.
- Crash correlation covers event IDs 1000, 1001, 1026, 7031 and 7034 over at most 30 days/200 results. Results are event-based indicators, not proven diagnoses.
- Resource sampling is a finite request of at most 60 seconds, with up to 60 samples and 1,000 process records per snapshot; no service or persistent monitor is installed.
- Scheduled Task inventory is read-only. Enable/disable/start/stop reject truncated, malformed or path-ambiguous identities and require always-on native confirmation plus post-action verification. Task creation, deletion and definition/trigger editing are unsupported.
- Storage health is best-effort; missing SMART/reliability counters are unknown/unavailable, not healthy.
- Security posture is a read-only Defender/Firewall/BitLocker/Secure Boot/TPM/UAC snapshot, not a compliance certification or malware scan.
- Network diagnostics are limited to one host/IP, a best-effort selected local route, one optional TCP port and bounded DNS/route/TCP phases; TCP attempts disclose attempted resolved addresses and require fresh native confirmation. No network scanning or port ranges.
- SVG assets are first-class in the project index and Mission Control. Local references are collected from HTML/CSS and SVG `href`/`xlink:href`; SVG is not treated as executable source.

## Windows certification remains pending

This environment does not contain the .NET SDK or Windows runtime. Run on Windows with Node.js 24+ and .NET 10:

```powershell
.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64
```

Then complete `docs/P0-WINDOWS-ACCEPTANCE.md`, `docs/P1-WINDOWS-ACCEPTANCE.md` and `docs/P2-WINDOWS-ACCEPTANCE.md` in Standalone, PowerToys Run and Command Palette. Do not describe this source bundle as Windows-certified until the build and all acceptance gates pass.
