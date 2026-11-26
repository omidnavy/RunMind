# RunMind 3.4.0 Preview 1.2 — Runner Build-Fix Status

## Baseline and change scope

- Baseline: RunMind 3.3.1 Stabilization and Runner Preview 1.1 source, extracted from the verified Preview 1.1 archive.
- Follow-up: Preview 1.2 addresses the second target-machine Release x64 compiler report. See `docs/WINDOWS-BUILD-FIXES-PREVIEW-1.2.md`.
- Runner remains an optional, separate native WPF executable. Standalone, PowerToys Run and Command Palette share only the existing application source; Quick Agent has not been added.
- Runner remains launcher-only and does not reference Full Agent, workspace/project intelligence, `AgentHostBridge`, WebView2, MCP or Skills.
- No new runtime package dependency was added.

## Preview 1.2 corrections

- Explicit `System.IO` imports in Runner settings and path/command operations.
- Explicit WPF `MessageBox` qualification where WinForms is also enabled.
- Consistent unsigned type in global hotkey modifier selection.
- Restored the shared process wait helper for the documented no-automatic-timeout (`0`) mode.
- Renamed the scheduled-task inventory PowerShell script local to avoid C# local-name shadowing.
- Extended the source-contract regression test to cover these compiler failures.

## Validation environment

The packaging environment is Linux with Node.js 22.16.0 and Python 3.13. No .NET SDK, MSBuild or Windows runtime is installed. Therefore the WPF projects, full Release x64 solution, publish output, PowerShell installer/uninstaller, Win32 hotkey registration, tray behavior and Windows registry integration have **not** been compiled or executed here.

Portable validation: **261/261 tests passed** with `node --test --test-concurrency=1 tests/*.test.cjs` on Node.js 22.16.0. All 11 project/XAML/manifest/config XML files parsed successfully. This is source-level regression validation, not a C# compile or Windows runtime test.

## Windows build and acceptance

On Windows with the .NET 10 SDK and Windows Desktop build components, run:

```powershell
.\Build-Release-x64.bat
```

If the full solution builds successfully, optionally install Runner separately:

```powershell
.\Install-RunMind-Runner.ps1
```

Then complete `docs/RUNNER-IMPLEMENTATION.md` and the Windows acceptance checklist. Verify Runner independently installs/uninstalls and leaves the other three hosts unchanged.

## Existing 3.3.1 gates remain open

- Credential-at-rest protection with native DPAPI-backed storage and migration/rollback verification remains open for the Full Agent hosts.
- NuGet/transitive dependency review and SBOM remain open.
- Windows Release x64 and PowerToys/WebView2 acceptance remains open for all hosts.
- Accessibility, performance, upgrade, long-session and end-to-end acceptance remain open.

Adding Runner or fixing source-level compiler errors does not close these gates. Do not declare the product production-ready before they are resolved.
