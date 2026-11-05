## RunMind 1.10.20

Custom OpenAI-compatible connections do not probe LM Studio-specific `/api/v1/models` or legacy `/api/v0/models` endpoints unless **Use LM Studio native metadata API** is explicitly enabled. The built-in LM Studio connection retains this capability. Message headers show the selected connection alias; older saved messages without that metadata use the legacy provider label.

## RunMind 1.10.19 — Native Reader DNS and Nemotron settings layout

- Corrected the Nemotron Conversational Mode checkbox layout: compact checkbox, aligned label, full-width explanatory text.
- Native Reader now filters mixed DNS answers and connects only to globally routable IPs instead of rejecting an entire public hostname because a separate answer is private/reserved.
- DNS responses containing no public address remain blocked as an SSRF protection. The diagnostic identifies the hostname and suggests checking local DNS/VPN/DNS filtering.

Overlay this release on the complete RunMind 1.10.18 source tree. Preserve `Assets`, `lib`, `Vendor`, `OCR`, and other omitted resources. Rebuild on Windows with `Build-Release-x64.bat`.

## RunMind 1.10.18 — Reader routing, Nemotron conversational mode, and branch-aware saves

- Desktop page retrieval first uses the shared C# Native Reader. If it fails to retrieve or extract readable content, RunMind tries the configured Custom Reader gateway, when present. Jina Reader is last: it is used directly when no custom gateway is configured, or after a configured custom gateway fails.
- Private/local/reserved destinations and other Native Reader security refusals are hard stops. They do not fall through to a remote Reader.
- Chat Generation Settings shows **Nemotron Conversational Mode** only for recognized NVIDIA Nemotron 3.5 Bolt E4B model IDs. The checkbox is usable on OpenAI-compatible connections and sends `chat_template_kwargs.conversational_mode=true` only when enabled. The PowerToys Run checkbox is independent and applies only to matching models sent through its LM Studio/OpenAI-compatible provider.
- The native Ollama API and LM Studio Native REST endpoint do not expose this chat-template argument. llama.cpp's OpenAI-compatible `/v1/chat/completions` API documents `chat_template_kwargs`; verify pass-through with the specific LM Studio/backend version. The checkbox defaults off.
- Saving a branched conversation now offers **All branches** (default) or **Active branch only**. Full `.runmind` saves include every branch's complete message history, branch names/IDs, active selection, and fork-origin metadata where available. Active-only saves remain standalone single-branch conversations. Existing `.runmind` files without a branch workspace remain backward compatible.
- The shared branch archive in IndexedDB preserves full branch histories between reloads; localStorage remains a compact fallback. Saving a `.runmind` file is the portable full-history backup.
- The shared Native Reader now treats non-text response types (PDF/image/audio/video) as a reason to use the configured Reader fallback instead of trying to parse their bytes as HTML. Native failure reasons remain available in source details and report exports. Successful native reads log the hostname, content type, and elapsed time to the application console; the full URL and query string are not logged.
- This source overlay adds no separate service or dependency. Merge the included files into the complete 1.10.17 tree, preserving assets, libraries, vendor files, OCR data, and build scripts. Build on Windows with `Build-Release-x64.bat`.

## RunMind 1.10.17 — Native-first webpage reading

Both the PowerToys Run plugin and standalone WPF app now include `Shared/NativePageReader.cs`. It is linked into both projects and invoked through the existing WebView2 message channel; no extra service, port, container, host object, or install step is required.

Desktop retrieval order:
1. Native C# `HttpClient` fetches public HTTP/HTTPS pages and returns bounded response text to the existing JavaScript metadata/HTML-to-Markdown converter.
2. If native retrieval or extraction fails, RunMind tries the configured Custom Reader gateway, but only when one has been configured.
3. If no custom gateway is configured, or if the configured custom gateway fails, RunMind tries Jina Reader.

