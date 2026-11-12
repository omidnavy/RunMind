# RunMind 2.2.3 — Command Palette Think toggle and Dock actions

- Reordered the PowerToys Run provider-auth fields so each provider's endpoint is immediately followed by its API key and custom header.
- Added the `https://omid.navy` website to the Command Palette extension About section and package metadata.
- Added stable command ID `runmind.open-empty-chat` to **Open chat with empty context**.
- Added `Configure-RunMind-CommandPalette-Alias.ps1`; the separate MSIX installer calls it after install. When Command Palette is closed and the existing host settings contain its initialized aliases, it adds the `RunMind` direct alias, backs up `settings.json`, and does not overwrite an alias assigned to another command. It skips rather than create an incomplete settings file or risk editing a live host configuration.
- If the installer skips alias setup, close Command Palette and run `Configure-RunMind-CommandPalette-Alias.ps1` from the source folder.

Static validation for this source update covers project and manifest XML, plugin JSON, C# source consistency, settings order, About URL, alias command ID and PowerShell script structure. A Windows/.NET build and live host verification are still required. The previous release's detailed validation notes follow.

## Previous release: RunMind 2.2.1 — PowerToys Run settings and handoff fixes

This is a follow-up release on the 2.2.0 three-host source baseline. It keeps the Standalone and Command Palette behavior intact while correcting PowerToys Run reasoning selection, adding Run-scoped provider authentication, updating the Run prompt default, and making chat launch snapshots reusable.

This source snapshot updates the PowerToys Command Palette host and establishes a shared inline-model configuration with the PowerToys Run plugin.

## Included in this update

- Command Palette starts its own chat process from the packaged `RunMind.CommandPalette.exe`; it does not launch or depend on Standalone.
- Command Palette hosts the same WPF/WebView2 chat window and canonical `Shared/ChatWindow.html`, `Shared/ArtifactTools.js`, and shared native bridge used by the other hosts.
- The branded **RunMind** fallback generates answers directly in a Command Palette content page. It accepts a plain search query, `RunMind <question>`, or the legacy `Ask RunMind <question>` prefix. **Open chat with empty context** remains available.
- The misleading top-level **Configure RunMind** command and the manual **Open chat with context** answer-paste form are removed. The former blank-chat/settings-flyout launch path is removed too.
- Command Palette's Installed Extensions settings page contains an Ollama/LM Studio provider selector, live model discovery from the selected endpoint, a free-text model ID for any provider model, endpoints, timeouts, system instructions, shared generation parameters, and provider-specific options. It does not launch the chat window.
- PowerToys Run and Command Palette now read/write one shared inline configuration file at `%LOCALAPPDATA%\RunMind\Shared\inline-model-settings.json`.
- Deployment validation checks that the Command Palette MSIX contains the shared chat page, helper script, brand assets, and WebView2 assemblies.

## Remaining work / limits

- The inline page streams the response and supports follow-up turns inside Command Palette. Its **Open chat with context** action transfers the complete ordered inline transcript, including all completed user/assistant turns. While generation is still running, it transfers the current user prompt but omits the unfinished assistant response. The destination restores that transcript through the existing `.runmind` session loader, so the imported messages become real conversation state for continued model requests.
- Appearance preferences are shared through `%LOCALAPPDATA%\RunMind\appearance.json`. Full chat preferences, credentials, and browser-backed conversation data remain in separate WebView2 profiles.
- The source archive does not bundle generated PDF.js/Tesseract/OCR payloads. `Build-Release-x64.bat` prepares the pinned resources before building all three hosts and packaging the Command Palette MSIX. `Install-RunMind-CommandPalette.bat` consumes the already-built MSIX and does not prepare resources or compile anything.

## Latest Command Palette compile corrections

The follow-up build report identified compile-time API mismatches in the previous source ZIP. This snapshot corrects them:

- Added explicit `System.IO` imports to the Command Palette process and chat-host source files.
- Replaced the invalid assignment to `Settings.SettingsPage` with a small `ICommandSettings` implementation exposing the custom settings page.
- Constructs `FallbackCommandItem` with the SDK-supported `(ICommand, displayTitle, id)` signature; the fallback page implements `IFallbackHandler` and receives the entered query. Title, subtitle, and icon are set on the fallback item.
- Imported `Shmuelie.WinRTServer.CsWinRT` so the one-argument factory overload is selected, and initialize CsWinRT COM wrappers before registration. This is the overload supplied by the CsWinRT-specific extension API for Shmuelie.WinRTServer 2.2.1.

