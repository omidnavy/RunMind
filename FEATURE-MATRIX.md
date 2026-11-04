# Feature matrix — 1.6.10 update

| Area | 1.6.10 behavior | Status / boundary |
|---|---|---|
| Project identity | `RunMind.Standalone`, `Community.PowerToys.Run.Plugin.RunMind`, `RunMind.sln`, `RunMind.exe` | Renamed; compatibility-sensitive settings and profile paths retained |
| PowerToys global search | Immediate `Ask RunMind`; provider discovery and generation execute asynchronously; completed results are requested only after the worker finishes | Implemented in source; verify competing plugin responsiveness on Windows |
| `llm` keyword | `llm + prompt` uses the same two-stage flow; `llm` alone returns no result | Implemented in source |
| Standalone launch | Normal app search by `RunMind`; every launch opens an independent process/window | Implemented in source; Windows validation pending |
| Standalone title bar | Native Windows title bar follows the Windows app light/dark preference and responds to theme-change broadcasts | Implemented in source; Windows visual validation pending |
| Conversation titles | New chats use the shared title generator; opening a saved file uses its filename; Save As pre-fills the suggested filename and synchronizes the in-app/native title with the chosen filename | Implemented in source; Windows/provider validation pending; selected Save As filename is returned by the native host |
| Native Save As | Both desktop hosts show the Windows Save As dialog with the generated title pre-filled and `.runmind` as the default extension | Implemented in shared host code; Windows dialog testing pending; browser-only page preview falls back to download |
| Conversation files | `.runmind` JSON format only; files with other formats/extensions are rejected | Implemented in source |
| WebView2 runtime | Both hosts detect the Evergreen Runtime and silently install Microsoft's bootstrapper per-user when absent | Implemented in source; download/install needs Windows validation and internet only on machines missing the runtime |
| Open saved conversation | Choose **Open in this window** to replace the current chat or **Open in new window** to preserve it and create an independent window | Implemented in source; Windows runtime testing pending |
| WebView2 message bridge | Two bounded, origin-checked operations handle native Save As and open a validated saved conversation in a new window; host objects remain disabled | Implemented in source |
| Cancellation | New fast query, hand-off, setting change and plugin disposal cancel linked HTTP work; stale delayed callbacks/results are suppressed | Implemented in source; PowerToys close/Escape lifecycle events are unavailable to the plugin |
| WebView2 profiles | PowerToys and standalone use separate user-data folders | Implemented; browser-local settings and IndexedDB are intentionally not synchronized |
| Branding | Supplied ICO/PNG/SVG assets integrated into standalone and PowerToys surfaces | Implemented in source |
| Existing chat features | Shared `ChatWindow.html` retained | Existing 1.5.1 functionality preserved in source; not re-audited feature-by-feature in this change |


---

# Feature matrix — 1.5.1

Status: **Included** = implemented in source; **Partial** = implemented with provider/browser/runtime constraints; **Roadmap** = retained for future work, not implemented in this ZIP.

