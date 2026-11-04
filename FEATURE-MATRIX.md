# RunMind — Feature Matrix

This document describes the **current feature set** of RunMind across its PowerToys Run plugin and standalone Windows application. It is a unified snapshot, not a release-by-release history.

## Status legend

- **Implemented** — available in the current source.
- **Conditional** — implemented, but depends on provider capabilities, browser permissions, server configuration, or Windows runtime behavior.
- **Partial** — a useful bounded subset exists; the full roadmap capability remains in scope.
- **Not included** — intentionally unsupported or not implemented.

---

## 1. Applications and Windows integration

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| Hosts | PowerToys Run plugin | Adds RunMind as a PowerToys Run plugin and launches the shared chat experience. | **Implemented** |
| Hosts | Standalone desktop app | `RunMind.exe` launches the same shared chat UI in a native WPF/WebView2 window. | **Implemented** |
| Hosts | Shared application | Both hosts use the same `ChatWindow.html` application and browser-side feature implementation. | **Implemented** |
| Windows | Multiple standalone windows | Each normal launch creates a separate RunMind process and chat window. Multiple PowerToys chat windows can also be open. | **Implemented**; standalone instances use the standalone WebView2 profile. |
| Windows | Window identity and task isolation | Each chat window receives a native-generated unique ID used to scope recent Agent Tasks and research checkpoints. | **Implemented** |
| Windows | Native title bar theme | Standalone title bar follows the Windows light/dark app preference and responds to Windows theme-change notifications. | **Implemented**; visual behavior should be verified on Windows. |
| Windows | Application icons | RunMind ICO/PNG/SVG assets are used by the standalone app, PowerToys windows, plugin images, and chat branding. | **Implemented** |
| PowerToys settings | Additional Options | Plugin settings are exposed through PowerToys' `AdditionalOptions` UI. A separate custom WPF settings panel is not used. | **Implemented** |
| Launch | Standalone discovery | Search for **RunMind** in Windows/PowerToys application search to launch the standalone app. | **Implemented** |

## 2. PowerToys Run query workflow

| Feature | Current behavior | Status / limits |
|---|---|---|
| Global search | Returns an immediate **Ask RunMind** result without waiting for the model. | **Implemented** |
| Action keyword | `llm <prompt>` uses the same two-stage query flow. `llm` without a prompt does not return a chat result. | **Implemented** |
| Delayed execution | Provider/model discovery and generation run asynchronously outside the PowerToys search callback. Ollama inline requests support an optional CPU thread cap; blank or `0` leaves thread selection to Ollama, while `1`–`128` sets `options.num_thread`. | **Implemented**; the cap does not apply to LM Studio or standalone chat. |
| Completed result | RunMind requests a refreshed PowerToys result after background generation finishes. | **Implemented** |
| Copy answer | Completed results provide a context-menu action to copy the full response. | **Implemented** |
| Open completed answer | Opening a completed result transfers the original prompt and generated answer into chat without generating the answer again. | **Implemented** |
| Open pending request | Opening a result while generation is in progress cancels the PowerToys request and transfers the original prompt to a chat window. The chat performs its own generation using chat settings. | **Implemented** |
| Cancellation | New queries, hand-off, relevant settings changes, and plugin disposal cancel active work and suppress stale results. | **Implemented** |
| Launcher lifecycle | The plugin API does not provide a reliable callback for every launcher-close or Escape event. | **Platform limitation**; those events cannot always cancel an active request. |
| Inline streaming | PowerToys result objects cannot be mutated token-by-token through the supported plugin contract. | **Not included**; streaming happens in the chat window. |
| Search matching | Global search accepts short prompts; the explicit `llm` keyword remains available. | **Implemented** |

