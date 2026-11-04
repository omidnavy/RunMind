# Changelog

## 1.6.12 — PowerToys plugin build fix

- Fixed the PowerToys plugin compilation error in `Main.CreateSettingPanel`. A WPF `Grid` is a `Panel`, not a `Control`, so it cannot be returned directly from the `ISettingProvider` method.
- Restored the standard PowerToys pattern used by Microsoft's own plugins: `CreateSettingPanel` throws `NotImplementedException` because RunMind supplies its settings through `AdditionalOptions` and does not implement a custom WPF settings panel.
- Kept both standalone and PowerToys projects on `net10.0-windows10.0.22621.0`; documented why the third-party linter's hardcoded `net9.0` check should not trigger a framework downgrade.
- Updated project, plugin, and MCP client version metadata to `1.6.12`.
- Static source checks passed. A Windows/.NET build remains to be run on the developer machine.

## 1.6.11 — Conversation UX and reliability audit

- Replaced the ambiguous **Export** toolbar entry with **Conversations** and separated resumable Save/Open actions from one-way export formats.
- Enforced `.runmind` file extensions as well as the RunMind JSON schema, and reject empty saved conversations consistently in the page and native Save As host.
- Removed live-app message action buttons from standalone HTML transcript exports; code-copy controls remain available in exported HTML.
- Made native conversation saves safer: write to a temporary file beside the target, then replace the destination only after the complete JSON file is written.
- Clear a cached WebView2 bootstrapper after a failed launch/installation so a bad download does not block every later startup attempt.
- Made the legacy PowerToys settings-panel interface non-throwing; current settings continue to use `AdditionalOptions`.
- Corrected documentation about the Windows build target and bundled PowerToys reference assemblies, added the promised static validation report, fixed the broken documentation link, removed obsolete branding screenshots from the README, and corrected the MIT copyright placeholder.
- Static JavaScript, HTML, metadata, XML/XAML and archive checks were performed. A Windows/.NET compile and live host tests remain necessary.

## 1.6.10 — Build warning cleanup and WebView2 bootstrapper clarification

- Enabled nullable annotations explicitly in the shared native conversation Save As helper. This removes CS8632 when the helper is compiled by the PowerToys project, without enabling nullable analysis across the whole plugin project.
- Clarified that the Evergreen WebView2 bootstrapper performs a quick runtime check on normal startup and downloads/runs Microsoft's installer only when the runtime is missing.
- Clarified that first launch can take longer on a machine without WebView2; subsequent launches reuse the installed shared runtime.

## 1.6.9 — RunMind

- Added automatic WebView2 Evergreen Runtime provisioning to both standalone and PowerToys hosts. RunMind checks for an existing runtime and, only when missing, downloads Microsoft's official bootstrapper and silently installs it for the current Windows user. A first-run internet connection is needed only on machines without the runtime; users do not install WebView2 manually.
- Removed conversation-file compatibility for `.llmchat`, legacy LocalLLM JSON, and other portable JSON files. The native conversation picker and readers now accept only `.runmind` files containing `format: "runmind"`.
- Kept the PowerToys and standalone chat hosts on the same shared runtime check and the same `.runmind` conversation validation.

## 1.6.8 — RunMind

- Added a native Windows Save As dialog to both desktop hosts. The current conversation title is pre-filled, `.runmind` is the default extension, and the chosen filename is written back into the file metadata and the in-app/native window title. Browser-only preview retains a download fallback.
- Centralized the native save-dialog bridge in one shared helper linked into both WPF projects. The bridge accepts only validated messages from the local chat page; WebView2 host objects remain disabled.
- Carried forward the 1.6.7 non-blocking PowerToys delayed-query implementation, RunMind project/folder/assembly naming, file-title behavior, and open-in-current/new-window choices.

## 1.6.7 — RunMind

- Renamed the source tree and build identities consistently: `RunMind.Standalone`, `Community.PowerToys.Run.Plugin.RunMind`, `RunMind.sln`, `RunMind.exe`, and `Community.PowerToys.Run.Plugin.RunMind.dll`. The PowerToys plugin GUID/action keyword remain stable; existing `%LOCALAPPDATA%\LocalLLM` browser profiles and storage keys remain in place to preserve user data.
- New conversation files save as `.runmind` (JSON with `format: "runmind"`). At the time of 1.6.7, opening also accepted `.llmchat` and legacy LocalLLM-format JSON; that compatibility was removed in 1.6.9 before publication.
- Opening a saved conversation now offers **Open in this window** or **Open in new window**. The latter leaves the current chat untouched and creates a separate chat window from the same validated conversation payload, in both standalone and PowerToys hosts.
- Removed synchronous `GetAwaiter().GetResult()` calls from the delayed PowerToys query callback. The callback now starts an asynchronous worker and returns immediately; model discovery and generation happen off the PowerToys search callback, and results are refreshed after completion.
- Added a narrowly scoped WebView2 message for opening saved conversations in new windows. Messages are accepted only from the app's local chat document, with format/version/message-count/size checks; host objects remain disabled.

