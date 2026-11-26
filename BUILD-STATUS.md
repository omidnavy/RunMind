# RunMind 3.3.1 — Stabilization Build Status

## Source baseline and scope

- Baseline: exact RunMind 3.3.0 P6 ZIP, SHA-256 verified before extraction and modification.
- Release: RunMind 3.3.1 Stabilization, shared by Standalone, PowerToys Run and PowerToys Command Palette.
- No new runtime package dependency or project-language support was added.
- Changes are limited to process-output/timeout hardening, WebView origin/navigation/CSP hardening, WebView2 installer coordination, version metadata, tests and documentation.

## Portable validation

- Runtime used: Node.js 22.16.0; target remains Node.js 24+.
- Full regression suite: **255/255 passed** with `node --test --test-concurrency=1 tests/*.test.cjs`.
- Stabilization-focused suite: **8/8 passed** with `node --test tests/stabilization-hardening.test.cjs`.
- Related legacy P0/P2/P3/P4/P6 contract and version tests: **61/61 passed** in the focused compatibility run.
- JavaScript syntax: **45/45 passed** (`node --check`) across shared non-vendor scripts and test harnesses.
- JSON validation: **2/2 passed** for source/package configuration JSON.
- XML/MSBuild/XAML validation: **6/6 passed**.
- Version metadata: **5/5 coherent** across all host packages, plugin manifest and Appx manifest.
- Static review confirms no remaining unbounded `ReadToEndAsync()` calls in source C#; remaining `ReadToEnd()` is the Native Reader decoding an already size-bounded byte array.
- Source baseline checksum and original ZIP CRC passed. Final-release checksum and extracted-ZIP verification are recorded in the release delivery after packaging.

## Windows build and runtime status

**Not certified.** The packaging environment is Linux, Node.js is 22.16.0, and the .NET 10 SDK is not installed. It cannot build or execute the Windows WPF/WebView2 hosts, install/test the Command Palette MSIX, exercise PowerToys Run, call Windows Event Log/CIM/PowerShell, validate DPAPI, or inspect Windows ACLs. Portable source tests are not a substitute for those checks.

On the target Windows machine, use Node.js 24+ and .NET 10, then run:

```powershell
.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64
```

Complete `docs/STABILIZATION-WINDOWS-ACCEPTANCE.md` and every P0–P6 Windows acceptance checklist in all three hosts. Capture build logs and acceptance results. Do not call the release production-ready before the critical security and Windows-certification gates are closed.

## Security and dependency gates

- **Credential at rest — open:** provider API keys, Reader credentials and MCP bearer tokens are stored in WebView2 `localStorage`; Command Palette inline provider credentials are stored in `%LOCALAPPDATA%\RunMind\Shared\inline-model-settings.json`. These are not protected with Windows DPAPI in this release. Do not use a shared Windows account/profile for secrets. A native DPAPI-backed secret store and migration/rollback test are required before production use.
- **NuGet dependency audit — incomplete:** no `dotnet restore`, `dotnet list package --vulnerable`, SBOM generation or Windows compilation could be run here. `Microsoft.Web.WebView2` remains pinned at 1.0.4314.35; NuGet lists 1.0.4370.26 as a newer stable package updated 2026-11-21. It was not adopted without the Windows build/compatibility test. `Microsoft.CommandPalette.Extensions` 0.12.260812002 is listed as the current stable version; `Microsoft.Windows.CsWinRT` 2.3.1 is the current stable version, while newer 3.0.0 entries are previews; `Shmuelie.WinRTServer` 2.2.1 remains pinned. Confirm all versions and transitive advisories on the target machine before certification.
- **PowerToys/WebView2 runtime compatibility — open:** live navigation, new-window handling, CSP behavior, extension activation, WebView2 runtime setup and package identity must be validated on supported Windows builds.
- **Code signing/distribution — open:** release build signing and trust establishment must be validated on the intended distribution devices; the repository's self-signed certificate workflow is for controlled machine/test use, not public distribution.

## Known limitations not changed by this patch

- CI results are user-supplied; no provider is queried automatically.
- Build/test flakiness and environment-sensitive diagnoses are heuristic and based on recorded/supplied evidence.
- Project memory remains bounded and local to a WebView profile; it is not automatically synchronized between the three hosts.
- Native operation coverage continues to depend on Windows edition, privileges, drivers and available counters; unsupported telemetry remains unavailable/unknown.
- This patch does not perform a full accessibility/usability study, long-duration soak test, Windows Defender/SmartScreen reputation test, or signed production installer validation.