## 3. Providers, models, and generation

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| Providers | Ollama | Supports local Ollama model discovery and chat/generation APIs. | **Implemented** |
| Providers | LM Studio | Supports the OpenAI-compatible API and the optional LM Studio Native REST mode where configured. | **Implemented**; native REST mode has different tool-call capabilities. |
| Providers | Additional model servers | Users can add multiple OpenAI-compatible endpoints, including custom base URLs and aliases. | **Implemented**; compatibility depends on the server's API implementation. |
| Connections | Per-connection configuration | Each connection has its own endpoint, enabled state, alias, optional API key, and configurable authentication-header name. | **Implemented** |
| Connections | Model discovery | Enabled connections are queried independently; discovered models are merged into the model selector and grouped by connection/alias. | **Implemented**; unavailable endpoints are reported independently. |
| Connections | Request isolation | Discovery and task requests use connection/authentication snapshots so later UI changes do not silently redirect an in-flight task. | **Implemented** |
| Authentication | Model API keys | Optional credentials are applied to discovery, metadata, chat, and tool-call requests for the configured connection. | **Implemented**; credentials are stored in local WebView storage and are not encrypted by RunMind. |
| Chat | Streaming responses | Ordinary chat streams responses from supported Ollama and OpenAI-compatible endpoints. | **Implemented**; provider stream compatibility applies. |
| Chat | Thinking display | Reasoning/thinking output can be displayed separately from the final answer when the provider/model returns it. | **Conditional**; depends on provider and model behavior. |
| Thinking | PowerToys preference | PowerToys generation uses its own PowerToys `Thinking` setting. | **Implemented** for the Ollama request path. |
| Thinking | Chat preference | Chat generation uses the chat application's own saved Thinking preference. | **Implemented**; independent from the PowerToys setting. |
| Thinking | Pending-query hand-off | If a pending PowerToys query is opened in chat, the original PowerToys request is cancelled and chat starts a new request using the chat's Think preference. | **Implemented** |
| Thinking | Provider-specific parameter | The Ollama request can send its `think` option. The OpenAI-compatible LM Studio request does not receive that Ollama-specific field. | **Provider limitation**; do not interpret the UI preference as a universal API parameter. |
| Generation settings | Provider/model options | Generation settings are applied to the active connection and model where the provider supports them. | **Conditional**; not every provider accepts every optional parameter. |
| Metrics | Generation metrics | The chat UI reports elapsed time, time to first token, token count, and throughput where available. | **Implemented**; values are estimates based on received output. |
| Regenerate | Regenerate response | Regeneration respects the current Thinking toggle rather than restoring an older message's saved setting. | **Implemented** |

## 4. Conversations, titles, saving, and export

| Feature | Current behavior | Status / limits |
|---|---|---|
| New conversation | Starts with an empty chat and a default RunMind title. | **Implemented** |
| Generated titles | The first prompt supplies an immediate fallback title; after a successful response, the shared title generator can create a more descriptive title using the initial prompt and answer. | **Implemented**; title generation depends on the configured model/provider succeeding. |
| Saved-chat title | Opening a saved conversation uses its saved filename as the conversation/window title. | **Implemented** |
| Save dialog | Both native desktop hosts open the Windows Save As dialog with a suggested conversation filename pre-filled. | **Implemented**; browser-only previews use a download fallback. |
| Save format | Resumable conversations use `.runmind` JSON with the RunMind format marker. | **Implemented** |
| File validation | The application accepts `.runmind` conversation files with the expected RunMind JSON structure and rejects other extensions/formats and empty sessions. | **Implemented** |
| Save safety | Native saves write a temporary file beside the destination and replace the target after the complete file has been written. | **Implemented** |
| Save cancellation | Cancelling Save As does not commit the chosen filename as the conversation's saved title. | **Implemented** |
| Open in current window | Replaces the current chat session with the selected saved conversation. | **Implemented** |
| Open in new window | Opens the selected conversation in a new independent chat window and leaves the current chat untouched. | **Implemented** |
| Conversation menu | The toolbar entry is labelled **Conversations** and separates resumable save/open actions from one-way export actions. | **Implemented** |
| Export HTML | Exports a readable standalone transcript with embedded styling and local code-copy support. Live-app-only controls are omitted. | **Implemented** |
| Export formats | Includes portable conversation JSON (export-only), Open WebUI import JSON, OpenAI-compatible request JSON, Markdown, CSV, TXT, JSONL, and Print/Save as PDF. | **Implemented**; third-party import compatibility depends on the target application's schema/version. |
| Conversation format compatibility | `.runmind` is the only supported resumable conversation format. | **Implemented**; legacy `.llmchat` and LocalLLM conversation formats are intentionally not supported. |

## 5. Chat interface and context management