## 1.6.6 — RunMind

- Renamed the visible product to **RunMind**. The standalone executable is now `RunMind.exe`, and the PowerToys plugin display name is RunMind. The existing plugin ID/DLL name, internal namespaces/bootstrap API, action keyword, WebView2 data directories, and `.llmchat` file-format identifier are retained to preserve compatibility.
- Enabled the shared browser-side conversation-title generator for standalone chats. A prompt-derived fallback appears immediately when the first message is sent; after the first successful assistant response it is replaced by a model-generated title using the active provider/model and that connection's configured authorization headers. Both update the in-app heading, browser document title, and native Windows caption.
- Preserved independent title generation across multiple standalone and PowerToys-launched windows.

## 1.6.5

- Standalone WPF title bar now follows the Windows app appearance preference (`AppsUseLightTheme`) using the native DWM immersive dark-mode attribute.
- Re-applies the title-bar appearance when Windows broadcasts `WM_SETTINGCHANGE` or `WM_THEMECHANGED`, so switching Windows between light and dark updates an already-open standalone window.
- Retains the normal Windows title bar and window controls; no custom chrome or taskbar grouping changes.

## 1.6.4

- Set the LocalLLM ICO on both PowerToys-hosted chat window types so the title bar and taskbar use the app branding.
- Removed standalone single-instance activation; each launch now opens a separate chat window in its own process.

## 1.6.3

- Integrated the latest supplied branding exports: colorful `Icon.ico` for the executable/window, colorful `Icon.png`, all supplied raster sizes, light/dark monochrome PNG variants, and the updated theme-aware `Icon.svg`.
- Added an explicit WebView2 favicon reference and deploy the ICO beside the virtual-hosted chat page, eliminating the implicit missing `/favicon.ico` request.
- Global PowerToys mode now accepts one-character prompts instead of suppressing them before delayed execution.
- Added actionable model-discovery diagnostics for browser network/CORS failures. Clarified that WebView2 fetches originate from `https://localhost`; provider servers must allow that origin. Rewriting the provider hostname to `127.0.0.1` avoids the virtual-host collision but does not bypass server-side CORS.

## 1.6.1

- Finalized the global two-stage PowerToys Run workflow and explicit `llm` action keyword while leaving standalone launch to normal Windows/PowerToys application search.
- Tightened stale-query protection and cancellation ownership across fast queries, delayed execution, and pending-result hand-off.
- Hardened WPF dispatcher checks and WebView2 initialization/disposal diagnostics for the PowerToys host; retained independent WebView2 profiles for standalone and PowerToys.
- Updated build, setup, feature, implementation, and validation documentation for the 1.6.1 source package.
- Validation remains static in this environment: JavaScript and metadata checks pass, but a Windows/.NET build and live PowerToys/WebView2 tests are still required.

## 1.6.0

- Replaced the configurable live-chat/inline switch with a global two-stage PowerToys Run result: immediate `Ask LocalLLM` placeholder, followed by a completed response result.
- Enabled global plugin matching while retaining the `llm` action keyword. `llm` by itself no longer opens a chat window; the standalone executable is launched through normal application search.
- Added query-operation cancellation. Every new fast query cancels the previous request; the delayed path uses linked cancellation tokens for model discovery, response headers, and streamed response bodies, and suppresses stale results.
- Clicking the pending result hands the request to the chat window and cancels the PowerToys request first. Clicking a completed result opens the chat with the existing prompt and answer.
- Marshalled PowerToys window creation, WebView2 initialization entry points, clipboard actions, and teardown to the WPF dispatcher; added safe disposal and stage/HRESULT diagnostics.
- Gave PowerToys and standalone separate WebView2 user-data folders, added a standalone single-instance activation signal, and integrated the supplied ICO/PNG/SVG branding assets.
- Kept the shared ChatWindow.html implementation and existing provider, Agent, Deep Research, workspace, and MCP code intact.

## 1.5.1

- Added immediate PowerToys Run live-chat handoff as the default inline behavior; selecting the result opens the shared chat and starts the prompt automatically. The previous blocking inline behavior remains configurable. Actual inline token mutation is not supported by the PowerToys plugin result contract.
- Added Agent and Deep Research task-scoped snapshots for model connection, generation settings, and tool definitions.
- Bounded direct page/document and Agent tool-turn response bodies.
- Added collision-resistant MCP tool names and safe expired-session recovery for idempotent reads/lists.
- Improved Deep Research source provenance: discovered/read distinction, retrieval route, final URL, and failed fetch records.
- Tightened LM Studio base URL validation and cancel conversation-title generation when the owning window closes.
- Retained the one-product/two-host architecture with no installer, startup registration, tray, or service.