These changes address the 23 reported compiler diagnostics by inspection against the referenced SDK APIs. They have not been compiled on Windows in this environment; the next verification step remains `Build-Release-x64.bat build` on the target machine.


## Deployment script correction after successful build

The Windows build completed successfully, but the install step exposed a PowerShell deployment-script error. `ForceApplicationShutdown` belongs to `Add-AppxPackage`; it is not a supported parameter of `Remove-AppxPackage`. The uninstall and upgrade paths now call `Remove-AppxPackage` without that unsupported switch. The install path detects whether the local `Add-AppxPackage` cmdlet exposes `ForceApplicationShutdown` and supplies it only when available. This keeps package replacement compatible with the installed Appx module.

## Validation status

For 2.2.1, static checks passed for the edited C# delimiter structure, provider-auth request coverage, six reasoning-selection mappings, requested system-prompt text, JSON/XML project metadata, embedded JavaScript syntax, and ZIP integrity. A Windows/.NET build and live PowerToys Run test were not possible in the editing environment because it does not have the .NET SDK or Windows/PowerToys runtime.


Static XML checks passed for the project files, publish profile, and package manifest. The complete Windows build and runtime checks have **not** been run in this environment because the .NET SDK and Windows/PowerToys runtime are unavailable here. On the target Windows machine, run:

```bat
Build-Release-x64.bat build
```

Then, after a successful build, install and validate the Command Palette extension with the separate installer:

```bat
Install-RunMind-CommandPalette.bat
```

The first resource preparation requires internet access; subsequent builds can use the generated local resource files.


## Command Palette activation failure reported after installation

The latest Command Palette and COM-server logs show that the extension was not reaching provider initialization. The COM server process started with `-RegisterProcessAsComServer -Embedding`, then exited while constructing `RunMindExtension` with `FileNotFoundException` for `Microsoft.Windows.SDK.NET, Version=10.0.26100.38`. The package contained `Microsoft.Windows.SDK.NET.dll`, but the .NET loader did not resolve the assembly by name from the generated dependency graph. Consequently, PowerToys timed out starting the extension and could not find the RunMind provider. The `Ask RunMind`, open-chat, and settings entries therefore had no opportunity to run.

The earlier searchable **Configure RunMind** command mentioned in that iteration was subsequently removed after user testing showed it opened the wrong UI. The current source adds a narrowly scoped fallback resolver for `Microsoft.Windows.SDK.NET.dll` from `AppContext.BaseDirectory`. It registers before the extension types are touched and logs the requested identity, actual packaged assembly identity, and resolved path. COM startup is isolated in a non-inlined method so WinRT extension types are first JIT-compiled only after the resolver has been registered. Other assembly resolution is unchanged.

This is a targeted runtime-resolution fix, not a confirmed runtime result. Rebuild and reinstall on Windows, reload Command Palette extensions, and inspect `%LOCALAPPDATA%\RunMind\CommandPalette\Logs\com-server.log`. A successful activation should log `CsWinRT ComWrappers initialized`, `Constructed RunMindExtension`, `Registered RunMindExtension for IExtension`, and `ComServer.Start() completed`. If the packaged assembly identity differs or loading fails for another reason, the new log records that detail.


## Command Palette usability follow-up — 2026-11-10 (superseded)

An earlier iteration renamed the fallback to **RunMind**, used the RunMind icon, and hid the COM-host package from Start and Windows Search. It also exposed top-level **Configure RunMind** and **Open chat with context** commands. User testing showed that **Configure RunMind** opened the chat window with a flyout instead of a real extension settings page, and that the context form required manual answer pasting. Both command paths have been removed in the later update below.

## Command Palette inline-model settings — 2026-11-10

This update removes the misleading top-level **Configure RunMind** command and the old code path that launched a blank chat window with the Generation Settings flyout. The extension exposes its settings only through Command Palette's Installed Extensions settings surface (`ICommandSettings.SettingsPage`). The settings page does not launch the chat application.

