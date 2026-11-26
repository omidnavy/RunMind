# Build and validation status

**Source snapshot:** RunMind 3.4.0.  
**Target platform:** Windows 11 x64, `net10.0-windows10.0.22621.0`.  
**Status:** Portable JavaScript regression tests pass in the available environment. Native Windows release certification remains open.

This page records checks performed for this source snapshot. It deliberately separates portable source tests from Windows/.NET build and runtime acceptance.

## Host layout

| Host | Role | Expected build output |
|---|---|---|
| Standalone | Full Agent workspace | `Release\Standalone\win-x64\publish\RunMind.exe` |
| PowerToys Run | Full Agent host | `Community.PowerToys.Run.Plugin.RunMind\bin\x64\Release\` |
| Command Palette | Full Agent host / MSIX | `Release\CommandPalette\x64\RunMind.CommandPalette.msix` |
| Runner | Restricted Quick Host | `Release\Runner\win-x64\publish\RunMind.Runner.exe` |

The release script builds all four projects and packages/signs the Command Palette extension. Building and installing are separate operations.

## Checks performed for this documentation pass

- **Portable regression suite:** 326 tests passed, 0 failed (`node --test tests/*.test.cjs`).
- **Test runtime:** Node.js `v22.16.0` on the packaging/test environment. The project guidance targets Node.js 24+; the suite has not been rerun under Node.js 24 in this environment.
- **Documentation links:** 114 local Markdown/HTML link and image targets checked; 0 missing targets.
- **Scope:** this pass changes documentation and documentation paths only. Application source, project files, tests, scripts, assets, and runtime content are preserved.

## Not verified here

No .NET SDK/MSBuild, Windows runtime, or PowerShell executable was available in the environment used for this pass. Therefore:

- No native Windows/.NET build or MSIX packaging was performed.
- No WebView2, PowerToys Run, Command Palette activation, Runner installation, DPAPI, Windows performance-counter, process-handle, or process-action acceptance was performed.
- The `.bat` and `.ps1` operational scripts were not executed or parsed by Windows PowerShell/PowerShell.

Run [`Validate-RunMind-Windows.ps1`](../../Validate-RunMind-Windows.ps1) on Windows with Node.js 24+ and the .NET 10 SDK to run the portable suite, build Release x64, and verify host outputs. That validation script does not install the hosts. Then complete the [P7 Windows acceptance checklist](../P7-WINDOWS-ACCEPTANCE.md), the [Process Intelligence Windows checklist](../system/PROCESS-INTELLIGENCE-WINDOWS-ACCEPTANCE.md), and the applicable [Runner acceptance checks](../runner/RUNNER-IMPLEMENTATION.md).

## Release gates still open

1. **Version metadata consistency:** this source tree is labelled 3.4.0, but Standalone, PowerToys Run, and Command Palette project metadata remain at 3.3.1; the Command Palette MSIX identity is 3.3.1.0. Runner metadata is 3.4.0. Unify and regression-test package/version identities before publishing a uniformly versioned release.
2. **Windows certification:** build all four hosts, package and install Command Palette, deploy PowerToys Run, install Runner, and complete current acceptance checklists on the target Windows installation.
3. **Credential protection at rest:** Full Chat provider API keys/custom headers, Jina/Reader credentials, and MCP bearer tokens are stored in WebView2 `localStorage`; PowerToys Run and Command Palette inline credentials are stored in `%LOCALAPPDATA%\RunMind\Shared\inline-model-settings.json`. These stores are not DPAPI-encrypted. Runner inline provider credentials use a separate DPAPI CurrentUser store. A native DPAPI vault with transactional migration and rollback remains an important security task.
4. **Dependency assurance:** perform NuGet restore and vulnerability/license review on Windows, inspect the transitive dependency graph, and produce an SBOM. No current vulnerability scan or SBOM result is claimed here.
5. **OCR resource reproducibility:** the npm tarball URLs use fixed versions but are not checked against committed SHA-512 integrity values; English/Persian trained data follows the mutable `tessdata_fast/main` branch and is not checksum-pinned.

## Evidence and reporting rules

- Keep generated build/test logs and release evidence outside the source tree; do not commit transient `.log` transcripts.
- Distinguish a passing source-level test from native build/runtime verification.
- Distinguish an accepted action request from a verified state change.
- Report unavailable Windows counters or metadata as unavailable, never as a zero or a healthy result.
- Historical reports are indexed in [`docs/archive/validation-reports/`](../archive/validation-reports/README.md); they are snapshots, not current certification.