## 1.5.0

- Added a direct-launch Windows desktop host using WebView2 and the existing shared chat application.
- Kept the PowerToys Run plugin as the second entry point to the same product.
- Both hosts share the existing WebView2 profile and therefore the same application settings and local browser storage.
- Pressing Enter on the `llm` action keyword without a prompt opens a clean chat window.
- Added explicit WebView2 disposal and window-lifetime checks to the PowerToys chat host.
- Removed global provider/base-URL mutation during model discovery; each discovery request now uses its own connection snapshot and credentials.
- Serialized concurrent model refresh operations.
- Added bounded MCP response reads, request deadlines, and parsing for multiple SSE data events.
- Applied bounded response reads to direct webpage and Reader retrieval.
- No installer, startup registration, tray, service, Electron, C# Agent bridge, or MCP process bridge was added.
## 1.4.0-preview.7

- Added conversation-title generation only when the user explicitly opens the standalone Chat window. The request uses the same initial PowerToys Run provider/model and combines the initial prompt and response. The native caption starts as `LocalLLM`; a generated conversation title updates the native caption and in-app heading. A prompt-derived fallback is used on failure. Normal inline PowerToys Run invocations do not trigger title generation.
- Added a unique native-generated window ID to the WebView bootstrap. Recent Agent Tasks and Deep Research checkpoints are now isolated using this ID before any history/checkpoint is loaded, avoiding reliance on `window.name` as the primary namespace.
- Made model server API-key authentication persistent per connection, including a configurable authentication-header name. Saved keys are used for discovery, metadata, chat, and tool-call requests.
- Made Jina Reader API key and custom gateway token persistent, along with endpoint configuration. Added/retained per-provider connection tests and explicit credential clearing.
- Made MCP server URLs, enabled state, and bearer tokens persistent; enabled saved servers attempt reconnect at chat startup. Added individual saved-token clearing.
- Kept direct website fetch as the first retrieval strategy. Jina Reader (`https://r.jina.ai`) remains the second fallback; an optional user-configured Reader-compatible gateway is the third. No Cloudflare dependency is included.
- Improved general Agent and Deep Research evidence instructions: follow links from landing/index pages to actual version-matched manuals, release notes, and source-tree build instructions; do not treat navigation pages as substantive documentation or invent unverified build details.
- Made saved workspace directory handles persistent in IndexedDB, rechecked permissions on new chats, reconnected an existing handle rather than repeatedly opening the folder picker, and notified other open windows after selection/reconnection/change/clear.
- Kept Deep Research drawer visibility separate from Deep Research execution mode. Opening the drawer does not enable research; enabling the composer mode opens it.
- Cleaned Agent settings prose and clarified persistent credential state.
- Validation: embedded JavaScript syntax, HTML IDs/selectors, project metadata, static storage/title/tool-flow checks, and ZIP integrity. No .NET compile or live Windows/WebView2 test was possible in this environment.
- Fixed two C# compile errors in the conversation-title WebView2 update path.
- Await the WPF `DispatcherOperation<Task<string>>` directly, then await the returned WebView2 script task separately.
- No behavior or public API changes beyond restoring compilation of the preview.7 title-update implementation.

## 1.4.0-preview.6

- Fixed the Jina Reader endpoint and request shape. The default is now `https://r.jina.ai`; requests use the documented `GET https://r.jina.ai/{target-url}` format instead of the unavailable EU hostname plus a `?url=` query parameter.
- Added a configurable Reader base URL, optional Jina Bearer API key, and a connection-test button in Agent settings. Anonymous Reader access remains supported; an API key raises the documented rate limits.
- Added an optional custom Reader-compatible HTTP gateway with its own optional Bearer token and connection test. It is only attempted after Jina fails. No Cloudflare service or account is used or required.
- Jina and custom gateway tokens are held in per-window `sessionStorage` only. They are not written to `localStorage`, exported conversation files, or task history.
- Reader requests use the documented `/{target-url}` path and accept either a Jina JSON envelope (`data.content`) or readable plain-text/Markdown responses. Retrieval diagnostics now identify the provider and HTTP status/detail where available.
- Updated Reader documentation and settings text. Public URLs may be sent to the selected Reader provider; private/local URLs, embedded credentials, and likely secret-bearing query parameters are blocked from remote forwarding.
- Kept `Main.cs` byte-for-byte unchanged. The native window caption is still created by C# from the initial model name; changing or removing that caption dynamically requires a small C# host change and is not silently included under the no-C# constraint.
- Embedded JavaScript syntax, DOM ID uniqueness/references, Reader URL construction and mock response parsing, project metadata, and ZIP integrity were checked. Live CORS behavior and the .NET build still require Windows/WebView2 testing.

