# Build and setup — RunMind 1.7.4

RunMind is one Windows application with two entry points: the PowerToys Run plugin and a direct-launch standalone desktop executable. Both hosts load the same `ChatWindow.html` and JavaScript application. There is no installer, startup registration, system tray, service, daemon, or second chat implementation.

## Requirements

- Windows 11 x64, build 22621 or later, with the matching Windows SDK targeting pack.
- .NET 10 SDK. Both projects target `net10.0-windows10.0.22621.0`, matching the current PowerToys plugin checklist.

### PowerToys plugin linting

`Community.PowerToys.Run.Plugin.Lint` 0.6.0 still reports a `net9.0` target-framework rule. That rule is stale: the current Microsoft PowerToys plugin checklist specifies `net10.0-windows10.0.22621.0`. Keep RunMind on .NET 10 rather than downgrading to satisfy the older linter rule.

The linter's `Repository missing` result also requires a published GitHub repository and matching plugin metadata. RunMind's current Git remote is a private/local Gogs endpoint, so no public GitHub repository URL is claimed in this source package.
- WebView2 Runtime installation is managed by RunMind. At startup, both hosts perform a quick availability check. If the Evergreen Runtime is missing, RunMind downloads Microsoft's official bootstrapper and silently installs it per-user. Only that first launch needs internet access and may take longer; normal launches use the existing shared runtime. See [Microsoft's WebView2 distribution guidance](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution).
- For the PowerToys plugin build, matching PowerToys reference assemblies are included under `lib\x64`: `PowerToys.Common.UI.dll`, `PowerToys.ManagedCommon.dll`, `PowerToys.Settings.UI.Lib.dll`, `Wox.Infrastructure.dll`, and `Wox.Plugin.dll`. Replace them with assemblies from your installed PowerToys build if you need to target a different PowerToys version.
- Ollama or an OpenAI-compatible model server reachable from the host/browser.

## Agent file and data capabilities in 1.7.2

The Agent can read supported text/source attachments by exact filename, validate attached or workspace JSON files (including bounded JSON Schema checks), read attached or workspace PDFs with PDF.js, automatically OCR pages whose extracted text is missing or suspicious, and inspect attached or workspace ZIP archives. PDF and ZIP attachments are signature-checked and bounded; binary content is retained when a `.runmind` conversation is saved and reopened. ZIP extraction remains approval-gated, CRC-checked, and writes only into a new folder inside the selected workspace. PDF reading is limited to 25 MB and 500 pages; encrypted PDFs, handwriting, low-quality scans, and complex reading order may still need manual review. CSV inference/joins and model endpoint discovery diagnosis are also available. See `FEATURE-MATRIX.md` for limits.

## PDF/OCR resources and build

RunMind uses PDF.js for PDF parsing and Tesseract.js for local OCR. The source overlay pins PDF.js `6.5.438`, Tesseract.js `7.0.0`, and `tesseract.js-core` `7.0.0`. English (`eng`) and Persian (`fas`, selected in RunMind as `fa_IR`) are the bundled language choices.

