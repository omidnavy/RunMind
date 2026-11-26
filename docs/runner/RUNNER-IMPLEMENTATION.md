# RunMind Runner — implementation and capability boundaries

## Purpose

RunMind Runner is an optional, standalone Windows entry point. It combines a fast native launcher, the RunMind inline-answer experience and a restricted shared RunMind chat interface. It does not require PowerToys and can be installed or removed independently from Standalone, PowerToys Run and Command Palette.

Runner is a Quick Host, not a copy of the Full Agent. Its chat page reuses the actual shared RunMind ChatWindow and theme catalogue, but the native host exposes no workspace, file attachment, Full Agent, developer, MCP, Skills or System Manager tool capabilities. Quick Agentic Mode and its system-management capability registry are a later shared milestone for all three quick hosts.

## Launcher plugin architecture

The launcher is driven by `IRunnerLauncherPlugin` and `RunnerPluginRegistry`. Each built-in plugin independently receives the current search text and may return a bounded list of typed results. Results are ranked centrally and dispatched through a small action enum; a plugin does not execute an action merely because text was typed.

The initial first-party plugin set is:

- **Applications** — bounded current-user and all-users Start Menu shortcut catalogue.
- **Files & Folders** — opens an explicit rooted path selected by the user.
- **URLs** — opens explicit HTTP/HTTPS URLs only.
- **Calculator** — a local, bounded recursive-descent arithmetic parser. It never uses `eval`, `Function`, shell parsing or model inference; the selected result copies the calculated value.
- **RunMind** — the inline-answer plugin. It follows the PowerToys Run action-keyword convention and activates only for `llm <prompt>`. Ordinary searches such as `notepad` remain ordinary launcher searches. The result stays inside Runner and streams the answer in its dedicated pane. `> model > prompt` overrides the default model, matching the existing inline provider syntax.

Future RunMind first-party launcher features can implement the same internal contract and register with the registry without changing the Runner shell. The Runner implementation introduced in 3.4.0 and carried forward in 3.4.1 does not load arbitrary third-party DLLs, scripts or remote plugin packages.

For an explicit `llm <prompt>` query, the registry returns the RunMind result rather than mixing in unrelated application/path matches. Model requests are debounced, cancellable, bounded by configured timeouts and response limits, and never start for unrelated launcher text. A new search cancels the previous request. Selecting the RunMind result focuses the inline response; it does not unexpectedly open a second window.

## Inline model settings

Runner has a provider profile dedicated to its RunMind launcher plugin. It is deliberately separate from the full ChatWindow provider profile and does not overwrite PowerToys Run or Command Palette settings.

Supported providers:

- **Ollama native API** — full `/api/generate` endpoint; model discovery through `/api/tags`.
- **LM Studio** — base URL; model discovery and generation use the native LM Studio API where available, with OpenAI-compatible `/v1` fallback for supported compatibility cases.

Runner settings include independent per-provider API keys and custom authentication headers, default model, model discovery/connection test, thinking/reasoning mode, keep-alive, generation/discovery timeouts, optional Ollama CPU thread limit, optional temperature/Top P/output-token/seed values, and the optional Nemotron 3.5 Bolt E4B conversational-mode compatibility flag.

Custom authentication header takes precedence over the corresponding Bearer API-key header when its header name is `Authorization`. Header names and values are validated before sending. Provider URLs must be absolute HTTP(S) URLs without embedded credentials, query strings or fragments. LM Studio temperature is limited to 0–1; Ollama temperature is limited to 0–2. Empty optional generation values are omitted so the model/provider default remains in effect.

API keys and custom headers are stored in a separate DPAPI `CurrentUser`-protected file under `%LOCALAPPDATA%\RunMind\Runner`. Non-secret model/provider settings are stored in `inline-model-settings.json`. Secrets are never serialized into that JSON file. Credentials are not migrated to, or shared with, the other PowerToys hosts by this Runner-only profile.

## Full Chat handoff

The header's **Open chat** action opens a lazy RunMind ChatWindow instance. It uses the canonical shared HTML, JavaScript, CSS and theme catalog. The current inline prompt and completed answer are handed into the initial transcript, avoiding a second generation for an answer that has already completed. If the user hands off while streaming, the partial response is preserved and the Runner request is cancelled. Full chat provider settings remain the existing ChatWindow settings and are independent of the Runner inline provider profile.