The page includes one provider-neutral **Thinking / reasoning mode** selector for both Ollama and LM Studio (`auto`, `on`, `off`, `low`, `medium`, `high`). `auto` leaves the model default intact. Keep-alive and CPU-thread controls are shown only for Ollama because those are Ollama request options. The model dropdown is populated from the selected provider's live model-list endpoint (Ollama `/api/tags`, LM Studio `/api/v1/models`, with `/v1/models` as a compatibility fallback). LM Studio discovery filters to installed LLMs and uses each model's native key. A separate editable model-ID field accepts any ID. A manually entered ID is sent to the provider without being rejected merely because it was not returned by model discovery.

Both Command Palette and the PowerToys Run plugin now link `Shared/InlineModelSettings.cs` and use `%LOCALAPPDATA%\RunMind\Shared\inline-model-settings.json` as the common inline configuration store. PowerToys Run exposes the same values in its existing plugin settings and refreshes its active snapshot when the shared file changes. The settings page's selected provider/model and generation settings therefore do not belong to an isolated Command Palette-only configuration.

The branded **RunMind** fallback is now a real inline conversation surface: it receives the search query through `IFallbackHandler`, submits it to the configured local provider, streams `Ollama /api/generate` NDJSON or LM Studio native `/api/v1/chat` SSE into the Command Palette page (using `/v1/chat/completions` only for legacy compatibility-only options or older servers when Thinking is Auto), and keeps follow-up turns in the same page. The page also provides **Open chat with context** without requiring manual answer copying. The existing chat host remains independent and can be opened with empty context.

Validation performed here is limited to source inspection, XML parsing, static consistency checks, and archive integrity. A Windows/.NET build and live provider requests were not run in this environment. Run `Build-Release-x64.bat` on Windows, then test the fallback with both Ollama and LM Studio. Use `Install-RunMind-CommandPalette.bat` separately to install it for daily use.


## Follow-up build correction — 2026-11-10

The next Windows build report exposed two source-folder upgrade issues:

- `InlineModelSettingsPage.cs` now imports `System.IO` explicitly for `InvalidDataException`.
- `ConfigureRunMindPage.cs` is obsolete and is not part of the current source archive. When users extract a new ZIP over an older source folder, however, Windows extraction does not delete files that disappeared from the newer archive. The Command Palette project now explicitly excludes this legacy file from compilation, and `Build-Release-x64.bat` removes it before building. The obsolete `RunMindLauncher.OpenSettings` call is therefore not restored; the broken command/flyout path remains removed.

These are source-level corrections. They have not been compiled in this environment; run `Build-Release-x64.bat build` on Windows to verify.


## Provider-neutral model selection and Command Palette inline conversation — 2026-11-10

The model settings page no longer contains a hard-coded model. It discovers all model IDs returned by the selected provider, displays them in the selector, and permits any manually entered model ID. Generation no longer rejects a custom ID solely because it was absent from the discovery response; Ollama or LM Studio returns the authoritative model-not-found error if the ID is invalid.

The thinking setting is now a single provider-neutral mode shared with PowerToys Run: `auto`, `on`, `off`, `low`, `medium`, or `high`. `auto` omits provider overrides so the model's own default remains active. Ollama-only keep-alive and CPU-thread controls are shown only for Ollama. LM Studio uses its native REST `reasoning` option for the shared thinking setting. The OpenAI-compatible endpoint remains available for the optional seed and model-specific Nemotron conversational template setting.

The Command Palette **RunMind** fallback implements `IFallbackHandler` and renders an inline conversation page inside Command Palette. It accepts a plain search query or `RunMind <question>`, records the query while the user types, and starts inference only after the user selects the fallback. It streams generated text, maintains follow-up history, and offers **Open chat with context**. The model selector is populated from the selected Ollama or LM Studio server and also supports arbitrary manually entered model IDs.

This update is static-checked only in this environment; it has not been compiled with the Windows .NET SDK or exercised against live provider endpoints.


## Provider-neutral settings and inline inference follow-up — 2026-11-10

The inline thinking/reasoning selector is a single shared setting for Ollama and LM Studio. RunMind translates the selected mode to Ollama's `think` request field or LM Studio's native `reasoning` field. `Auto` omits the override so the model uses its configured default. Support for reasoning levels remains model-dependent.

The Command Palette model dropdown is populated from the selected provider: Ollama `/api/tags`; LM Studio `/api/v1/models`, with `/v1/models` as a compatibility fallback. The model ID also has an editable text override, so users can enter any custom model name or key without being limited to discovered choices. There is no hard-coded Qwen model.