The native reader validates every redirect and DNS connection, rejects private/local/reserved destinations, sends no cookies or ambient credentials, blocks HTTPS-to-HTTP redirect downgrades, and enforces a 25-second request timeout, five redirects, and a 1.5 MiB response cap. It does not execute page JavaScript; dynamic pages, bot challenges, and unsupported content may still require the remote Reader. Native outbound HTTP bypasses system proxy settings to preserve destination validation; the existing remote fallback remains available for proxy-dependent networks.

The Agent setting **Use an external Reader only when the local Native Reader fails** controls the remote fallback. Turning it off keeps webpage retrieval local and reports the native failure instead. Browser-only previews continue to use browser fetch because they do not have the WPF native bridge. Direct binary downloads are unchanged and are not replaced by Markdown extraction.

The ZIP is a source overlay. Extract over the complete project tree so `Assets`, `lib`, `Vendor`, `OCR`, build scripts, and other existing resources remain available. Build both projects on Windows with the repository's normal `Build-Release-x64.bat` script. This environment cannot run the Windows/.NET 10 build or live WebView2/network checks.

## RunMind 1.10.16 — literal identifier rendering

- Fixed Markdown emphasis boundaries so embedded underscores and asterisks in identifiers remain literal. For example, `UD-Q3_K_XL` and `UD-Q3*K*XL` are no longer interpreted as emphasis around `K`.
- This corrects display rendering; `sendMessage()` and `providerMessages()` already retain the raw user-entered string in the conversation and provider payload.
- Normal standalone Markdown emphasis remains supported. Inline code remains protected from formatting parsing.

# RunMind build and setup notes

## Source overlay

This release package is an overlay for the complete RunMind 1.10.17 source tree. It contains the updated shared chat UI, both native WPF host files, the shared `NativePageReader.cs`, project/plugin version metadata, and release documentation. The trimmed development archive used to prepare this release omitted native host code and supporting resources, so this overlay restores the required host source files but still relies on the complete project for `Assets`, `lib`, `Vendor`, `OCR`, build scripts, and other resources. Merge the included paths into the complete tree before building.

## RunMind 1.10.15 — Branch naming correction

- Generic child branches now use `Branch 1`, `Branch 2`, etc. Branches created from meaningful prompts still receive descriptive prompt-based names.
- Saved auto-generated `Alternative N` labels are migrated back to `Branch N`; existing descriptive names are retained and collisions are resolved.
- The editable QuickAI prompt default from 1.10.14 is unchanged.

## RunMind 1.10.14 — QuickAI prompt refinement

- PowerToys Run's editable multiline prompt defaults to the exact two-sentence QuickAI prompt requested for that release. When an existing settings value exactly matches the prior built-in starter prompt, RunMind updates it to the new default. Custom edits and an explicitly empty prompt are preserved.
- 1.10.14 temporarily changed generated generic branch labels from `Branch N` to `Alternative N`; 1.10.15 restores the requested `Branch N` labels.

## RunMind 1.10.13 — copy sent prompts

- Added a **Copy** action beneath text-bearing user messages in the chat transcript. It copies the original prompt text exactly as entered, including line breaks and Markdown syntax.
- Copying is available even while another assistant response is streaming. It does not change the active prompt, branch, or generation state. Attachments are not embedded into the copied text.
- Existing assistant-response copying, user-message editing, and branch actions remain available under their previous rules.

## RunMind 1.10.12 — QuickAI default and native window placement

- PowerToys Run's multiline **Custom system instructions** setting is pre-filled with the editable QuickAI starter prompt. Users can rewrite it or clear it completely; an explicitly empty setting disables the additional instructions. The existing `InlineSystemPrompt` key is preserved.
- New chat windows in both PowerToys and standalone start at 1120 × 836 DIPs instead of 1120 × 860 DIPs. A shared native helper selects the monitor under the pointer, converts its Windows work area to WPF DIPs, clamps the initial dimensions with a 12-DIP safety margin, and centers the window in that work area.
- The placement helper runs once during `SourceInitialized`; it does not keep adjusting the window while the user resizes it. This release includes `Shared/WindowLayout.cs`, `RunMind.Standalone/MainWindow.xaml`, and `RunMind.Standalone/MainWindow.xaml.cs` because the native host source is required for this change.
- The 1.10.11 PowerToys query fix is retained: delayed query work returns the completed result directly to its query generation; no `ChangeQuery` call writes text back into the search box.