The overlay intentionally does not contain the third-party JavaScript/WASM binaries or traineddata files. On the first build, `Prepare-RunMind-OCR.ps1` downloads the pinned packages and the two language models into `Vendor\` and `OCR\tessdata\`. This first preparation requires internet access. It does not require Node.js or npm; it uses Windows PowerShell and the built-in `tar.exe`. Once those resource folders are populated, subsequent builds skip downloads and can run offline. Both project files copy the local resources into their output directories.

Use the included one-click script on Windows:

```powershell
.\Build-Release-x64.bat
```

For a manual build, prepare the resources first, then restore and build:

```powershell
.\Prepare-RunMind-OCR.ps1
dotnet restore .\RunMind.sln
dotnet build .\RunMind.sln -c Release -p:Platform=x64
```

The standalone host can be built independently after the same preparation step:

```powershell
dotnet build .\RunMind.Standalone\RunMind.Standalone.csproj -c Release -p:Platform=x64
```

Run `RunMind.exe` directly from the standalone build output. Every launch creates a separate process and chat window. Multiple standalone windows can run simultaneously; each receives a unique window instance ID. The native title bar follows the Windows app light/dark appearance preference and updates when that preference changes.

### OCR languages

The PDF OCR menu starts with English and Persian selected. Optional language packs are downloaded individually from the `tessdata_fast` model collection and cached in the current WebView2 profile. After a successful download, that profile can use the pack offline. Select multiple languages for mixed-language documents, for example `eng+fas`. RunMind does not automatically identify a document's language.

Available language packs: French (`fra`), Spanish (`spa`), Arabic (`ara`), Chinese Simplified (`chi_sim`), Chinese Traditional (`chi_tra`), Russian (`rus`), Finnish (`fin`), German (`deu`), Italian (`ita`), Portuguese (`por`), Japanese (`jpn`), Korean (`kor`), Turkish (`tur`), and Urdu (`urd`). The PowerToys and standalone hosts use separate WebView2 profiles, so optional language packs are downloaded separately in each host. Clearing a host's WebView2 site data also clears its cached optional OCR packs.

The PowerToys project requires the matching `lib\x64` reference assemblies before it can build. After building it, deploy the **entire** plugin output directory, including `Community.PowerToys.Run.Plugin.RunMind.dll`, `plugin.json`, `ChatWindow.html`, `Images\`, `Assets\Brand\Icon.svg`, `Assets\Brand\Icon.ico`, and the copied WebView2 dependencies. Restart PowerToys Run after replacing the plugin files.

## PowerToys Run responsiveness

The plugin returns its pending result synchronously and runs model discovery/generation on a background task. For Ollama, the inline request also sends `options.num_thread` using the configurable **Ollama max CPU threads (optional)** setting. Leave it blank or set it to `0` to let Ollama choose automatically; enter `1`–`128` to pass that maximum as `options.num_thread`. This is an Ollama-only per-request limit and does not change LM Studio model settings or standalone chat.

The shared chat page throttles growing-Markdown rendering to at most one update every 90 ms. This reduces browser-side rendering work but does not control CPU/GPU usage inside the model server.

## PowerToys Run behavior

The plugin is global and retains `llm` as its explicit action keyword:

- `git bash commands` shows an immediate `Ask RunMind` result, then a completed response result after the model responds.
- `llm git bash commands` invokes the same flow explicitly.
- `llm` by itself does not launch a chat or generate a response. Search for **RunMind** to launch the standalone app through normal application search. The previous `LocalLLM` name is reserved by the plugin filter so it is not accidentally sent as a model prompt; use **RunMind** to launch the renamed executable.
- Selecting the initial result before generation completes cancels the PowerToys request and opens a chat that sends the prompt. Selecting it after completion reuses the completed answer. Selecting the completed answer opens chat with the prompt and response already present.
- Use the context menu on the completed response to copy the full Markdown answer.

PowerToys Run owns the timing of immediate and background plugin results. Its settings expose separate delays for immediate plugins and background-execution plugins; keep the defaults initially, then tune them to taste. The synchronous delayed callback now starts `Task.Run` work and returns immediately. On completion, the worker calls `IPublicAPI.ChangeQuery(rawQuery, true)` so the next fast/delayed query pass can publish the finished answer. The plugin never blocks the callback waiting for a model response and does not mutate an already-returned result with streaming tokens.

## WebView2 profiles and application data

The hosts deliberately have separate WebView2 user-data folders:

- Standalone: `%LOCALAPPDATA%\LocalLLM\Standalone\WebView2`
- PowerToys: `%LOCALAPPDATA%\Microsoft\PowerToys\LocalLLM\PowerToys\WebView2`

This avoids both hosts trying to use the same WebView2 profile concurrently. The browser stores are separate, however: `localStorage` settings, Agent task history, Deep Research checkpoints, credentials, and IndexedDB workspace directory handles do **not** synchronize between standalone and PowerToys in this version. Ordinary chat transcripts are not automatically persisted; use Save conversation / Open conversation to persist and reopen sessions. The JavaScript application is shared; the browser profile data is not.

## Cancellation behavior and boundary

A new fast query cancels the prior query operation. Cancellation is propagated through model discovery, HTTP request setup, response headers, and the provider response stream. A query-generation guard prevents delayed callbacks from an older, different search from starting new work or publishing stale results. Selecting the pending result cancels the inline request before handing the prompt to chat. Settings changes and plugin disposal also cancel the active request.

The PowerToys `IDelayedExecutionPlugin` interface does not provide a `CancellationToken`, query-cleared notification, or launcher-hidden/closed callback. Therefore the plugin can stop work on a new search or explicit hand-off, but it cannot observe the user pressing Escape or closing PowerToys Run while leaving the same query unchanged. No plugin-only implementation can guarantee cancellation for that missing host event.

## Branding

`Icon.ico` is the standalone executable/window icon. The light/dark 64px PNG exports are used by PowerToys Run. The complete supplied raster icon set is retained under `Assets/Brand/` for other UI surfaces. The chat header embeds the supplied `Icon.svg` path inline and uses `currentColor`, so its stroke follows the active UI theme.

## Validation

The 1.7.4 upgrade overlay does not include a validation report. Static checks cover JavaScript syntax, project XML/JSON metadata, release-version consistency, the composer placeholder selection rule, selectable message attribution and generation statistics, and ZIP integrity. A successful .NET 10 build and WebView2 text-selection behavior still need verification on Windows.

### Local model server CORS

The WebView2 page runs at `https://localhost`. Browser-side discovery and generation requests to Ollama/LM Studio are cross-origin, so the model server must permit the exact origin `https://localhost` (and any other origins you intentionally use). For Ollama, configure `OLLAMA_ORIGINS` to include `https://localhost` and restart the server; for LM Studio, enable its local-server CORS option / allow the WebView origin in the server's CORS configuration. The app normalizes provider URLs from `localhost` to `127.0.0.1` to avoid WebView2 virtual-host interception, but that hostname rewrite does not bypass CORS. PowerToys inline requests use .NET HTTP and are not subject to browser CORS.

