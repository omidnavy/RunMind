## RunMind 2.1.1 — Session-scoped conversations and Agent Skills Manager

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| Agent runtime | Shared Agent runtime and native adapter | Both desktop hosts use the same `ChatWindow.html` Agent runtime and the same named `AgentHostBridge` contract. Tool definitions, policy checks, approval UX, and orchestration remain in the shared page. | **Implemented**; no unrestricted shell or generic native execution tool. |
| Project Profiles | Shared profile catalog | Profiles are stored under `%LOCALAPPDATA%\RunMind\ProjectProfiles` and are visible to Standalone and PowerToys Run. | **Implemented**; profile data excludes secrets and browser folder handles. |
| Project Profiles | Snapshot and apply | Captures project instructions, provider/model and connection ID, safe Agent settings, Skill names, MCP IDs, folder-name hint, and permission policy. Applying copies these settings into the active conversation. | **Implemented**; later profile edits do not mutate existing conversations. Applying a profile rotates the workspace context and invalidates old folder/Git grants. |
| Workspace | Conversation Workspace Context | `.runmind` v2 saves the per-conversation context; v1 remains importable. Folder name is a reconnect hint. | **Implemented**; workspace context is optional. |
| Workspace | IndexedDB folder grants | Browser directory handles are stored by Workspace Context ID in IndexedDB. | **Implemented**; browser grants remain local to the WebView profile and are not exported or transferred across hosts/devices. A different folder is never silently substituted. |
| Permissions | Central Agent approval policy | Git reads default Allow; local Git writes, remote Git operations, and downloads default Ask. Per-conversation policy is saved with the context/profile. | **Implemented**; Git destructive operations always require fresh approval. Native Git folder selection always prompts separately. Existing read-only approval persistence remains device-local. |
| Git | Existing repository selection | Native folder picker authorizes one normal working tree for the current conversation context. | **Implemented**; local `.git` directory required; linked worktrees and `.git` files are not supported. Native selection is separate from browser folder access. |
| Git | Read operations | `git_status`, `git_diff`, `git_log`, `git_show`, `git_branches`, `git_remotes`. | **Implemented**; output is bounded; remote URL credentials/query secrets are redacted. |
| Git | Local write operations | `git_stage`, `git_unstage`, `git_commit`, `git_create_branch`, `git_switch_branch`, `git_merge`, `git_revert`, `git_cherry_pick`, `git_stash`. | **Implemented**; requires approval according to conversation policy; commits always require fresh approval because hooks may execute. |
| Git | Remote and destructive operations | `git_fetch`, `git_pull`, `git_push` require explicit remote and branch. `git_reset_hard`, `git_clean`, `git_restore`, and `git_delete_branch` are destructive. | **Implemented**; destructive operations always require fresh approval. Git runs through `ProcessStartInfo.ArgumentList`; HTTP/HTTPS clone only, Git for Windows required. |
| Git | Repository clone | `git_clone` clones to a new folder chosen through the native folder picker. Supports self-hosted Gogs/Gitea HTTP/HTTPS URLs and uses the user's configured Git credential manager. | **Implemented**; no overwrite; the cloned repository is native-Git authorized for the new conversation context. Browser file access requires a separate user folder grant. |
| Downloads | Original-byte download | `download_file` uses browser fetch for a selected browser workspace (up to 10 MB) or the native streaming Save-dialog path when browser access is unavailable/blocked or no browser folder is selected. | **Implemented**; native 4 GB / two-hour cap; no cookies/proxy; private, loopback, link-local, and reserved destination IPs are blocked. |
| Downloads | Resume and integrity | Native path uses `.runmind.part`, URL fingerprint, ETag/Last-Modified validation, HTTP Range resume, progress, cancellation, SHA-256, optional expected checksum, and no-overwrite finalization. | **Implemented**; resume requires a server validator. Browser fetch is limited to 10 MB and may fail due to CORS. No extraction or execution. |
| Agent Skills | Metadata discovery | `list_agent_skills` discovers global Skills and project `.agents/skills` metadata. | **Implemented**; name/description frontmatter is required; metadata is listed before instruction content is loaded. |
| Agent Skills | On-demand load | `load_agent_skill` loads the requested `SKILL.md` and up to 10 explicitly requested references. | **Implemented**; each file is limited to 128 KB; Skill instructions cannot grant permissions and scripts are never auto-executed. |
| Native Reader | Existing NativePageReader bridge | NativePageReader remains included, connected, and disposed by both desktop hosts. | **Preserved**; `Shared/NativePageReader.cs` is byte-for-byte unchanged from 2.0.9. |
| Conversation storage | Active session identity | `sessionStorage` holds a per-window session ID; the full temporary branch archive is keyed by that ID in IndexedDB. | **Implemented**; reload recovery is per window; new conversations rotate the ID and do not reuse another window's active history. |
| Conversation storage | Temporary session cleanup | Normal host close deletes the current archive, its Deep Research checkpoint/history, and session metadata; startup expires abandoned sessions. | **Implemented**; crashed sessions have a 24-hour lease grace period. New chats cannot claim another window's research checkpoint. Legacy Local Storage branch duplicates are retired on upgrade; workspace folder handles are preserved. |
| Conversation storage | Explicit persistence | `.runmind` files are the user-controlled way to retain and transfer conversations. | **Implemented**; native Save As accepts format versions 1 and 2. Unsaved temporary sessions are not a persistent conversation library. |
| Agent Skills | Skills Manager | Agent Settings lists Global and Current Project Skills, displays metadata, installs selected local folders, and removes a selected installation after confirmation. | **Implemented**; existing names are not overwritten; project operations require an authorized workspace; the two scopes are independent. |
| Appearance | Pre-paint theme bootstrap | Standalone and PowerToys inject the saved theme before document content renders. | **Implemented**; preserves the existing shared appearance preference file and in-page theme controls. |

### 2.1.x not included