## 1.4.0-preview.5

- Fixed Recent Agent Tasks leaking between independent chat windows. Task-history storage now uses a versioned key scoped to the unique top-level chat-window ID; the old shared v1 history is intentionally not migrated because its rows cannot be reliably attributed to one window.
- Updated the Recent Agent Tasks help text to explain that each chat window has a separate task list. Clearing history now clears only the current window's list.
- Added a configurable EU Reader fallback for `fetch_webpage`. Direct browser fetch remains first; if browser CORS, anti-bot, or HTTP access restrictions block it, the app can send a public URL to Jina AI's EU Reader and return its extracted page text.
- The Reader fallback is enabled by default and can be disabled in Agent settings. The UI discloses that the public URL is sent to the remote reader. Local/private IPs, localhost, credential-bearing URLs, and URLs with likely secret-bearing query parameters are never forwarded to the remote reader.
- Replaced the GitHub-specific API branch in `fetch_webpage` with one generic page reader path. GitHub and HAProxy are now ordinary URL targets; there is no special workflow for either task.
- Improved Deep Research activity output to show fetched character count and the retrieval route. Error messages now preserve both the direct-fetch and Reader-fallback failures, and the research instructions tell the model to try an alternate official page rather than abandoning the task after one failed read.
- `Main.cs` remains unchanged. Embedded JavaScript syntax, fetch-fallback mocks, private-URL blocking, opt-out, HTML IDs/selectors, and ZIP integrity were checked. Live WebView2/CORS behavior and .NET compilation still require Windows testing.

## 1.4.0-preview.4

- Replaced the full-width research section above the transcript with an optional right-side overlay drawer. It no longer pushes the conversation down or permanently occupies chat space.
- Added a dedicated header toggle to open/reopen the research drawer and a visible close (×) control in its title bar. Escape also closes it.
- Separated Deep Research mode from drawer visibility. Closing the drawer does not stop research, delete its checkpoint, or silently disable the mode; switching to ordinary chat/Agent Mode hides the drawer while preserving saved research results.
- Scoped research checkpoint keys with a unique per-window identifier stored in `window.name`, while retaining sessionStorage for reload/resume in that same window. Newly created chat windows no longer read another window's checkpoint, even when WebView2 shares a browser profile.
- Cleaned Recent Agent Tasks: only conversations with evidence of an actual tool invocation are retained. Ordinary chat and greetings (such as `hi`) are removed from the old history list. The history popup is now height-limited and scrollable.
- Kept C# unchanged. The embedded JavaScript passes `node --check`; actual .NET compilation and live WebView2 behavior still require Windows testing.

## 1.4.0-preview.3

- Removed the request-specific GitHub URL → HTML → read-back → summary shortcut. Agent Mode now relies on its general tool loop to plan and execute user-requested multi-step tasks; GitHub is not a hardcoded task workflow.
- Fixed cross-window research leakage: research checkpoints are stored in per-WebView `sessionStorage`, not shared profile `localStorage`. A new chat window starts with an empty research workspace; opening a different conversation clears the current workspace association.
- Made Agent Mode and Deep Research mutually exclusive. Enabling one turns off the other; Deep Research still has access to the shared agent and enabled MCP tools.
- Added compatibility parsing for a model that prints a complete function-style tool call as text (for example, `researchupdateplan(...)`) rather than returning native structured `tool_calls`. Only a response consisting of a recognized tool invocation is promoted to an action.
- Added generic file-intent preflight and refreshes the tool registry after folder access changes, so file tools become available immediately after the user selects a folder.
- Added one general completion-audit turn for sequential multi-step Agent requests after tool execution, to discourage stopping after the first convenient action.
- Moved the clearly labeled Close workspace control into the visible research toolbar.
- Canonicalizes tool-name aliases in native tool-call responses, including missing underscores.
- `Main.cs` remains unchanged. JavaScript syntax, parser smoke tests, static HTML checks, and ZIP integrity checks were run; live Windows/WebView2 and .NET compilation tests remain unavailable here.

## 1.4.0-preview.2

- Fixed the Deep Research workspace visibility bug: Close/Escape hides the panel without cancelling or deleting the active/saved research task.
- Added Copy plan and Copy results controls for live research work and partial/final reports.
- Added persistent, explicitly opted-in per-tool “Always allow this tool” approvals, with a visible revocation list and clear-all control in Agent settings.
- Improved GitHub repository fetching to return README text, repository metadata, and a bounded root-file listing in the readable content field.
- Strengthened the Agent system instructions to complete dependent multi-step requests rather than stopping after the initial fetch.
- Clarified that native WebView2/Windows file-access prompts are browser-controlled and cannot be restyled by the HTML application.
- `Main.cs` remains unchanged. JavaScript syntax and static HTML checks were run; Windows/WebView2 and .NET runtime tests remain unavailable in this environment.

