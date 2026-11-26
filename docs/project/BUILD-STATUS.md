# Build and validation status

**Source snapshot:** RunMind 3.4.1.  
**Target platform:** Windows 11 x64, `net10.0-windows10.0.22621.0`.  
**Status:** Portable JavaScript regression tests and static version/link checks pass in the available environment. Native Windows release certification remains open.

This page records checks performed for the 3.4.1 metadata and repository-front-page update. It deliberately separates portable source checks from Windows/.NET build and runtime acceptance.

## Host layout

| Host | Role | Expected build output |
|---|---|---|
| Standalone | Full Agent workspace | `Release\Standalone\win-x64\publish\RunMind.exe` |
| PowerToys Run | Full Agent host | `Community.PowerToys.Run.Plugin.RunMind\bin\x64\Release\` |
| Command Palette | Full Agent host / MSIX | `Release\CommandPalette\x64\RunMind.CommandPalette.msix` |
| Runner | Restricted Quick Host | `Release\Runner\win-x64\publish\RunMind.Runner.exe` |

The release script builds all four projects and packages/signs the Command Palette extension. Building and installing are separate operations.

## Checks performed for this update

- **Portable regression suite:** 326 tests passed, 0 failed (`node --test --test-concurrency=1`).
- **Test runtime:** Node.js `v22.16.0`. Project guidance targets Node.js 24+; repeat the suite under Node.js 24+ on the target Windows toolchain before certification.
- **Version metadata:** all four host project versions, the PowerToys Run plugin manifest, Command Palette MSIX project and manifest versions, MCP client identification, and Runner uninstall display version are statically aligned at `3.4.1` (MSIX four-part version `3.4.1.0`).
- **README references:** 27 local Markdown/HTML link and image references in the repository front page resolve from the repository root, including all four bundled screenshots.
- **Scope:** this update changes release/version metadata, the associated regression assertion, current release documentation, and `README.md`. It does not add application features or change feature behavior.

## Not verified here

No Windows/.NET release build, MSIX packaging, PowerToys deployment or WebView2 runtime acceptance is claimed by this documentation update. The `.bat` and `.ps1` operational scripts were not executed as part of this source-only release pass.

On Windows with Node.js 24+ and the .NET 10 SDK, run [`Validate-RunMind-Windows.ps1`](../../Validate-RunMind-Windows.ps1). It runs the portable suite, builds Release x64, and verifies host outputs; it does not install hosts. Then complete the [P7 Windows acceptance checklist](../P7-WINDOWS-ACCEPTANCE.md), the [Process Intelligence Windows checklist](../system/PROCESS-INTELLIGENCE-WINDOWS-ACCEPTANCE.md), and applicable [Runner acceptance checks](../runner/RUNNER-IMPLEMENTATION.md).

## Release gates still open

1. **Windows certification:** build all four hosts, package and install Command Palette, deploy PowerToys Run, install Runner, verify generated `3.4.1` assembly/package versions, and complete current acceptance checklists on the target Windows installation.
2. **Credential protection at rest:** Full Chat provider API keys/custom headers, Jina/Reader credentials, and MCP bearer tokens are stored in WebView2 `localStorage`; PowerToys Run and Command Palette inline credentials are stored in `%LOCALAPPDATA%\RunMind\Shared\inline-model-settings.json`. These stores are not DPAPI-encrypted. Runner inline provider credentials use a separate DPAPI CurrentUser store. A native DPAPI vault with transactional migration and rollback remains an important security task.
3. **Dependency assurance:** perform NuGet restore and vulnerability/license review on Windows, inspect the transitive dependency graph, and produce an SBOM. No current vulnerability scan or SBOM result is claimed here.
4. **OCR resource reproducibility:** the npm tarball URLs use fixed versions but are not checked against committed SHA-512 integrity values; English/Persian trained data follows the mutable `tessdata_fast/main` branch and is not checksum-pinned.

## Evidence and reporting rules

- Keep generated build/test logs and release evidence outside the source tree; do not commit transient `.log` transcripts.
- Distinguish a passing source-level test from native build/runtime verification.
- Distinguish an accepted action request from a verified state change.
- Report unavailable Windows counters or metadata as unavailable, never as a zero or a healthy result.
- Historical reports are indexed in [`docs/archive/validation-reports/`](../archive/validation-reports/README.md); they are snapshots, not current certification.