Project indexing, semantic search, reusable workflows, Standalone Launcher, and PowerToys Command Palette UI remain future releases. The shared Agent host contract is the integration point for those later hosts; this release does not ship their UI.

## RunMind 2.0.9 — configurable inline timeouts

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| PowerToys Run | Model discovery timeout | Configurable timeout for requesting and reading the selected provider's model list. Default 8 seconds. | **Implemented**; valid range 1–300 seconds; legacy installations retain the 8-second default. |
| PowerToys Run | Provider connection timeout | Configurable initial connection/response-header deadline for inline Ollama and LM Studio generation requests. Default 15 seconds. | **Implemented**; valid range 1–300 seconds; the existing Request timeout remains the overall request limit. |

## RunMind 2.0.8 — reliable inline answer refresh

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| PowerToys Run | Dynamic fast-result preview | The **Ask RunMind** pending result displays a bounded, whitespace-normalized preview of the latest prompt received by the fast query path, alongside **Open in chat** and background-generation status. | **Implemented**; formatting is bounded to 100 characters and performs no regex or network work. Updates occur when PowerToys dispatches a new query. |

## RunMind 2.0.6 — asynchronous inline result publication

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| PowerToys Run | Non-blocking delayed generation | The delayed plugin callback starts model generation in the background and returns immediately, releasing PowerToys' per-plugin execution gate. Completed answers are sent through `IResultUpdated` with the originating query generation. | **Implemented**; a pending **Open in chat** result remains visible while generation runs, and the completed answer appears as an additional result. |
| PowerToys Run | Non-blocking fast result | The fast result is returned immediately while the delayed model request runs outside the plugin execution gate. | **Implemented**; PowerToys still manages its own search-result list refresh. |

## RunMind 2.0.5 — non-blocking inline query cancellation

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| PowerToys Run | Fast query replacement | New fast queries invalidate the previous operation immediately and start asynchronous cancellation callbacks without waiting for them in `IPlugin.Query()`. The previous cancellation source remains alive until callback completion. | **Implemented**; the host must dispatch updated input; PowerToys Input smoothing can delay that dispatch. |
| PowerToys Run | Inline timing diagnostics | Debug builds report fast-result preparation time and prior-query cancellation callback duration without logging query text. | **Implemented**; diagnostic output is omitted from Release builds. |

## RunMind 2.0.4 — PowerToys inline latest-query handoff

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| PowerToys Run | Pending-result click handoff | A pending result click resolves against the latest active query received by RunMind and uses that operation's prompt, model, and provider settings consistently. | **Implemented**; cannot use text PowerToys has not yet dispatched to the plugin. No artificial debounce is added. |

## RunMind 2.0.3 — inline reliability and WebView security hardening

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| PowerToys Run | Query-operation lifecycle | Distinguishes started, already-running, and terminal operations; pre-start cancellation signals waiters so terminal operations cannot cause an indefinite wait. | **Implemented** |
| PowerToys Run | Settings/query synchronization | Publishes settings and invalidates the prior query atomically; versioned snapshots reject stale fast/delayed callbacks. | **Implemented** |
| PowerToys Run | Input validation | One shared filter governs fast and delayed query paths. | **Implemented** |
| WebView2 | Navigation and message origin | Default-deny navigation; trusts only the mapped HTTPS localhost chat page on its default port; external HTTP(S)/mailto opens through Windows; unsupported schemes and untrusted messages are blocked. | **Implemented** |
| Providers | Bounded inline generation | Ollama and LM Studio accumulated output is capped at 8 Mi characters; incomplete streams are rejected unless an explicit completion marker is received. | **Implemented**; provider must emit Ollama `done: true`, LM Studio `[DONE]`, or a non-null final `finish_reason`. |
| Providers | Timeout diagnostics | Model discovery, connection establishment, and generation timeouts are distinguished in diagnostics. | **Implemented**; fixed limits remain 8 seconds for model discovery and 15 seconds for connection establishment. |
| Ollama | Endpoint query parameters | Query parameters are retained when deriving `/api/tags` for model discovery. | **Implemented** for native discovery; chat-window base URL forwarding remains deferred until path-aware URL composition is added. |

## RunMind 2.0.2 — Clay appearance palettes

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| Appearance | Clay Light and Dark | Add the paired Clay palette to both independent selectors. Shared reference colors are `#F9F9F7` (light background), `#2D2D2B` (dark background / light foreground), and `#CC7D5E` (accent); supporting surface and border shades follow the screenshot's warm-neutral appearance. | **Implemented**; exact base colors are retained, secondary UI shades are adapted to RunMind's theme tokens. |
| Appearance | Cross-host preferences | Clay selections use the existing shared preference file and apply in standalone and PowerToys-hosted chat windows without restart. | **Implemented** |

