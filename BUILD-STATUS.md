# RunMind 3.4.0 — Complete Runner Build Status

## Source baseline

- Input baseline: `RunMind-3.4.0-Runner-Preview-1.2-2026-11-23.zip`.
- Input SHA-256 verified before modification: `a49d32464a38ed95d1d31da6b34877241131fddc9f1b0ade1a18ff81a5547fd6`.
- This source package consolidates the Runner work into one 3.4.0 delivery. It supersedes the earlier Preview 1 / 1.1 / 1.2 source packages.
- Runner is an independently installable optional WPF host. Standalone, PowerToys Run and Command Palette remain included and unchanged in product scope.
- Quick Agentic system-management execution is not included yet. It remains the next shared milestone for all three quick hosts.

## Complete Runner scope in this source

- Internal first-party plugin registry with Applications, Files & Folders, URLs, Calculator and RunMind inline-answer providers.
- RunMind inline generation follows the PowerToys Run `llm` action keyword. Use `llm <prompt>` to request an answer and `llm > model > prompt` to override the selected model. Ordinary launcher searches do not trigger model requests.
- Independent Ollama-native and LM Studio inline provider profiles, model discovery, API-key/custom-header support, reasoning/generation parameters, provider-specific temperature bounds, configured request/discovery timeouts and bounded streaming.
- DPAPI CurrentUser protection for Runner inline credentials. Credentials are separate from non-secret JSON settings and from the full-chat provider profile.
- Lazy reuse of the canonical RunMind ChatWindow, shared theme catalog and appearance preferences. Completed and partial inline answers can be handed off to full chat.
- Restricted Quick Host chat boundary: no workspace selection, attachments, Full Agent, MCP, Skills, developer tools, System Manager or arbitrary native tool dispatch.
- Native launcher startup remains independent of WebView2 startup and provider network calls. App indexing is asynchronous; ChatWindow loads only when requested.
- Configurable global hotkey, tray/single-instance lifecycle, optional current-user startup and independent Runner installer/uninstaller.

## Portable validation performed (latest Runner window UX update)

Environment: Linux container, Node.js `v22.16.0`; Python XML parsing. The latest checks ran directly against this modified source tree.

- Full regression suite: **280/280 passed** using `node --test`.
- Runner-specific contract and window UX suite: **25/25 passed** using `node --test tests/runner-contract.test.cjs tests/runner-window-ux.test.cjs`.
- XML, XAML, MSBuild project and manifest parsing: checked again after the latest edits.
- Runner XAML named controls and code-behind bindings: checked for unique names and the new monitor, sizing, provider and ranking controls.
- New contract tests cover WindowChrome, cursor-monitor selection, auto sizing, conditional provider settings, plugin ranking and themed control heights.
- JavaScript syntax and JSON validation remain applicable to unchanged files and are repeated during package validation.

These are portable source-level checks. They do not constitute a .NET compilation or Windows integration test.

## Windows build and acceptance still required

This environment does not have `dotnet`, MSBuild, `csc`, Windows Desktop targeting packs or a Windows runtime. Therefore the updated 3.4.0 source has **not** been compiled or executed here. The previous Preview 1.2 build success on the user's machine does not certify this modified source.

On Windows with .NET 10 SDK and Windows Desktop build components, run:

```powershell
.\Build-Release-x64.bat
```

Then install Runner independently:

```powershell
.\Install-RunMind-Runner.ps1
```

Complete the Windows acceptance items in `docs/RUNNER-IMPLEMENTATION.md`, especially:

- Release x64 compilation of all four hosts.
- Runner installation, upgrade, shortcut, hotkey, tray, single-instance, sign-in startup and uninstall behavior.
- App/file/folder/URL launch plus safe calculator behavior.
- `llm <prompt>` inline generation for Ollama and LM Studio; confirm normal launcher text never calls the model.
- Model override, authentication precedence, request/discovery timeouts, cancellation and streaming limits.
- DPAPI credential persistence and current-user isolation.
- Shared theme behavior and completed/partial response handoff to ChatWindow.
- Quick Host restrictions, first-launch performance and repeated open/close behavior.
- Verify Runner install/uninstall leaves the other three hosts untouched.

Do not mark 3.4.0 certified or production-ready until these Windows checks pass.

## Existing platform gates remain open

- Full Agent credential-at-rest migration to native DPAPI-backed storage and rollback verification.
- Complete NuGet/transitive dependency security review and SBOM.
- Windows Release x64, WebView2, PowerToys and native System Manager acceptance.
- Accessibility, upgrade, performance, long-session and end-to-end acceptance.

## Follow-up correction from Windows Release x64 build report

The user-provided build output showed that Standalone, PowerToys Run and Command Palette compiled successfully, while Runner failed on ten source-level errors. The following corrections are now included in this archive:

- `RunnerThemeManager.cs`: explicitly alias `System.Windows.Media.Color` and `ColorConverter` to resolve the WPF / drawing namespace collision.
- `RunnerSettingsWindow.xaml.cs`: explicitly alias `System.Windows.Controls.ComboBox` so the settings helpers use WPF controls rather than the Windows Forms type.
- `RunnerInlineModelClient.cs`: explicitly import `System.IO` and `System.Net.Http` for `StreamReader`, `HttpClient`, `HttpRequestMessage`, `HttpResponseMessage` and `HttpContent`.
- Add a Runner regression contract test for these explicit aliases and imports.

The portable suite at that correction stage reported 271/271 overall and 16/16 Runner-specific tests. This environment still has no .NET SDK/MSBuild or Windows Desktop targeting packs, so these source corrections have **not** been compiled here. Re-run `Build-Release-x64.bat` on Windows before treating the correction as verified by compilation.

