# RunMind 3.3.1 — Windows Stabilization Acceptance

Run these checks on supported Windows 11 systems using Node.js 24+ and .NET 10. Repeat applicable user-facing tests in Standalone, PowerToys Run and PowerToys Command Palette. Record Windows build, PowerToys version, WebView2 Runtime version, .NET SDK version, test date and logs.

## 1. Build/package gates

- [ ] Start from a clean checkout/extraction of the 3.3.1 source ZIP.
- [ ] Run `Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64` and preserve the complete output/log.
- [ ] Confirm all three projects build Release x64 with no errors; investigate all warnings.
- [ ] Confirm Command Palette MSIX package identity/version is 3.3.1.0, signature validates and install/uninstall works.
- [ ] Confirm PowerToys Run plugin manifest/package version is 3.3.1 and deployment succeeds after PowerToys is closed.
- [ ] Run the full portable suite under Node.js 24+: `node --test --test-concurrency=1 tests/*.test.cjs`.
- [ ] Run `dotnet list RunMind.sln package --vulnerable --include-transitive` (or the supported equivalent for the SDK/NuGet client) and review every direct/transitive finding.
- [ ] Generate an SBOM for each shipped host/package and review licenses/notices.

## 2. WebView2 navigation and origin boundary

For each host:

- [ ] Confirm `https://localhost/ChatWindow.html` loads from the mapped app folder.
- [ ] Confirm embedded navigation to arbitrary HTTPS/HTTP pages is denied and opened externally only where intended.
- [ ] Confirm `javascript:`, `data:`, `file:`, `ms-*`, custom schemes and credentialed URLs are not forwarded to an external handler.
- [ ] Confirm popup/new-window requests use the same external-URL restrictions.
- [ ] Confirm native messages sent from an unexpected source are ignored before any handler action.
- [ ] Confirm valid page messages still support conversation save/open, appearance, native workspace, Agent and System Manager operations.
- [ ] Confirm CSP does not break provider settings, Agent tools, dialogs, theme loading, OCR, workers or approved local assets.
- [ ] Confirm no runtime `eval`/inline script regression is required by the UI.

## 3. Process output and timeout behavior

- [ ] Exercise service inventory with normal output and with a machine containing a large service list.
- [ ] Exercise fixed diagnostics that write large stdout and stderr; verify bounded output and no UI hang.
- [ ] Exercise timeout handling with a controlled long-running test process; confirm process-tree termination and cleanup.
- [ ] Confirm System Manager timeout `0` preserves its documented no-automatic-timeout behavior; confirm finite nonzero values time out as configured.
- [ ] Confirm stderr warnings do not corrupt successful structured JSON results; nonzero exits preserve useful error evidence.

## 4. WebView2 prerequisite installation

- [ ] Test with Evergreen Runtime already installed.
- [ ] Test first launch with the Runtime absent and the user allowed to install.
- [ ] Test simultaneous first launch from multiple RunMind hosts/processes.
- [ ] Test offline/download failure, denied install and a deliberately stalled installer.
- [ ] Confirm lock/installer waits fail within their configured bounds and all processes/locks are released.
- [ ] Confirm retry works after correcting the underlying install issue.

## 5. Feature regression by host

- [ ] P0: workspace identity/containment, approvals, cancellation, server orphan recovery and evidence-based tool completion.
- [ ] P1: project index refresh, architecture, diagnostics, Mission Control and test-impact evidence.
- [ ] P2/P5: event/crash/service/resource/network/storage/security reporting and approved state-changing operations.
- [ ] P3: containers, SQLite, OpenAPI, runtime correlation and Playwright diagnostics.
- [ ] P4: MCP catalogue, orchestration barriers, checkpoints/resume, Skills context, timeline and artifact verification.
- [ ] P6: Git status/diffs, build/test records, dependency audits, environment probes, change ownership/recovery and workspace-specific project memory.
- [ ] Verify all three hosts use the same workspace policy and approval boundary; confirm no host-specific bypass.

## 6. Credentials — do not skip

The current 3.3.1 release does not DPAPI-protect credentials. Until the migration is implemented, use a dedicated Windows user profile and do not store production credentials in RunMind. Before any production sign-off, require: DPAPI-backed native secret storage; explicit native read/write APIs; opaque IDs in browser/settings state; migration with verification; cleanup of plaintext copies only after successful migration; rollback on failure; cross-host/user-profile tests; and confirmation that logs, exports and project memory never include secret values.

- [ ] Verify no credentials appear in logs, error messages, exported diagnostics, checkpoints, project memory or backup previews.
- [ ] Verify secrets are isolated between Windows user profiles and between host profiles where expected.
- [ ] Complete the DPAPI design/implementation/test milestone before production use.

## 7. Upgrade, UX and soak tests

- [ ] Upgrade from 3.3.0 to 3.3.1 while preserving provider, project and chat data.
- [ ] Clean install, uninstall, reinstall and verify no stale host processes or locked profiles remain.
- [ ] Confirm focus/keyboard navigation, screen-reader names, high-contrast presentation and reduced-motion behavior.
- [ ] Run a multi-hour session with repeated chat, Agent, workspace indexing, tool-result, history and WebView lifecycle activity.
- [ ] Monitor memory/CPU growth and verify close/reopen releases process and profile locks.
- [ ] Record known issues and sign the acceptance record for each host.

## 8. Final release decision

Do not mark the release Windows-certified or production-ready until: all three host builds succeed; P0–P6 acceptance passes; WebView2 and native operations pass; DPAPI credential storage/migration is implemented and verified; NuGet vulnerability/SBOM review is clean or has documented accepted exceptions; upgrade/uninstall tests pass; and remaining accessibility/reliability findings have owners and disposition.