The fallback handler now only records the root-palette query while the user types. It starts an HTTP inference request only when the user selects the **RunMind** fallback and opens its content page. The page streams the response, supports follow-up prompts, and opens the separate Command Palette-hosted chat with the complete ordered inline transcript. The destination uses the normal `.runmind` session loader so imported messages are active model context, not display-only content.

The PowerToys Run plugin exposes the same provider, endpoints, model ID, thinking/reasoning mode, timeouts, system prompt, and generation parameters through its existing plugin settings. Both hosts use the same `%LOCALAPPDATA%\RunMind\Shared\inline-model-settings.json` file. Keep-alive and CPU-thread settings remain Ollama-specific; Nemotron conversational template mode remains LM Studio/model-specific.

Validation for this update: source inspection, XML parsing, JSON template parsing, static reference checks, and ZIP integrity. This environment does not have the Windows .NET SDK or PowerToys runtime, so this update has **not** been compiled or exercised against Ollama/LM Studio here. Run `Build-Release-x64.bat` on Windows, then test one request against each provider before installing with `Install-RunMind-CommandPalette.bat`.


## Command Palette fallback SDK-constructor and stale source correction — 2026-11-10

The next Windows build report identified two issues in the Command Palette project:

- `FallbackCommandItem` in the current SDK requires `(ICommand command, string displayTitle, string id)`. The fallback page is now passed through that constructor with the stable ID `runmind.inline-conversation`; because `InlineAnswerPage` implements `IFallbackHandler`, the SDK constructor wires its query handler automatically.
- `AskRunMindPage.cs` is not part of the current source archive. It can remain in an existing checkout when a newer ZIP is extracted over an older folder, so the project explicitly excludes it and the build script removes it before building. Its obsolete `RunMindLauncher.OpenWithPrompt` call is not restored.

The constructor shape was checked against Microsoft's current Command Palette toolkit implementation. This patch addresses the two reported compile errors, but still requires a Windows build to confirm the complete project.


## Command Palette inline runtime diagnostics

The fallback page explicitly assigns its `IFallbackHandler` to the `FallbackCommandItem`. Runtime diagnostics are written to `%LOCALAPPDATA%\RunMind\CommandPalette\Logs\com-server.log`, including fallback query receipt, content-page activation, provider request start/result, form submit action, and context-chat process activation. This allows a failed inline request or context transfer to be localized from one log.


## Command Palette form action-data fix — 2026-11-10

User testing confirmed that Command Palette could discover models and render the inline conversation page, but inline generation reported an empty model ID and the **Open chat with context** action did not launch the chat host. The COM log showed the inline page loading `inline-model-settings.json` with `Provider=ollama` and an empty `Model`.

The root cause was the Command Palette `FormContent` submission contract. Adaptive Card input values and `Action.Submit.data` are supplied as separate JSON arguments (`SubmitForm(inputs, data)`). The settings form and inline conversation form only overrode the legacy one-argument `SubmitForm(payload)` method. As a result, their input fields could render and model discovery could run, but the separate `action` data (`save`, `refresh`, `send`, or `openChat`) was discarded. The settings Save action therefore did not persist the selected model, and the context action never reached its launch handler.

Both forms now explicitly override `SubmitForm(string inputs, string data)`, merge input values and action data, and retain a one-argument compatibility overload. The settings form logs submission/action metadata and reads the shared settings file back after saving, verifying that the chosen model was persisted. The inline conversation form now receives the separate action value, allowing Send and Open chat with context to execute their intended handlers.

Validation here covers source inspection, JSON payload merge cases, project XML, and archive integrity. The Windows/.NET build and live Command Palette form submission, Ollama, LM Studio, and chat-window launch tests still need confirmation on the target Windows machine.


## Inline UX, timeout, branding, and build-script polish — 2026-11-10

