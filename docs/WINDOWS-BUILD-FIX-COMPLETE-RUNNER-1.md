# Windows build correction — Complete Runner

## Input build report

The first Release x64 build of `RunMind-3.4.0-Complete-Runner` reported ten C# errors, all in `RunMind.Runner`. The same build output showed successful compilation of:

- `RunMind.Standalone`
- `Community.PowerToys.Run.Plugin.RunMind`
- `RunMind.CommandPalette`

Runner errors were:

- `RunnerThemeManager.cs`: ambiguous `Color` between `System.Drawing.Color` and `System.Windows.Media.Color`.
- `RunnerSettingsWindow.xaml.cs`: ambiguous `ComboBox` between WPF and Windows Forms.
- `RunnerInlineModelClient.cs`: missing `HttpClient`, `HttpResponseMessage`, `HttpRequestMessage`, `HttpContent` and `StreamReader` types.

## Source corrections

1. Added `MediaColor` and `MediaColorConverter` aliases for the WPF media types, and used those aliases throughout `RunnerThemeManager.ParseColor`.
2. Added a `WpfComboBox` alias for `System.Windows.Controls.ComboBox` and used it in the settings helper method signatures.
3. Added explicit `System.IO` and `System.Net.Http` imports to `RunnerInlineModelClient.cs`.
4. Added a regression contract test verifying the aliases and imports.

No runtime package or dependency was added. The changes are confined to the Runner host and its regression tests/documentation.

## Portable validation

- Full Node.js regression suite: 271/271 passed.
- Runner contract suite: 16/16 passed.
- JavaScript syntax: 56 files checked.
- JSON: 4 files parsed.
- XML/XAML/MSBuild/manifest: 11 files parsed.

## Remaining validation

The packaging environment has no `dotnet`, MSBuild, C# compiler or Windows Desktop targeting packs. Therefore, this source correction has not been compiled in this environment. Run the following on Windows with the .NET 10 SDK:

```powershell
.\Build-Release-x64.bat
```

The fix is considered accepted only after the complete four-host Release x64 build succeeds. Then perform Runner launch, inline Ollama/LM Studio, Calculator, theme, chat handoff and installer checks.