Runner ChatWindow uses `quickHostMode`. The native host does not instantiate `AgentHostBridge`, `WorkspaceFileBridge`, `NativePageReader`, `SystemManager` or MCP/Skills adapters. It only handles theme discovery and the narrow chat lifecycle message. Workspace selection, file attachment and Full Agent controls are disabled in both UI and event handlers; unsupported native requests are rejected.

## Theme system

Runner reuses `Shared/ThemeCatalog.cs` and `Shared/AppearancePreferences.cs`, discovers the same metadata-driven CSS themes as the other RunMind hosts, and persists the shared appearance preferences. The native WPF launcher maps the selected theme's palette tokens onto its resource dictionary. The embedded ChatWindow uses the actual shared RunMind theme CSS and components. Runner does not maintain a second theme catalogue.

## Startup and lifecycle

- The native WPF launcher appears without initializing WebView2 or contacting an LLM provider.
- The Start Menu shortcut catalogue loads asynchronously after the window is usable.
- Model discovery only happens on request or when `llm <prompt>` is submitted after the input debounce.
- The WebView2 chat window is created only after **Open chat** is selected.
- A configurable global hotkey defaults to `Ctrl+Alt+Space`.
- A single-instance mutex, system-tray lifecycle, Escape-to-hide and explicit Exit are included.
- Windows sign-in startup is opt-in and writes only the current-user RunMind Runner registry value.
- Install/uninstall scripts are independent and do not modify the other three hosts.

## Build and install

On Windows with the .NET 10 SDK and Windows Desktop build components:

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

The build publishes a framework-dependent `win-x64` executable to `Release\Runner\win-x64\publish`. The target machine needs the .NET 10 Desktop Runtime. Runner is optional and does not need PowerToys.

## Windows acceptance still required

- Build the full Release x64 solution with the .NET 10 SDK and capture output.
- Install Runner as a standard user; verify Start Menu/optional Desktop shortcuts, upgrade and uninstall.
- Verify hotkey conflict handling, hide/show, tray menu, duplicate launch behavior and clean exit.
- Verify optional startup writes/removes only the current-user Runner Run value and that `--background` starts hidden.
- Exercise application indexing with inaccessible Start Menu paths, reparse points and high shortcut counts.
- Verify the built-in plugin results: application launch, rooted file/folder open, HTTP/HTTPS URL open, calculator arithmetic and invalid-expression handling.
- Verify `notepad` and ordinary search text never trigger model requests; `llm <prompt>` does, and `llm > model > prompt` selects the explicit model.
- Test both Ollama and LM Studio discovery, authentication precedence, generation streaming, cancellation, provider-specific temperature validation, request timeout and discovery timeout.
- Test **Open chat** with completed and in-progress inline answers; ensure the existing answer/partial text is transferred rather than regenerated.
- Verify the shared light/dark/system themes in Runner and ChatWindow, including theme changes from either surface.
- Verify Quick Host cannot select a workspace, attach files, invoke Full Agent, MCP, Skills, developer tools or System Manager capabilities.
- Measure idle and active memory/CPU, first launch, first ChatWindow initialization, and repeated open/close behavior.
- Verify the Runner installer and uninstaller leave Standalone, PowerToys Run and Command Palette untouched.

## Validation boundary

The packaging environment has Node.js 22.16.0 but no .NET SDK, MSBuild or Windows runtime. Portable Node tests, JavaScript syntax checks and XML/source validation can be run here; they do not prove C# compilation, generated WPF XAML compilation, WebView2 runtime behavior, DPAPI operation, native hotkey/tray behavior or installer correctness. Complete the Windows acceptance items above before treating Runner as certified.

## Next milestone: shared Quick Agentic Mode

After Runner's Windows acceptance, introduce one restricted Quick Agent Core and one approval/security boundary for Runner, PowerToys Run and Command Palette. Host modes remain independently configurable (Chat only, Agentic with approval, Quick actions with explicitly allow-listed low-risk operations). Global policy remains authoritative. Full Agent and workspace/developer capabilities stay in Standalone.