## Follow-up correction from the next Windows Release x64 build report

The user-provided log again showed that Standalone, PowerToys Run and Command Palette compiled successfully, while Runner reported 24 errors and one nullable warning. This archive includes these additional source corrections:

- `RunnerInlineCredentialsStore.cs` and `RunnerInlineSettings.cs`: added explicit `System.IO` imports for filesystem APIs and IO exception types.
- `RunnerThemeManager.cs`: aliased `System.Windows.Application` as `WpfApplication` to avoid collisions from Windows Forms global usings.
- `MainWindow.xaml.cs`: aliased and used `System.Windows.Clipboard` as `WpfClipboard` for both clipboard actions.
- `RunnerPlugins.cs`: renamed the parenthesized expression local to `parenthesizedValue` to avoid CS0136 with the later `out var value`.
- `RunnerInlineModelClient.cs`: changed nullable HTTP status formatting to capture a non-null pattern variable, addressing CS8629.
- Added a Runner regression contract test for these cases.

The current portable test counts are recorded in the validation report accompanying this archive. No .NET compiler is available in this environment; Windows Release x64 compilation remains unverified until the user reruns `Build-Release-x64.bat`.


For the current Fix 2 source, validation reports **272/272** full-suite tests and **17/17** Runner-specific contract tests. See `docs/WINDOWS-BUILD-FIX-COMPLETE-RUNNER-2.md`.


## Follow-up: Runner window, provider settings and global result weights

The user reported that the Runner window could not be moved/resized, multi-monitor targeting was missing, controls were inconsistent, generation settings were separated from Inline model, provider-specific sections remained visible together, the launcher stayed oversized, and global mode lacked plugin ranking weights. This source now includes:

- WPF `WindowChrome` with a real caption drag zone and 8-DIP resize border; Runner is no longer a transparent layered window.
- Per-Monitor-V2 DPI awareness and cursor-monitor placement on hotkey invocation. General settings can disable cursor-monitor following and use the primary display instead.
- Automatic height adjustment based on result count, bounded by the chosen monitor work area; a setting allows users to disable auto sizing and choose a manual height.
- Common 38-DIP one-line text/password/ComboBox dimensions, a separate 48-DIP launcher search field, a larger intentional multiline system-prompt field, and a fully themed ComboBox popup/item/arrow template.
- Settings reorganized into only General and Inline model tabs. Generation/request fields now live under Inline model. Ollama endpoint/credentials/keep-alive/CPU fields and LM Studio endpoint/credentials/Nemotron compatibility fields are conditionally displayed for the selected provider; common generation controls remain shared.
- Per-plugin global-mode result weights (0–100; 50 neutral; higher values move results earlier). Weights are applied before the result cap and are not applied to action-keyword mode.

The latest portable validation is 280/280 full-suite tests and 25/25 Runner-specific tests. XAML/XML parsing and control-name checks passed. This environment does not provide .NET SDK/MSBuild or Windows, so these latest C# and WPF changes have not been compiled or exercised on Windows. Run `Build-Release-x64.bat` and complete the new manual acceptance checks before treating them as verified.

## Follow-up: Runner interaction and settings UX corrections

The next user review identified seven required behaviors. The current source includes the following updates:

1. **Movable and resizable window:** the main launcher uses non-layered WPF `WindowChrome`, a caption drag area, 8-DIP resize border, clickable caption actions and a usable minimum size.
2. **Active-cursor monitor:** the hotkey targets the display under the current pointer by default. A General setting disables this behavior and targets the primary display. The Runner manifest opts into Per-Monitor-V2 DPI awareness.
3. **Input and select styling:** standard single-line TextBox, PasswordBox and ComboBox controls use 38-DIP heights; launcher search is 48 DIPs; the multiline system prompt has a dedicated larger height. ComboBox selection and popup/item visuals use theme resources instead of native white popup defaults.
4. **Generation settings location:** generation and request options are within the Inline model settings tab. Settings navigation contains General and Inline model only.
5. **Provider-specific settings:** Ollama-only endpoint, credentials, keep-alive and CPU controls appear for Ollama. LM Studio-only endpoint, credentials and Nemotron compatibility controls appear for LM Studio. Shared options remain common. Switching providers clears previously discovered model results.
6. **PowerToys Run-style window sizing:** automatic height grows/shrinks from current result count, caps at the selected monitor's working area and can be disabled so users can retain a manual window height.
7. **Global result priorities:** each built-in plugin has a persisted 0–100 priority; 50 is neutral, higher values move that plugin earlier. Weights affect global mode only and are applied before the common result cap.

Portable validation on the current source:

- Full regression suite: **280/280 passed**.
- Runner-specific contract and UX tests: **25/25 passed**.
- JavaScript syntax checks: **57/57 passed**.
- XAML, project and manifest XML parsing: **12/12 passed**.
- JSON parsing (BOM-aware): **4/4 passed**.
- MainWindow, RunnerSettingsWindow and RunnerChatWindow XAML event-handler references all resolve to code-behind methods; named controls are unique.
- Tests and static checks were repeated from the final extracted source archive.

These checks are source-level and portable. This environment has no .NET SDK, MSBuild, Windows Desktop runtime or Windows multi-monitor desktop. Accordingly, the latest WPF/C# modifications have **not** been compiled or interactively tested here. Run `Build-Release-x64.bat` on Windows and complete `docs/RUNNER-WINDOW-UX-AND-RANKING.md` before accepting this update.
