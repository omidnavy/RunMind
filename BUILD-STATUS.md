# RunMind 3.4.0 Preview 1 — Runner Source Status

## Baseline and change scope

- Baseline: RunMind 3.3.1 Stabilization, SHA-256 verified before extraction and modification.
- Added the optional native WPF `RunMind.Runner` project to the solution. Existing Standalone, PowerToys Run and Command Palette code is not modified for Agent behavior in this preview.
- Runner is launcher-only and does not reference the Full Agent bridge, workspace bridge, WebView2, MCP or Skills.
- Added bounded Start Menu application discovery, explicit shortcut launch, rooted path/HTTP(S) open, hotkey/tray/single-instance lifecycle, current-user settings, optional sign-in startup, installer/uninstaller and release publishing.
- No Quick Agent or Agentic Mode integration is included yet. That is the next milestone and must be implemented once in a restricted shared Core before host adapters are added.

## Validation environment

The packaging environment is Linux with Node.js 22.16.0 and Python 3.13. No .NET SDK is installed. Therefore the new WPF project, full solution, release publish, PowerShell installer/uninstaller, Win32 hotkey registration, tray behavior and Windows registry integration have **not** been compiled or executed here. Do not treat this source preview as a Windows-certified release.

Portable validation: **260/260 regression tests passed** with `node --test --test-concurrency=1 tests/*.test.cjs` on Node.js 22.16.0, including five new Runner source-contract tests. XML parsing and source checks are recorded with this preview. This is not a C# compile or Windows runtime test. Run the Windows acceptance checklist on the target machine before distributing it.

## Windows build and acceptance

On Windows with .NET 10 SDK and the Desktop Runtime, run:

```powershell
.\Build-Release-x64.bat
.\Install-RunMind-Runner.ps1
```

Then complete `docs/RUNNER-IMPLEMENTATION.md`. Verify Runner independently installs/uninstalls and leaves the three existing hosts unchanged.

## Existing 3.3.1 gates remain open

- Credential-at-rest protection with native DPAPI-backed storage and migration/rollback verification remains open for the Full Agent hosts.
- NuGet/transitive dependency review and SBOM remain open.
- Existing three-host Windows Release x64 and PowerToys/WebView2 acceptance remains open.
- Accessibility, performance, upgrade, long-session and end-to-end acceptance remain open.

Adding Runner does not close or waive any of these gates. Do not declare the product production-ready before they are resolved.