## RunMind 2.0.0 — Data, document, and media capabilities

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| JSON | Advanced transforms | Pretty/minify, recursive key sorting/renaming, pick/omit fields, flatten/unflatten, array filtering, and array-field mapping. Optional destination writes a new JSON file after approval. | **Implemented**; prototype-related key paths are rejected; source is unchanged. |
| Datasets | CSV/TSV processing | Delimiter inference plus existing analysis, statistics, filters, deduplication, merge, comparison, schema inference, and joins. | **Implemented**; inputs remain size/row bounded; explicit delimiters must be one character. |
| Datasets | Rule validation | Required/unique columns and bounded per-column rules for empty values, numbers/ranges, equality, containment, membership, email, and HTTP(S) URLs. | **Implemented**; up to 100,000 rows, 100 rules, and 200 returned issues. |
| Web | Sitemap traversal | Recursively follows sitemap indexes, deduplicates URLs, searches URL/last-modified/frequency/priority metadata, and skips child sitemaps on other hosts. | **Implemented**; max 20 sitemap documents, 2,000 matching URLs, and bounded scan work; no change monitoring. |
| Documents | DOCX creation | Creates Open XML Word documents from Markdown headings, paragraphs, inline emphasis/code, lists, fenced code, and simple pipe tables. | **Implemented**; basic document layout only; no arbitrary template/layout engine. |
| Spreadsheets | XLSX creation | Creates multi-sheet Open XML workbooks with typed text/number/boolean cells, header styling, frozen header rows, filters, and bounded column widths. Strings are never interpreted as formulas. | **Implemented**; max 10 sheets, 10,000 data rows per sheet, 50 columns, and 100,000 cells total. |
| Documents | PDF creation | Creates paginated text PDFs with headings, paragraphs, lists, and text-rendered tables. | **Implemented**; built-in fonts, common Western European characters, no embedded image or complex layout rendering. |
| Reports | Markdown / HTML | Adds title, summary, optional table of contents, generated-at metadata, and safe Markdown rendering in standalone HTML reports. | **Implemented**; supplied complete HTML documents are retained as provided. |
| Charts | SVG charts | Supports grouped bar, multi-series line, pie, and doughnut charts with bounded labels and data points. | **Implemented**; max 40 labels and 8 series; generated SVG is editable. |
| Diagrams | SVG + Mermaid | Generates SVG flowcharts and editable Mermaid source; both are created by default with selectable flow direction. | **Implemented**; max 16 nodes and 40 connections. |
| Media | QR generation | Generates QR code SVG entirely offline. | **Implemented**; max 106 UTF-8 payload bytes, error correction L. |
| Media | Image galleries | Creates standalone responsive HTML galleries referencing workspace PNG/JPEG/WebP/GIF images by relative path. | **Implemented**; max 200 images and 100 MB of source images; originals are not copied. |
| Reliability | Artifact verification | Checks Office ZIP/XML structure, PDF markers/pages, SVG/HTML roots, JSON syntax, and CSV/TSV readability after generation or on demand. | **Implemented**; structural verification only, not visual or semantic validation. |

### Explicitly not included in 2.0.0

- Presentation generation.
- Metadata-preserving document conversion.
- Website change monitoring/scheduled snapshots.

## 1.10.34 updates

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| UI | Flyout sizing and placement | Flyouts use larger content-appropriate default widths and the full safe viewport; they are not shortened to fit only the gap next to the trigger. Long content scrolls only when it exceeds the available viewport. | **Implemented** |
| UI | Temporary flyout positioning | All toolbar, Conversations, Appearance, and Deep Research flyouts can be dragged by the top grip and resized from the bottom-right grip. Geometry is clamped to the viewport and resets when the flyout closes; no preferences are persisted. | **Implemented** |
| Conversations | Export panel layout | The Conversations flyout has a wider default layout and responsive Save/Open buttons; Export copy remains visible when the content fits the available viewport. | **Implemented** |

## 1.10.33 updates

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| Rendering | Transcript render pass | Latest assistant and editable-user indexes are computed once per full render; streaming continues to patch only the active message. | **Implemented** |
| CSV | Numeric summaries | Blank cells are excluded, min/max/mean use iterative calculations, and duplicate headings retain separate indexed summaries. | **Implemented** |
| CSV | Delimiter and duplicate heading safety | Delimiters must be one character; export quotes values containing that delimiter; ambiguous named-column operations stop with an explicit error. | **Implemented** |
| Workspace | Bounded search | Search has independent result, file-scan, per-file content, and cumulative content-byte budgets; common generated/dependency directories are skipped and oversized-file skips are reported. | **Implemented** |
| Persistence | IndexedDB commit handling | Workspace and branch operations resolve only after their transactions complete; failed workspace database opens can be retried. | **Implemented** |
| Workspace | Post-approval revalidation | Edit, delete, and restore compare the approved file snapshot again before backup and mutation. The browser API does not provide atomic cross-process create-if-absent. | **Implemented with API limitation** |
| Attachments | Filename ambiguity | Repeated filenames across conversation messages are reported as ambiguous instead of silently selecting the newest attachment. | **Implemented** |

## 1.10.32 updates

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| Appearance | Appearance mode | System follows Windows; Light and Dark are explicit overrides. Changes apply immediately. | **Implemented** |
| Appearance | Curated color palettes | Five independently selectable Light palettes and five Dark palettes. Classic Light and Warm Graphite preserve the existing defaults. | **Implemented** |
| Appearance | Shared preferences | Palette choices persist in one per-user file and are shared by standalone and PowerToys-hosted chat windows. | **Implemented** |
| Appearance | Native window caption | Standalone and PowerToys chat window title bars follow the effective light/dark mode and update when Windows appearance changes in System mode. | **Implemented** |

## 1.10.31 updates

- [x] Use a consistent **Compare with AI** action label and a branch-comparison button styled with the active RunMind theme.

## 1.10.30 updates

- [x] Align LM Studio title generation with the normal reasoning-off request settings (`reasoning_effort: none`, `enable_thinking: false`), preserve Nemotron conversational mode, and retry explicit optional-parameter rejections.
- [x] Extract assistant text from standard string or structured OpenAI-compatible message content, and report a diagnostic when no assistant text is returned.
- [x] Make the AI Branch Comparison composer single-line when empty, auto-grow to five visible lines, then scroll internally; leave the main chat composer unchanged.

## 1.10.29 updates

- [x] Resolve the active WebView2 control after initialization, including when initialization falls back to a replacement temporary-profile control, before applying a generated conversation title.
- [x] Ignore title/navigation events from replaced WebView2 controls; verify the generated title is visible in the active page before synchronizing the native window title.
- [x] Retry transient title-application failures and record diagnostics while preserving the already-visible fallback title on failure.

## 1.10.28 updates

- [x] Give the AI Branch Comparison composer five visible lines by default and grow it automatically up to its maximum height before internal scrolling; leave the main chat composer unchanged.

## 1.10.27 updates

