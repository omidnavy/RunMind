# RunMind 3.4.0 — Complete Runner Build Status

## Source baseline

- Input baseline: `RunMind-3.4.0-Runner-Preview-1.2-2026-11-23.zip`.
- Input SHA-256 verified before modification: `a49d32464a38ed95d1d31da6b34877241131fddc9f1b0ade1a18ff81a5547fd6`.
- This source package consolidates the Runner work into one 3.4.0 delivery. It supersedes the earlier Preview 1 / 1.1 / 1.2 source packages.
- Runner is an independently installable optional WPF host. Standalone, PowerToys Run and Command Palette remain included and unchanged in product scope.
- Quick Agentic system-management execution is not included yet. It remains the next shared milestone for all three quick hosts.

## Latest Runner interaction update — full-surface selects, immediate themes and RunMind result handoff (2026-11-24)

- **ComboBoxes:** the shared WPF template now uses a full-width/full-height transparent toggle surface. Clicking the displayed value or open area opens the dropdown, not only the arrow. The editable Model selector has a code-behind editor hook so its text area also opens the list while remaining editable.
- **Themes:** changing Appearance Mode, Light theme or Dark theme immediately persists the preference and reapplies the palette to all open Runner windows. The main native caption synchronizer reloads the current preference. These appearance changes remain saved even when Settings is closed with Cancel; Save settings is still used for the other Runner settings.
- **RunMind result:** a single click on a RunMind row opens the exact result prompt in the real Standalone Full Agent. Enter also activates it; the second half of a double-click is ignored to prevent duplicate conversations, while other plugin double-click behavior is unchanged. Completed answers, partial streaming output and errors are transferred as transcript context without re-running the prompt. A click during the debounce period still hands off the prompt even if no inline response exists yet.

Latest portable validation on this source:

- Full Node regression suite: **298/298 passed**.
- Runner-focused contract, UX, handoff and interaction tests: **42/42 passed**.
- JavaScript syntax: **60/60 passed**.
- JSON: **4/4 parsed**.
- XML/XAML/project/manifest: **11/11 parsed**.
- Runner window XAML control-name and event-handler checks: passed.

The current environment does not have .NET SDK/MSBuild or Windows Desktop runtime. The Release x64 build and interactive WPF acceptance remain pending on Windows. See `docs/RUNNER-INTERACTION-FIXES-2026-11-24.md` for the manual test sequence.

## Latest source update — real Standalone handoff (2026-11-24)

- Removed all empty-state sample/suggestion cards. Empty Runner remains a clean launcher shell; results stay collapsed until a plugin returns real matches.
- Removed `RunnerChatWindow.xaml` / `.xaml.cs` and WebView2 references/assets from the Runner project.
- Open Chat transfers prompt plus completed/partial inline response as context into a genuine Standalone Full Agent conversation. The prompt is displayed, not re-submitted.
- When Standalone is already running, a bounded current-user-only named pipe opens a new Full Agent conversation window in that existing process without replacing its current conversation.
- When Standalone is not running, Runner locates `RunMind.exe` through running processes, registry install metadata, a current-user Local AppData path cache written by Standalone, conventional locations or development output paths; then it launches Standalone with a bounded temporary handoff file.
- A second Standalone process waits briefly for the existing current-user pipe when another instance is detected. If the listener is still unavailable, it proceeds with a real Full Agent window rather than losing the context.
- The WPF launcher and plugin registry remain native; only Standalone owns the WebView2 chat/runtime, Full Agent provider settings, workspace, MCP and Skills.
- **Deployment requirement:** Runner and Standalone must both be built/deployed from this source package for the named-pipe handoff and `--runner-handoff-file` startup contract. Installing this Runner beside an older Standalone binary that predates the handoff listener is not a supported handoff configuration. PowerToys Run and Command Palette do not need to be upgraded for this Runner handoff change.

Latest portable validation on the final source tree and extracted archive:

- Full Node regression suite: **293/293 passed**.
- Runner-specific contract, window UX and handoff suite: **38/38 passed**.
- JavaScript syntax checks: **58/58 passed**.
- JSON files: **4/4 parsed**.
- XML/XAML/project/manifest files: **11/11 parsed**.
- ZIP integrity test and SHA-256 sidecar verification: passed.

This environment has no .NET SDK/MSBuild or Windows runtime, so Release x64 compilation and interactive Windows handoff acceptance remain unverified.

## Latest Windows build correction — stale retired Runner chat XAML (2026-11-24)

The reported `MC3074` error referenced `RunMind.Runner/RunnerChatWindow.xaml`, but inspection of the preceding source archive confirmed that this XAML and its code-behind were already absent. The likely cause was an in-place extraction into a directory containing the obsolete file from an earlier package; ZIP extraction overwrites existing files but does not delete files removed from newer packages.

