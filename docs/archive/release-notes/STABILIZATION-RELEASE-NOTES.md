> **Archive:** Historical development record. This file describes the source state at the time it was written; use the current README, build guides, feature matrix, and feature-specific documentation for present behavior.

# RunMind 3.3.1 — Stabilization

RunMind 3.3.1 is a cross-host hardening patch on top of P0–P6. It does not introduce a new feature phase. All three hosts continue to use the shared native bridge and canonical chat UI.

## Improvements

### Native process reliability

- Windows diagnostic utilities and service inventory now use concurrent bounded stdout/stderr readers, retaining at most 512 KB per stream rather than reading the complete process output before truncating.
- Fixed-argument process launch remains based on `ProcessStartInfo.ArgumentList`; it does not pass through a shell.
- Timed-out utility processes are terminated as a process tree, followed by bounded process/output-reader cleanup. A configured timeout of zero continues to mean no automatic timeout, as documented by System Manager.

### WebView2 trust boundary

- Standalone embedded navigation now denies by default and permits only the exact mapped HTTPS localhost `/ChatWindow.html` document.
- Credential-free HTTP/HTTPS/mailto links are opened externally; custom schemes and credentialed URLs are not forwarded to the OS. New-window requests follow the same rule.
- Native WebView message dispatch checks the trusted page source first: HTTPS, `localhost`, default port, no userinfo, no fragment, and exact chat-page path. This applies to Standalone, PowerToys Run and Command Palette shared host code.
- The chat page CSP no longer allows inline JavaScript. Inline styles remain allowed because the current interface uses them; WebAssembly evaluation is retained for supported local browser components.

### WebView2 prerequisite setup

- Per-process install coordination and the cross-process installer lock have finite two-minute waits.
- The Microsoft bootstrapper process has a five-minute execution limit; timeout kills its process tree and waits only a bounded cleanup interval.
- These finite waits prevent a stuck installer/lock from leaving app startup blocked forever.

## Validation performed

- Original 3.3.0 P6 ZIP and SHA-256 sidecar verified before changes.
- Full portable suite: **255/255 passed** under Node.js 22.16.0.
- Stabilization-specific tests: **8/8 passed**.
- Focused carried-forward compatibility tests: **61/61 passed**.
- Static source sweep: no remaining `ReadToEndAsync()` process output reads; the remaining `ReadToEnd()` is bounded Native Reader byte-array decoding.
- The same source changes are linked into all three hosts.

## Explicitly unresolved

- Windows/.NET 10 Release x64 build and acceptance have not been run in this environment.
- API keys, Reader tokens and MCP bearer tokens remain in WebView `localStorage`; Command Palette and PowerToys Run inline credentials remain in a plaintext per-user JSON settings file. They are not DPAPI-protected. A native secret store and migration are required before production use.
- No NuGet restore/vulnerability report/SBOM was generated because the .NET SDK is unavailable. The pinned WebView2 package (1.0.4314.35) is behind the newer stable 1.0.4370.26 listed on NuGet on 2026-11-21. It was not updated without target-Windows build and host compatibility testing.
- PowerToys/Command Palette activation, WebView2 CSP/navigation behavior, real Windows services, PowerShell/CIM telemetry, code signing and installer behavior remain target-machine checks.

This release is a **stabilization candidate**, not a production certification. See `docs/STABILIZATION-AUDIT.md` and `docs/STABILIZATION-WINDOWS-ACCEPTANCE.md`.