- The provider switch in Command Palette settings is now an explicit provider action (`Use Ollama` / `Use LM Studio`). Selecting either action saves the provider choice, clears the previous provider's model selection, and automatically refreshes the model list. This matches the current Adaptive Card form API: input changes are not delivered as standalone change events, but Action.Submit is.
- Removed the separate 15-second provider-connection timeout from inline generation in both Command Palette and PowerToys Run. The configured Request timeout (30–3600 seconds) now governs the whole generation request, including model loading and response headers, as well as the response stream. Model discovery retains its separate timeout. Old `ProviderConnectionTimeoutSeconds` values in existing JSON are ignored.
- If a provider rejects the selected Think/reasoning mode with a specific unsupported/invalid-configuration error before any answer tokens are emitted, Command Palette retries once with `Thinking=auto`, persists `auto` to the shared settings file, and shows a notice that this model does not support the selected mode. Other errors are not downgraded, and requests are not replayed after partial answer text has streamed.
- Command Palette package logos and the PowerToys Run plugin icon now derive from `Assets/Brand/256x256.png`; obsolete `Images/icon.png`, `icon-dark.png`, and `icon-light.png` references/assets have been removed from the PowerToys Run plugin.
- `Build-Release-x64.bat` now only prepares resources, cleans, and builds the complete Release x64 solution. `Install-RunMind-CommandPalette.bat` is the separate Command Palette package installer/uninstaller.
- Implemented `ICommandProvider4.GetCommandItem` for the stable `runmind.inline-conversation` ID so a fallback command already pinned to the Command Palette Dock can be resolved by the host.

### Known Command Palette form limitation

The multiline follow-up field remains multiline. In the Command Palette host version used for this project, `ContentFormControl` submits Enter only when the focused `TextBox` is single-line; multiline inputs are deliberately excluded so Enter inserts a newline. The extension API does not expose the host's key event to an out-of-process extension, so RunMind cannot implement the requested Enter-to-send / Shift+Enter-newline combination by itself. The current experience therefore keeps multiline input and the explicit **Send** button. Exact Enter/Shift+Enter handling requires a PowerToys Command Palette host change.

### Fallback ranking and search weight

RunMind has one `IFallbackCommandItem`, and it is returned as the only item in `FallbackCommands()`. The order of that returned array controls ordering within RunMind, not the global ranking between extensions. The documented extension API has no `SearchWeight` property; cross-extension fallback order/search weighting is controlled by Command Palette itself. The SDK only allows an extension to return its own fallback items in order. The new `GetCommandItem` override addresses the separate Dock pin lookup warning; it does not force global search ranking.

Validation for this update: source-level checks, Adaptive Card/project/manifest JSON/XML parsing, icon dimensions and alpha checks, stale-reference scans, timeout-path scans, and ZIP integrity. A Windows/.NET compilation and live provider tests were not available here. Run `Build-Release-x64.bat` on Windows, verify the solution build, and use `Install-RunMind-CommandPalette.bat` for the extension package.


## Inline settings form compile fix — 2026-11-10

The next Windows build report identified two C# local-variable shadowing errors in the provider-switch branch of `InlineModelSettingsPage.SubmitForm`. The provider-switch locals are now named `providerSavedAt` and `providerPersisted`, avoiding collision with the save-action locals later in the method.

The Command Palette SDK annotates the two Adaptive Card form payload arguments as potentially nullable. Both settings and conversation handlers now normalize null payload arguments to empty JSON objects (`{}`) before passing them to the payload-flattening helpers. This addresses the four nullable warnings reported at those call sites without changing valid payload handling.

Validation: source assertions, project/manifest XML parsing, JSON validation, and ZIP integrity. The .NET SDK is not installed in this environment, so a Windows compile has not been run here.


## Unified x64 release build and separate Command Palette installer — 2026-11-10

The release workflow now has one build entry point for all three hosts. `RunMind.sln` contains `RunMind.Standalone`, `Community.PowerToys.Run.Plugin.RunMind`, and `RunMind.CommandPalette`; `Build-Release-x64.bat` verifies all three project files, prepares pinned OCR resources, cleans/builds the full Release x64 solution, and then packages/signs the Command Palette MSIX. It performs no installation.

`Build-RunMind-CommandPalette.ps1` is the package step called by the release script. It creates/reuses the local code-signing certificate in the current user's certificate store, signs and validates the MSIX, and exports only the public certificate. Release outputs are `Release\CommandPalette\x64\RunMind.CommandPalette.msix` and `RunMind.CommandPalette.cer`.

`Install-RunMind-CommandPalette.bat` is now install/uninstall only. It consumes the prebuilt MSIX, verifies its signature and publisher, requests one administrator approval to add the public certificate to Local Machine\Trusted People when necessary, and installs the package. It does not require the .NET SDK, prepare OCR assets, restore NuGet packages, compile, or package the application. It does not enable Developer Mode or modify Windows developer settings.