## Multiple standalone windows

Each normal launch of `RunMind.exe` starts a new independent process and chat window. You can keep several standalone windows open alongside any number of PowerToys-launched chat windows. The standalone WebView2 profile is shared between standalone processes, while each chat receives its own window instance ID and conversation task state.


## RunMind project and deployment names

The source projects and folders are now `RunMind.Standalone` and `Community.PowerToys.Run.Plugin.RunMind`; the solution is `RunMind.sln`. The plugin assembly name is `Community.PowerToys.Run.Plugin.RunMind.dll`, and the standalone executable is `RunMind.exe`. The plugin GUID and `llm` keyword remain unchanged so PowerToys settings stay associated with the same plugin.

When updating an existing installation, close PowerToys completely, remove the old deployed `Community.PowerToys.Run.Plugin.LocalLLM` plugin folder, and deploy the new `Community.PowerToys.Run.Plugin.RunMind` output folder. Do not leave both folders installed: they carry the same plugin GUID. The `%LOCALAPPDATA%\LocalLLM\...` WebView2 profile locations and browser storage keys intentionally remain unchanged to preserve existing settings, credentials and workspace permissions.

## Conversation file title and Save As behavior

RunMind's only native conversation format is `.runmind` (JSON with `format: "runmind"`). Other conversation formats are rejected. Opening a saved file sets the native and in-app titles from the selected file name (without extension). Saving opens the native Windows Save As dialog in both desktop hosts, pre-fills the generated conversation title, and defaults to `.runmind`. If the user changes the filename, the host writes that filename into the conversation payload and returns it to the page so the in-app and native titles stay synchronized. When the shared page is previewed outside a desktop host, it falls back to a normal browser download.

When a saved file is selected, choose **Open in this window** to replace the current conversation (with a confirmation when it contains messages), or **Open in new window** to preserve the current chat and create an independent RunMind window. The host bridge accepts only two bounded operations: opening a validated conversation in a new window and saving a validated conversation through the native Windows Save As dialog. WebView2 host objects remain disabled.