- [x] Apply the PowerToys native-response conversation title only after WebView2 and the chat page finish initializing, so the initial fallback title cannot overwrite the generated title.
- [x] Skip the title-generation request when the window closes or WebView2 initialization fails.

## 1.10.26 updates

- [x] Opening **AI Branch Comparison** prepares the discussion without automatically calling a model.
- [x] The initial assessment instruction is pre-filled in the composer and remains editable; the user explicitly presses **Send** to begin the assessment.
- [x] The System prompt and immutable original comparison dossier remain hidden and are supplied automatically as separate model messages.
- [x] The submitted instruction becomes the first visible user message and is included in per-message copy and complete Markdown export; the composer then supports normal follow-ups.

## 1.10.25 updates

- [x] Branch comparison replaces the complete lower application workspace below the header, including branch controls, transcript, chat composer, and compose toolbar; **Back to chat** restores the normal chat layout.
- [x] The centered **AI Branch Comparison** modal provides a persistent in-session discussion, follow-up chat, independent model switching, and explicit re-examination of the original branch responses.
- [x] Each discussion is bound to an immutable snapshot of the original branch requests/responses and metadata. Reopening an unchanged comparison restores its discussion; changed branch content creates a new discussion.
- [x] Copy individual user/assistant messages, copy the complete discussion as Markdown, and download the full comparison dossier and discussion as a `.md` file. The export retains complete branch responses, the initial assessment, and assistant model/provider attribution.
- [x] The comparison discussion uses no Agent, Deep Research, or tools. Complete dossier and message history remain in memory for the app session; the UI shows an approximate context-size estimate instead of silently discarding messages.

## 1.10.23 updates

- [x] Side-by-side branch previews stay within a bounded, independently scrollable comparison panel so the chat transcript and composer retain their layout space.
- [x] AI branch assessments open in a fixed modal with its own scrollable result area; long output does not expand the chat page. Close, backdrop click, Escape, and Stop are supported.
- [x] The 1.10.22 comparison model selection, fixed prompt, provider routing, streaming, and branch-history isolation remain unchanged.

## 1.10.22 updates

- [x] Existing branch side-by-side comparison can request a model-generated assessment using a predefined RunMind template.
- [x] The comparison model selector defaults to the currently selected chat model and can independently select any discovered Ollama or OpenAI-compatible model without changing the main chat selection.
- [x] The assessment compares the latest assistant response and its preceding user request from each branch, streams into the comparison view, and can be cancelled. It does not modify branch histories or add a conversation message.

## 1.10.21 updates

- [x] A public hostname that resolves locally only to non-public DNS addresses is treated as a recoverable Native Reader failure. No private address is contacted; RunMind continues to the configured Custom Reader and then Jina, or directly to Jina when no custom gateway is configured.
- [x] Fallback is orchestrated inside the application before the result is returned to the model. Explicit private/local URL targets and unsafe redirects remain hard security stops.

## 1.10.20 updates

- [x] Custom OpenAI-compatible connections can explicitly opt into LM Studio native metadata endpoints; built-in LM Studio keeps metadata discovery enabled.
- [x] Message headers display the selected connection alias, with provider fallback for legacy messages.
- [x] Connection aliases persist in conversation files, branch archives, and JSONL exports.

## RunMind 1.10.19 fixes

- Nemotron Conversational Mode now uses a dedicated compact checkbox row spanning the full Generation Settings grid; the explanatory text is no longer squeezed into one half-column.
- Native Reader accepts mixed DNS answers when at least one globally routable address exists, but connects only to the public candidates. In 1.10.19, a public hostname resolving only to non-public addresses was treated as a hard block; 1.10.21 changes that case to a recoverable Native failure so the configured remote Reader fallback can run without connecting to any non-public address.

## RunMind 1.10.18 — Reader routing, Nemotron conversational mode, and branch-aware saves

- Desktop webpage retrieval order: **Native C# Reader → configured Custom Reader gateway → Jina Reader**. If no custom gateway is configured, Jina is the next step. If a configured custom gateway fails, Jina remains the final remote fallback. Security refusals for private/local destinations are never sent to remote services.
- The Agent settings panel presents the Custom Reader gateway before Jina and explains the fallback order.
- The Chat Generation Settings menu exposes **Nemotron Conversational Mode** only when a recognized NVIDIA Nemotron 3.5 Bolt E4B model is selected. It is enabled only for OpenAI-compatible chat completions, and sends `chat_template_kwargs: { conversational_mode: true|false }` to supported servers.
- PowerToys Run exposes an independent checkbox. It sends the same template argument only for a matching Nemotron 3.5 Bolt E4B model over the LM Studio/OpenAI-compatible provider. The native Ollama API does not expose this parameter. Both controls default off.
- The model feature requires a server that forwards `chat_template_kwargs` into the model chat template. NVIDIA documents the flag, and llama.cpp documents the request field. LM Studio behavior varies by runtime/engine; some have reported ignoring this field. RunMind reports explicit rejection, but cannot detect silent ignoring.
- Saving a branched conversation defaults to **All branches**; **Active branch only** is available as an escape hatch. Full saves include branch IDs/names, all message histories, the active branch, and fork lineage where known. Active-only saves reopen as a standalone Original branch. Older single-branch `.runmind` files remain supported.
- Full per-window branch histories are persisted to IndexedDB, with compact localStorage snapshots retained as a fallback. Native Reader failure reasons are surfaced in Research sources and included in Markdown report exports when remote fallback succeeds. Non-text HTTP response types are sent through the configured remote Reader path rather than parsed as HTML. Successful native reads log hostname, content type, and elapsed time without logging the full URL or query string.

## RunMind 1.10.17 — Native-first webpage reading