| Feature | Current behavior | Status / limits |
|---|---|---|
| Shared chat UI | Both hosts use the same browser-side chat implementation. | **Implemented** |
| Text selection | Application controls, status labels, composer placeholder, and message action buttons are excluded from text selection. User-entered prompt text, message bodies, author/timestamp/provider/model attribution, and generation statistics remain selectable. | **Implemented** |
| Themes | System, Light, and Dark chat themes are available. | **Implemented** |
| Streaming rendering | Streaming updates patch only the active message and are throttled to at most one Markdown render every 90 ms; a final render runs when generation ends. | **Implemented** |
| Thinking disclosure | Thinking content can be expanded/collapsed while a response is streaming. | **Implemented** |
| Context manager | Shows approximate input/context usage and distinguishes included, summarized, and omitted conversation messages. | **Implemented** |
| Context limits | Supports a manual context override, optional recent-turn cap, and optional stricter history-token budget. | **Implemented** |
| Adaptive history | When context capacity is known, recent whole user turns are selected to leave room for output and provider overhead; the newest turn is retained. | **Implemented**; estimates are approximate, not provider token counts. |
| Context summary | Older conversation context can be summarized on demand; the summary is reviewable/editable and can be saved or cleared. | **Implemented** |
| Context preview | Shows a preview of the selected request context. The UI preview is bounded for responsiveness and does not change the actual request. | **Implemented** |
| Native continuation state | LM Studio Native REST continuation identifiers are invalidated when context selection changes or the managed transcript is edited/imported/summarized beyond the configured context window. | **Implemented** |

## 6. Agent Mode and approvals

| Feature | Current behavior | Status / limits |
|---|---|---|
| Agent Mode | Browser-side, model-driven tool-calling loop for multi-step tasks. | **Implemented**; success depends on model tool-use capability and tool results. |
| Planning | Supports an `update_plan` tool to create and update concise multi-step plans. | **Implemented** |
| Regex testing | `regex_test` evaluates JavaScript regular expressions in a disposable Web Worker with a 750 ms hard timeout, bounded pattern/input sizes, and bounded match output. | **Implemented**; supports `i`, `m`, `s`, and `u` flags. It is a JavaScript regex tester, not a formal proof that every pattern is linear-time. |
| Model connection diagnosis | `diagnose_connection` checks the selected model server's discovery endpoint, lists a bounded set of models, checks whether the selected model is present, and classifies common failures. | **Partial**; it does not send a generation request, verify tool calling, or reliably distinguish network failures from CORS rejections. |
| Tool execution | Tool calls are executed by the browser-side application and returned to the model as tool results. | **Implemented**; tool turns are not token-streamed. |
| Tool limits | Tool turns are bounded by configured step/request limits and response-size limits. | **Implemented** |
| Approval UI | Inline themed approval cards replace browser-native confirmation dialogs for Agent/MCP actions. | **Implemented** |
| Approval choices | Supports Allow once and Deny; task-scoped read approvals and persistent per-tool Always allow are also available where configured. | **Implemented**; Always allow is scoped by tool name and can cover future arguments/paths, so use carefully. |
| Approval management | Persistent Always-allow entries can be reviewed, revoked individually, or cleared. | **Implemented** |
| Cancellation | Stop/cancel aborts active operations where supported and dismisses a pending approval. | **Implemented**; a server/provider may not stop work that it has already accepted. |
| Task history | Recent Agent Tasks stores local task snapshots and can reopen or clear them. | **Implemented**; history is scoped to the current chat window. |
| Request snapshots | Agent runs snapshot the selected model connection, generation settings, and available tool definitions when the task starts. | **Implemented** |
| Model behavior | System instructions encourage the agent to verify official documentation, follow relevant links, and avoid claiming an action succeeded without evidence. | **Implemented as instructions**; actual execution quality depends on the model and available tools. |
| Parallel tools/subagents | Parallel tool orchestration, autonomous subagents, and background scheduled agents are not provided. | **Not included** |

