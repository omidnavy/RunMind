# Build and validation status

**Source:** RunMind 3.4.0 — documentation and repository hygiene pass.  
**Target:** Windows 11 x64, `net10.0-windows10.0.22621.0`.  
**Status:** Portable source checks can be run in this environment; native Windows release certification remains a target-machine task.

## Current host model

| Host | Role | Build output |
|---|---|---|
| Standalone | Full Agent workspace | `Release\Standalone\win-x64\publish\RunMind.exe` |
| PowerToys Run | Full Agent host | `Community.PowerToys.Run.Plugin.RunMind\bin\x64\Release\` |
| Command Palette | Full Agent host / MSIX | `Release\CommandPalette\x64\RunMind.CommandPalette.msix` |
| Runner | Restricted Quick Host | `Release\Runner\win-x64\publish\RunMind.Runner.exe` |

The release pipeline builds all four projects and packages/signs the Command Palette extension. Build and installation are separate operations.

## Current source verification

The repository hygiene pass ran the full portable Node regression suite: **326/326 passed** under Node.js **22.16.0**. Additional static checks passed: 62 JavaScript/CJS/MJS files; 4 JSON files; 13 XML/XAML/MSBuild/manifest/SVG files; 22 CSS files with 850 top-level rules and zero parser errors; 98 Markdown files, 72 local Markdown links with zero broken targets, and 429 unique ChatWindow HTML IDs. The project target for tests/developer integrations is Node.js 24+; rerun the suite under Node.js 24 on Windows for target-runtime evidence.

Repository information-file inventory after cleanup: 98 Markdown files (52 archived historical records including the archive index), 3 text files (the product README plus two license files), 7 PowerShell scripts, 3 batch scripts, no `.bar` files and no committed `.log` transcripts. The exact duplicate validation report and five generated log transcripts were removed.

Run `Validate-RunMind-Windows.ps1` on Windows with Node.js 24+ and the .NET 10 SDK to rerun the portable suite, build Release x64 and verify all host outputs. The validation script does not install any host. Keep detailed command/build output in CI or release evidence outside the source tree; do not commit generated `.log` transcripts.

This source cleanup has **not** been certified by a Windows/.NET build. The container had no .NET SDK/MSBuild or Windows runtime, and PowerShell scripts were statically reviewed but not parsed/executed by Windows PowerShell. Do not treat historical build logs or archived reports as proof that the current source compiles or that current installers/process actions work.

## Release and security gates still open

1. **Version metadata consistency:** this source line is labelled 3.4.0, but Standalone, PowerToys Run and Command Palette project metadata remain at 3.3.1 (Command Palette MSIX identity 3.3.1.0); Runner is 3.4.0. Unify and regression-test version/package identity metadata before publishing a uniformly versioned release.
2. **Windows certification:** build all four host projects, package/install Command Palette, deploy PowerToys Run, install Runner, and complete the current P7 and Process Intelligence acceptance checklists.
3. **Credential protection at rest:** Full Chat provider API keys/custom headers, Jina/Reader credentials and MCP bearer tokens are stored in WebView2 `localStorage`; PowerToys Run and Command Palette inline credentials are stored in `%LOCALAPPDATA%\RunMind\Shared\inline-model-settings.json`. These stores are not DPAPI-encrypted. Runner's inline provider credentials use a separate DPAPI CurrentUser store. A native DPAPI vault plus transactional migration/rollback remains an important security task.
4. **Dependency assurance:** run NuGet restore and vulnerability review on Windows, inspect the transitive dependency graph and licenses, and produce an SBOM. No current vulnerability/SBOM result is claimed here.
5. **OCR language-data reproducibility:** the npm tarball URLs use fixed versions but their SHA-512 integrity values are not verified; English/Persian trained-data follows the mutable `tessdata_fast/main` branch and is not checksum-pinned.

## Reporting rules

- Distinguish source-level tests from native build/runtime verification.
- Distinguish an accepted request from a verified state change.
- Report unavailable Windows counters/metadata as unavailable; do not turn missing telemetry into a zero or a healthy result.
- Record build logs and acceptance evidence in the release system or CI artifact store, not as permanent root-level repository files.