Windows 10 version 2004 and later and Windows 11 enable sideloading by default. A signed MSIX still has to be trusted by the target device. The local self-signed certificate handles this for controlled test machines; a CA-trusted code-signing certificate or Microsoft Store signing is required for friction-free public distribution. Enterprise policy can disable sideloading independently of Developer Mode.

Validation for this source update: solution project membership, batch-script call graph, PowerShell source structure, project/manifest XML, release output path consistency, and ZIP integrity were checked statically. This environment does not have Windows PowerShell or the .NET SDK, so PowerShell execution, MSIX packaging/signing, installation, and the Windows build remain to be tested on the target machine.


## Command Palette Dock isolation and host-specific default prompt — 2026-11-10

- The fallback query handler remains a long-lived `InlineAnswerPage` so it can own the active root-query and follow-up history. The pinned Dock resolver now creates a new `InlineAnswerPage` for the same stable `runmind.inline-conversation` identifier. This prevents the Dock page from inheriting the root fallback's current search or transcript. The page's form label is now neutral (**Your message**) and its empty state explains both entry paths.
- Added `CommandPaletteSystemPrompt` to the shared settings schema with the requested launcher-focused default. This is distinct from `InlineSystemPrompt`, which remains the PowerToys Run inline prompt. Command Palette request generation uses its dedicated value across Ollama, LM Studio native chat, and LM Studio OpenAI-compatible mode. Its settings page edits only this dedicated value. PowerToys Run's save path carries the Command Palette value forward unchanged so saving its own settings does not overwrite it.
- The inline form remains multiline. The current PowerToys Command Palette host submits Enter only for a single-line TextBox; for a multiline TextBox Enter is consumed as a newline, and the extension API does not expose a Shift-aware key event. Enter-to-send plus Shift+Enter-newline requires an upstream Command Palette host change; RunMind continues to provide the explicit Send action rather than introducing a broken single-line workaround.

Validation: project/manifest XML, JSON, source-level Dock and prompt separation assertions, references, and archive integrity. Windows/.NET compilation and live dock reactivation were not available in this environment.


## Windows x64 runtime-pack restore and .NET telemetry opt-out — 2026-11-10

The release log showed `NETSDK1112` during `dotnet clean`: the Command Palette project declares `RuntimeIdentifiers=win-x64`, but the .NET 10 Windows runtime packs had not yet been restored. `Build-Release-x64.bat` now explicitly runs `dotnet restore RunMind.sln --runtime win-x64 -p:Configuration=Release -p:Platform=x64` before cleaning. This restores the `Microsoft.NETCore.App.Runtime.win-x64` and `Microsoft.WindowsDesktop.App.Runtime.win-x64` packs required by the Command Palette project. The existing clean/build and separate MSIX release packaging steps follow afterward.

The release script sets `DOTNET_CLI_TELEMETRY_OPTOUT=1` before invoking any .NET CLI command. It also sets `DOTNET_NOLOGO=1`, `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`, and `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1` to suppress .NET first-run welcome/logo and workload update notices in that build process. These are process-scoped settings; they do not modify the user's persistent Windows environment.

Validation: batch-script step order and environment flags were checked statically; project XML and archive integrity were verified. This environment has no Windows .NET SDK, so the restore/clean/build sequence must be confirmed on the target PC.


## Dock and search conversation controls — 2026-11-10

- The Dock now resolves to one dedicated, persistent `InlineAnswerPage` owned by `RunMindCommandsProvider`. It is separate from the long-lived search-fallback page, so the Dock and search fallback no longer share transcript state. Repeated Dock activation can continue the Dock's own thread.
- Both the regular inline fallback and pinned Dock page now expose **Start fresh** alongside **Send** and **Open chat with context**. Start fresh cancels an active generation, clears the current prompt/answer and all previous turns, resets provider/model display state, replaces the Adaptive Card form to clear its text field, and keeps the page open. This gives users an explicit choice to continue the existing inline thread or clear it without opening the full chat window.
- The pinned Dock `ICommandItem` is now created once and reused, instead of creating a new page object from `GetCommandItem` on each lookup. The page keeps its own history while the extension provider lives; Start fresh clears it on demand.
- The inline message field remains multiline. The current Command Palette host handles Enter-to-submit only for single-line `TextBox` inputs; it does not expose modifier-aware key events to the out-of-process extension. Therefore the exact **Enter = send / Shift+Enter = newline** combination still requires a host-side `ContentFormControl` change. The supported extension UI keeps multiline input and the Send action rather than breaking Shift+Enter/newline entry. See upstream PowerToys issue #46003 and fix PR #48768 for the host's Enter submit behavior.