- Both desktop hosts use a shared C# `HttpClient` Native Reader to retrieve public HTTP/HTTPS pages without browser CORS restrictions. The response is passed to the existing JavaScript HTML/metadata extraction and Markdown conversion code.
- The 1.10.17 retrieval order was **Native Reader → configured Jina Reader → optional custom Reader gateway**; 1.10.19 changes it to **Native Reader → configured Custom Reader gateway → Jina Reader**. Browser-only previews retain direct browser fetch followed by the external Reader fallback.
- The same native-first path is used by webpage, sitemap, RSS/XML, and document-reading tools. Binary downloads continue to preserve original response bytes and are not passed through the Markdown Reader.
- The WebView2 bridge accepts only bounded read/cancel messages from the local `https://localhost/ChatWindow.html` page. It exposes no host objects or unrestricted HTTP proxy.
- Native Reader safeguards: HTTP/HTTPS only; public DNS/IP destinations only; private/local/link-local/reserved destinations rejected; redirects validated one by one; HTTPS-to-HTTP downgrade blocked; cookies and ambient credentials disabled; maximum five redirects, 25 seconds, and 1.5 MiB response body.
- The Native Reader does not execute page JavaScript. SPA shells, bot challenges, non-text content, blocked destinations, and failed/empty extractions are handed to the configured remote Reader where policy permits. External fallback can disclose the target URL and page request to that provider.
- The native connection intentionally bypasses system proxies so its DNS/IP validation applies to the actual destination; environments requiring an outbound proxy can still use the configured remote Reader fallback.

## RunMind 1.10.16 — literal identifier rendering

- Inline Markdown emphasis does not reinterpret underscores or asterisks embedded within alphanumeric identifiers. Model names and quantization labels retain their exact characters. User prompt contents continue to be sent unchanged.

# RunMind — Feature Matrix

This document describes the **current RunMind 2.0.0 feature set** across its PowerToys Run plugin and standalone Windows application. It is a unified snapshot, not a release-by-release history.

## RunMind 1.10.15 — generated branch-label correction

- Generic child branches use `Branch 1`, `Branch 2`, etc. Meaningful fork prompts continue to produce descriptive branch names. Saved auto-generated `Alternative N` labels are migrated back to `Branch N`; descriptive names are retained.
- The exact editable QuickAI default prompt introduced in 1.10.14 remains unchanged.

## RunMind 1.10.14 — QuickAI prompt refinement

- PowerToys Run's multiline system-prompt setting is pre-filled with the concise two-sentence QuickAI prompt. The setting remains editable and clearable. Only the exact prior built-in default is migrated; custom and empty values are preserved.
- 1.10.14 temporarily migrated generated branch labels to `Alternative N`; 1.10.15 restores `Branch N`.

## RunMind 1.10.13 — copy sent prompts

- Chat transcript: each text-bearing user message has a **Copy** action that copies the exact original prompt text. It remains available during response streaming; attachment payloads are not copied. Existing assistant copy actions are unchanged.

## RunMind 1.10.12 — QuickAI defaults and native window sizing

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| PowerToys settings | QuickAI starter prompt | The multiline PowerToys prompt setting is pre-filled with the user's QuickAI text. It is editable and can be cleared to disable the additional system prompt. | **Implemented**; key remains `InlineSystemPrompt`, maximum 12,000 characters. |
| Native windows | Work-area-aware initial placement | PowerToys and standalone windows start at 1120 × 836 DIPs and are centered/clamped within the work area of the monitor under the pointer. | **Implemented**; applied once at window creation; later user resizing is not overridden. |
| Native windows | Short-display safety | A 12-DIP margin is retained where possible; minimum dimensions are relaxed only when a monitor work area is smaller than the configured minimum. | **Implemented** |

## RunMind 1.10.11 — PowerToys Run live-input preservation

- The fast query path returns its pending result without network I/O and cancels superseded requests. The delayed query path waits for provider completion on PowerToys' background worker and returns the completed result directly to the host's current query generation. RunMind no longer calls `ChangeQuery`, so completed responses cannot replace newer search-box text or caret position.

## RunMind 1.10.10 — multiline PowerToys prompt setting

- PowerToys Run custom system instructions use the native multiline settings control, support real line breaks and vertical scrolling, and retain the 12,000-character maximum and existing setting key. Legacy escaped `\n` prompt values remain normalized.

## RunMind 1.10.9 — flyout scrolling hotfix

| Area | Fix | Current behavior | Status / limits |
|---|---|---|---|
| Toolbar UX | Persistent flyout scrolling | Scroll events originating inside an open toolbar or Conversations flyout no longer invoke viewport repositioning. The shared placement routine preserves outer and nested scroll offsets during legitimate repositioning and keeps a non-collapsing measurement height. | **Fixed**; Windows WebView2 visual confirmation remains required. |
| Toolbar UX | Scroll containment | Flyouts use contained overscroll so reaching a menu's scroll boundary does not unexpectedly scroll the page behind it. | **Implemented** |

## RunMind 1.10.8 — custom instructions and generation controls

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| PowerToys settings | Inline custom system instructions | Optional plugin-local prompt is sent as Ollama `system` or an LM Studio system message; literal `\n` sequences create line breaks. | **Implemented**; limited to 12,000 characters and independent of chat-app preferences. |
| PowerToys settings | Inline generation defaults | Temperature, Top P, Max output tokens, and Seed are independently configurable and passed in provider-native request fields. | **Implemented**; blank/invalid values defer to provider defaults. |
| Chat settings | Custom system instructions | Multiline prompt editor with 16,000-character limit, counter, preview, local persistence, and independent Clear action. | **Implemented**; applies to normal chat, Agent Mode, Deep Research, and specialist sub-agents. |
| Prompt composition | Built-in and user instructions | User preferences are composed alongside built-in instructions and do not grant tool permissions or replace mode/tool contracts. | **Implemented**; active agent/research tasks retain a prompt snapshot. |
| Generation | Agent and research provider settings | Configured temperature, Top P, max output tokens, context length where supported, and seed flow through Ollama and LM Studio tool-call requests. | **Implemented with provider limits**; explicit LM Studio 400/422 parameter rejections are cached and omitted on later requests. |