## 1.4.0-preview.1

- Added a standalone Deep Research mode while retaining existing chat and Agent Mode.
- Added editable research plans with checkpoint-applied revisions, page/tool budgets, source collection, and Markdown report export.
- Added pause/resume/cancel lifecycle controls and localStorage checkpoints.
- Hardened transcript checkpointing so persisted conversations contain complete tool-call batches.
- Added direct file downloads, bounded web extraction (links, metadata, tables, sitemap, RSS/Atom), and GitHub repository README retrieval.
- Expanded selected-folder tools for editing, deleting, copying, moving, renaming, metadata, checksums, comparisons, project overview, and duplicate detection.
- Added CSV analysis/transforms/comparison and JSON validation/formatting tools.
- Added Markdown/HTML report creation, SVG chart/flow-diagram generation, and browser-canvas image operations.
- Preserved C# source unchanged; only JavaScript, version metadata, and documentation were updated.
- This remains a preview: full .NET compilation and live WebView2/provider/MCP testing were not possible in the build environment.



---

# Version 1.3.0
## Starting point and preservation
- Based on the user's 1.2.3 source and the 1.3.0-preview.4 development snapshot.
- Preserves the existing compact PowerToys Run UI, inline provider settings, standalone WebView2 chat, exports, context manager, and normal streaming chat path.
- `Main.cs` is unchanged. No C# MCP bridge, native process launcher, or STDIO transport was added.
- `RequestTimeoutSeconds` behavior for inline PowerToys Run results is unchanged; standalone chat remains governed by user cancellation and provider completion/errors.

## Agent approvals and folder access
- Replaced browser-native confirmation dialogs for Agent/MCP tool execution with an inline approval card styled with the existing chat theme.
- Approval cards show the requested tool, server, path/URL, and arguments; potentially mutating actions receive a warning treatment.
- Actions: Allow once, Deny, and (for read-only agent tools only) Allow for this task. The task-scoped read approvals reset when a new agent run starts. MCP tool calls/resources/prompts and file creation remain explicit per-call approvals.
- Stop/cancel dismisses a pending approval.
- A user-selected folder remains available across prompts and Agent Mode toggles for the current WebView session. The user can explicitly clear it from Agent settings. The model instructions now tell the model not to ask for the folder again when file tools are already available.
- Folder access is not persisted across WebView restarts. File tools remain absent until a folder is selected.

## Model server connections
- Added a browser-side connection manager for multiple Ollama and OpenAI-compatible endpoints.
- Enabled servers are discovered sequentially and their models are merged into a single dropdown, grouped by alias and endpoint host.
- Connections can be added, disabled, enabled, and removed (the two built-in defaults can be disabled but not removed).
- Selected connection determines the endpoint used for discovery, metadata, ordinary streaming chat, and Agent tool-call requests.
- Conversation exports record the selected connection ID so duplicate model names can be restored to the same configured server when available.
- Endpoints and aliases are stored in WebView localStorage.

## Agent workflow
- Agent Mode supports `update_plan` for concise multi-step plans, with pending/in-progress/completed statuses rendered in the assistant transcript.
- Added local recent task history (up to 20 snapshots) for Agent Mode and MCP-enabled chats. History can reopen a snapshot or be cleared. Snapshots are stored in the WebView profile; attachment bytes are deliberately excluded.
- Existing maximum tool-step setting remains the per-run tool-call limit. Read-only approvals can be allowed once or for the current run; the existing automatic read-only setting remains available.
- Tool errors are returned to the model as tool results so it can attempt recovery within the configured step limit.

## MCP client
- Browser-only Streamable HTTP MCP client retained: initialize/initialized, tool discovery/calls, resource listing/reading, prompt listing/retrieval, pagination, session IDs, protocol-version headers, optional bearer authentication, disconnect, enable/disable, remove, and reconnect.
- MCP access is available in Agent Mode and optionally in ordinary chat. MCP tool/resource/prompt actions use the inline approval card.
- Server URLs are saved locally; bearer tokens are session-only. HTTP/HTTPS only. STDIO intentionally unsupported because no native process bridge is allowed.
- MCP resources are discoverable/readable; this build does not create a persistent vector index or automatically embed/index all remote resources.

## Search and file tools
- Explicit URLs are routed to `fetch_webpage`; public GitHub repository URLs use GitHub's public API to retrieve metadata and README.
- `web_search` is exposed only when a SearXNG URL is configured.
- File tools: list, read UTF-8 text, create new file without overwrite, restricted to the selected folder. No edit/delete or shell/process execution.

