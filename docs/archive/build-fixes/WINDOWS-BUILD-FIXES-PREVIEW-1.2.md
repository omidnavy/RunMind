> **Archive:** Historical development record. This file describes the source state at the time it was written; use the current README, build guides, feature matrix, and feature-specific documentation for present behavior.

# Windows Build Fixes — RunMind 3.4.0 Runner Preview 1.2

## Input

Target-machine Release x64 build output reported 35 C# compiler errors across Runner and the three existing hosts. The failures grouped into five source-level causes.

## Corrections

| Reported failure | Correction |
|---|---|
| `Path`, `File`, `Directory`, `IOException` and `DirectoryNotFoundException` not found in Runner settings/command services | Added explicit `using System.IO;` to both source files. |
| `MessageBox` ambiguous between WPF and Windows Forms | Fully qualified the three Runner calls as `System.Windows.MessageBox.Show`. |
| `GlobalHotkey.cs` switch expression had no best common type | Changed the fallback arm from integer `0` to unsigned `0u`, matching the modifier constants. |
| `WaitForProcessExit` missing in `Shared/SystemManager.cs` | Restored the helper using `Process.WaitForExit()` and returning `true`. A timeout value of zero retains the documented no-automatic-timeout behavior. |
| `script` local shadowing in `SystemManagerScheduledTasks` | Renamed the list-action raw PowerShell string to `inventoryScript` and updated its invocation. |

## Regression validation

- `node --test --test-concurrency=1 tests/*.test.cjs`: **261/261 passed** on Node.js 22.16.0.
- XML parsing: all 11 project/XAML/manifest/config XML files parsed successfully.
- Source-contract assertions cover the five corrections above.
- Release ZIP is extracted and rechecked after packaging.

## What is not verified here

This environment does not have the .NET SDK, MSBuild or Windows runtime. Therefore the C# compiler, WPF generated code, native hotkey P/Invoke, tray lifecycle, and PowerShell installer have not been executed here. A clean target-machine build is still required. Do not treat Preview 1.2 as Windows-certified until that build succeeds.

## Windows verification

From the extracted project root on Windows with the .NET 10 SDK installed:

```powershell
.\Build-Release-x64.bat
```

If compilation succeeds, run the separate Runner installer only when you want Runner installed:

```powershell
.\Install-RunMind-Runner.ps1
```