## RunMind 1.10.7 UI hotfix

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| Toolbar UX | Viewport-aware flyouts | Every toolbar and Conversations panel uses shared viewport-fixed placement, horizontal and vertical clamping, above-trigger fallback, bounded height with internal scrolling, and resize/scroll repositioning. | **Implemented**; visual validation in Windows WebView2 remains required. |

## RunMind 1.10.7 — shared viewport-aware flyout positioning

- All toolbar and Conversations flyouts use one viewport-fixed placement routine.
- Panels stay within all four WebView viewport edges, open above their trigger when needed, and become internally scrollable when taller than the available area.
- Open flyouts reposition after window/visual-viewport resizing and document scrolling.

## RunMind 1.10.0 — Agent Orchestration & Workflow Intelligence (retained in 1.10.1)

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| Execution | Shared tool-batch scheduler | Agent Mode, Deep Research, and sub-agent tool batches use one scheduler. | **Implemented**; one model tool-call response is a dependency layer; next model turn waits for all results. |
| Execution | Bounded parallel reads | Independent tools on the explicit read-only built-in allowlist can execute concurrently, with a configurable limit of 1–4. | **Implemented with limits**; writes, approval-gated reads, MCP calls, duplicate calls, `research_topic` in Deep Research, and delegated agents remain serialized. |
| Execution | Result ordering | Tool results return to the provider in original model call order, regardless of start/completion order. | **Implemented** |
| Execution | Cancellation | The main Stop control aborts the active run, marks queued operations cancelled, and prevents later model-turn layers from being dispatched. | **Implemented with limits**; in-flight adapters and external services must honor the abort signal; already accepted side effects cannot be undone. |
| Workflow graph | Dependency-layer visualization | Recent operations are grouped by model turn and show state, duration, sanitized inputs, bounded outputs, and prior-layer dependencies. | **Implemented**; it visualizes known turn barriers, not speculative argument-level dependencies. |
| Workflow graph | Read-only tool priorities | Drag parallel read-only operation cards to change their preferred start order in future independent groups. | **Implemented with limits**; tool-type preference only; no reorder of active/history or serialized operations. |
| Sub-agents | Specialized roles | Researcher, Coder, Security Analyst, Reviewer, and Summarizer delegate roles are available through the parent agent. | **Implemented with limits**; one level only, selected model, at most four child model/tool turns, read-only child tools. |
| Sub-agents | Structured results | Child results are normalized to summary, findings, sources, uncertainties, and limitations. | **Implemented with limits**; when the model does not return valid JSON, its text is preserved as an unstructured summary. |
| Conversation | Branching | Branch from a completed message, switch, rename, delete, return to Original, and compare latest assistant responses. | **Implemented with limits**; eight branches per window, last 16 messages per local snapshot, bounded text, binary attachments omitted; `.runmind` remains the full archive. |
| Conversation UX | Branch manager visibility and layout | Branch controls are collapsed by default, accessible through a compact **Branches** toggle when multiple branches exist, and can be explicitly closed. | **Implemented**; the comparison view takes over the entire area below the app header, including the composer. |
| Analytics | Local workflow analytics | Per-tool invocations, success rate, average duration; provider/model requests, failures, average latency, and reported tokens. | **Implemented with limits**; stored per WebView profile; provider tokens appear only when reported. |
| Recovery | Operation intent/retry | Operation intent is persisted before execution; interrupted operations are identified; known read-only operations can be retried after user confirmation when outcome is ambiguous. | **Implemented with limits**; not an exactly-once guarantee for arbitrary external operations. |


## RunMind 1.10.1 hotfix

| Area | Fix | Current behavior | Status / limits |
|---|---|---|---|
| Deep Research | Active context-limit metrics | Stores `estimatedPromptTokens` from the calculated `estimatedTokens` value when an active context limit is available. | **Fixed**; prevents an undeclared-variable error before provider chat requests. |

## RunMind 1.10.5 mode integration and Warm Graphite theme

| Area | Fix | Current behavior | Status / limits |
|---|---|---|---|
| Deep Research | Composer mode toggle | Enabling Deep Research from the composer opens the Research workspace flyout. | **Fixed**; the same bubbling click is ignored by outside-click dismissal, while other flyouts remain mutually exclusive. |
| Theme | Warm Graphite dark palette | Dark mode uses warm neutral charcoal surfaces, ivory text, and muted amber accent colors instead of blue/indigo tones. | **Implemented**; Light and System-Light palettes are unchanged; this is not a display-level blue-light filter. |

## RunMind 1.10.4 toolbar and permissions UX

| Area | Feature | Current behavior | Status / limits |
|---|---|---|---|
| Toolbar | Deep Research workspace flyout | The workspace is anchored below its toolbar icon using the same flyout layout as other toolbar sections. | **Implemented**; bounded to the viewport width and height. |
| Toolbar | Active and expanded state | The Deep Research icon reflects the open state and exposes synchronized `aria-expanded`. | **Implemented** |
| Toolbar | Exclusive flyout behavior | Opening any top-level toolbar or conversation menu closes the previously open flyout; outside click and Escape close the active flyout. | **Implemented**; nested in-panel disclosures are not part of the top-level group. |
| Agent permissions | Per-tool always-approval removal | Each saved always-approved tool has its own Remove action; Clear approvals remains available. | **Implemented**; changes persist immediately in the current WebView profile. |

## RunMind 1.10.3 branch UX refinement

