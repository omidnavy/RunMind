# RunMind 2.8.1 — P1 Mission Control Refinement Build Status

## Current source bundle

This source tree contains the P0 foundation and P1 Project Intelligence implementation for all three Windows hosts: Standalone, PowerToys Run and PowerToys Command Palette. P1 includes Project Understanding improvements, static Project Diagnostics, Project Mission Control (versioned Agent report and structured Agent Settings dashboard with freshness, coverage, prioritized evidence and optional test-impact input), Test Impact Analysis, and static Release Readiness. All five capabilities reuse the P0 persistent index and unified workspace.

Language-aware project analysis and project-specific tooling remain limited to JavaScript, TypeScript, JSX/TSX, HTML and CSS. Other source files may be read/searched as generic text only.

## Validation actually performed

- Node.js `v22.16.0`.
- Full portable suite: **177/177 passed** with `node --test --test-concurrency=1`.
- JavaScript/CJS/MJS syntax: **42/42 passed**, including the bundled Acorn and TypeScript parser files and the Mission Control logic tests.
- Repository JSON: **4/4 parsed**.
- Project/property/XAML/AppxManifest XML-family files: **7/7 parsed**.
- Standalone, PowerToys Run, Command Palette and Appx package versions agree on **2.8.1 / 2.8.1.0**.
- ZIP CRC/integrity and SHA-256 are verified in the final release bundle.
- C# compilation and Windows runtime behavior were not run in this environment; P0/P1 Windows acceptance remains pending.

## P1 capability boundaries

- Project Understanding is static/index-based; important call paths and architectural claims require source inspection.
- Project Diagnostics is read-only static analysis. Environment probes, builds, tests and dependency audits require separate explicit approval and actual execution.
- Project Mission Control is a structured evidence report over the existing index; it incrementally refreshes stale index data when possible, prioritizes findings and displays supporting paths. It does not maintain a separate database or run project commands. See `docs/PROJECT-MISSION-CONTROL.md` for report fields, status semantics, freshness behavior and safety boundaries.
- Test Impact Analysis accepts explicit changed paths, follows static reverse imports, and reports confidence. It does not infer Git changes or execute tests.
- Release Readiness is static only. Git cleanliness, live vulnerability results, actual builds/tests, artifact signatures, SBOM, package creation and publication remain unverified until separately checked.

## P0 Windows certification remains pending

This environment does not contain the .NET SDK or Windows runtime. Run on Windows with Node.js 24+ and .NET 10:

```powershell
.\Validate-Phase1-Windows.ps1 -Configuration Release -Platform x64
```

Then complete `docs/P0-WINDOWS-ACCEPTANCE.md` in Standalone, PowerToys Run and Command Palette. Do not describe this source bundle as Windows-certified until both checks pass.