## 7. Workspace and local tools

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| Workspace access | User selects a local folder through the browser/Windows directory picker. | **Implemented**; Windows/WebView2 permission prompts remain browser-controlled. |
| Conversation attachments | PNG/JPEG/GIF/WebP images and bounded text/source files can be attached; PDF and ZIP files are also available to their Agent file tools by exact filename. | **Implemented**; PDF/ZIP are binary file-tool inputs and are not automatically included as extracted text in the model prompt. |
| Workspace persistence | The directory handle is retained in IndexedDB and permissions are rechecked/reconnected for later chats. | **Conditional**; Windows/browser may require a renewed user permission gesture. |
| Workspace scope | File tools operate relative to the user-selected workspace; text/JSON readers, PDF reading, and ZIP inspection can also resolve exact attachment filenames. | **Implemented**; workspace access is not an unrestricted filesystem or shell. Attachment reads do not require workspace permission. |
| File operations | List, read, create, edit, delete, search, copy, move, and rename files/folders. | **Implemented**; bounded operations and user approvals apply. |
| File recovery | `edit_file` and `delete_file` create automatic backups in the workspace `.runmind-backups` folder. `restore_file_backup` restores one of those backups and creates a recovery point when replacing an existing destination. | **Implemented**; backups are limited to 100 MB per file and are retained until the user removes them. There is not yet a backup browser or retention policy. |
| File metadata | Inspect metadata and calculate checksums for files. | **Implemented** |
| Comparisons | Compare files/folders, produce project overviews, and scan for duplicates. | **Implemented**; folder comparisons may use path/size metadata, and equal size alone does not prove equal content. |
| CSV tools | Analyze, filter, deduplicate, merge, compare, infer column types (`infer_dataset_schema`), and join datasets (`join_datasets`) using inner, left, or full joins. | **Implemented**; schema inference uses at most 10,000 rows, joins are capped at 100,000 output rows, and outputs require approval and a new destination. |
| JSON tools | Validate JSON syntax and validate data against a bounded JSON Schema implementation (`validate_json_schema`); pretty-print, minify, and sort JSON. | **Implemented with limits**; supports local `$ref`, common structural/type/constraint keywords and logical combinators. Unsupported advanced keywords are reported as warnings, not asserted as validated. |
| PDF reading | `read_pdf` uses PDF.js for page-aware Unicode text extraction from workspace PDFs or attached PDFs, then automatically runs local Tesseract OCR on pages whose text is absent or suspicious. Results retain page order and report extraction method, selected languages, and low-confidence OCR pages. | **Implemented with limits**; encrypted PDFs are unsupported; OCR quality depends on scan resolution, typography, language selection, and layout. Input is limited to 25 MB / 500 pages, page OCR rendering to 12 million pixels, and output to 90,000 characters. Attached PDFs are signature-checked. |
| OCR language management | English (`eng`) and Persian (`fas`, locale `fa_IR`) are the initial bundled choices. Optional French, Spanish, Arabic, Simplified/Traditional Chinese, Russian, Finnish, German, Italian, Portuguese, Japanese, Korean, Turkish, and Urdu packs can be downloaded individually; multiple languages can be selected together. | **Implemented with limits**; optional packs are cached per WebView2 profile after download. The PowerToys and standalone hosts have separate caches. Language detection is not automatic; a first build requires internet access to fetch pinned runtime/model resources. |
| Archive tools | `inspect_archive` and approval-gated `extract_archive` inspect/extract standard ZIP files from the workspace or conversation attachments. Extraction validates CRC-32 and rejects traversal, symlinks, encryption, duplicates, and oversized entries. | **Implemented with limits**; ZIP64, multi-disk ZIP, TAR, and other formats are not supported. Attached ZIPs are signature-checked; extraction still requires a selected workspace and approval. Limits: 500 entries, 100 MB expanded total, 25 MB per file, and 200:1 compression ratio. |
| Reports | Create Markdown and HTML reports and SVG charts/flow diagrams in the selected workspace. | **Implemented**; new-file and approval rules apply. |
| Images | Read image metadata and resize, crop, convert, or create thumbnails using browser canvas. | **Implemented**; input sizes and output formats are bounded (PNG/JPEG/WebP). |
| Image understanding | `read_image` attaches a workspace PNG, JPEG, WebP, or GIF to the next model turn for visual analysis. | **Conditional**; requires a vision-capable selected model and is limited to 8 MB per image. The image is sent to the configured model endpoint. |
| Downloads | Download HTTP resources into the selected workspace with approval and no-overwrite behavior. | **Conditional**; direct downloads require server CORS access and have a size limit. |
| Shell/process execution | Run arbitrary shell commands, launch local processes, or provide a native command-execution bridge. | **Not included** |
| Advanced document generation | Native DOCX/XLSX/PPTX/PDF document editing and conversion. | **Not included** |

## 8. Web search and retrieval