| Area | Fix | Current behavior | Status / limits |
|---|---|---|---|
| Conversation title | Separate parent title from branch labels | Switching branches never mutates the conversation title. The immutable `main` branch is displayed as **Original**. | **Fixed**; the underlying branch ID remains `main` for storage compatibility. |
| Branch naming | Context-aware branch labels | New branches use the most recent user prompt at the fork point; generic prompts receive numbered `Branch N` labels and duplicate labels are disambiguated. | **Implemented**; deterministic prompt-derived labels can be renamed by the user. |
| Branch manager spacing | Single-row open state | The **Branches** toggle hides while the manager is open, leaving the close button inside the manager as the only dismissal control. | **Fixed**; removes the redundant toggle row and its vertical gap. |


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
| Delayed execution | Provider/model discovery and generation run in PowerToys’ delayed background query phase; the synchronous plugin callback waits only in that worker and returns the result to the same host query generation. Ollama inline requests support an optional CPU thread cap; blank or `0` leaves thread selection to Ollama, while `1`–`128` sets `options.num_thread`. | **Implemented**; the fast keystroke callback remains network-free; the cap does not apply to LM Studio or standalone chat. |
| Completed result | The delayed plugin call returns its completed result directly; PowerToys updates the result list only if that query session is still current. RunMind never changes the search-box text to refresh results. | **Implemented** |
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
| Connections | Selected-model diagnostics | Recovery view tests model discovery, a small generation request, and function-call output without executing the returned function. | **Conditional**; LM Studio Native REST mode reports generation/tool calling as not tested/not applicable. Other custom endpoints must expose a compatible chat-completions route. |
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
| New conversation | Starts a fresh chat from the Conversations menu in the current window or a separate window. The separate-window option leaves the current conversation untouched; replacing the current chat offers Save, Discard, or Cancel first. | **Implemented** |
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
| Model connection diagnosis | `diagnose_connection` checks the selected model server's discovery endpoint and classifies common failures. Recovery diagnostics also test a small generation request and a harmless tool-calling response. | **Implemented with limits**; the tool probe is never executed; LM Studio Native REST is not covered by chat-completions probes. |
| Tool execution | Tool calls are executed by the browser-side application and returned to the model as tool results. Ollama NDJSON and OpenAI-compatible SSE streams expose partial tool-call names/arguments and live progress before a call is executed. | **Implemented with limits**; arguments must finish parsing before execution. Non-streaming JSON responses remain supported. |
| Tool limits | Tool turns are bounded by configured step/request limits and response-size limits. | **Implemented** |
| Approval UI | Inline themed approval cards replace browser-native confirmation dialogs for Agent/MCP actions. | **Implemented** |
| Approval choices | Supports Allow once and Deny; task-scoped read approvals and persistent per-tool Always allow are also available where configured. | **Implemented**; Always allow is scoped by tool name and can cover future arguments/paths, so use carefully. |
| Approval management | Persistent Always-allow entries can be reviewed, revoked individually, or cleared. | **Implemented** |
| Cancellation | Stop/cancel aborts active operations where supported and dismisses a pending approval. | **Implemented**; a server/provider may not stop work that it has already accepted. |
| Task history | Recent Agent Tasks stores local task snapshots and can reopen or clear them. | **Implemented**; history is scoped to the current chat window. |
| Request snapshots | Agent runs snapshot the selected model connection, generation settings, and available tool definitions when the task starts. | **Implemented** |
| Model behavior | System instructions encourage the agent to verify official documentation, follow relevant links, and avoid claiming an action succeeded without evidence. | **Implemented as instructions**; actual execution quality depends on the model and available tools. |
| Parallel tools/subagents | Shared bounded execution for independent read-only built-ins and bounded one-level specialist sub-agents. | **Implemented with limits**; see the 1.10.0 section above; scheduled background agents remain out of scope. |

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
| Web extraction | Bounded extraction supports links, metadata, tables, sitemap, RSS/Atom, and readable page content. `page_to_markdown` converts direct HTML to Markdown with headings, lists, links, tables, blockquotes, and code blocks; Reader output is retained as Markdown. | **Implemented with limits**; complex client-rendered layouts and direct CORS-restricted pages may require Reader fallback. Raw XML operations still require direct XML access. |
| Page downloads | Can save readable webpage snapshots; Reader may be used for Markdown snapshot fallback. | **Conditional**; distinct from `download_file`, which saves original response bytes. |
| Direct resource downloads | `download_file` writes the original response bytes rather than Reader-extracted Markdown. | **Conditional**; CORS, workspace permission, approval, overwrite, and size limits apply. |
| Multi-query research | `research_topic` runs up to six SearXNG queries, returns up to eight results per query, deduplicates normalized URLs, and prioritizes preferred domains. | **Conditional**; requires configured SearXNG. Snippets are discovery leads, not evidence. |
| Website crawling | `crawl_website` performs breadth-first same-origin traversal with bounded depth and page count. | **Implemented with limits**; maximum 20 pages and depth 3; not a whole-web crawler. |
| Link validation | `check_links` checks up to 30 HTTP(S) links with bounded HEAD requests. | **Implemented with limits**; private/local destinations are skipped; CORS, network, or HEAD restrictions are `unverified`, not `broken`. |
| Webpage comparison | `compare_webpages` fetches two pages and reports exact trimmed lines in common or unique to each. | **Implemented with limits**; exact line comparison only, not semantic comparison. |

## 9. Deep Research