Validation: source-level assertions, project/manifest parsing, Adaptive Card JSON validation, and archive integrity. Windows/.NET compilation and in-Dock runtime testing remain to be confirmed on the target machine.


## Command Palette API authentication and empty-chat handoff — 2026-11-10

The Command Palette inline settings now expose separate optional API key and custom authentication header fields for Ollama and LM Studio. API keys are sent as `Authorization: Bearer <key>`. A custom header accepts `Header-Name: value`; it is sent with the request and replaces the API-key Authorization header if the custom header is also named `Authorization`. Both credential types are applied to model discovery and generation requests. Values are not written to RunMind diagnostics. They are stored in the user's local RunMind settings JSON without DPAPI encryption, so the Windows account and its local profile remain the security boundary. These credentials are scoped to Command Palette inline requests, not PowerToys Run.

Selecting **Open chat with context** on an empty inline page now launches the Command Palette chat host in empty mode rather than throwing `There are no inline messages to transfer.` Existing non-empty transcript handoff remains unchanged.

The search fallback and Dock command now have distinct IDs (`runmind.inline-search-fallback` and `runmind.inline-conversation`). The Dock item is also exposed as a normal top-level command named **RunMind Dock**, so it can be pinned by new users; existing pins that still reference `runmind.inline-conversation` resolve to the persistent Dock page. This removes the duplicated ID that appeared in the Command Palette log.

### Enter / Shift+Enter limitation

The extension uses Command Palette `FormContent` / Adaptive Cards. The host implementation submits Enter only when the focused text box does not accept returns (`AcceptsReturn == false`). The inline prompt must be multiline to support newlines, so the extension cannot implement Enter-to-send while reserving Shift+Enter for a newline. That behavior needs a host-side PowerToys change: for a multiline input, submit on Enter when Shift is not pressed, and allow Shift+Enter through to the TextBox. RunMind's extension cannot intercept that key event itself.

The uploaded Command Palette exception report points to `Microsoft.CmdPal.UI.Settings.ExtensionsPage` while setting `ItemsRepeater.ItemsSource`; it has no RunMind stack frame. The accompanying host log also shows the RunMind duplicate pinned command ID; that ID collision is addressed by the distinct fallback/Dock IDs above. The host-side `ItemsRepeater` exception itself remains a PowerToys issue unless a new stack trace implicates RunMind.


## Command Palette authentication and empty-chat handoff hardening — 2026-11-10

The Command Palette settings form provides separate optional API-key and custom-auth-header fields for Ollama and LM Studio. API keys are sent as `Authorization: Bearer <key>`; custom headers use `Header-Name: value` and override the generated Authorization header when their name is `Authorization`. Both values are applied to model discovery and all supported generation request paths. Credential values are not logged. The Adaptive Card fields use password masking. The stored values remain in the per-user `inline-model-settings.json` without DPAPI encryption; this is explicitly disclosed in the settings UI.

The custom header parser now requires an RFC-compatible ASCII header name and a control-character-free value, rejects reserved transport/content headers, and avoids CR/LF/header injection.

When **Open chat with context** is selected from an empty inline conversation, Command Palette now launches RunMind chat in empty mode rather than rejecting the empty transcript. Non-empty conversations continue to transfer the complete ordered history.

The search fallback ID is `runmind.inline-search-fallback`, distinct from the persistent Dock command ID `runmind.inline-conversation`. The pinned Dock page is exposed as a top-level command and remains independent from search fallback history. Older pinned IDs continue to resolve to the persistent Dock command.

The exception report attached for this update is a PowerToys Command Palette settings-page exception in `ItemsRepeater.set_ItemsSource`; its stack contains no RunMind frame. The RunMind COM log shows the inline request and context chat process completing successfully. The duplicate RunMind Dock/fallback ID warning in the corresponding Command Palette log is addressed by the separate IDs above.

Enter/Shift+Enter remains a Command Palette host limitation. The extension receives Adaptive Card submissions, not the focused TextBox keyboard event. The host's current form control must be changed so Enter submits for multiline fields while Shift+Enter is left unhandled to insert a newline. This cannot be implemented reliably inside the out-of-process RunMind extension alone. See `docs/COMMAND-PALETTE-KEYBOARD-HOST-PATCH.md`.