## RunMind 1.10.11 — PowerToys Run live-input preservation

- The fast PowerToys query callback still returns an immediate pending result and cancels the previous query when the user types a newer one.
- The delayed plugin phase runs on PowerToys' background query worker. RunMind performs the provider request there and returns the completed result directly from `IDelayedExecutionPlugin.Query`.
- RunMind no longer calls `IPublicAPI.ChangeQuery` to refresh a response. That API changes the search-box text, which could overwrite characters typed after a fast model response. PowerToys checks the delayed task's query-session cancellation and generation before adding its results.
- The asynchronous HTTP implementation uses `ConfigureAwait(false)` throughout the provider/model-read path, so synchronously waiting inside the delayed worker does not require the WPF UI dispatcher. The fast keystroke callback never waits for network I/O.

## RunMind 1.10.10 — multiline PowerToys Run prompt

- In PowerToys Settings → PowerToys Run → RunMind, **Custom system instructions** is now a native multiline text editor. Users can paste or type multiple paragraphs and press Enter for line breaks; PowerToys provides the vertical scrollbar.
- The setting retains the `InlineSystemPrompt` key, so the existing preference is preserved. Legacy literal `\n` line-break sequences are still normalized. The 12,000-character limit is enforced in the settings editor and runtime.
- Requires PowerToys Run 0.80.0 or later, which introduced the `MultilineTextbox` additional-option type.

## RunMind 1.10.9 — flyout scrolling hotfix

- All toolbar flyouts and the Conversations menu now allow uninterrupted scrolling. Scrolling inside a menu no longer triggers the document-level viewport-reposition handler.
- The shared placement routine preserves the outer panel and nested scroll offsets when the window is resized or the panel otherwise needs repositioning. It no longer removes the panel height limit during measurement, which was causing Chromium/WebView2 to clamp its scroll position back to zero.
- Flyout scroll boundaries are contained so the page behind the menu does not unexpectedly move.

## RunMind 1.10.8 — Custom instructions and generation settings

### PowerToys Run
- Open PowerToys Run settings, select RunMind, and use **Custom system instructions (optional)** for PowerToys inline requests only.
- The PowerToys Additional Options control is a single-line text field. Enter the literal sequence `\n` where a line break is required. The prompt is limited to 12,000 characters.
- Temperature, Top P, Max output tokens, and Seed are optional. Leave them empty to use provider defaults. Valid ranges are Temperature `0–2`, Top P `0–1`, Max output tokens `1–1,000,000`, and Seed `0–2,147,483,647`.
- These settings are independent from the chat application's local preferences and are not transferred when a pending query is opened in chat.

### Chat application
- Open the toolbar **Generation settings** menu.
- Edit **Custom system instructions** in the multiline editor (up to 16,000 characters). The character counter and preview show how the prompt is composed. **Clear custom instructions** is separate from **Reset generation settings**.
- The chat prompt is saved in the chat WebView's local preferences and applies to normal chat, Agent Mode, Deep Research, and specialist sub-agents. Active agent/research tasks retain the prompt snapshot with which they started.
- RunMind's built-in tool contracts, approval rules, operational constraints, and mode instructions remain active. User preferences do not grant permissions or replace those requirements.
- Temperature, Top P, Max output tokens, Context length where supported, and Seed are applied to regular and tool-calling requests. Some provider/model combinations reject optional parameters; RunMind only removes a parameter after an explicit validation error identifies it.

## Build

Use the complete Windows source tree and its existing build script:

```powershell
.\Build-Release-x64.bat
```

The build script, PowerToys reference assemblies, assets, vendor resources, and OCR data must be present at the paths expected by the project files. This trimmed overlay does not include those dependencies.

## Validation boundary

JavaScript syntax, request mapping, settings composition/persistence, XML/JSON metadata, and package integrity are checked during preparation. A Windows/.NET 10 compilation and live PowerToys, WebView2, Ollama, and LM Studio integration test must still be performed on Windows.
