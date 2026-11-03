#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HtmlAgilityPack;
using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using Clipboard = System.Windows.Clipboard;

namespace Community.PowerToys.Run.Plugin.LocalLLM;

public sealed partial class Main
{
    private async Task<List<string>> FetchLMStudioModelsAsync(SettingsSnapshot settings)
    {
        var baseUri = ValidateLMStudioBaseUrl(settings.LMStudioBaseUrl);
        var modelsUri = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/models");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var response = await HttpClient.GetAsync(modelsUri, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {TrimForError(body)}";
            throw new HttpRequestException(
                $"LM Studio returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{detail}",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("LM Studio's model-list response did not contain a 'data' array. Check the configured OpenAI-compatible base URL.");
        }

        return data.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Uri ValidateLMStudioBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException("The LM Studio base URL must be an absolute HTTP or HTTPS URL, for example http://localhost:1234/v1.");
        }

        return uri;
    }

    private async Task<string> SendOllamaChatStreamAsync(
        string model,
        IReadOnlyList<ConversationMessage> history,
        SettingsSnapshot settings,
        CancellationToken cancellationToken,
        Action<string> onChunk)
    {
        var endpoint = BuildOllamaChatUri(settings.Endpoint);
        var messages = history
            .Where(message => (message.Role == "user" || message.Role == "assistant") && !string.IsNullOrWhiteSpace(message.Content))
            .Select(message => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["role"] = message.Role,
                ["content"] = message.Content,
            })
            .ToList();

        var requestBody = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = true,
            ["keep_alive"] = settings.KeepAliveSeconds,
        };
        if (TryResolveThinkValue(model, settings.Thinking, out var thinkValue))
        {
            requestBody["think"] = thinkValue;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {TrimForError(body)}";
            throw new HttpRequestException(
                $"Ollama returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{detail}", null, response.StatusCode);
        }

        var output = new StringBuilder();
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(error.GetString()))
            {
                throw new InvalidOperationException($"Ollama: {error.GetString()}");
            }

            if (root.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                var chunk = content.GetString() ?? string.Empty;
                if (chunk.Length > 0)
                {
                    output.Append(chunk);
                    onChunk(chunk);
                }
            }

            if (root.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True) break;
        }

        return output.ToString();
    }

    private async Task<string> SendLMStudioChatStreamAsync(
        string model,
        IReadOnlyList<ConversationMessage> history,
        SettingsSnapshot settings,
        CancellationToken cancellationToken,
        Action<string> onChunk)
    {
        var baseUri = ValidateLMStudioBaseUrl(settings.LMStudioBaseUrl);
        var endpoint = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/chat/completions");
        var messages = history
            .Where(message => (message.Role == "user" || message.Role == "assistant") && !string.IsNullOrWhiteSpace(message.Content))
            .Select(message => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["role"] = message.Role,
                ["content"] = message.Content,
            })
            .ToList();
        var requestBody = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = true,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {TrimForError(body)}";
            throw new HttpRequestException(
                $"LM Studio returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{detail}", null, response.StatusCode);
        }

        var output = new StringBuilder();
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].Trim();
            if (data.Length == 0) continue;
            if (data.Equals("[DONE]", StringComparison.Ordinal)) break;

            using var json = JsonDocument.Parse(data);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException("LM Studio: " + error.ToString());
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) continue;
            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object) continue;
            if (!delta.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) continue;
            var chunk = content.GetString() ?? string.Empty;
            if (chunk.Length > 0)
            {
                output.Append(chunk);
                onChunk(chunk);
            }
        }

        return output.ToString();
    }

    private static Uri BuildOllamaChatUri(string endpoint)
    {
        var generateUri = ValidateGenerateEndpoint(endpoint);
        var builder = new UriBuilder(generateUri);
        var path = builder.Path.TrimEnd('/');
        var lastSlash = path.LastIndexOf('/');
        builder.Path = (lastSlash >= 0 ? path[..(lastSlash + 1)] : string.Empty) + "chat";
        builder.Query = string.Empty;
        builder.Fragment = string.Empty;
        return builder.Uri;
    }

    private sealed record ConversationMessage(
        string Id,
        string Role,
        string Content,
        string? Provider,
        string? Model,
        DateTimeOffset Timestamp);

    private sealed record ChatModel(string Provider, string Name);

    private sealed class InteractiveChatWindow
    {
        private readonly Main _owner;
        private readonly string _title;
        private readonly string _initialModel;
        private readonly bool _isError;
        private readonly SettingsSnapshot _settings;
        private readonly List<ConversationMessage> _messages = new();
        private readonly Window _window;
        private readonly WebView2 _webView;
        private CancellationTokenSource? _activeRequest;
        private bool _pageReady;

        public InteractiveChatWindow(Main owner, string title, string model, string prompt, string response, bool isError)
        {
            _owner = owner;
            _title = string.IsNullOrWhiteSpace(title) ? "LocalLLM conversation" : title;
            _initialModel = model;
            _isError = isError;
            _settings = owner.GetSettingsSnapshot();

            if (!string.IsNullOrWhiteSpace(prompt))
            {
                _messages.Add(new ConversationMessage(Guid.NewGuid().ToString("N"), "user", prompt, null, null, DateTimeOffset.Now));
            }
            _messages.Add(new ConversationMessage(Guid.NewGuid().ToString("N"), "assistant", response, "Ollama", model, DateTimeOffset.Now));

            _webView = new WebView2 { AllowDrop = false, Margin = new Thickness(0) };
            _window = new Window
            {
                Title = _title,
                Width = 1040,
                Height = 820,
                MinWidth = 650,
                MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(17, 19, 24)),
                Content = _webView,
            };
            _window.Closed += (_, _) => _activeRequest?.Cancel();
        }

        public void Show()
        {
            _window.Show();
            _window.Activate();
            _ = InitializeWebViewAsync();
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                await _webView.EnsureCoreWebView2Async();
                var core = _webView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.AreDevToolsEnabled = true;
                core.WebMessageReceived += OnWebMessageReceived;
                core.NavigationStarting += OnNavigationStarting;
                core.NewWindowRequested += OnNewWindowRequested;
                _webView.NavigateToString(BuildInteractiveHtml());
            }
            catch (Exception ex)
            {
                _window.Content = new TextBlock
                {
                    Text = "The interactive chat view could not start. Make sure the Microsoft Edge WebView2 Runtime is installed.\n\n" + ex.Message,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(24),
                    Foreground = Brushes.White,
                };
            }
        }

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            OpenExternalUrl(e.Uri);
        }

        private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            OpenExternalUrl(e.Uri);
        }

        private void OpenExternalUrl(string? value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeMailto) return;
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) { _owner._context?.API.ShowMsg("Could not open link", ex.Message); }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var json = JsonDocument.Parse(e.WebMessageAsJson);
                var root = json.RootElement;
                if (!root.TryGetProperty("type", out var typeElement)) return;
                var type = typeElement.GetString();
                switch (type)
                {
                    case "ready":
                        if (!_pageReady)
                        {
                            _pageReady = true;
                            SendInitialState();
                            _ = RefreshModelsAsync();
                        }
                        break;
                    case "refreshModels":
                        _ = RefreshModelsAsync();
                        break;
                    case "send":
                        var content = root.TryGetProperty("content", out var contentElement) ? contentElement.GetString() ?? string.Empty : string.Empty;
                        var provider = root.TryGetProperty("provider", out var providerElement) ? providerElement.GetString() ?? "Ollama" : "Ollama";
                        var model = root.TryGetProperty("model", out var modelElement) ? modelElement.GetString() ?? _initialModel : _initialModel;
                        _ = SendMessageAsync(content, provider, model);
                        break;
                    case "copy":
                        if (root.TryGetProperty("text", out var textElement))
                        {
                            Clipboard.SetText(textElement.GetString() ?? string.Empty);
                            Post(new { type = "notice", message = "Copied to clipboard." });
                        }
                        break;
                    case "saveExport":
                        var format = root.TryGetProperty("format", out var formatElement) ? formatElement.GetString() ?? "txt" : "txt";
                        var fileName = root.TryGetProperty("filename", out var fileNameElement) ? fileNameElement.GetString() ?? "LocalLLM-conversation" : "LocalLLM-conversation";
                        var exportContent = root.TryGetProperty("content", out var exportElement) ? exportElement.GetString() ?? string.Empty : string.Empty;
                        SaveExport(format, fileName, exportContent);
                        break;
                    case "openLink":
                        if (root.TryGetProperty("url", out var urlElement)) OpenExternalUrl(urlElement.GetString());
                        break;
                }
            }
            catch (Exception ex)
            {
                Post(new { type = "requestError", message = ex.Message });
            }
        }

        private void SendInitialState()
        {
            var initialMessages = _messages.Select(message => new
            {
                id = message.Id,
                role = message.Role,
                content = message.Content,
                provider = message.Provider,
                model = message.Model,
                timestamp = message.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                html = message.Role == "assistant" ? RenderMarkdownHtml(message.Content) : string.Empty,
            }).ToList();
            Post(new
            {
                type = "init",
                title = _title,
                initialProvider = "Ollama",
                initialModel = _initialModel,
                messages = initialMessages,
                isError = _isError,
            });
        }

        private async Task RefreshModelsAsync()
        {
            // Discover both providers at the same time. Either provider may be offline
            // without preventing the other provider's models from appearing.
            var ollamaTask = FetchOllamaModelsForChatAsync();
            var lmStudioTask = FetchLMStudioModelsForChatAsync();
            await Task.WhenAll(ollamaTask, lmStudioTask).ConfigureAwait(true);
            var (ollama, ollamaError) = await ollamaTask.ConfigureAwait(true);
            var (lmStudio, lmStudioError) = await lmStudioTask.ConfigureAwait(true);

            var models = new List<ChatModel>();
            models.AddRange(ollama.Select(name => new ChatModel("Ollama", name)));
            models.AddRange(lmStudio.Select(name => new ChatModel("LM Studio", name)));
            if (!models.Any(model => model.Provider == "Ollama" && model.Name.Equals(_initialModel, StringComparison.OrdinalIgnoreCase)))
            {
                models.Insert(0, new ChatModel("Ollama", _initialModel));
            }

            Post(new
            {
                type = "models",
                models = models.Select(model => new { provider = model.Provider, name = model.Name }).ToList(),
                selectedProvider = "Ollama",
                selectedModel = _initialModel,
                ollamaError,
                lmStudioError,
            });
        }

        private async Task<(List<string> Models, string? Error)> FetchOllamaModelsForChatAsync()
        {
            try { return (await _owner.FetchModelsFromEndpointAsync(_settings).ConfigureAwait(false), null); }
            catch (Exception ex) { return (new List<string>(), Main.GetUsefulExceptionMessage(ex)); }
        }

        private async Task<(List<string> Models, string? Error)> FetchLMStudioModelsForChatAsync()
        {
            try { return (await _owner.FetchLMStudioModelsAsync(_settings).ConfigureAwait(false), null); }
            catch (Exception ex) { return (new List<string>(), Main.GetUsefulExceptionMessage(ex)); }
        }

        private async Task SendMessageAsync(string content, string provider, string model)
        {
            content = content.Trim();
            model = model.Trim();
            if (content.Length == 0) return;
            if (_activeRequest is not null)
            {
                Post(new { type = "requestError", message = "A response is already being generated. Wait for it to finish first." });
                return;
            }
            if (!provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
                && !provider.Equals("LM Studio", StringComparison.OrdinalIgnoreCase))
            {
                Post(new { type = "requestError", message = "Unknown model provider." });
                return;
            }
            if (string.IsNullOrWhiteSpace(model))
            {
                Post(new { type = "requestError", message = "Choose a model first." });
                return;
            }

            _messages.Add(new ConversationMessage(Guid.NewGuid().ToString("N"), "user", content, null, null, DateTimeOffset.Now));
            var assistant = new ConversationMessage(Guid.NewGuid().ToString("N"), "assistant", string.Empty, provider, model, DateTimeOffset.Now);
            _messages.Add(assistant);
            var output = new StringBuilder();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));
            _activeRequest = cts;
            Post(new { type = "streamStart", id = assistant.Id, provider, model });

            try
            {
                Action<string> onChunk = chunk =>
                {
                    output.Append(chunk);
                    Post(new { type = "chunk", id = assistant.Id, text = chunk });
                };
                var requestHistory = _messages.Where(message => message.Id != assistant.Id).ToList();
                var result = provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
                    ? await _owner.SendOllamaChatStreamAsync(model, requestHistory, _settings, cts.Token, onChunk).ConfigureAwait(true)
                    : await _owner.SendLMStudioChatStreamAsync(model, requestHistory, _settings, cts.Token, onChunk).ConfigureAwait(true);

                if (string.IsNullOrWhiteSpace(result)) result = "The model completed the request but returned no text.";
                ReplaceMessage(assistant.Id, result);
                Post(new { type = "complete", id = assistant.Id, content = result, html = RenderMarkdownHtml(result), provider, model });
            }
            catch (Exception ex)
            {
                _messages.RemoveAll(message => message.Id == assistant.Id);
                Post(new { type = "requestError", id = assistant.Id, message = Main.GetUsefulExceptionMessage(ex) });
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_activeRequest, cts)) _activeRequest = null;
            }
        }

        private void ReplaceMessage(string id, string content)
        {
            var index = _messages.FindIndex(message => message.Id == id);
            if (index < 0) return;
            var old = _messages[index];
            _messages[index] = old with { Content = content };
        }

        private void SaveExport(string format, string fileName, string content)
        {
            var config = format.ToLowerInvariant() switch
            {
                "html" => ("HTML document (*.html)|*.html", ".html"),
                "openwebui" => ("JSON file (*.json)|*.json", ".json"),
                "json" => ("JSON file (*.json)|*.json", ".json"),
                "api" => ("JSON file (*.json)|*.json", ".json"),
                "md" => ("Markdown file (*.md)|*.md", ".md"),
                "csv" => ("CSV file (*.csv)|*.csv", ".csv"),
                "jsonl" => ("JSON Lines file (*.jsonl)|*.jsonl", ".jsonl"),
                _ => ("Text file (*.txt)|*.txt", ".txt"),
            };
            var safeName = string.Join("", fileName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "LocalLLM-conversation";
            if (!safeName.EndsWith(config.Item2, StringComparison.OrdinalIgnoreCase)) safeName += config.Item2;
            var dialog = new SaveFileDialog
            {
                Title = "Export LocalLLM conversation",
                Filter = config.Item1,
                DefaultExt = config.Item2,
                AddExtension = true,
                FileName = safeName,
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog(_window) != true) return;
            var encoding = format.Equals("csv", StringComparison.OrdinalIgnoreCase)
                ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
                : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(dialog.FileName, content, encoding);
            Post(new { type = "notice", message = "Export saved." });
        }

        private void Post(object value)
        {
            if (_window.Dispatcher.HasShutdownStarted || _window.Dispatcher.HasShutdownFinished) return;
            if (!_window.Dispatcher.CheckAccess())
            {
                try { _window.Dispatcher.BeginInvoke(new Action(() => Post(value))); }
                catch (InvalidOperationException) { }
                catch (TaskCanceledException) { }
                return;
            }

            if (!_window.IsVisible || _webView.CoreWebView2 is null) return;
            try { _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, value.GetType())); }
            catch (InvalidOperationException) { }
        }

        private static string RenderMarkdownHtml(string markdown)
        {
            var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
            var html = Markdown.ToHtml(markdown ?? string.Empty, pipeline);
            var document = new HtmlDocument();
            document.LoadHtml(html);
            Main.SanitizeMarkdownHtml(document);
            return document.DocumentNode.InnerHtml;
        }

        private static string BuildInteractiveHtml()
        {
            return """
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; font-src data:; connect-src 'none';">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>LocalLLM Chat</title>
            <style id="themeCss">
            /* Developer theme customization: edit the CSS variables in this :root block and its light/dark overrides. */
            :root{color-scheme:light dark;--bg:#111318;--panel:#191c23;--panel-2:#20242d;--surface:#1b1f28;--surface-user:#252d49;--border:#303746;--text:#e8edf5;--muted:#9aa4b6;--accent:#9aabff;--accent-hover:#b5c0ff;--code:#0d0f14;--button:#252c38;--button-text:#e8edf5;--danger:#ffaaa8;--shadow:0 8px 28px rgba(0,0,0,.18);--radius:12px;--font: "Segoe UI",system-ui,sans-serif;--mono:Consolas,"Cascadia Code",monospace}
            @media(prefers-color-scheme:light){:root:not([data-theme="dark"]):not([data-theme="light"]){--bg:#f5f6f8;--panel:#fff;--panel-2:#f1f3f7;--surface:#fff;--surface-user:#e9edff;--border:#d9deea;--text:#20232b;--muted:#656d7c;--accent:#4055ba;--accent-hover:#30449f;--code:#f0f2f6;--button:#f0f2f7;--button-text:#20232b;--danger:#a52020;--shadow:0 8px 28px rgba(20,35,65,.08)}}
            :root[data-theme="light"]{color-scheme:light;--bg:#f5f6f8;--panel:#fff;--panel-2:#f1f3f7;--surface:#fff;--surface-user:#e9edff;--border:#d9deea;--text:#20232b;--muted:#656d7c;--accent:#4055ba;--accent-hover:#30449f;--code:#f0f2f6;--button:#f0f2f7;--button-text:#20232b;--danger:#a52020;--shadow:0 8px 28px rgba(20,35,65,.08)}
            :root[data-theme="dark"]{color-scheme:dark;--bg:#111318;--panel:#191c23;--panel-2:#20242d;--surface:#1b1f28;--surface-user:#252d49;--border:#303746;--text:#e8edf5;--muted:#9aa4b6;--accent:#9aabff;--accent-hover:#b5c0ff;--code:#0d0f14;--button:#252c38;--button-text:#e8edf5;--danger:#ffaaa8;--shadow:0 8px 28px rgba(0,0,0,.18)}
            *{box-sizing:border-box}html,body{height:100%;margin:0}body{background:var(--bg);color:var(--text);font-family:var(--font);font-size:14px;line-height:1.55;display:flex;flex-direction:column;overflow:hidden}button,select,textarea{font:inherit;color:var(--text)}button,select{border:1px solid var(--border);background:var(--button);border-radius:8px;padding:8px 10px}button{cursor:pointer;white-space:nowrap}button:hover{border-color:var(--accent);color:var(--accent-hover)}button:disabled{opacity:.5;cursor:default}select{max-width:330px;min-width:210px}option,optgroup{background:var(--panel);color:var(--text)}.toolbar{display:flex;gap:8px;align-items:center;flex-wrap:wrap;padding:12px 16px;background:var(--panel);border-bottom:1px solid var(--border)}.brand{font-size:11px;font-weight:700;letter-spacing:.11em;color:var(--accent);margin-right:6px}.title{font-weight:650;font-size:15px;min-width:80px;flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.toolbar-spacer{flex:1}.model-select{display:flex;align-items:center;gap:7px}.model-select label{font-size:12px;color:var(--muted)}#status{font-size:12px;color:var(--muted);padding:0 16px;min-height:25px;display:flex;align-items:center}.messages{flex:1;overflow:auto;padding:20px max(18px,calc((100% - 920px)/2));scroll-behavior:smooth}.message{margin:0 0 18px;display:flex;flex-direction:column;gap:5px;max-width:100%}.message.user{align-items:flex-end}.message.assistant{align-items:flex-start}.message-meta{font-size:11px;color:var(--muted);display:flex;gap:8px;align-items:center}.message-content{width:fit-content;max-width:100%;min-width:80px;padding:13px 16px;border:1px solid var(--border);background:var(--surface);border-radius:var(--radius);overflow-wrap:anywhere;box-shadow:var(--shadow)}.message.user .message-content{background:var(--surface-user)}.message-content p:first-child{margin-top:0}.message-content p:last-child{margin-bottom:0}.message-content h1,.message-content h2,.message-content h3,.message-content h4{line-height:1.25;margin:.8em 0 .4em}.message-content h1{font-size:1.5em}.message-content h2{font-size:1.3em}.message-content h3{font-size:1.12em}.message-content a{color:var(--accent);text-decoration:underline}.message-content blockquote{border-left:3px solid var(--accent);margin:10px 0;padding:2px 12px;color:var(--muted)}.message-content ul,.message-content ol{padding-left:1.6em}.message-content hr{border:0;border-top:1px solid var(--border);margin:16px 0}.message-content table{border-collapse:collapse;display:block;max-width:100%;overflow:auto}.message-content td,.message-content th{padding:7px 10px;border:1px solid var(--border);text-align:left}.message-content th{background:var(--panel-2)}.message-content pre{position:relative;overflow:auto;padding:34px 13px 13px;border:1px solid var(--border);border-radius:9px;background:var(--code);font-family:var(--mono);font-size:12.5px;line-height:1.55;white-space:pre;max-width:100%}.message-content pre code{font-family:inherit;white-space:pre}.message-content code:not(pre code){font-family:var(--mono);font-size:.92em;padding:2px 5px;background:var(--code);border-radius:5px}.copy-code{position:absolute;top:5px;right:5px;padding:4px 8px;font-size:11px}.composer{border-top:1px solid var(--border);background:var(--panel);padding:12px max(16px,calc((100% - 920px)/2)) 14px}.composer-inner{display:flex;gap:10px;align-items:flex-end}.composer textarea{resize:vertical;min-height:54px;max-height:220px;flex:1;border:1px solid var(--border);border-radius:10px;background:var(--bg);padding:12px;outline:none;line-height:1.45}.composer textarea:focus{border-color:var(--accent)}.composer-hint{font-size:11px;color:var(--muted);margin-top:5px}.primary{background:var(--accent);color:#111624;border-color:transparent;font-weight:650;min-width:76px;padding:11px 14px}.primary:hover{color:#111624;background:var(--accent-hover)}.error{border:1px solid color-mix(in srgb,var(--danger) 40%,transparent);border-radius:8px;color:var(--danger);padding:10px 12px;margin:8px 0}.notice{color:var(--accent)}.export-document{max-width:940px;margin:0 auto;padding:28px 20px}.export-document>header{border-bottom:1px solid var(--border);padding-bottom:15px;margin-bottom:22px}.export-document>header h1{font-size:25px;margin:0 0 5px}.export-document>header p,.export-footer{font-size:12px;color:var(--muted)}.export-document .message-content{box-shadow:none}.export-document .message{page-break-inside:avoid}.export-footer{border-top:1px solid var(--border);padding-top:12px;margin-top:24px}.hidden{display:none!important}@media(max-width:760px){.toolbar{padding:9px}.title{flex-basis:100%;order:2}.brand{flex:1}.model-select{order:3;flex:1}.model-select select{min-width:0;width:100%}.toolbar button{padding:7px}.messages{padding:14px 10px}.composer{padding:10px}.composer-inner{align-items:stretch}.primary{min-width:58px}}@media print{html,body{height:auto;overflow:visible;background:#fff;color:#111}.toolbar,.composer,#status,.copy-code{display:none!important}.messages{overflow:visible;padding:0;max-width:none}.message-content{background:#fff!important;color:#111!important;box-shadow:none;border-color:#ddd}.message-meta,.export-footer{color:#555}.message{page-break-inside:avoid}.message-content pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f7f7f7;color:#111;max-width:100%}a{color:#111}}
            </style>
            </head>
            <body>
            <div class="toolbar">
              <span class="brand">LOCALLLM</span>
              <span class="title" id="conversationTitle">Model response</span>
              <div class="model-select"><label for="modelSelect">Model</label><select id="modelSelect" aria-label="Model selection"><option>Loading models…</option></select><button id="refreshModels" type="button" title="Refresh model lists" aria-label="Refresh model lists">↻</button></div>
              <button id="copyMarkdown" type="button" title="Copy the complete conversation as Markdown">Copy Markdown</button>
              <button id="themeButton" type="button" title="Cycle theme: system, light, dark">Theme: System</button>
              <select id="exportSelect" aria-label="Export conversation"><option value="">Export…</option><option value="html">Standalone HTML</option><option value="openwebui">Open WebUI JSON</option><option value="json">Conversation JSON</option><option value="api">OpenAI-compatible request JSON</option><option value="md">Markdown (.md)</option><option value="csv">CSV (.csv)</option><option value="txt">Plain text (.txt)</option><option value="jsonl">JSONL (.jsonl)</option><option value="pdf">Print / Save as PDF</option></select>
            </div>
            <div id="status" role="status">Ready</div>
            <main id="messages" class="messages" aria-live="polite"></main>
            <div class="composer">
              <div class="composer-inner"><textarea id="promptInput" rows="2" placeholder="Ask a follow-up… (Enter to send, Shift+Enter for a new line)"></textarea><button id="sendButton" class="primary" type="button">Send</button></div>
              <div class="composer-hint">Each window keeps its own conversation in memory. Exported files are independent and do not connect to LocalLLM or a model server.</div>
            </div>
            <script>
            (function(){
             'use strict';
             const $=id=>document.getElementById(id);
             function makeId(){return 'msg-'+Date.now().toString(36)+'-'+Math.random().toString(36).slice(2)}
             const state={title:'LocalLLM conversation',messages:[],models:[],selected:{provider:'Ollama',name:''},busy:false,theme:'system',ollamaError:null,lmStudioError:null};
             function post(value){try{window.chrome.webview.postMessage(value)}catch(e){showStatus('The WebView connection is unavailable.',true)}}
             function escapeHtml(value){return String(value??'').replace(/[&<>\"']/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;','\"':'&quot;',"'":'&#39;'}[ch]))}
             function formatTime(value){try{return new Date(value).toLocaleString()}catch(e){return String(value??'')}}
             function showStatus(text,isError){$('status').textContent=text||'';$('status').className=isError?'error':''}
             function currentKey(model){return model.provider+'::'+model.name}
             function addMessageElement(message){
               const wrapper=document.createElement('article');wrapper.className='message '+message.role;wrapper.dataset.id=message.id;
               const meta=document.createElement('div');meta.className='message-meta';
               const role=document.createElement('strong');role.textContent=message.role==='user'?'You':'Assistant';meta.appendChild(role);
               if(message.provider&&message.model){const model=document.createElement('span');model.textContent=message.provider+' · '+message.model;meta.appendChild(model)}
               const time=document.createElement('span');time.textContent=formatTime(message.timestamp);meta.appendChild(time);
               const content=document.createElement('div');content.className='message-content';content.dataset.content='true';
               if(message.role==='assistant'&&message.html!==undefined){content.innerHTML=message.html}else{content.textContent=message.content||'';content.style.whiteSpace='pre-wrap'}
               wrapper.append(meta,content);$('messages').appendChild(wrapper);addCopyButtons(wrapper);return wrapper
             }
             function addCopyButtons(scope){scope.querySelectorAll('pre').forEach(pre=>{if(pre.querySelector('.copy-code'))return;const button=document.createElement('button');button.type='button';button.className='copy-code';button.textContent='Copy code';button.addEventListener('click',()=>{const code=pre.querySelector('code');post({type:'copy',text:(code||pre).innerText||''});button.textContent='Sent';setTimeout(()=>button.textContent='Copy code',900)});pre.insertBefore(button,pre.firstChild)})}
             function renderAll(){const container=$('messages');container.innerHTML='';state.messages.forEach(addMessageElement);container.scrollTop=container.scrollHeight}
             function rebuildModels(preferredKey){
               const select=$('modelSelect');select.innerHTML='';
               ['Ollama','LM Studio'].forEach(provider=>{
                 const group=document.createElement('optgroup');group.label=provider;
                 const rows=state.models.filter(item=>item.provider===provider);
                 if(rows.length===0){const option=document.createElement('option');option.disabled=true;option.textContent=provider==='Ollama'?'No models discovered':'No models discovered';group.appendChild(option)}
                 rows.forEach(item=>{const option=document.createElement('option');option.value=currentKey(item);option.textContent=item.name;option.dataset.provider=item.provider;option.dataset.model=item.name;group.appendChild(option)});
                 select.appendChild(group);
               });
               let key=preferredKey||currentKey(state.selected);let target=[...select.options].find(option=>option.value===key&&!option.disabled);
               if(!target&&state.selected.name){const group=[...select.querySelectorAll('optgroup')].find(element=>element.label===state.selected.provider);if(group){const option=document.createElement('option');option.value=currentKey(state.selected);option.textContent=state.selected.name+' (current)';option.dataset.provider=state.selected.provider;option.dataset.model=state.selected.name;group.appendChild(option);target=option}}
               if(target){select.value=target.value;state.selected={provider:target.dataset.provider||state.selected.provider,name:target.dataset.model||state.selected.name}}
             }
             function getSelected(){const option=$('modelSelect').selectedOptions[0];if(!option||option.disabled)return state.selected;return {provider:option.dataset.provider||'Ollama',name:option.dataset.model||option.textContent}}
             function send(){if(state.busy)return;const input=$('promptInput');const content=input.value.trim();if(!content)return;const selected=getSelected();if(!selected.name){showStatus('Choose a model first.',true);return}const user={id:makeId(),role:'user',content,provider:null,model:null,timestamp:new Date().toISOString()};state.messages.push(user);addMessageElement(user);$('messages').scrollTop=$('messages').scrollHeight;input.value='';setBusy(true);showStatus('Generating…');post({type:'send',content,provider:selected.provider,model:selected.name})}
             function setBusy(busy){state.busy=busy;$('sendButton').disabled=busy;$('promptInput').disabled=busy;$('refreshModels').disabled=busy;$('exportSelect').disabled=busy;$('sendButton').textContent=busy?'…':'Send'}
             function themeLabel(){return state.theme[0].toUpperCase()+state.theme.slice(1)}
             function applyTheme(){if(state.theme==='system')document.documentElement.removeAttribute('data-theme');else document.documentElement.setAttribute('data-theme',state.theme);$('themeButton').textContent='Theme: '+themeLabel()}
             function escapeTitleName(){return (state.title||'LocalLLM-conversation').replace(/[\\/:*?"<>|]+/g,'-').trim()||'LocalLLM-conversation'}
             function messageBody(message){if(message.role==='assistant'&&message.html!==undefined)return message.html;return escapeHtml(message.content).replace(/\r?\n/g,'<br>')}
             function transcriptHtml(){return state.messages.map(message=>'<article class="message '+message.role+'"><div class="message-meta"><strong>'+escapeHtml(message.role==='user'?'You':'Assistant')+'</strong>'+(message.provider&&message.model?'<span>'+escapeHtml(message.provider+' · '+message.model)+'</span>':'')+'<span>'+escapeHtml(formatTime(message.timestamp))+'</span></div><div class="message-content">'+messageBody(message)+'</div></article>').join('\n')}
             function buildStandaloneHtml(){
               const title=escapeHtml(state.title);const effective=state.theme==='system'?'system':state.theme;const css=$('themeCss').textContent;
               return '<!DOCTYPE html>\n<html lang="en" data-theme="'+effective+'"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>'+title+'</title><style>'+css+'\nbody{height:auto;overflow:visible}.export-document{max-width:940px;margin:0 auto;padding:28px 20px}.copy-code{display:block}.export-document .message{page-break-inside:avoid}</style></head><body><main class="export-document"><header><h1>'+title+'</h1><p>Exported from LocalLLM · '+escapeHtml(new Date().toLocaleString())+'</p></header>'+transcriptHtml()+'<footer class="export-footer">Standalone conversation export. This file has no live connection to LocalLLM, Ollama, or LM Studio.</footer></main><script>(function(){document.querySelectorAll("pre").forEach(function(pre){var button=document.createElement("button");button.type="button";button.className="copy-code";button.textContent="Copy code";button.onclick=function(){var code=pre.querySelector("code"),area=document.createElement("textarea");area.value=(code||pre).innerText;document.body.appendChild(area);area.select();try{document.execCommand("copy");button.textContent="Copied"}catch(e){button.textContent="Copy failed"}area.remove()};pre.insertBefore(button,pre.firstChild)})})();</scr'+'ipt></body></html>';
             }
             function buildPortableJson(){return JSON.stringify({schema:'localllm.conversation',version:1,title:state.title,createdAt:state.messages[0]?.timestamp||new Date().toISOString(),updatedAt:new Date().toISOString(),messages:state.messages.map(message=>({id:message.id,role:message.role,content:message.content,provider:message.provider||null,model:message.model||null,timestamp:message.timestamp}))},null,2)}
             function buildApiJson(){const lastAssistant=[...state.messages].reverse().find(message=>message.role==='assistant'&&message.model);return JSON.stringify({model:lastAssistant?.model||'',messages:state.messages.map(message=>({role:message.role,content:message.content})),stream:false},null,2)}
             function buildOpenWebUiJson(){
               const ids=state.messages.map(()=>makeId());const messages={};
               state.messages.forEach((message,index)=>{const item={id:ids[index],parentId:index===0?null:ids[index-1],childrenIds:index<ids.length-1?[ids[index+1]]:[],role:message.role,content:message.content,timestamp:Math.floor(new Date(message.timestamp).getTime()/1000)};if(message.role==='assistant'){item.model=message.model||undefined;item.done=true}messages[ids[index]]=item});
               const models=[...new Set(state.messages.filter(message=>message.model).map(message=>message.model))];
               const createdAt=state.messages.length?Math.floor(new Date(state.messages[0].timestamp).getTime()/1000):Math.floor(Date.now()/1000);const updatedAt=Math.floor(Date.now()/1000);
               return JSON.stringify([{chat:{title:state.title,models,history:{currentId:ids.length?ids[ids.length-1]:null,messages}},meta:{tags:[]},pinned:false,folder_id:null,created_at:createdAt,updated_at:updatedAt}],null,2)
             }
             function buildMarkdown(){return '# '+state.title+'\n\n'+state.messages.map(message=>'## '+(message.role==='user'?'You':'Assistant')+(message.provider&&message.model?' · '+message.provider+' · '+message.model:'')+'\n\n_'+formatTime(message.timestamp)+'_\n\n'+message.content).join('\n\n---\n\n')+'\n'}
             function buildText(){return state.messages.map(message=>'['+formatTime(message.timestamp)+'] '+(message.role==='user'?'You':'Assistant')+(message.provider&&message.model?' ('+message.provider+' · '+message.model+')':'')+'\n'+message.content).join('\n\n'+'='.repeat(72)+'\n\n')+'\n'}
             function csvCell(value){const text=String(value??'');const safe=/^[\u0000-\u0020]*[=+\-@]/.test(text)?"'"+text:text;return '"'+safe.replace(/"/g,'""')+'"'}
             function buildCsv(){return [['timestamp','role','provider','model','content'],...state.messages.map(message=>[message.timestamp,message.role,message.provider||'',message.model||'',message.content])].map(row=>row.map(csvCell).join(',')).join('\r\n')+'\r\n'}
             function buildJsonl(){return state.messages.map(message=>JSON.stringify({role:message.role,content:message.content,provider:message.provider||null,model:message.model||null,timestamp:message.timestamp})).join('\n')+'\n'}
             function doExport(format){if(!format)return;if(format==='pdf'){window.print();$('exportSelect').value='';return}let extension=format,content='';if(format==='html'){extension='html';content=buildStandaloneHtml()}else if(format==='openwebui'){extension='openwebui';content=buildOpenWebUiJson()}else if(format==='json'){content=buildPortableJson()}else if(format==='api'){content=buildApiJson()}else if(format==='md'){content=buildMarkdown()}else if(format==='csv'){content=buildCsv()}else if(format==='jsonl'){content=buildJsonl()}else{content=buildText();extension='txt'}post({type:'saveExport',format:extension,filename:escapeTitleName(),content});$('exportSelect').value=''}
             $('copyMarkdown').addEventListener('click',()=>post({type:'copy',text:buildMarkdown()}));$('sendButton').addEventListener('click',send);$('promptInput').addEventListener('keydown',event=>{if(event.key==='Enter'&&!event.shiftKey){event.preventDefault();send()}});$('refreshModels').addEventListener('click',()=>{showStatus('Refreshing both model lists…');post({type:'refreshModels'})});$('modelSelect').addEventListener('change',()=>{state.selected=getSelected()});$('exportSelect').addEventListener('change',event=>doExport(event.target.value));$('themeButton').addEventListener('click',()=>{state.theme=state.theme==='system'?'light':state.theme==='light'?'dark':'system';applyTheme()});
             window.chrome?.webview?.addEventListener('message',event=>{
               const data=event.data||{};
               if(data.type==='init'){state.title=data.title||state.title;$('conversationTitle').textContent=state.title;document.title=state.title;state.messages=data.messages||[];state.selected={provider:data.initialProvider||'Ollama',name:data.initialModel||''};rebuildModels();renderAll();showStatus(data.isError?'The original request returned an error; you can still start a follow-up.':'Conversation ready');$('promptInput').focus()}
               else if(data.type==='models'){state.models=(data.models||[]).sort((a,b)=>a.provider.localeCompare(b.provider)||a.name.localeCompare(b.name));state.ollamaError=data.ollamaError||null;state.lmStudioError=data.lmStudioError||null;const keep=currentKey(getSelected());rebuildModels(keep);if(state.ollamaError||state.lmStudioError){const notes=[];if(state.ollamaError)notes.push('Ollama: '+state.ollamaError);if(state.lmStudioError)notes.push('LM Studio: '+state.lmStudioError);showStatus('Some models could not be loaded. '+notes.join(' · '),true)}else showStatus('Models refreshed from Ollama and LM Studio.')}
               else if(data.type==='streamStart'){const message={id:data.id,role:'assistant',content:'',provider:data.provider,model:data.model,timestamp:new Date().toISOString(),html:''};state.messages.push(message);addMessageElement(message);$('messages').scrollTop=$('messages').scrollHeight}
               else if(data.type==='chunk'){const node=document.querySelector('[data-id="'+CSS.escape(data.id)+'"] .message-content');const message=state.messages.find(item=>item.id===data.id);if(node&&message){message.content+=data.text;node.textContent=message.content;node.style.whiteSpace='pre-wrap';$('messages').scrollTop=$('messages').scrollHeight}}
               else if(data.type==='complete'){const message=state.messages.find(item=>item.id===data.id);const node=document.querySelector('[data-id="'+CSS.escape(data.id)+'"] .message-content');if(message){message.content=data.content;message.html=data.html;message.provider=data.provider;message.model=data.model}if(node){node.style.whiteSpace='normal';node.innerHTML=data.html;const wrapper=node.closest('.message');if(wrapper){const meta=wrapper.querySelector('.message-meta');if(meta){meta.innerHTML='<strong>Assistant</strong><span>'+escapeHtml(data.provider+' · '+data.model)+'</span><span>'+escapeHtml(formatTime(message?.timestamp))+'</span>'}addCopyButtons(wrapper)}}setBusy(false);showStatus('Response complete.');$('promptInput').focus()}
               else if(data.type==='requestError'){setBusy(false);const messageNode=data.id?document.querySelector('[data-id="'+CSS.escape(data.id)+'"]'):null;if(messageNode)messageNode.remove();state.messages=state.messages.filter(item=>item.id!==data.id);const error=document.createElement('div');error.className='error';error.textContent=data.message||'The request failed.';$('messages').appendChild(error);$('messages').scrollTop=$('messages').scrollHeight;showStatus(data.message||'The request failed.',true)}
               else if(data.type==='notice'){showStatus(data.message||'Done.')}
             });
             document.addEventListener('click',event=>{const anchor=event.target.closest('a[href]');if(anchor){event.preventDefault();post({type:'openLink',url:anchor.href})}});
             applyTheme();if(window.chrome?.webview)post({type:'ready'});else showStatus('WebView2 bridge not available.',true);
            })();
            </script>
            </body>
            </html>
            """;
        }
    }
}