The Runner project now excludes both retired files using `DefaultItemExcludesInProjectFolder`, plus explicit `Page Remove` and `Compile Remove` entries. `Build-Release-x64.bat` also deletes the stale XAML and code-behind before restore/build if they remain in a reused source folder. This prevents WPF default item discovery from compiling stale chat-window artifacts even when the source directory is reused. The handoff regression suite asserts the project exclusions and build-script cleanup.

This is a source/project-level correction. The current environment does not contain .NET SDK/MSBuild or Windows Desktop targeting packs, so the Windows Release x64 build remains unverified here.


## Latest Windows build correction — Standalone locator filesystem imports (2026-11-24)

The Windows build report identified missing `Path`, `File` and `DirectoryInfo` symbols in `RunMind.Runner/StandaloneLocator.cs`. The file now imports `System.IO` explicitly instead of relying on SDK implicit-using behavior. The handoff contract suite includes a regression assertion for this explicit import and the filesystem discovery operations it supports. A source scan of Runner C# files confirmed that other files using filesystem APIs already import `System.IO`.

Portable validation on this source tree: **293/293** full regression tests and **38/38** Runner-specific tests passed. This environment still has no .NET SDK/MSBuild or Windows runtime; therefore the actual Windows Release x64 compilation remains to be confirmed on the user's machine.

## Complete Runner scope in this source

- Internal first-party plugin registry with Applications, Files & Folders, URLs, Calculator and RunMind inline-answer providers.
- RunMind inline generation follows the PowerToys Run `llm` action keyword. Use `llm <prompt>` to request an answer and `llm > model > prompt` to override the selected model. Ordinary launcher searches do not trigger model requests.
- Independent Ollama-native and LM Studio inline provider profiles, model discovery, API-key/custom-header support, reasoning/generation parameters, provider-specific temperature bounds, configured request/discovery timeouts and bounded streaming.
- DPAPI CurrentUser protection for Runner inline credentials. Credentials are separate from non-secret JSON settings and from the full-chat provider profile.
- Open Chat hands off to the actual Standalone Full Agent application; Runner does not host a duplicate chat window or bundle WebView2.
- Inline prompt and completed/partial answer are transferred as transcript context without automatic resubmission. Existing Standalone processes receive the handoff over a current-user-only named pipe; otherwise Runner launches the discovered Standalone executable with a bounded handoff file.
- Standalone persists its executable path under current-user Local AppData for discovery by an independently installed Runner.
- Runner has no workspace, MCP, Skills, developer-tool or Full Agent bridge. Those capabilities remain in Standalone.
- The empty Runner state is intentionally blank. No sample prompts or suggestion cards are displayed, and the result panel remains hidden until there are actual results.
- Native launcher startup remains independent of WebView2 startup and provider network calls. Application indexing is asynchronous.
- Configurable global hotkey, tray/single-instance lifecycle, optional current-user startup and independent Runner installer/uninstaller.

## Portable validation performed (latest real Standalone handoff update)

Environment: Linux container, Node.js `v22.16.0`; Python XML parsing. The latest checks ran directly against this modified source tree.

- Full regression suite: **292/292 passed** using `node --test`.
- Runner-specific contract, window UX and handoff suite: **37/37 passed** using `node --test tests/runner-contract.test.cjs tests/runner-window-ux.test.cjs tests/runner-standalone-handoff.test.cjs tests/runner-inline-model.test.cjs`.
- XML, XAML, MSBuild project and manifest parsing: checked again after the latest edits.
- Runner XAML named controls and code-behind bindings: checked for unique names and the new monitor, sizing, provider and ranking controls.
- Contract tests cover WindowChrome, cursor-monitor selection, auto sizing, conditional provider settings, plugin ranking, themed controls, blank empty state, removal of the duplicate Runner chat host, bounded current-user IPC, executable discovery and prompt/answer handoff.
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
- MainWindow and RunnerSettingsWindow XAML event-handler references resolve to code-behind methods; the removed RunnerChatWindow is not part of the current Runner project.
- Tests and static checks were repeated from the final extracted source archive.

These checks are source-level and portable. This environment has no .NET SDK, MSBuild, Windows Desktop runtime or Windows multi-monitor desktop. Accordingly, the latest WPF/C# modifications have **not** been compiled or interactively tested here. Run `Build-Release-x64.bat` on Windows and complete `docs/RUNNER-WINDOW-UX-AND-RANKING.md` before accepting this update.


## Follow-up: theme selector labels and launcher search field

The user reported two remaining visual defects in the Runner settings/launcher UI. This source corrects both:

