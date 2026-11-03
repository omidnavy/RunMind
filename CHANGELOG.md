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
