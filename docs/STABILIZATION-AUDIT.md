# RunMind 3.3.1 — Stabilization Audit

**Audit date:** 2026-11-23  
**Baseline:** RunMind 3.3.0 P6 exact ZIP, checksum-verified before modification  
**Scope:** Shared Agent Core/native bridge, three host integrations, WebView2 boundary, process execution, release metadata, portable regression coverage and dependency pins.

## Executive result

RunMind 3.3.1 addresses three actionable hardening defects found in the source review: unbounded process output capture in Windows diagnostics/service inventory, inconsistent Standalone WebView2 navigation policy, and indefinite WebView2 prerequisite coordination/installer waits. Native WebView message dispatch is now origin-checked consistently across the three hosts, and inline JavaScript is removed from the chat document CSP. Eight stabilization regression tests were added. The full portable suite passes **255/255** under Node.js 22.16.0.

This is not a production sign-off. Windows builds/runtime behavior were not available in the audit environment. Credential-at-rest protection is still insufficient for production: several credentials remain in browser localStorage or a plaintext per-user JSON settings file. Dependency vulnerability reporting and SBOM generation also remain unverified because .NET/NuGet restore could not run.

## Source changes and findings

| Area | Finding in 3.3.0 | 3.3.1 action | Remaining verification |
|---|---|---|---|
| Windows diagnostic processes | `RunFixedDiagnosticCommand` and legacy service inventory read complete stdout/stderr before clipping, so a noisy utility could allocate unbounded memory | Both paths now concurrently drain through the shared 512 KB-per-stream reader; timeout kills process tree and cleanup is bounded | Exercise WMI/SC/CIM utilities and timeout paths on Windows |
| Service inventory | Separate process implementation duplicated capture/wait logic | Reuses the common fixed-diagnostic bounded process path and configured timeout | Verify real service inventory and settings value 0 on Windows |
| Standalone navigation | WebView2 could retain non-HTTP/custom schemes and did not apply default-deny policy | Navigation denies by default; only exact mapped chat document stays embedded; supported external HTTP/HTTPS/mailto links open externally only when credential-free | Validate Edge/WebView2 runtime navigation behavior and external handlers |
| Native message origin | Some message handlers applied their own partial checks while dispatch paths were inconsistent | All relevant WebView message entry points require HTTPS localhost/default port/no userinfo/no fragment/exact `/ChatWindow.html` before dispatch | Attempt spoofed origins and messages on all three hosts |
| Chat page CSP | Inline JavaScript was permitted | Removed `unsafe-inline` from `script-src`; scripts are loaded from the local resource tree | Validate all interactive flows and local WebAssembly use under deployed CSP |
| WebView2 setup | Installer coordination and installer process waits could block indefinitely | Two-minute coordination/lock bounds and five-minute installer execution cap with process-tree termination | Test concurrent first launch, missing runtime, offline, permission failure and hung installer |
| Version consistency | 3.3.0 metadata | Synchronize all host packages, Appx identity, plugin manifest and MCP client version to 3.3.1 | Confirm generated MSIX/package metadata in Windows build |

## Security review findings retained as blockers

### 1. Credential protection at rest — critical production blocker

The source persists: provider API keys/authentication headers, Jina/custom Reader credentials and MCP bearer tokens in WebView2 profile `localStorage`; Command Palette and PowerToys Run inline provider API keys/custom authentication headers in `%LOCALAPPDATA%\RunMind\Shared\inline-model-settings.json`. These stores are not protected by a RunMind DPAPI secret-vault abstraction. The UI masks secret entry and code avoids writing credentials into intended diagnostic output, but neither mitigates local-at-rest exposure to same-user processes, profile access or malware running under that account.

**Required before production:** add a native Windows DPAPI-backed secret store scoped to the current user; use opaque credential IDs in browser/ordinary settings; migrate secrets transactionally; verify retrieval and provider requests; then remove old secret copies only after success. Include import/upgrade/rollback, multi-host contention, user-profile separation, backup/restore and failure-injection tests. Do not implement an untested one-way migration during this patch.

### 2. Windows/.NET certification — critical release blocker