| Feature | Current behavior | Status / limits |
|---|---|---|
| Direct webpage fetch | Attempts browser-side direct retrieval first. | **Implemented**; subject to CORS, mixed-content, authentication, and anti-bot restrictions. |
| Web search | Offers `web_search` only when a valid SearXNG endpoint is configured. | **Conditional**; SearXNG is not bundled and the server must permit browser requests. |
| HTTP handling | Eligible HTTP URLs are upgraded to HTTPS for browser retrieval; fallback retrieval can use the original public URL where allowed. | **Implemented** |
| Jina Reader | Uses `https://r.jina.ai` as a fallback after direct fetch fails. | **Conditional**; public URL content is sent to the Reader service. Optional API key supported. |
| Custom Reader gateway | Supports an optional user-configured Reader-compatible HTTP gateway with its own token and connection test. | **Conditional**; only used after earlier retrieval routes fail. |
| Private URL protection | Local/private addresses, embedded credentials, and likely secret-bearing query parameters are blocked from remote Reader forwarding. | **Implemented** |
| Web extraction | Bounded extraction supports links, metadata, tables, sitemap, RSS/Atom, and readable page content. | **Partial**; raw XML operations require direct XML access; Reader Markdown is not represented as raw XML. |
| Page downloads | Can save readable webpage snapshots; Reader may be used for Markdown snapshot fallback. | **Conditional**; distinct from `download_file`, which saves original response bytes. |
| Direct resource downloads | `download_file` writes the original response bytes rather than Reader-extracted Markdown. | **Conditional**; CORS, workspace permission, approval, overwrite, and size limits apply. |
| Full crawler workflows | Deep recursive crawling, automatic whole-site link checking, and full cross-site comparison workflows are not provided as turnkey features. | **Not included** |

## 9. Deep Research

| Feature | Current behavior | Status / limits |
|---|---|---|
| Research mode | Separate Deep Research mode uses the shared Agent tools and enabled MCP tools. | **Implemented** |
| Mode exclusivity | Agent Mode and Deep Research are mutually exclusive. | **Implemented** |
| Research drawer | Right-side overlay drawer can be opened/closed independently from research execution. Closing it does not stop the task or delete results. | **Implemented** |
| Research plan | Creates an editable plan; approved revisions are applied at checkpoints. | **Implemented** |
| Task lifecycle | Supports pause, resume, and cancel with checkpoint persistence. | **Conditional**; pause occurs at complete tool-batch checkpoints; abrupt interruption does not provide exactly-once execution guarantees. |
| Budgets | Research uses bounded page/tool budgets. | **Implemented** |
| Sources | Tracks discovered versus read sources, retrieval route, final URL, and failed retrieval attempts. | **Implemented** |
| Evidence quality | Instructions encourage following landing-page links to version-matched manuals, release notes, and source/build instructions. | **Model-dependent**; generated citations are not independently validated. |
| Results | Supports copying plans/results and exporting Markdown reports. | **Implemented** |
| Checkpoint isolation | Research checkpoints are scoped to the individual chat window/session. | **Implemented** |

## 10. MCP client

| Feature | Current behavior | Status / limits |
|---|---|---|
| Transport | Browser-side MCP Streamable HTTP client. | **Implemented**; servers must support the transport and allow WebView2 CORS requests. |
| Server management | Configure server URLs, enable/disable, remove, disconnect, and reconnect. | **Implemented** |
| Authentication | Optional bearer tokens are stored with the local WebView profile configuration. | **Implemented**; stored locally without RunMind encryption. |
| Initialization | Supports initialize/initialized, protocol-version headers, session IDs, and server reconnection. | **Implemented** |
| Tools | Discover and call server tools through Agent/MCP approval flow. | **Implemented** |
| Resources | List and read MCP resources. | **Implemented**; resources are not automatically indexed into a local knowledge base. |
| Prompts | List and retrieve server prompts. | **Implemented** |
| Pagination | Supports pagination for applicable listing operations. | **Implemented** |
| Tool naming | Sanitized MCP tool identifiers include a stable hash to reduce name collisions. | **Implemented** |
| Expired sessions | Reinitializes sessions for idempotent list/read operations; does not automatically replay tool calls. | **Implemented** |
| Advanced protocol features | Resource templates/subscriptions, completions, elicitation, progress notifications, OAuth flows, and MCP Tasks. | **Not included** |
| STDIO transport | Launching local MCP processes or using STDIO servers. | **Not included**; no native process bridge is provided. |
| MCP knowledge index | Persistent vector indexing/embedding of remote resources. | **Not included** |

## 11. Storage, privacy, and security boundaries