## Limitations and verification
- Model server API-key authentication is not implemented for model endpoints. OpenAI-compatible endpoints that require bearer/API-key auth will not work in this build. MCP bearer auth is supported but session-only.
- Remote model and MCP endpoints must allow WebView2 CORS/network access. Some browser-only MCP server implementations and auth flows may not work.
- LM Studio Native REST mode does not support Agent tool calls; use OpenAI-compatible mode.
- No persistent knowledge-base embedding/indexing, MCP STDIO, file edit/delete, task retry UI, or secure credential vault.
- JavaScript syntax checked with Node.js; HTML IDs/selectors checked for duplicates/missing references; ZIP integrity checked. A full .NET build and live WebView2/provider/MCP tests could not be run because the .NET SDK and configured live test servers were unavailable in this environment.

- `web_search` is only included in the model's Agent tool definitions when Agent settings contain a syntactically valid HTTP(S) SearXNG instance URL.
- Saving a non-empty malformed URL is rejected with an explanatory toast; an empty URL saves normally and explicitly leaves web search disabled.
- Tool execution independently rechecks the normalized configured endpoint and fails closed if the setting is missing or invalid. This protects against stale tool calls or settings changed after tool discovery.
- The configured URL is normalized (trailing slash removed); credentials embedded in URLs are rejected.
- `fetch_webpage` remains available independently, so direct URL fetching does not require a search engine.

---

# Version 1.2.3

## Regenerate thinking-toggle fix

- Regenerate no longer restores the previous assistant message's `thinkRequested` value.
- It preserves the current thinking toggle, so enabling thinking before regenerating with a different model/provider is respected.
- The standalone chat's existing provider/model selection behavior is unchanged.
- `Main.cs` and the solution are unchanged; only `ChatWindow.html` and version metadata in the project and plugin manifests changed.

---
# Version 1.2.2

## Inline PowerToys Run provider selection

- Added a `PowerToys Run LLM provider` setting with `Ollama` and `LM Studio` choices.
- The selected provider controls inline PowerToys Run model discovery and generation.
- Ollama uses the configured `/api/tags` and `/api/generate` endpoints, retaining its `keep_alive` and `think` request options.
- LM Studio uses the configured OpenAI-compatible base URL, `GET /v1/models` for model discovery, and streaming `POST /v1/chat/completions` for generation.
- The existing default-model field is shared; its description now says to use the model name/ID from the selected provider.
- The standalone WebView2 chat keeps its own provider selector and behavior. The new PowerToys Run provider setting does not force the chat window to use the same provider.
- `RequestTimeoutSeconds` continues to apply only to inline PowerToys Run generation, for either provider.
- `KeepAliveSeconds` and `Thinking` are Ollama-specific settings; LM Studio requests do not receive those Ollama-only fields.

---

# Version 1.2.1

- Fixed live thinking disclosure interaction: streaming updates now patch the existing assistant message in place and preserve the existing `<details>` node, so the user can expand/collapse thinking while generation is active. The thinking body and status are updated without replacing the disclosure control.
- Made the LM Studio API mode setting provider-specific in the UI. The setting is hidden for Ollama selections and shown only when an LM Studio model is selected. The saved preference is retained when switching providers.
- Added an explicit `[hidden]` style for setting fields so the provider-specific setting is reliably removed from the CSS grid.

Validation: extracted the embedded JavaScript and passed `node --check`. Runtime interaction with WebView2 and live Ollama/LM Studio streams still requires testing in the host application.

## Context manager UI follow-up

The earlier context controls were too easy to miss inside Generation Settings and did not amount to a usable context manager. They have been moved into a dedicated **Context** toolbar panel.

- Shows estimated input-token usage and a utilization bar using a manual per-request context override or detected active/model context metadata.
- Shows separate counts for included messages, summarized messages, and unsummarized omitted messages.
- Provides an optional hard recent-turn cap, an optional stricter estimated history-token budget, and provider-aware model context length.
- The adaptive history-token budget selects whole recent user turns from newest to oldest when an active context length is known; an optional manual cap can make it stricter. The newest turn is always retained, even if it alone exceeds the estimate budget.
- Provides a reviewable/editable summary, explicit save and clear actions, and a preview of the selected context. The preview is limited to the most recent 30 included messages for UI responsiveness; this does not change the request itself.
- Summary and history-token-budget settings are preserved in portable conversation files. The recent-turn and token-budget preferences are also remembered locally.
- Context estimates are explicitly approximate. They are based on serialized text length and do not precisely represent model tokenization, image costs, system prompt overhead, provider formatting, or reserved output tokens.
- Changing context selection invalidates LM Studio native response continuation IDs so a later request cannot accidentally continue with a stale server-side context chain.

