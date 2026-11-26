> **Archive:** Historical development record. This file describes the source state at the time it was written; use the current README, build guides, feature matrix, and feature-specific documentation for present behavior.

# Windows build correction — Complete Runner Fix 2

## Input build report

The next user-provided Release x64 build showed that Standalone, PowerToys Run and Command Palette compiled successfully, while Runner reported 24 errors and one nullable warning. All reported failures were confined to Runner.

## Source corrections

1. Added explicit `System.IO` imports to `RunnerInlineCredentialsStore.cs` and `RunnerInlineSettings.cs`, covering `Path`, `File`, `Directory`, `FileInfo`, `FileAttributes`, `InvalidDataException` and related filesystem APIs.
2. Added a `WpfApplication` alias for `System.Windows.Application` and used it for WPF resource access in `RunnerThemeManager.cs`.
3. Added a `WpfClipboard` alias for `System.Windows.Clipboard` and used it for both clipboard actions in `MainWindow.xaml.cs`.
4. Renamed the local parenthesized expression result to `parenthesizedValue` in `RunnerPlugins.cs`, avoiding the later `out var value` scope collision.
5. Changed `RunnerInlineModelClient.UsefulMessage` to pattern-match the nullable HTTP status code into a non-null local before formatting it, addressing CS8629.
6. Added a regression contract test covering the explicit imports, WPF aliases, calculator local name and nullable status-code pattern.

No new runtime dependency or project reference was added. The changes are limited to Runner source, its regression test and documentation.

## Portable source validation

- Full Node.js regression suite: 272/272 passed.
- Runner-specific contract suite: 17/17 passed.

These tests validate source contracts and portable behavior; they are not a C# compilation.

## Validation boundary

The source/package validation environment does not contain the .NET SDK, MSBuild, C# compiler or Windows Desktop targeting packs. These corrections have not been compiled in this environment. Run the complete Release x64 build on Windows:

```powershell
.\Build-Release-x64.bat
```

A clean build must include all four projects. Then perform the Runner Windows acceptance checks documented in `RUNNER-IMPLEMENTATION.md`.
