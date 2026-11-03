
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