| Area | Current behavior | Status / limits |
|---|---|---|
| WebView2 profiles | PowerToys and standalone use separate WebView2 user-data folders. | **Implemented**; their settings and browser storage are intentionally not synchronized. |
| Multiple standalone instances | Standalone windows share the standalone WebView2 profile. | **Implemented**; browser-local settings are shared between standalone windows, while task/checkpoint state is window-scoped. |
| Local settings | Model connections, generation preferences, Agent settings, Reader settings, MCP configuration, and related preferences are stored in local WebView storage. | **Implemented** |
| Credentials | API keys and tokens are stored in local WebView profile storage. | **Implemented by design**; plaintext at rest, not encrypted by RunMind, and not included in conversation exports. |
| Workspace handles | Directory handles are stored in IndexedDB and permission is rechecked. | **Implemented**; browser/Windows permission prompts cannot be restyled by RunMind. |
| Native bridge | Only narrowly scoped, validated WebView2 messages are used for native save and open-in-new-window operations. | **Implemented**; WebView2 host objects remain disabled. |
| Bridge validation | Native handlers validate the local page origin, message type, payload size, and conversation format before acting. | **Implemented** |
| WebView origin | Chat content is hosted through WebView2 virtual-host mapping at `https://localhost`; it is not a separate local web server. | **Implemented**; provider and search servers must allow the origin through CORS. |
| Remote retrieval | Only public URLs are eligible for remote Reader forwarding; private/local and likely secret-bearing URLs are blocked. | **Implemented** |
| Local data processing | Conversations, workspace tools, and chat UI operate locally; model requests go to the configured model endpoint. | **Implemented**; remote Reader, SearXNG, MCP, and model services receive the requests/data needed for their configured functions. |

## 12. Runtime, build, and platform requirements

| Area | Current behavior | Status / limits |
|---|---|---|
| Target framework | Standalone and PowerToys projects target `net10.0-windows10.0.22621.0`. | **Implemented**; keep aligned with the current PowerToys reference assemblies. |
| Architecture | Current one-click build script produces the Windows x64 release. | **Implemented** |
| Build script | `Build-Release-x64.bat` cleans and builds the PowerToys plugin and standalone app. | **Included in source** |
| PowerToys references | The source package includes the five PowerToys/Wox reference assemblies under `lib\x64`. | **Included**; assemblies must remain at the relative paths expected by the projects. |
| .NET | Requires the .NET 10 SDK to build. Framework-dependent standalone deployments require the matching .NET 10 Desktop Runtime. | **External prerequisite**; WebView2 provisioning does not install .NET. |
| WebView2 | Both hosts check for the Microsoft Edge WebView2 Evergreen Runtime and invoke Microsoft's per-user bootstrapper when missing. | **Implemented**; first install requires internet; the browser runtime is not bundled inside RunMind. |
| Runtime recovery | A failed bootstrapper launch/installation clears the cached installer so a later launch can retry. | **Implemented** |
| Network | No internet connection is needed for normal use with already-installed local model/runtime services, except features that explicitly use remote web/Reader/MCP services or require a missing WebView2 runtime. | **Conditional** |
| Installer/service | No Windows service, tray process, startup registration, or separate background agent is installed. | **Not included** |

## 13. Not currently included

The following are intentionally outside the current feature set or remain unimplemented:

- PowerToys inline token-by-token result mutation.
- Shell/terminal command execution and arbitrary local process launching.
- MCP STDIO, OAuth, elicitation, resource subscriptions/templates, completions, progress, and MCP Tasks.
- Persistent vector database, embeddings, or automatic indexing of MCP resources/local files.
- Parallel tool orchestration, autonomous subagents, scheduled tasks, and conversation branching.
- Full recursive site crawling and automatic whole-site link auditing as turnkey workflows.
- Native DOCX/XLSX/PPTX generation or conversion, PDF OCR/editing, and a complete PDF rendering engine.
- A credential vault or encryption layer for locally stored API keys and MCP tokens.
- A bundled local model server or bundled WebView2 browser runtime.

---

## Important implementation boundaries

- Model capabilities vary. Agent tool calling, reasoning output, optional generation parameters, and context metadata depend on the selected model and provider API.
- CORS is enforced by WebView2. A provider being reachable from Windows does not guarantee it can be called from the chat page; the server must allow the `https://localhost` origin.
- WebView2 directory access and permission prompts are controlled by Windows/browser APIs.
- Token/context figures are estimates unless explicitly reported by the provider; character-based estimates do not equal exact model tokenization.
- Source-level checks do not replace a Windows build and live tests against the user's PowerToys, WebView2, model servers, Reader/SearXNG endpoints, workspace permissions, and MCP servers.