- **Theme selector labels:** Light and Dark theme ComboBoxes now use an explicit `ThemeNameTemplate` DataTemplate bound to `ThemeDefinition.Name`. This avoids the custom ComboBox selected-item presenter falling back to the record-generated `ToString()` value (for example, `ThemeDefinition { Id = ..., Name = ... }`). The template explicitly uses the active `RunnerText` brush and truncates long names cleanly.
- **Launcher search input:** the primary inline search TextBox is now 52 DIPs high with zero extra vertical padding, centered content, and explicit ClearType text rendering. The surrounding 68-DIP search surface remains unchanged.

Validation on the current source: **282/282** full Node regression tests; **57/57** JavaScript syntax checks; **12/12** XML/XAML/project/manifest parse checks. The final archive is tested again after extraction. Windows/.NET compilation and interactive WPF rendering remain pending because this environment has no Windows desktop runtime or .NET SDK.

## Historical intermediate state: compact launcher suggestions and direction-aware growth (superseded)

The latest user review requested direction-aware expansion, removal of redundant launcher labels, a more compact search input and a useful empty state. The current source now includes:

- Removed the `RUNNER · QUICK AI` subtitle, the plugin inventory tagline and the `RUNMIND ↵` chip inside the search field.
- Reduced default width to 680 DIPs. The search row is 54 DIPs high and the TextBox is 40 DIPs with 17-DIP text, transparent background and centered text/caret.
- An intermediate version displayed four clickable quick-start suggestions. These were removed after review; the current empty state is intentionally blank, as recorded in the latest handoff section at the beginning of this file.
- A non-empty query with no matches collapses the results panel. A matching query shows only the unified results list without an additional Results heading/count row.
- Dynamic height uses the current window's monitor and physical bounds. If the window center is in the lower half of the work area, it keeps the old bottom edge and expands upward; in the upper half it keeps the old top edge and expands downward. The adjusted window is clamped to the monitor work area.
- Added source-level regression tests for these behaviors.

Latest portable validation on this source tree:

- Full regression suite: **285/285 passed** (`node --test`).
- Runner-specific contract and window UX tests: **30/30 passed**.
- XML/XAML/project/manifest parsing: **12/12 passed**.
- JSON parsing: **4/4 passed**.
- MainWindow XAML event-handler references: **12 unique handlers verified**.
- Obsolete launcher labels: **0 remain**.

Node.js version: 22.16.0. This environment does not provide the .NET SDK/MSBuild or Windows Desktop runtime. Consequently, the updated WPF/C# source has not been compiled or visually exercised on Windows. Re-run `Build-Release-x64.bat` and complete the Windows acceptance checks for lower-half window growth, no-match behavior, suggestion actions and 100–200% DPI scaling before accepting this change.

## Follow-up: Windows Release x64 build feedback — Compact UX build correction

The latest user-provided Windows build log showed that the other three hosts compiled successfully and Runner had one compile error plus two warnings. This source corrects the three reported items:

- `MainWindow.xaml.cs`: explicitly aliases `System.Windows.Controls.Button` as `WpfButton` and uses that alias in the suggestion click handler, resolving CS0104 caused by the WPF/Windows Forms hybrid imports.
- `MainWindow.xaml.cs`: awaits the `Dispatcher.InvokeAsync` operation in `Window_Loaded`, so the initial height/monitor positioning completes in sequence and does not produce CS4014.
- `App.xaml.cs`, `RunMind.Runner.csproj` and `app.manifest`: replaces manifest-declared DPI awareness with an explicit `Application.SetHighDpiMode(PerMonitorV2)` call before `base.OnStartup` and before creating any app windows; the project declares `ApplicationHighDpiMode=PerMonitorV2`, and duplicate DPI declarations were removed from the manifest. This addresses WFO0003 while retaining the intended Per-Monitor-V2 mode for the native WPF/WinForms host.
- Added a Runner regression test for the explicit WPF Button type, awaited dispatcher initialization and the DPI configuration contract.

Portable validation on this source:

- Full Node regression suite: **286/286 passed**.
- Runner-specific contract and window UX suites: see the validation report accompanying this archive.
- XML/XAML/project/manifest and JSON parsing: rerun during package validation.
- The incoming Compact UX archive checksum was verified before modification, and the final archive was extracted and checked after packaging.

The environment still has no .NET SDK, MSBuild, Windows Desktop targeting packs or Windows runtime. Therefore this correction has **not** been compiled locally. Re-run `Build-Release-x64.bat` on Windows and confirm that Runner compiles without CS0104, CS4014 or WFO0003. Also confirm that cursor-monitor placement and resizing continue to work across mixed-DPI monitors.


## Real Standalone handoff — Windows acceptance

