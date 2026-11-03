#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Wox.Plugin;
using Clipboard = System.Windows.Clipboard;

namespace Community.PowerToys.Run.Plugin.LocalLLM;

/// <summary>
/// PowerToys Run plugin that sends the user's exact query to the selected local LLM provider.
/// No system prompt or sampling parameters are injected.
/// </summary>
public sealed class Main : IPlugin, IDelayedExecutionPlugin, ISettingProvider, IContextMenu
{
    public static string PluginID => "550A34D0CFA845449989D581149B3D9C";
    public string Name => "LocalLLM";
    public string Description => "Query a local language model using Ollama or LM Studio.";

    private const string DefaultEndpoint = "http://localhost:11434/api/generate";
    private const string DefaultLMStudioBaseUrl = "http://localhost:1234/v1";
    private const string DefaultModel = "qwen3:14b";
    private const string DefaultThinking = "auto";
    private const int DefaultKeepAliveSeconds = 300;
    private const int DefaultRequestTimeoutSeconds = 600;

    // The HttpClient is shared, but individual requests have their own configurable timeout.
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };
    private static readonly object ModelCacheLock = new();
    private static string? _modelCacheEndpoint;
    private static DateTimeOffset _modelCacheTimestamp;
    private static List<string> _modelCache = new();

    // Reuse WebView2 environments across chat windows to avoid profile/process races.
    private static readonly SemaphoreSlim WebView2EnvironmentLock = new(1, 1);
    private static CoreWebView2Environment? _localAppDataWebView2Environment;
    private static CoreWebView2Environment? _temporaryWebView2Environment;

    private readonly object _settingsLock = new();
    private PluginInitContext? _context;
    private string _iconPath = @"Images\icon.png";
    private string _endpoint = DefaultEndpoint;
    private string _lmStudioBaseUrl = DefaultLMStudioBaseUrl;
    private string _defaultModel = DefaultModel;
    private string _thinking = DefaultThinking;
    private int _provider;
    private int _keepAliveSeconds = DefaultKeepAliveSeconds;
    private int _requestTimeoutSeconds = DefaultRequestTimeoutSeconds;

    public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption>
    {
        new()
        {
            Key = "InlineProvider",
            DisplayLabel = "PowerToys Run LLM provider",
            DisplayDescription = "Provider used for inline PowerToys Run results. The standalone chat window has its own provider selector.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Combobox,
            ComboBoxItems = new List<KeyValuePair<string, string>>
            {
                new("Ollama", "0"),
                new("LM Studio", "1"),
            },
            ComboBoxValue = 0,
        },
        new()
        {
            Key = "LLMEndpoint",
            DisplayLabel = "Ollama API endpoint",
            DisplayDescription = "Full Ollama generate endpoint, for example http://localhost:11434/api/generate.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = DefaultEndpoint,
        },
        new()
        {
            Key = "LMStudioBaseUrl",
            DisplayLabel = "LM Studio base URL",
            DisplayDescription = "OpenAI-compatible LM Studio base URL used when LM Studio is selected for inline PowerToys Run, and by the WebView2 chat window, for example http://localhost:1234/v1.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = DefaultLMStudioBaseUrl,
        },
        new()
        {
            Key = "Model",
            DisplayLabel = "Default model",
            DisplayDescription = "Model identifier as shown by the selected provider (Ollama model name or LM Studio model ID). You can override it per query with > model > prompt.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = DefaultModel,
        },
        new()
        {
            Key = "Thinking",
            DisplayLabel = "Thinking / think",
            DisplayDescription = "Ollama only: use auto, true, false, or a model-specific value such as low. Auto uses low for gpt-oss, false for Qwen, and omits the field for other models.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = DefaultThinking,
        },
        new()
        {
            Key = "KeepAliveSeconds",
            DisplayLabel = "Keep model loaded (seconds)",
            DisplayDescription = "Ollama only: value sent as keep_alive. Use 0 to unload the model after the request. Range: 0–86400 seconds.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = DefaultKeepAliveSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        },
        new()
        {
            Key = "RequestTimeoutSeconds",
            DisplayLabel = "Request timeout (seconds)",
            DisplayDescription = "Maximum time to wait for an inline PowerToys Run generation request (30–3600 seconds). Does not affect the standalone chat window.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = DefaultRequestTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        },
    };

    public void Init(PluginInitContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _context.API.ThemeChanged += OnThemeChanged;
        UpdateIconPath(_context.API.GetCurrentTheme());
    }

    public void UpdateSettings(PowerLauncherPluginSettings settings)
    {
        string endpoint = DefaultEndpoint;
        string lmStudioBaseUrl = DefaultLMStudioBaseUrl;
        string model = DefaultModel;
        string thinking = DefaultThinking;
        int provider = 0;
        int keepAliveSeconds = DefaultKeepAliveSeconds;
        int requestTimeoutSeconds = DefaultRequestTimeoutSeconds;

        if (settings?.AdditionalOptions is not null)
        {
            var options = settings.AdditionalOptions;
            endpoint = GetTextOption(options, "LLMEndpoint", endpoint);
            lmStudioBaseUrl = GetTextOption(options, "LMStudioBaseUrl", lmStudioBaseUrl);
            provider = options.FirstOrDefault(option => option.Key == "InlineProvider")?.ComboBoxValue == 1 ? 1 : 0;
            model = GetTextOption(options, "Model", model);
            thinking = GetTextOption(options, "Thinking", thinking);

            if (int.TryParse(GetTextOption(options, "KeepAliveSeconds", DefaultKeepAliveSeconds.ToString()), out var parsedKeepAlive)
                && parsedKeepAlive is >= 0 and <= 86400)
            {
                keepAliveSeconds = parsedKeepAlive;
            }

            if (int.TryParse(GetTextOption(options, "RequestTimeoutSeconds", DefaultRequestTimeoutSeconds.ToString()), out var parsedTimeout)
                && parsedTimeout is >= 30 and <= 3600)
            {
                requestTimeoutSeconds = parsedTimeout;
            }
        }

        lock (_settingsLock)
        {
            _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint.Trim();
            _provider = provider;
            _lmStudioBaseUrl = string.IsNullOrWhiteSpace(lmStudioBaseUrl) ? DefaultLMStudioBaseUrl : lmStudioBaseUrl.Trim().TrimEnd('/');
            _defaultModel = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
            _thinking = string.IsNullOrWhiteSpace(thinking) ? DefaultThinking : thinking.Trim();
            _keepAliveSeconds = keepAliveSeconds;
            _requestTimeoutSeconds = requestTimeoutSeconds;
        }
    }

    // This plugin performs its work through delayed execution; don't call an LLM for
    // every keystroke in the immediate query path.
    public List<Result> Query(Query query) => new();

    public List<Result> Query(Query query, bool delayedExecution)
    {
        if (query is null || string.IsNullOrWhiteSpace(query.Search))
        {
            return new List<Result>();
        }

        var settings = GetSettingsSnapshot();
        var (model, prompt) = ParseModelAndPrompt(query.Search, settings.DefaultModel);

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new List<Result>
            {
                CreateResponseResult(
                    model,
                    "Enter a prompt after the LocalLLM keyword. To choose a model for one query, use: > model-name > your prompt.",
                    isError: true),
            };
        }

        try
        {
            var models = FetchModelsFromEndpointAsync(settings).GetAwaiter().GetResult();
            if (!models.Contains(model, StringComparer.OrdinalIgnoreCase))
            {
                var available = models.Count == 0
                    ? $"{GetProviderName(settings)} returned an empty model list."
                    : $"Available models: {string.Join(", ", models)}";

                return new List<Result>
                {
                    CreateResponseResult(
                        model,
                        $"Model '{model}' was not found. {available}",
                        isError: true,
                        prompt: prompt),
                };
            }

            var response = QueryLLMStreamAsync(prompt, model, settings).GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(response))
            {
                response = "The model completed the request but returned no text.";
            }

            return CreateResponseResults(model, response, isError: false, prompt: prompt);
        }
        catch (Exception ex)
        {
            var message = GetUsefulExceptionMessage(ex);
            return new List<Result>
            {
                CreateResponseResult(model, $"Request failed: {message}", isError: true, prompt: prompt),
            };
        }
    }

    /// <summary>
    /// Sends the query unchanged to the selected provider, using the configured default model.
    /// This overload is retained for callers that may use the original plugin method.
    /// </summary>
    public Task<string> QueryLLMStreamAsync(string input)
    {
        var settings = GetSettingsSnapshot();
        return QueryLLMStreamAsync(input, settings.DefaultModel, settings);
    }

    public async Task<bool> IsValidModelAsync(string model)
    {
        var settings = GetSettingsSnapshot();
        try
        {
            var models = await FetchModelsFromEndpointAsync(settings).ConfigureAwait(false);
            return models.Contains(model, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private Task<string> QueryLLMStreamAsync(
        string input,
        string model,
        SettingsSnapshot settings)
    {
        return settings.Provider == 1
            ? QueryLMStudioStreamAsync(input, model, settings)
            : QueryOllamaStreamAsync(input, model, settings);
    }

    private async Task<string> QueryOllamaStreamAsync(
        string input,
        string model,
        SettingsSnapshot settings)
    {
        var endpoint = ValidateGenerateEndpoint(settings.Endpoint);
        var requestBody = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["model"] = model,
            // Deliberately pass the user's prompt directly: no hidden/system prompt is added.
            ["prompt"] = input,
            ["stream"] = true,
            ["keep_alive"] = settings.KeepAliveSeconds,
        };

        if (TryResolveThinkValue(model, settings.Thinking, out var thinkValue))
        {
            requestBody["think"] = thinkValue;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var connectionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, connectionTimeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, sendTimeout.Token).ConfigureAwait(false);
        connectionTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {TrimForError(body)}";
            throw new HttpRequestException($"Ollama returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{detail}", null, response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        var output = new StringBuilder();
        var recordNumber = 0;
        while (true)
        {
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            recordNumber++;
            JsonDocument json;
            try { json = JsonDocument.Parse(line); }
            catch (JsonException ex) { throw new InvalidDataException($"Ollama returned malformed NDJSON at record {recordNumber}: {ex.Message}", ex); }
            using (json)
            {
                var root = json.RootElement;
                if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(errorElement.GetString()))
                    throw new InvalidOperationException($"Ollama: {errorElement.GetString()}");
                if (root.TryGetProperty("response", out var chunk) && chunk.ValueKind == JsonValueKind.String)
                    output.Append(chunk.GetString());
                if (root.TryGetProperty("done", out var doneElement) && doneElement.ValueKind == JsonValueKind.True)
                    break;
            }
        }
        return output.ToString();
    }

    private async Task<string> QueryLMStudioStreamAsync(string input, string model, SettingsSnapshot settings)
    {
        var endpoint = BuildLMStudioUri(settings.LMStudioBaseUrl, "chat/completions");
        var requestBody = new
        {
            model,
            messages = new[] { new { role = "user", content = input } },
            stream = true,
        };

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var connectionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, connectionTimeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, sendTimeout.Token).ConfigureAwait(false);
        connectionTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {TrimForError(body)}";
            throw new HttpRequestException($"LM Studio returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{detail}", null, response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        var output = new StringBuilder();
        var recordNumber = 0;
        while (true)
        {
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].Trim();
            if (data.Length == 0) continue;
            if (data.Equals("[DONE]", StringComparison.OrdinalIgnoreCase)) break;
            recordNumber++;
            JsonDocument json;
            try { json = JsonDocument.Parse(data); }
            catch (JsonException ex) { throw new InvalidDataException($"LM Studio returned malformed SSE JSON at event {recordNumber}: {ex.Message}", ex); }
            using (json)
            {
                var root = json.RootElement;
                if (root.TryGetProperty("error", out var errorElement))
                    throw new InvalidOperationException($"LM Studio: {(errorElement.ValueKind == JsonValueKind.String ? errorElement.GetString() : errorElement.ToString())}");
                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object
                        && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                        output.Append(content.GetString());
                    else if (choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
                        && message.TryGetProperty("content", out content) && content.ValueKind == JsonValueKind.String)
                        output.Append(content.GetString());
                }
            }
        }
        return output.ToString();
    }

    private async Task<List<string>> FetchModelsFromEndpointAsync(SettingsSnapshot settings)
    {
        var modelsUri = settings.Provider == 1
            ? BuildLMStudioUri(settings.LMStudioBaseUrl, "models")
            : BuildTagsUri(settings.Endpoint);
        var cacheKey = modelsUri.AbsoluteUri;
        lock (ModelCacheLock)
        {
            if (string.Equals(_modelCacheEndpoint, cacheKey, StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.UtcNow - _modelCacheTimestamp < TimeSpan.FromSeconds(5))
                return new List<string>(_modelCache);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var response = await HttpClient.GetAsync(modelsUri, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {TrimForError(body)}";
            throw new HttpRequestException($"Could not query the {GetProviderName(settings)} model list (HTTP {(int)response.StatusCode} {response.ReasonPhrase}).{detail}", null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        var modelNames = new List<string>();
        var arrayName = settings.Provider == 1 ? "data" : "models";
        if (!json.RootElement.TryGetProperty(arrayName, out var modelsElement) || modelsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"The {GetProviderName(settings)} model-list response did not contain a '{arrayName}' array. Check the configured API URL.");

        foreach (var model in modelsElement.EnumerateArray())
        {
            var nameProperty = settings.Provider == 1 ? "id" : "name";
            if (model.TryGetProperty(nameProperty, out var nameElement) && nameElement.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(nameElement.GetString()))
                modelNames.Add(nameElement.GetString()!);
        }
        lock (ModelCacheLock)
        {
            _modelCacheEndpoint = cacheKey;
            _modelCacheTimestamp = DateTimeOffset.UtcNow;
            _modelCache = new List<string>(modelNames);
        }
        return modelNames;
    }

    private static string GetProviderName(SettingsSnapshot settings) => settings.Provider == 1 ? "LM Studio" : "Ollama";

    private static Uri BuildLMStudioUri(string baseUrl, string relativePath)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(baseUri.Host))
            throw new InvalidOperationException("The LM Studio base URL must be an absolute HTTP or HTTPS URL, for example http://localhost:1234/v1.");
        return new Uri(baseUrl.TrimEnd('/') + "/" + relativePath.TrimStart('/'), UriKind.Absolute);
    }

    private static bool TryResolveThinkValue(string model, string? thinkingSetting, out object value)
    {
        var setting = thinkingSetting?.Trim();
        if (string.IsNullOrWhiteSpace(setting) || setting.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            if (model.StartsWith("gpt-oss", StringComparison.OrdinalIgnoreCase))
            {
                value = "low";
                return true;
            }

            if (model.StartsWith("qwen", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }

            // Don't send an unnecessary/possibly unsupported field to other models.
            value = false;
            return false;
        }

        if (bool.TryParse(setting, out var booleanValue))
        {
            value = booleanValue;
            return true;
        }

        value = setting;
        return true;
    }

    private List<Result> CreateResponseResults(string model, string response, bool isError, string prompt = "")
    {
        var results = new List<Result>();
        var fullResponse = response ?? string.Empty;

        // Inline launcher results keep their existing preview behavior. Selecting any
        // section opens a new independent chat session seeded with the complete prompt
        // and initial response.
        var sections = SplitResponseForInlineReading(fullResponse, 420);
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var preview = CreatePreview(section);
            var result = new Result
            {
                SubTitle = preview,
                IcoPath = _iconPath,
                ContextData = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["copy"] = fullResponse,
                    ["model"] = model,
                    ["prompt"] = prompt ?? string.Empty,
                    ["isError"] = isError.ToString(),
                },
                Action = _ =>
                {
                    try
                    {
                        ShowNativeResponseWindow(
                            $"LocalLLM · {model}",
                            model,
                            prompt,
                            fullResponse,
                            isError);
                    }
                    catch (Exception ex)
                    {
                        _context?.API.ShowMsg(
                            "Could not display the LLM response",
                            ex.Message);
                    }

                    return true;
                },
            };

            // results.Add();

            if (i == 0)
            {
                result.Title = $"LocalLLM · {model}:";
            }

            results.Add(result);
        }

        return results;
    }

    private Result CreateResponseResult(string model, string response, bool isError, string prompt = "")
    {
        var title = isError ? $"LocalLLM · {model} · Request failed" : $"LocalLLM · {model}";
        var preview = CreatePreview(response);

        return new Result
        {
            Title = title,
            SubTitle = preview,
            IcoPath = _iconPath,
            ContextData = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["copy"] = response,
                ["model"] = model,
                ["prompt"] = prompt ?? string.Empty,
                ["isError"] = isError.ToString(),
            },
            Action = _ =>
            {
                try
                {
                    ShowNativeResponseWindow(title, model, prompt, response, isError);
                }
                catch (Exception ex)
                {
                    _context?.API.ShowMsg("Could not display the LLM response", ex.Message);
                }

                return true;
            },
        };
    }

    private static List<string> SplitResponseForInlineReading(string response, int maxChunkLength)
    {
        var chunks = new List<string>();
        if (string.IsNullOrEmpty(response))
        {
            chunks.Add("(The model returned an empty response.)");
            return chunks;
        }

        // Preserve the user's existing compact multi-result UX. Split at natural line or
        // word boundaries when possible, but never trim either side: concatenating the
        // sections must reproduce the original response byte-for-byte at the string level.
        maxChunkLength = Math.Max(80, maxChunkLength);
        var start = 0;
        while (start < response.Length)
        {
            var remaining = response.Length - start;
            if (remaining <= maxChunkLength)
            {
                chunks.Add(response[start..]);
                break;
            }

            var hardEnd = start + maxChunkLength;
            var newline = response.LastIndexOf('\n', hardEnd - 1, maxChunkLength);
            var space = response.LastIndexOf(' ', hardEnd - 1, maxChunkLength);
            var minimumUsefulBoundary = start + Math.Max(40, maxChunkLength / 3);
            var end = newline >= minimumUsefulBoundary
                ? newline + 1
                : space >= minimumUsefulBoundary
                    ? space + 1
                    : hardEnd;

            // Guard against a pathological boundary result; every iteration must advance.
            if (end <= start || end > hardEnd)
            {
                end = hardEnd;
            }

            // Avoid cutting a UTF-16 surrogate pair between adjacent sections.
            if (end < response.Length && end > start
                && char.IsHighSurrogate(response[end - 1])
                && char.IsLowSurrogate(response[end]))
            {
                end--;
            }

            chunks.Add(response[start..end]);
            start = end;
        }

        return chunks;
    }

    public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
    {
        var results = new List<ContextMenuResult>();
        if (selectedResult?.ContextData is not Dictionary<string, string> data
            || !data.TryGetValue("copy", out var fullResponse))
        {
            return results;
        }

        var section = data.TryGetValue("copySection", out var sectionValue) ? sectionValue : null;
        results.Add(new ContextMenuResult
        {
            PluginName = Name,
            Title = section is null ? "Copy response (Markdown)" : "Copy this section",
            FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
            Glyph = "\xE8C8",
            AcceleratorKey = Key.Enter,
            Action = _ =>
            {
                try
                {
                    Clipboard.SetText(section ?? fullResponse);
                }
                catch (Exception ex)
                {
                    _context?.API.ShowMsg("Could not copy response", ex.Message);
                }

                return true;
            },
        });

        if (section is not null)
        {
            results.Add(new ContextMenuResult
            {
                PluginName = Name,
                Title = "Copy full response (Markdown)",
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                Glyph = "\xE8C8",
                Action = _ =>
                {
                    try
                    {
                        Clipboard.SetText(fullResponse);
                    }
                    catch (Exception ex)
                    {
                        _context?.API.ShowMsg("Could not copy response", ex.Message);
                    }

                    return true;
                },
            });
        }

        return results;
    }

    public Control CreateSettingPanel()
    {
        // All settings are supplied through AdditionalOptions; a custom WPF panel is not used.
        throw new NotImplementedException("LocalLLM uses PowerToys Run's AdditionalOptions settings UI.");
    }

    private void OnThemeChanged(Theme currentTheme, Theme newTheme) => UpdateIconPath(newTheme);

    private void UpdateIconPath(Theme theme)
    {
        var context = _context;
        if (context is null)
        {
            return;
        }

        var candidate = theme == Theme.Light || theme == Theme.HighContrastWhite
            ? context.CurrentPluginMetadata.IcoPathLight
            : context.CurrentPluginMetadata.IcoPathDark;

        if (!string.IsNullOrWhiteSpace(candidate))
        {
            _iconPath = candidate;
        }
    }

    private SettingsSnapshot GetSettingsSnapshot()
    {
        lock (_settingsLock)
        {
            return new SettingsSnapshot(
                _endpoint,
                _lmStudioBaseUrl,
                _provider,
                _defaultModel,
                _thinking,
                _keepAliveSeconds,
                _requestTimeoutSeconds);
        }
    }

    private static string GetTextOption(
        IEnumerable<PluginAdditionalOption> options,
        string key,
        string fallback)
    {
        var value = options.FirstOrDefault(option => option.Key == key)?.TextValue;
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static (string Model, string Prompt) ParseModelAndPrompt(string search, string defaultModel)
    {
        var trimmed = search.Trim();
        if (!trimmed.StartsWith(">", StringComparison.Ordinal))
        {
            return (defaultModel, trimmed);
        }

        // Format: > model-name > prompt. Split only twice so '>' inside the prompt survives.
        var segments = trimmed.Split('>', 3, StringSplitOptions.TrimEntries);
        if (segments.Length == 3
            && string.IsNullOrWhiteSpace(segments[0])
            && !string.IsNullOrWhiteSpace(segments[1])
            && !string.IsNullOrWhiteSpace(segments[2]))
        {
            return (segments[1], segments[2]);
        }

        // If the override syntax is incomplete, don't silently discard user input.
        return (defaultModel, trimmed);
    }

    private static Uri ValidateGenerateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("The Ollama endpoint must be an absolute HTTP or HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException("The Ollama endpoint must include a hostname.");
        }

        return uri;
    }

    private static Uri BuildTagsUri(string endpoint)
    {
        var generateUri = ValidateGenerateEndpoint(endpoint);
        var builder = new UriBuilder(generateUri);
        var path = builder.Path.TrimEnd('/');
        var lastSlash = path.LastIndexOf('/');
        var lastSegment = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;

        if (!lastSegment.Equals("generate", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Model discovery expects the endpoint path to end with /api/generate. " +
                "Set the Ollama API endpoint to a URL such as http://localhost:11434/api/generate.");
        }

        builder.Path = (lastSlash >= 0 ? path[..(lastSlash + 1)] : string.Empty) + "tags";
        builder.Query = string.Empty;
        builder.Fragment = string.Empty;
        return builder.Uri;
    }

    private static string CreatePreview(string text)
    {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return System.Text.RegularExpressions.Regex
                .Replace(text.Trim(), @"\r?\n\s*\r?\n+", "\n");
        // var preview = $"<html><head></head><body><p style=\"margin:0\">{text}</p></body></html>";

        // return preview;
        // var   preview = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // const int maxLength = 155;
        // if (preview.Length > maxLength)
        // {
        //     preview = preview[..(maxLength - 1)] + "…";
        // }

        // return preview;
    }

    private static string GetUsefulExceptionMessage(Exception exception)
    {
        var exceptionChain = new List<Exception>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            exceptionChain.Add(current);
        }

        if (exceptionChain.Any(item => item is OperationCanceledException))
        {
            return "The request timed out. Increase Request timeout (seconds) in the plugin settings or use a smaller/faster model.";
        }

        var diagnosticText = string.Join(" ", exceptionChain.Select(item => item.Message));
        if (diagnosticText.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
            || diagnosticText.Contains("connection refused", StringComparison.OrdinalIgnoreCase))
        {
            return "The connection was refused. Check that the selected provider is running and its API URL is correct.";
        }

        return exceptionChain.LastOrDefault()?.Message ?? exception.Message;
    }

    private static string TrimForError(string text)
    {
        var singleLine = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        const int maxLength = 500;
        return singleLine.Length <= maxLength ? singleLine : singleLine[..(maxLength - 1)] + "…";
    }

    private void ShowNativeResponseWindow(string title, string model, string? prompt, string response, bool isError)
    {
        // Some PowerToys SDK versions annotate query text as nullable. Keep the original
        // prompt whenever available, but never pass null into the bootstrap payload.
        var safePrompt = prompt ?? string.Empty;
        var settings = GetSettingsSnapshot();
        var navigation = BuildChatNavigation(title, model, safePrompt, response, isError, settings);
        var webView = CreateChatWebView();

        var appWindow = new Window
        {
            Title = title,
            Width = 1120,
            Height = 860,
            MinWidth = 680,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = webView,
        };

        appWindow.Show();
        appWindow.Activate();
        _ = InitializeChatWebViewAsync(appWindow, webView, navigation.FolderPath, navigation.BootstrapJson);
    }

    private async Task InitializeChatWebViewAsync(
        Window appWindow,
        WebView2 webView,
        string appFolderPath,
        string bootstrapJson,
        bool useTemporaryUserDataFolder = false,
        Exception? primaryInitializationException = null)
    {
        string userDataFolder = "(not resolved)";
        try
        {
            // Never let WebView2 infer a profile next to the PowerToys host executable.
            // PowerToys commonly runs from a protected install location, which can cause
            // EnsureCoreWebView2Async() to fail with E_ACCESSDENIED.
            userDataFolder = GetWebView2UserDataFolder(useTemporaryUserDataFolder);
            var environment = await GetWebView2EnvironmentAsync(useTemporaryUserDataFolder, userDataFolder);
            await webView.EnsureCoreWebView2Async(environment);

            var core = webView.CoreWebView2;
            // Give the static page a stable localhost origin so browser CORS policies can
            // recognize the page. API requests are still issued directly by JavaScript.
            core.SetVirtualHostNameToFolderMapping(
                "localhost",
                appFolderPath,
                CoreWebView2HostResourceAccessKind.DenyCors);

            // The page is intentionally standalone: no WebMessageReceived handler, host object,
            // or request/response bridge is registered with the plugin.
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
                {
                    return;
                }

                if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    && uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    && uri.AbsolutePath.Equals("/ChatWindow.html", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    || uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                    try
                    {
                        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        _context?.API.ShowMsg("Could not open link", ex.Message);
                    }
                }
            };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
                {
                    return;
                }

                if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                    || uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        _context?.API.ShowMsg("Could not open link", ex.Message);
                    }
                }
            };
            core.NavigationCompleted += async (_, e) =>
            {
                if (!e.IsSuccess)
                {
                    appWindow.Content = new TextBlock
                    {
                        Margin = new Thickness(24),
                        TextWrapping = TextWrapping.Wrap,
                        Text = "The LocalLLM chat page could not be loaded. Check that ChatWindow.html is deployed beside the plugin DLL.",
                    };
                    return;
                }

                try
                {
                    // This one-way call supplies only the original query/result and settings.
                    // All model discovery and subsequent chat turns stay in page JavaScript.
                    await core.ExecuteScriptAsync("window.initializeLocalLLM(" + bootstrapJson + ");");
                }
                catch (Exception ex)
                {
                    _context?.API.ShowMsg("Could not initialize the LocalLLM chat session", ex.Message);
                }
            };
            core.Navigate("https://localhost/ChatWindow.html");
        }
        catch (Exception ex) when (!useTemporaryUserDataFolder && IsAccessDeniedException(ex))
        {
            // If the user's LocalAppData profile is redirected or restricted, retry using a
            // fresh control and a per-user temporary directory. Retrying with a fresh control
            // avoids reusing a WebView2 control whose initialization already failed.
            var fallbackWebView = CreateChatWebView();
            appWindow.Content = fallbackWebView;
            await InitializeChatWebViewAsync(
                appWindow,
                fallbackWebView,
                appFolderPath,
                bootstrapJson,
                useTemporaryUserDataFolder: true,
                primaryInitializationException: ex);
        }
        catch (Exception ex)
        {
            var priorFailure = primaryInitializationException is null
                ? string.Empty
                : $"\nPrimary profile attempt also failed: {primaryInitializationException.Message}";
            var message = "The LocalLLM chat window could not initialize WebView2. "
                + ex.Message
                + $"\nUser-data folder: {userDataFolder}"
                + priorFailure
                + "\nCheck that Microsoft Edge WebView2 Runtime is installed and that this folder is writable.";
            appWindow.Content = new TextBlock
            {
                Margin = new Thickness(24),
                TextWrapping = TextWrapping.Wrap,
                Text = message,
            };
            _context?.API.ShowMsg("Could not initialize the LocalLLM chat window", message);
        }
    }

    private static WebView2 CreateChatWebView() => new()
    {
        DefaultBackgroundColor = System.Drawing.Color.FromArgb(17, 19, 24),
        AllowExternalDrop = false,
    };

    private static async Task<CoreWebView2Environment> GetWebView2EnvironmentAsync(
        bool useTemporaryFolder,
        string userDataFolder)
    {
        await WebView2EnvironmentLock.WaitAsync();
        try
        {
            if (useTemporaryFolder && _temporaryWebView2Environment is not null)
            {
                return _temporaryWebView2Environment;
            }

            if (!useTemporaryFolder && _localAppDataWebView2Environment is not null)
            {
                return _localAppDataWebView2Environment;
            }

            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);

            if (useTemporaryFolder)
            {
                _temporaryWebView2Environment = environment;
            }
            else
            {
                _localAppDataWebView2Environment = environment;
            }

            return environment;
        }
        finally
        {
            WebView2EnvironmentLock.Release();
        }
    }

    private static string GetWebView2UserDataFolder(bool useTemporaryFolder)
    {
        if (useTemporaryFolder)
        {
            return Path.Combine(Path.GetTempPath(), "PowerToys-Run-LocalLLM", "WebView2");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("Windows did not provide a Local AppData path for the current user.");
        }

        return Path.Combine(localAppData, "Microsoft", "PowerToys", "LocalLLM", "WebView2");
    }

    private static bool IsAccessDeniedException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException
                || current.HResult == unchecked((int)0x80070005)) // E_ACCESSDENIED
            {
                return true;
            }
        }

        return false;
    }

    private static (string FolderPath, string BootstrapJson) BuildChatNavigation(
        string title,
        string model,
        string prompt,
        string response,
        bool isError,
        SettingsSnapshot settings)
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(Main).Assembly.Location)
            ?? AppContext.BaseDirectory;
        var htmlPath = Path.Combine(assemblyDirectory, "ChatWindow.html");
        if (!File.Exists(htmlPath))
        {
            throw new FileNotFoundException(
                "ChatWindow.html was not found beside the LocalLLM plugin assembly. Ensure the file is copied to the plugin output directory.",
                htmlPath);
        }

        var thinkInitiallyEnabled = TryResolveThinkValue(model, settings.Thinking, out var thinkValue)
            && (thinkValue is bool boolValue
                ? boolValue
                : !string.Equals(Convert.ToString(thinkValue, System.Globalization.CultureInfo.InvariantCulture), "false", StringComparison.OrdinalIgnoreCase));
        var bootstrap = new
        {
            title,
            initialModel = model,
            prompt = prompt ?? string.Empty,
            response = response ?? string.Empty,
            isError,
            ollamaBaseUrl = BuildOllamaBaseUrl(settings.Endpoint),
            lmStudioBaseUrl = RewriteLocalhostToLoopback(settings.LMStudioBaseUrl),
            keepAliveSeconds = settings.KeepAliveSeconds,
            initialThink = thinkInitiallyEnabled,
        };
        var serializedBootstrap = JsonSerializer.Serialize(bootstrap);
        return (assemblyDirectory, serializedBootstrap);
    }

    private static string RewriteLocalhostToLoopback(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return baseUrl;
        }

        var builder = new UriBuilder(uri);
        if (builder.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            // The WebView2 app itself is mapped at https://localhost; use the equivalent
            // loopback IP for provider calls so they cannot collide with the mapped app host.
            builder.Host = "127.0.0.1";
        }

        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string BuildOllamaBaseUrl(string endpoint)
    {
        // A malformed endpoint must not prevent an error window from retaining the original
        // prompt. The inline request will already have surfaced the relevant endpoint error;
        // use the standard local Ollama address as a recoverable chat-window fallback.
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var generateUri)
            || (generateUri.Scheme != Uri.UriSchemeHttp && generateUri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(generateUri.Host))
        {
            generateUri = new Uri(DefaultEndpoint, UriKind.Absolute);
        }

        var builder = new UriBuilder(generateUri);
        var path = builder.Path.TrimEnd('/');
        var lastSlash = path.LastIndexOf('/');
        var lastSegment = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
        if (!lastSegment.Equals("generate", StringComparison.OrdinalIgnoreCase))
        {
            builder = new UriBuilder(new Uri(DefaultEndpoint, UriKind.Absolute));
            path = builder.Path.TrimEnd('/');
            lastSlash = path.LastIndexOf('/');
        }

        var prefix = lastSlash >= 0 ? path[..(lastSlash + 1)] : string.Empty;
        var apiIndex = prefix.LastIndexOf("/api/", StringComparison.OrdinalIgnoreCase);
        if (apiIndex >= 0)
        {
            prefix = prefix[..(apiIndex + 1)];
        }
        else
        {
            prefix = prefix.TrimEnd('/');
        }

        builder.Path = prefix.TrimEnd('/');
        builder.Query = string.Empty;
        builder.Fragment = string.Empty;
        if (builder.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            builder.Host = "127.0.0.1";
        }

        return builder.Uri.ToString().TrimEnd('/');
    }

    private sealed record SettingsSnapshot(
        string Endpoint,
        string LMStudioBaseUrl,
        int Provider,
        string DefaultModel,
        string Thinking,
        int KeepAliveSeconds,
        int RequestTimeoutSeconds);
}
