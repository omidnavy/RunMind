## 1.6.10 — Build warning cleanup and WebView2 bootstrapper clarification

- Enabled nullable annotations explicitly in the shared native conversation Save As helper. This removes CS8632 when the helper is compiled by the PowerToys project, without enabling nullable analysis across the whole plugin project.
- Clarified that the Evergreen WebView2 bootstrapper performs a quick runtime check on normal startup and downloads/runs Microsoft's installer only when the runtime is missing.
- Clarified that first launch can take longer on a machine without WebView2; subsequent launches reuse the installed shared runtime.

## 1.6.9 — RunMind

- Added automatic WebView2 Evergreen Runtime provisioning to both standalone and PowerToys hosts. RunMind checks for an existing runtime and, only when missing, downloads Microsoft's official bootstrapper and silently installs it for the current Windows user. A first-run internet connection is needed only on machines without the runtime; users do not install WebView2 manually.
- Removed conversation-file compatibility for `.llmchat`, legacy LocalLLM JSON, and other portable JSON files. The native conversation picker and readers now accept only `.runmind` files containing `format: "runmind"`.
- Kept the PowerToys and standalone chat hosts on the same shared runtime check and the same `.runmind` conversation validation.

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


- Fixed two C# compile errors in the conversation-title WebView2 update path.
- Await the WPF `DispatcherOperation<Task<string>>` directly, then await the returned WebView2 script task separately.
- No behavior or public API changes beyond restoring compilation of the preview.7 title-update implementation.

# Changelog

## 1.6.8 — RunMind

- Added a native Windows Save As dialog to both desktop hosts. The current conversation title is pre-filled, `.runmind` is the default extension, and the chosen filename is written back into the file metadata and the in-app/native window title. Browser-only preview retains a download fallback.
- Centralized the native save-dialog bridge in one shared helper linked into both WPF projects. The bridge accepts only validated messages from the local chat page; WebView2 host objects remain disabled.
- Carried forward the 1.6.7 non-blocking PowerToys delayed-query implementation, RunMind project/folder/assembly naming, file-title behavior, and open-in-current/new-window choices.

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
