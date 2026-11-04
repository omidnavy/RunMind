# RunMind 1.6.12 — build fix and validation report

## Scope

This update is based on the complete RunMind 1.6.11 source package. The review covered both WPF projects, the shared native helpers, the shared chat page, plugin metadata, build script, bundled PowerToys reference assemblies, documentation, license, screenshots, and branding assets.

## Changes in 1.6.12

- Fixed the PowerToys plugin compile error in `Main.CreateSettingPanel`. Returning `new Grid()` was invalid because WPF `Grid` derives from `Panel`, whereas the PowerToys `ISettingProvider.CreateSettingPanel()` contract returns `System.Windows.Controls.Control`.
- Restored the documented PowerToys plugin pattern used by Microsoft's own System plugin: throw `NotImplementedException` for the legacy custom panel because RunMind provides settings through `AdditionalOptions` and does not implement a custom settings UI.
- Kept both projects on `net10.0-windows10.0.22621.0` and updated all release metadata to `1.6.12`.
- Documented that `Community.PowerToys.Run.Plugin.Lint` 0.6.0's `net9.0` rule conflicts with the current official PowerToys plugin checklist, which specifies .NET 10. The separate `Repository missing` check cannot be truthfully satisfied until the project has a public GitHub repository and matching metadata.

## Static checks performed

- Verified both project files target `net10.0-windows10.0.22621.0`; no project was downgraded to .NET 9.
- Verified `CreateSettingPanel` returns no `Grid` and follows the `NotImplementedException` pattern.
- Verified plugin JSON parses and its version matches both project versions.
- Verified both MCP client identity strings match `1.6.12`.
- Parsed both project files and all XAML files as XML.
- Extracted the inline JavaScript from `ChatWindow.html` and checked it with `node --check`.
- Checked HTML ID uniqueness and literal DOM ID selectors.
- Verified all five bundled PowerToys reference assemblies and declared icon assets exist.
- Checked local Markdown links and archive integrity.

## Build limitation

The current environment does not include the .NET SDK or Windows runtime, so a real WPF compilation could not be executed here. Run `Build-Release-x64.bat` on Windows. The original `Grid` to `Control` compile error is removed in source; the Windows build is still the final confirmation.

## Lint note

The community linter's version 0.6.0 documentation lists `net9.0` as a validation rule, while Microsoft's current PowerToys plugin checklist specifies `net10.0-windows10.0.22621.0`. Keep the .NET 10 target. `Repository missing` remains until a public GitHub repository and matching plugin metadata are available.