The audit host is Linux, has Node.js 22.16.0 and has no .NET SDK. It cannot build net10.0-windows projects or test WebView2, PowerToys, WinRT COM activation, MSIX installation, DPAPI, Registry, Event Log, CIM or PowerShell. Portable tests establish source-level contracts, not Windows behavior.

### 3. NuGet dependency audit — open

No restore, `dotnet list package --vulnerable`, transitive dependency inspection or SBOM generation was possible. Package pins observed in the project:

- `Microsoft.Web.WebView2` 1.0.4314.35 across hosts. NuGet lists 1.0.4370.26 as a newer stable release updated 2026-11-21, alongside a newer prerelease; upgrade only after Windows build and PowerToys regression testing.
- `Microsoft.CommandPalette.Extensions` 0.12.260812002; NuGet lists this as the current stable Command Palette SDK. The extension SDK is pre-1.0 and should remain pinned until its compatibility notes and host tests pass.
- `Microsoft.Windows.CsWinRT` 2.3.1; newer 3.0.0 entries are prereleases in the queried package list, so no preview upgrade is recommended for stabilization.
- `Shmuelie.WinRTServer` 2.2.1 remains pinned; confirm its upstream maintenance/security status and transitive graph before public distribution.
- Windows SDK build-tools/MSIX package pins remain as in 3.3.0.

A newer package version is not evidence that the pinned version is vulnerable. Run a vulnerability report and evaluate release notes on Windows; update only packages with compatibility evidence.

## Cross-host architecture review

- All three host projects link the shared chat page/resources and native bridge implementation; this patch keeps hardening logic shared rather than forking behavior.
- Standalone and Command Palette use the shared WPF window implementation; PowerToys Run has its own host wiring and now applies the same origin requirements.
- WebView host objects remain disabled; native functionality is exposed through named message handlers rather than arbitrary COM host objects.
- Project changes remain restricted to JavaScript, TypeScript, JSX/TSX, HTML and CSS, with SVG treated as a vector asset. No additional project-language analyzers or toolchains were added.
- Process invocations reviewed use fixed executable/argument construction and bounded output/timeout paths where applicable. The remaining Native Reader `StreamReader.ReadToEnd()` reads a byte array after `ReadBoundedBytesAsync` enforces the response byte cap; it is not an unbounded process stream.
- User-configured System Manager timeout of zero remains intentional no-automatic-timeout behavior; it should be used only when the operator explicitly wants that policy.

## Validation evidence

- Original release checksum matched the supplied sidecar before extraction. Original ZIP integrity check passed.
- `node --test --test-concurrency=1 tests/*.test.cjs`: **255/255 passed**.
- `node --test tests/stabilization-hardening.test.cjs`: **8/8 passed**.
- Focused carried-forward P0/P2/P3/P4/P6 contract tests: **61/61 passed**.
- Static source grep: no `ReadToEndAsync()` calls remain; the only `ReadToEnd()` occurrence in shared C# is Native Reader decoding of size-bounded response bytes.
- A diff against the unmodified 3.3.0 extraction was reviewed; changes are confined to three host metadata files, shared hardening files, the new hardening tests and related test expectations. Release documentation is updated separately.

## Prioritized close-out plan

1. **P0 — Credential security:** implement and test DPAPI-backed credentials/migration/rollback.
2. **P0 — Windows release certification:** build Release x64 for all three hosts with Node.js 24+/.NET 10; complete P0–P6 acceptance matrix.
3. **P1 — Dependency assurance:** restore pinned graph, run NuGet vulnerability audit, inspect transitive licenses/advisories, create SBOM, review WebView2 1.0.4370.26 and pinned WinRT/Command Palette dependencies.
4. **P1 — Real-world reliability:** exercise missing/installed WebView2 runtime, simultaneous launches, service command timeouts, high-output process utilities, provider auth, MCP, project builds/tests, Git, native approval boundaries and recovery flows.
5. **P2 — Release engineering:** test clean install, upgrade from 3.3.0, settings/profile migration, uninstall/reinstall, MSIX signing/trust on intended devices and log/export behavior.
6. **P2 — User experience:** keyboard-only navigation, screen-reader labels, focus restoration, high contrast, reduced motion and long-session memory/performance checks.

**Decision:** accept 3.3.1 as a portable-tested stabilization candidate. Do not mark it stable/production-ready until P0 items are completed and signed-off evidence is recorded.
