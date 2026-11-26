# RunMind 3.4.0 — Compact UX Windows Build Fix

## Source baseline

`RunMind-3.4.0-Runner-Compact-UX-2026-11-24.zip`

Verified input SHA-256:

`98ccb3f97ad9286b3a0f59d36a7eb480253e2de54f4d72f8eb6264f2ec325b48`

## Fixes

### CS0104 — ambiguous `Button`

`MainWindow.xaml.cs` now declares `WpfButton` as an explicit alias for `System.Windows.Controls.Button` and uses that alias in `Suggestion_Click`. This prevents the implicit Windows Forms `Button` global using from conflicting with WPF.

### CS4014 — unawaited dispatcher operation

`Window_Loaded` now awaits `Dispatcher.InvokeAsync(..., DispatcherPriority.Loaded)` before continuing with background catalog initialization. Initial result sizing and monitor placement therefore finish as part of the awaited startup sequence.

### WFO0003 — conflicting manifest DPI settings

The Runner project now declares:

```xml
<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
```

`App.OnStartup` calls `System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` before `base.OnStartup` and before creating the main window. The duplicated `<dpiAware>` and `<dpiAwareness>` elements have been removed from `app.manifest`.

This retains the intended Per-Monitor-V2 process DPI mode while using the .NET configuration path recommended for a project with Windows Forms enabled. The runtime call is explicit because this project uses WPF startup rather than the Windows Forms `ApplicationConfiguration.Initialize` entry point.

## Validation boundary

Portable Node regression tests and source-structure checks are run against the final extracted archive. This environment does not have .NET SDK/MSBuild or Windows Desktop runtime, so these checks do not prove that the WPF project compiles. Run `Build-Release-x64.bat` on Windows and confirm all four hosts build without the reported Runner error/warnings.