| Area | Feature | Status | Notes |
|---|---|---|---|
| Hosts | PowerToys Run plugin and direct-launch Windows desktop host | Included | Two entry points to the same WebView2/JavaScript application; isolated WebView2 profiles. Browser-local settings/data are not synchronized. No installer, startup registration, tray, or service. |
| Activation | `llm` with no prompt | Removed | No plugin result. Launch standalone through normal application search by `RunMind`. |
| PowerToys query | Two-stage immediate + delayed result | Included | Immediate `Ask RunMind` placeholder; delayed completed answer. Pending-result selection hands off to chat and cancels the inline request. No token-by-token mutation of an existing result. |
| Existing UI | PowerToys Run inline UI and standalone WebView2 chat | Included | Existing experience preserved. |
| Chat | Ollama and OpenAI-compatible streaming chat | Included | Uses current selected connection. |
| Models | Multiple Ollama/OpenAI-compatible server connections and unified selector | Included | Per-connection endpoint, enable state, optional API key, and custom authentication-header name persist in `localStorage`. |
| Credentials | Persistent model API keys | Included | Plaintext in WebView profile `localStorage`, as explicitly requested; not encrypted. |
| Titles | Conversation-based native and in-app title | Included | Both hosts use the shared browser title generator; standalone generates once after its first successful assistant response. |
| Agent | Agent Mode, plan, approvals, task history | Included | General model-driven tool loop; Recent Agent Tasks is isolated by a native-generated per-window ID. |
| Agent | Persistent per-tool Always allow | Included | Saved in profile `localStorage`; exact tool-name scope, with revoke/clear UI. |
| Workspace | Saved directory handle across chats/reloads | Partial | IndexedDB persistence and permission recheck/reconnect; browser/Windows may require a renewed permission gesture. Reuses saved handle rather than requiring repeated folder selection. |
| Workspace | List/read/create/edit/delete/search/copy/move/rename files and folders | Included | Relative paths, selected workspace only; bounded operations. |
| Workspace | Metadata, checksum, compare files/folders, project overview, duplicate scan | Included | Folder comparisons use paths/sizes; same size does not prove identical content. |
| Data | CSV analyze/statistics/filter/deduplicate/merge/compare | Included | Simple bounded CSV parser and row/file limits. |
| Data | JSON validate/pretty/minify/sort | Included | JSON Schema validation is not included. |
| Web | Direct URL fetch | Included | Always tried first. No third-party forwarding when it succeeds. |
| Downloads | Direct file download | Included with CORS boundary | `download_file` writes the original response bytes only; it does not substitute Reader-extracted Markdown. The server must permit the WebView origin through CORS. `download_webpage` may save a Markdown snapshot through Reader fallback. |
| Web | Jina Reader fallback | Included | Default `https://r.jina.ai`, documented `GET /{target-url}` form, optional persistent Bearer key and connection test. Only used after direct fetch fails. |
| Web | Custom Reader-compatible gateway | Included | Optional user-configured third fallback with its own persistent token and connection test. No Cloudflare dependency. |
| Web | HTTP-to-HTTPS upgrade for browser requests | Included | For eligible pages; if secure retrieval fails, remote Reader may be tried with original public HTTP URL. |
| Web | SearXNG web search | Partial | Offered only if a SearXNG endpoint is configured; its CORS/JSON behavior depends on the instance. |
| Web | Links/metadata/tables/sitemap/RSS extraction | Partial | Bounded extraction; raw XML operations require direct XML access. Reader Markdown is not presented as raw XML. |
| Web | Deep recursive crawl, check links, compare webpages, RSS discovery orchestration | Roadmap | Not implemented as full workflows. |
| Downloads | Direct HTTP resource download | Included | 10 MB cap; no overwrite; selected workspace and approval required. |
| Reports | Markdown/HTML reports, SVG charts/diagrams | Included | New file only; write approval applies. |
| Images | Metadata, resize, crop, convert, thumbnail | Included | Browser canvas; PNG/JPEG/WebP output; bounded input size. |
| Research | Separate Deep Research mode and editable live plan | Included | Drawer visibility is independent from execution mode. Opening drawer does not enable research; enabling composer mode opens drawer. |
| Research | Pause/resume/cancel/checkpoints | Partial | Pause at complete tool-batch checkpoint; abort active requests where supported; per-window sessionStorage. No exactly-once guarantee across abrupt interruption. |
| Research | Sources, budgets, Markdown export, copy plan/results | Partial | Sources distinguish discovered vs read, record retrieval route/final URL, and retain failed fetch attempts. Model-generated citations are not independently citation-validated. |
| Research | Follow landing-page links and inspect version-matched manuals/source instructions | Partial | Reinforced in Agent/Research system instructions; model ability and available tools determine completion. |
| MCP | Browser Streamable HTTP client, tools/resources/prompts | Partial | Server configuration, enabled state, and bearer tokens persist in `localStorage`; auto reconnect. Tool identifiers include stable 64-bit name hashes to avoid sanitized-name collisions. Expired sessions are reinitialized only for idempotent list/read operations; tool calls are never automatically replayed. CORS/server compatibility apply. |
| MCP | Resource templates/subscriptions, completions, elicitation, progress, OAuth, Tasks | Roadmap | Not implemented. |
| Runtime | Non-streaming tool-call adapters, bounded calls, approval/checkpoint/cancel | Partial | Normal chat streaming retained; tool turns are not streamed. |
| Runtime | Parallel tools, subagents, scheduled tasks, branches, analytics, advanced recovery | Roadmap | Not implemented. |
| Knowledge | Persistent local/MCP knowledge index and unified search | Roadmap | MCP resources can be read but are not indexed. |
| Documents | DOCX/XLSX/PDF/PPTX generation and conversion | Roadmap | Not included. |
| Files | Archive inspect/extract, PDF reading, workspace image understanding, clipboard | Roadmap | Not included. |
| Media | QR generation, galleries, richer metadata | Roadmap | Not included. |