Validation for this follow-up: embedded JavaScript syntax check passed; duplicate HTML IDs check passed; context-plan tests passed for full history, recent-turn limiting, token-budget trimming, newest-turn preservation, and summary/omission boundaries. WebView2 and provider runtime integration still require testing in the application.


## Follow-up reliability corrections

- The PowerToys Run request-timeout setting is now scoped to inline PowerToys Run generation only. It is no longer included in the WebView2 bootstrap and is never applied to standalone chat generation. Chat generation uses the user-triggered Stop cancellation signal; discovery and metadata lookups retain short independent deadlines so a stalled metadata endpoint cannot block the UI indefinitely.
- Context detection now queries Ollama `/api/ps` for the active loaded context and `/api/show` model metadata for the model maximum. LM Studio uses `/api/v1/models` to read loaded instance context and model maximum, with `/api/v0/models` as a compatibility fallback. Metadata failure does not block chat generation.
- Context UI distinguishes manual request overrides, active loaded context, and model maximum (not currently loaded).
- The implicit 12-user-turn cap has been removed. Blank means no hard turn cap; old persisted default-12 preferences are migrated to no cap. When context capacity is detected, an adaptive history budget retains space for output and provider overhead. An optional user-set history-token cap can make that budget stricter.
- Validation: embedded JavaScript syntax, HTML ID uniqueness, and static assertions for timeout separation/context metadata/history defaults were checked. WebView2/provider runtime behavior and C# compilation still require testing on a machine with the .NET SDK and PowerToys reference assemblies.

- Streaming cleanup follow-up: Ollama and both LM Studio stream readers now cancel unfinished response bodies and release reader locks on errors, Stop, and early completion, rather than leaving an unread stream behind.

- LM Studio's object-shaped capability metadata (vision, tool-use, reasoning) is now normalized into the model-info capability labels. Stream readers were also exercised with mocked completion/error streams to verify cancellation and reader-lock cleanup.

---

# Version 1.2.0

This source update keeps the existing PowerToys Run inline result layout and makes the following coordinated changes.

## Chat UI and streaming

- Streaming content updates are batched with `requestAnimationFrame` and patch only the active message. Completed transcript messages are left in place.
- The one-second metrics refresh updates only the metrics element; it no longer rebuilds the transcript or reparses Markdown.
- Thinking disclosure state is handled through one delegated listener rather than reattaching listeners after each full render.
- Preferences reject `null`, arrays, and non-object JSON values.
- Connection establishment has a 15-second ceiling, model discovery has a separate short deadline, and generation retains its longer overall deadline. The PowerToys Run Ollama model list is cached for five seconds. WebView2 request deadlines remain active through response-body consumption, including streamed reads.
- Ollama NDJSON and LM Studio SSE parsers report malformed records/events and fail clearly when a stream is unusable.
- Time-to-first-token is recorded and shown alongside elapsed time, token count, and throughput.

## Provider support

- Chat model discovery and generation now go through a small provider registry with normalized callbacks.
- LM Studio OpenAI-compatible requests retry only for recognized optional-parameter validation errors. Rejected capabilities are cached per endpoint/model for the current WebView session.
- LM Studio native REST v1 is available as an opt-in setting. It supports native reasoning events, context-length configuration, server response IDs, and stateful continuation. If a native conversation is imported, edited, summarized, or exceeds the configured history window, the next request starts a fresh native thread seeded with the managed local transcript.
- The OpenAI-compatible adapter remains available for broad compatibility.

## Conversation context

- The full conversation remains local and is retained for export/editing.
- Requests can be limited to a configurable number of recent user turns (default 12).
- Older turns can be summarized on demand. The generated summary is included in subsequent requests and exported with the conversation; original messages are not deleted.
- The UI labels rough context estimates as estimates; they are character-based approximations, not tokenizer counts.
- Editing/regenerating invalidates a potentially stale summary.

## PowerToys Run inline results

- The existing compact multi-result presentation is preserved.
- Section splitting now preserves all characters exactly (concatenating sections reproduces the original response) and avoids splitting UTF-16 surrogate pairs.
- The existing context-menu copy action continues to copy the complete response, not the selected section.

## Validation and limitations

- Imported attachment count, declared and decoded image size, allowed image MIME types, and per-message aggregate size are checked.
- JavaScript endpoint normalization rejects invalid schemes and embedded credentials.
- C# model discovery has its own 8-second timeout; C# generation keeps the configured timeout through stream reading.
- PowerToys Run's plugin query contract remains synchronous and exposes no documented in-place result mutation callback. Inline generation therefore still returns completed results; live token-by-token launcher result updates were intentionally not implemented.
- The archive does not include the PowerToys reference assemblies under `lib\\x64`, and this environment does not have the .NET SDK. The C# project could not be compiled here. The embedded JavaScript passed `node --check`, but no WebView2/provider runtime integration test was possible.