1. Start Runner with Standalone closed; click Open Chat with no prompt. Verify the actual RunMind Standalone executable launches and displays its Full Agent interface.
2. Start Runner with an active `llm` query and a completed answer; click Open Chat. Verify both messages appear in the real Standalone conversation as context and the model is not asked to generate them again.
3. Repeat while an inline generation is in progress. Verify the partial answer is transferred and the Runner generation is cancelled.
4. Keep an unrelated Standalone conversation open, then hand off from Runner. Verify the existing conversation is preserved and a new Full Agent conversation window opens within the existing Standalone process.
5. Start Standalone while another instance is still initializing, then hand off from Runner. Verify pipe retry routes the context to the running process when its listener becomes ready.
6. Install Standalone in a non-default location, run it once, close it, then use Runner Open Chat. Verify the cached executable path discovers it.
7. With Standalone absent and its cached/registered path invalid, verify Runner displays a clear not-found message and does not open a fake chat window.
8. Confirm Runner's output/publish directory contains no WebView2 package or duplicate ChatWindow runtime, while Standalone still includes its normal WebView2/chat files.

## Latest Windows build correction — preserve win-x64 assets for Standalone publish (2026-11-24)

The user's Windows log confirmed all four Release x64 hosts compile and the Command Palette MSIX packages/signs successfully. Step [6/8] then failed with `NETSDK1047` because the earlier Step [2/8] restore used `--runtime win-x64`, but the Step [4/8] solution build performed a default implicit restore. That second restore rewrote the Standalone assets file without the `net10.0-windows10.0.22621.0/win-x64` target required by the later `dotnet publish --no-restore`.

`Build-Release-x64.bat` now runs Clean first, then restores the full solution with `--runtime win-x64`, then passes `--no-restore` to the solution build in Step [4/8]. This ordering ensures Clean cannot remove the newly restored assets and prevents the solution build from overwriting them with a framework-only restore. The Standalone and Runner publish steps can therefore consume the preserved runtime-specific targets. Command Palette packaging remains its own project-specific build and is unchanged.

A regression test asserts the clean/restore/build/publish sequence and prevents a default implicit restore from being reintroduced between RID restore and publish. Source validation on the final extracted archive: **294/294** full regression tests and **39/39** Runner-specific tests passed; **59/59** JavaScript syntax checks, **4/4** JSON parses and **11/11** XML/XAML/project/manifest parses passed. ZIP integrity and SHA-256 verification passed. The environment used for this source update does not have .NET SDK/MSBuild, so the user's next Windows run remains the confirmation of the publish fix.


## Change Impact Explorer v2 — implementation and validation (2026-11-24)

This source extends the existing P4 project-index/Git-review engine rather than introducing a parallel analyzer. The shared Agent tool and built-in workflow are available in Standalone, PowerToys Run and PowerToys Command Palette. The optional Runner remains a deliberately restricted quick host and does not host the Full Agent.

Implemented in this update:

- Git-aware operation by default when `project_change_impact` is called without explicit paths. Fresh `gitRead` approval precedes the native snapshot and index refresh. Working-tree mode includes staged, unstaged, renamed/deleted and untracked paths. Optional local `base_ref` mode analyzes `base_ref...HEAD` plus working-tree changes without fetch or Git mutation.
- Unified-diff hunk mapping to current AST declaration ranges, changed-symbol inference, removed declaration candidates, TypeScript checker cross-file references and static import/export binding consumers.
- Reverse direct/transitive dependency traversal, removed/renamed module importer candidates, dynamic dependency signals and unresolved-import reporting.
- Expanded local asset graph for HTML, CSS, SVG, media, fonts, scripts, preload/module-relative URL patterns and non-indexed asset changes. Asset-owner modules feed impact and test selection.
- Candidate test recommendations with evidence/confidence, plus bounded Istanbul/LCOV changed-line coverage evidence. Aggregate coverage is not misrepresented as test-to-line attribution.
- Transparent blast-radius/risk and structural confidence/completeness reporting, explicit graph/index/compiler truncation, parser errors, unresolved edges and static-analysis limitations.
- Dedicated guide and expanded P4 Windows acceptance checklist.

Validation environment: Node.js `v22.16.0` in the packaging container. Full portable regression suite: **307/307 passed**; focused Change Impact Explorer v2 suite: **9/9 passed**; JavaScript syntax: **61/61 passed**; JSON parse: **4/4 passed**; XML/XAML/project/manifest parse: **12/12 passed**. The same full suite and syntax/structure checks are repeated from the final extracted ZIP. Final archive integrity and SHA-256 sidecar are verified.

**Windows certification is still required.** This environment has no .NET SDK/MSBuild or Windows Desktop runtime. The added `impact_snapshot` native Git bridge and all WPF/C# projects must be compiled using .NET 10 on Windows, followed by the new `Change Impact Explorer v2` acceptance cases in `docs/P4-WINDOWS-ACCEPTANCE.md`. Do not interpret portable JavaScript tests as native Windows build certification.