| Feature | Current behavior | Status / limits |
|---|---|---|
| Research mode | Separate Deep Research mode uses the shared Agent tools and enabled MCP tools. | **Implemented** |
| Mode exclusivity | Agent Mode and Deep Research are mutually exclusive. | **Implemented** |
| Research drawer | Right-side overlay drawer can be opened/closed independently from research execution. Closing it does not stop the task or delete results. | **Implemented** |
| Research plan | Creates an editable plan; revisions are queued and applied at the next safe checkpoint. The model can update step statuses. | **Implemented**; no dependency graph or drag/drop ordering yet. |
| Task lifecycle | Supports pause, resume, cancel, periodic persistence, and recovery of paused/interrupted or stale checkpoints in another window using the same WebView2 profile. | **Conditional**; pause occurs after a complete tool-call batch; the last committed transcript is restored after interruption, without exactly-once guarantees. |
| Budgets | Supports page and tool-call budgets plus an optional 0–240 minute time limit. Limits can be adjusted while a task is active. | **Implemented with limits**; time is a soft boundary and an in-flight retrieval may finish before synthesis begins. |
| Domain policy | Preferred, allowed, and blocked hostname lists apply to Deep Research discovery and retrieval. | **Implemented**; blocked entries take precedence; allowlist is restrictive only when populated; preferred domains are ranking hints, not trust scores. |
| Live activity | Shows model planning/synthesis, streaming tool names/argument previews, active web operation, and a timestamped activity feed. | **Implemented**; progress depends on provider streaming support and partially parsed arguments are not executed. |
| Sources | Tracks normalized-URL-deduplicated sources with title, domain, final URL, author, publication/modified dates when available, excerpt, retrieval route/time, read status, and failures. | **Implemented with limits**; metadata is best-effort and source authority is not automatically scored. |
| Evidence register | Stores claims and exact quotations against sources already read; checks whether the normalized quotation appears in the bounded stored excerpt. | **Implemented with limits**; verifies quote presence only, not claim support, source authority, or factual truth. |
| Results | Supports copying plans/results and exporting Markdown with task metadata, sources, evidence register, and activity history. | **Implemented** |
| Checkpoint storage | Uses IndexedDB with bounded task checkpoints and heartbeat; sessionStorage is a fallback if durable storage is unavailable. | **Conditional**; PowerToys and standalone profiles remain separate; cross-window recovery is within a shared profile. |
| Research workspace UX | The existing drawer has Research, Review, and Recovery tabs; Research retains the existing plan, source/domain settings, budgets, sources, evidence, activity, and task controls. | **Implemented**; this is one drawer with views, not a second panel. |
| Research templates | Provides General, Technical, Product/Technology Comparison, Security Assessment, Academic, Website, and Troubleshooting templates. Templates set initial instructions and plan steps; users can edit the plan. | **Implemented**; templates are starting points, not fixed workflows. |
| Claim assessment | The model can register a material claim with a verdict, explanation, uncertainty, and supporting/contradicting/context passages from sources already read. | **Partial**; the verdict is model-assisted. Local verification checks exact quotation presence only, not entailment or factual truth. |
| Research Review | Filters claims, shows explanations and quoted passages, links each passage to its source, and lets users Accept/Reject or request targeted further investigation. | **Implemented**; decisions are user annotations, not machine certification. Targeted investigation resumes the same task when a resumable transcript exists. |
| Source quality assessment | Records qualitative source type, author/date visibility, freshness, strengths, limitations, and explanation. | **Partial**; model-assisted and explainable, with no numeric trust score or independent reputation verification. |
| Source independence | Displays the number of hostnames represented by claim evidence and records a model-generated note about possible shared underlying reporting. | **Partial**; different hostnames do not prove editorial independence, and this is not a graph-based provenance analysis. |
| Context intelligence | Shows estimated research context tokens, known active context limit, utilization, and compaction count. Older tool messages can be compacted at safe checkpoints. | **Partial**; tokens are estimated from serialized characters ÷ 4; compaction runs only when an active context limit is known and does not use the model's tokenizer. |
| Checkpoint history | Retains up to eight IndexedDB snapshots per task, lists their status/time/reason and counts, and supports confirmed restore to an earlier snapshot. | **Conditional**; requires IndexedDB and storage availability. Snapshot content is bounded; history does not sync between distinct WebView2 profiles. |
| Tool-operation recovery | Records tool intent before execution and bounded result data. Exact matching completed calls in an uncommitted batch can reuse saved results; ambiguous operations require user acknowledgement before retry. | **Partial**; exact tool-name/argument matching only. Different arguments can still repeat a side effect; arbitrary tools cannot receive exactly-once guarantees. |
| Report export | Markdown export includes task metadata, sources, evidence, claim assessments, user decisions, qualitative source assessments, operation recovery state, and activity. | **Implemented**; inline citations in the model-written report still depend on model output quality. |

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
| Multiple standalone instances | Standalone windows share the standalone WebView2 profile. | **Implemented**; browser-local settings are shared. Research checkpoints are keyed by window, but paused/interrupted or stale checkpoints can be recovered into another window in the same profile. |
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
- Unrestricted arbitrary parallel tool execution, nested/autonomous background subagents, and scheduled tasks. Conversation branching and bounded subagents are included as described above.
- Cross-origin whole-site crawling, semantic webpage comparison, and automatic whole-site link auditing.
- Native DOCX/XLSX/PPTX/PDF document creation, PDF editing/annotation, and document conversion.
- A credential vault or encryption layer for locally stored API keys and MCP tokens.
- A bundled local model server or bundled WebView2 browser runtime.

---

## Important implementation boundaries

- Model capabilities vary. Agent tool calling, reasoning output, optional generation parameters, and context metadata depend on the selected model and provider API.
- CORS is enforced by WebView2. A provider being reachable from Windows does not guarantee it can be called from the chat page; the server must allow the `https://localhost` origin.
- WebView2 directory access and permission prompts are controlled by Windows/browser APIs.
- Token/context figures are estimates unless explicitly reported by the provider; character-based estimates do not equal exact model tokenization.
- Deep Research claim verdicts and source-quality explanations are model-assisted, not independently proven. Local quote matching confirms only that normalized quotation text occurs in a bounded stored excerpt; it does not prove logical entailment, factual truth, source authority, or editorial independence.
- Deep Research time budgets are soft boundaries: current HTTP operations complete before the task stops retrieving and synthesizes collected evidence.
- Source-level checks do not replace a Windows build and live tests against the user's PowerToys, WebView2, model servers, Reader/SearXNG endpoints, workspace permissions, and MCP servers.


RunMind now requests one same-query refresh through the PowerToys public API after background generation completes. The cached answer is returned through the standard delayed-query result path without another LLM request; the refresh is skipped if the active query has changed.