## Follow-up fixes (2026-10-30)

- Fixed live thinking disclosure interaction: streaming updates now patch the existing assistant message in place and preserve the existing `<details>` node, so the user can expand/collapse thinking while generation is active. The thinking body and status are updated without replacing the disclosure control.
- Made the LM Studio API mode setting provider-specific in the UI. The setting is hidden for Ollama selections and shown only when an LM Studio model is selected. The saved preference is retained when switching providers.
- Added an explicit `[hidden]` style for setting fields so the provider-specific setting is reliably removed from the CSS grid.


---
# Version 1.1.0
## LocalLLM WebView2 chat update

This source update adds an interactive response-window chat while leaving the PowerToys Run inline query path Ollama-only.

## What's included

- Replaces the response window's legacy WPF `WebBrowser` renderer with WebView2.
- Seeds each response window with that query's original prompt and initial Ollama response. Even a request-error window retains the prompt when available.
- Each window keeps its own conversation history in memory; closing the window ends that session unless the user exports it.
- Discovers Ollama and LM Studio models concurrently when the chat window starts. The **single** model selector contains `Ollama` and `LM Studio` option groups and has a refresh button. If one endpoint is unavailable, models from the other can still be used.
- Adds the `LMStudioBaseUrl` PowerToys Run setting (default `http://localhost:1234/v1`). This setting is only used for model discovery and follow-up turns in the WebView2 chat window; the inline PowerToys Run query remains on the configured Ollama `/api/generate` endpoint.
- Streams Ollama follow-ups through `/api/chat` and LM Studio follow-ups through the OpenAI-compatible `/v1/chat/completions` API.
- Supports System / Light / Dark themes. The CSS color, font, radius, and shadow variables are centralized in the `:root` rules in `Main.Chat.cs` for developer customization.
- Adds exports for standalone HTML, Open WebUI import JSON, portable conversation JSON, OpenAI-compatible request JSON, Markdown, CSV, TXT, JSONL, and Print / Save as PDF. CSV uses UTF-8 with BOM and guards against common spreadsheet formula-injection prefixes.

## Saved HTML is independent

The standalone HTML export contains the rendered transcript, embedded CSS, and a small local code-copy helper. It does not contain the WebView2 messaging bridge or any Ollama/LM Studio endpoint logic, and it does not connect to the plugin after export.

## Open WebUI export

The Open WebUI export is a JSON array containing a conversation `chat` object with a linear message tree (`parentId`, `childrenIds`, and `currentId`). To import it, use Open WebUI's **Settings → Data Controls → Import Chats**. The file follows the documented import shape, but it has not been tested against a live Open WebUI instance from this environment.

## Build requirements and validation

- Windows and the .NET SDK targeted by the included project file.
- Microsoft Edge WebView2 Runtime.
- The PowerToys common-library DLLs that were intentionally absent from the supplied ZIP, restored to the relative `lib\x64` paths expected by the project.
- Restore NuGet packages, including `Microsoft.Web.WebView2`.

The editing environment did not have the .NET SDK or the omitted PowerToys common libraries, so a full Windows build/runtime test could not be performed here. The embedded JavaScript syntax and project XML were checked, along with key implementation-path checks. Please run a local build and test the chat against your actual Ollama and LM Studio instances before replacing your working plugin.


---

# Version 1.0.3.1
## Dependencies

The source uses `Markdig` to render Markdown and `HtmlAgilityPack` to sanitize generated HTML. Keep both package references in the project file. The PowerToys reference assemblies and target framework should remain aligned with the PowerToys version installed on the Windows machine.

## Behavior and UX

- Sends the user's query unchanged as Ollama's `prompt`. No hidden prompt or temperature is added.
- Keeps the configured default model unchanged when a one-off model override is used.
- Supports `> model-name > your prompt` for per-query model selection.
- Makes `keep_alive` and request timeout configurable in PowerToys Run settings.
- Uses Ollama's `/api/tags` to distinguish a missing model from an endpoint/network failure.
- Parses newline-delimited `/api/generate` streaming JSON and reports API errors instead of displaying them as model output.
- Opens a clean, responsive, theme-aware HTML response page with browser scrolling, selectable text, clickable HTTP/HTTPS/mailto links, a Copy Markdown button, and per-code-block copy buttons.
- Disables model-supplied raw HTML, strips images and active elements, restricts link schemes, and uses a restrictive Content Security Policy. The response page makes no external requests on its own.
- Keeps the PowerToys result subtitle short; use Enter to open the full response and the result context menu to copy the complete Markdown response.
