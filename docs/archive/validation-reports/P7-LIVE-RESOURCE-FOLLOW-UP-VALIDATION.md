> **Archive:** Historical development record. This file describes the source state at the time it was written; use the current README, build guides, feature matrix, and feature-specific documentation for present behavior.

# P7 Live Resource Intelligence — follow-up validation (2026-11-25)

## Source changes

- Removed the extra `resourceMonitoring` capability requirement from UI Live Monitor start/sample operations. The user clicking **Start monitoring** is the explicit authorization. The System Manager master switch remains required.
- Kept `resourceMonitoring` protection on the finite Agent `system_manager_resource_monitor` report.
- Removed flyout-close and tab-change stop behavior. Closing the flyout or selecting Settings/Application Adapters only hides the Live Monitor view; the same WebView continues polling and retaining its capped session history. Explicit Stop, session expiry, page/WebView teardown or native-host disposal stops collection.
- Added Windows PDH GPU telemetry using `GPU Engine(*)\Utilization Percentage`, `GPU Adapter Memory(*)\Dedicated Usage` and `GPU Adapter Memory(*)\Shared Usage` counters. Added GPU current readings, utilization and memory charts, peak values and CSV fields.
- Missing/unsupported GPU counters remain unavailable. Utilization is shown as the busiest observed physical engine, not a sum. Dedicated/shared memory are shown as usage bytes; total capacity is not inferred.
- Corrected System Manager tab grid to three columns and Deep Research to four columns.
- Updated P7 guide, Windows acceptance checklist, implementation audit, roadmap, release notes, changelog and build status.

## Portable checks

- Full Node regression suite: **321/321 passed** (`node --test --test-concurrency=1 tests/*.test.cjs`).
- Focused Live Resource Intelligence suite: **14/14 passed**.
- JavaScript/CJS/MJS syntax: **62/62 passed** (`node --check`).
- JSON: **4/4 parsed**.
- XML/XAML/MSBuild/manifest/SVG: **13/13 parsed**.
- CSS: **22/22 parsed**, zero top-level syntax errors using `tinycss2`.
- Source tests specifically cover master-switch authorization, retained Agent-report capability gating, flyout/tab lifecycle, both tab-column counts, PDH GPU counters, GPU UI values and CSV fields.

## Not performed

- .NET 10/MSBuild compilation, because the SDK is not installed in this environment.
- Windows execution of PDH counters or confirmation of GPU readings against Task Manager.
- Windows UI acceptance, counter overhead measurement, or packaging/signing of Windows host binaries.

Do not treat portable source-contract tests as Windows build/runtime certification. Follow `docs/P7-WINDOWS-ACCEPTANCE.md` on Windows.
