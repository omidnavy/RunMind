# RunMind Runner — 3.4.0 Preview 1.1

## Purpose

RunMind Runner is an optional, standalone Windows quick launcher. It is a fourth entry point and has no dependency on PowerToys Run or PowerToys Command Palette. It can be installed, started, configured and removed independently of RunMind Standalone.

This first increment is intentionally a launcher, not an AI agent. It does not load the Full Agent, `AgentHostBridge`, WebView2, the workspace bridge, MCP, Skills, project indexing or developer infrastructure. The Quick Agent and Agentic Mode are separate follow-up work and must not be smuggled into this launcher through UI-only hiding.

## Current source capabilities

- Native WPF launcher window with quick search and keyboard navigation.
- Search of bounded Start Menu `.lnk` entries in the current-user and all-users Programs folders. Traversal skips reparse points, ignores inaccessible locations, stops at depth 8 and caps the catalogue at 3,500 shortcuts.
- Explicit launch of a selected Start Menu shortcut.
- Explicit opening of rooted files/folders and HTTP/HTTPS URLs. Direct file targets with executable/script/installer/shortcut extensions are rejected. No arbitrary command-line parsing or shell command execution is exposed.
- Global summon shortcut, initially `Ctrl+Alt+Space`, registered by Windows and configurable per current user.
- Tray lifecycle, single-instance behavior, Escape-to-hide, and an explicit Exit command.
- Optional current-user sign-in startup. It is disabled by default and writes only the RunMind Runner value under `HKCU` Run. Uninstall removes this registration and the application. The uninstaller accepts `-KeepSettings` when the user wants to retain the small Runner settings folder for a future reinstall.
- Atomic per-user JSON settings under `%LOCALAPPDATA%\RunMind\Runner`.
- Separate per-user installation and uninstall scripts. No administrator rights or PowerToys installation is required.

## Deliberate non-goals

- No workspace/project folder selection, indexing or source analysis.
- No arbitrary process execution, shell, PowerShell or command runner.
- No system-management actions, process/service mutation, network diagnostics or Windows power controls in this first launcher-only increment.
- No LLM provider configuration, chat, MCP, Skills or Full Agent initialization.
- No automatic startup unless explicitly enabled in Runner Settings.

## Follow-up architecture: Quick Agent

Quick Agent will be a distinct constrained Agent experience shared by PowerToys Run, Command Palette and Runner. Its capability registry and security policy must be enforced at the execution boundary. It must not expose Full Agent developer tools, workspace/project access, MCP, Skills or arbitrary tool discovery. Each host will have an independent mode setting, bounded by global policy:

1. Chat only — no tool execution.
2. Agentic — actions require approval.
3. Quick actions — only explicitly permitted low-risk actions may run automatically; sensitive actions remain confirmation-gated.

This future layer will reuse one approval/security system and one Quick Agent Core across the three quick surfaces. Runner must remain responsive without loading that layer until it is enabled and needed.

## Preview 1.1 build corrections

The follow-up preview corrects the compile errors reported by the first Windows Release x64 build. See `WINDOWS-BUILD-FIXES-PREVIEW-1.1.md` for the exact source changes and remaining validation boundary.

## Build and install

On Windows with the .NET 10 SDK:

```powershell
.\Build-Release-x64.bat
.\Install-RunMind-Runner.ps1
```

Optional desktop shortcut:

```powershell
.\Install-RunMind-Runner.ps1 -DesktopShortcut
```

Uninstall only Runner:

```powershell
.\Uninstall-RunMind-Runner.ps1
```

Retain Runner preferences while removing the executable:

```powershell
.\Uninstall-RunMind-Runner.ps1 -KeepSettings
```

The build publishes a framework-dependent `win-x64` executable to `Release\Runner\win-x64\publish`. The target machine needs the .NET 10 Desktop Runtime unless a future release changes the publish mode.

## Windows acceptance still required

- Build the complete solution in Release x64 with .NET 10 and capture the output.
- Install Runner as a standard user and verify Start Menu shortcut creation/removal.
- Verify hotkey conflict handling, hide/show, tray click/menu, duplicate launch behavior and clean exit.
- Verify the startup toggle adds/removes only the current-user Runner Run value, and that `--background` starts hidden.
- Exercise Start Menu paths with inaccessible folders, reparse points and high shortcut counts.
- Verify direct path/URL input, including blocked executable/script extensions and rejection of non-HTTP(S) schemes.
- Verify Runner can be installed/uninstalled while Standalone and both PowerToys hosts remain untouched.
- Check memory/CPU footprint while idle and after the catalogue has loaded.

This environment has no .NET SDK and is not Windows, so the WPF project has not been compiled or run here. Portable tests and XML/source checks do not certify the Windows executable or native hotkey/tray behavior.


## Preview 1.2 build corrections

Preview 1.2 follows the second target-machine compiler report. It adds explicit `System.IO` imports for Runner file/path operations, fully qualifies WPF message boxes in the presence of WinForms imports, fixes the global-hotkey switch expression's `uint` type, restores the shared no-timeout process wait helper, and renames the scheduled-task inventory script local to avoid C# scope shadowing. The portable source-contract suite checks these regression points. This does not replace a Release x64 build with the .NET 10 SDK on Windows.