| Reliability | Agent/research request context | Improved | Agent and research model requests snapshot the selected connection, tool definitions and generation settings at task start, preventing later UI changes from silently switching request configuration. |
| Reliability | Provider model discovery | Improved | Discovery uses per-connection URL/auth snapshots and concurrent refresh calls share one in-flight operation. |
| Reliability | MCP HTTP responses | Improved | 30-second request/body deadline, bounded reads, JSON-RPC response ID matching, and multi-event SSE data parsing. |
| Reliability | Web retrieval response sizes | Improved | Direct document/page and Reader response bodies are streamed with a 2 MB cap; Agent tool-turn responses are bounded. |
| Host lifecycle | WebView2 close handling | Improved | Hosts dispose WebView2 controls and the PowerToys host checks window lifetime during initialization/title updates. |

## Important behavior and limitations

- The app's own chat and drawer states are per WebView. The native bootstrap gives every new chat window a unique ID used for research checkpoints and Recent Agent Tasks. Same-window reloads can restore sessionStorage; a new window starts separate.
- Model connections, API keys, Agent settings, Reader settings/keys, MCP server configurations/tokens, and Always-approved tool names persist in `localStorage` in each host's WebView profile. Credentials are plaintext as explicitly requested and are not exported with conversations.
- Workspace handles are stored in IndexedDB and reused across chat windows. WebView2 may require a new permission gesture after a restart or permission revocation, but the stored directory can be reauthorized without opening the folder picker again.
- The native Windows caption starts as `RunMind`. PowerToys-opened chats retain prompt/answer title generation; standalone now generates once after its first successful response. The selected model name is not shown.
- The WebView origin is `https://localhost` through WebView2 virtual-host mapping; it is not a separately running localhost server. Browser CORS and mixed-content restrictions still apply to direct web fetching.
- Direct web fetch is the first choice. Jina Reader and the optional custom gateway are fallback providers for public URLs. Private/local IPs, embedded URL credentials, and likely secret-bearing query parameters are not forwarded remotely.
- Browser-native directory picker and read/write permission prompts are controlled by WebView2/Windows and cannot be restyled by HTML/CSS.
- Always-approved tool entries are scoped by tool name and can cover future paths/arguments; use with care.
- Full .NET compilation, live PowerToys/WebView2, model-server, Reader network, folder permission and MCP server testing require Windows and were not available during this packaging run.

- PowerToys Run inline result objects cannot be updated token-by-token through the supported plugin contract. The default live-chat handoff avoids waiting for generation before presenting an actionable result; actual token streaming takes place in the chat window. The old blocking inline mode is no longer part of the current settings.

| Multiple standalone windows | Each normal launch starts a separate RunMind process and chat window | Implemented; WebView2 profile is shared by the standalone instances |
| PowerToys chat window branding | RunMind ICO is assigned to both PowerToys-created chat window types | Implemented in source; Windows shell rendering pending |


## 1.6.7 changes

- Standalone opts into the existing shared browser-side conversation-title generator after its first successful response. The native WPF host mirrors `document.title` into the Windows caption.
- Product branding and the source tree/build identities were renamed to RunMind. Compatibility-sensitive plugin identifiers, browser profile paths, and storage keys remain unchanged.
- New conversation files use `.runmind`, and saved-conversation opening offers current-window and new-window choices.
- PowerToys delayed generation no longer waits synchronously for provider HTTP operations. It runs on a worker and requests a fresh result pass after completion.
