#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HtmlAgilityPack;
using ManagedCommon;
using Markdig;
using Microsoft.PowerToys.Settings.UI.Library;
using Wox.Plugin;
using Clipboard = System.Windows.Clipboard;

namespace Community.PowerToys.Run.Plugin.LocalLLM;

/// <summary>
/// PowerToys Run plugin that sends the user's exact query to an Ollama-compatible
/// /api/generate endpoint. No system prompt or sampling parameters are injected.
/// </summary>
public sealed class Main : IPlugin, IDelayedExecutionPlugin, ISettingProvider, IContextMenu
{
    public static string PluginID => "550A34D0CFA845449989D581149B3D9C";
    public string Name => "LocalLLM";
    public string Description => "Query an Ollama-compatible local language model.";

    private const string DefaultEndpoint = "http://localhost:11434/api/generate";
    private const string DefaultModel = "qwen3:14b";
    private const string DefaultThinking = "auto";
    private const int DefaultKeepAliveSeconds = 300;
    private const int DefaultRequestTimeoutSeconds = 600;

    // The HttpClient is shared, but individual requests have their own configurable timeout.
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly object _settingsLock = new();
    private PluginInitContext? _context;
    private string _iconPath = @"Images\icon.png";
    private string _endpoint = DefaultEndpoint;
    private string _defaultModel = DefaultModel;
    private string _thinking = DefaultThinking;
    private int _keepAliveSeconds = DefaultKeepAliveSeconds;
    private int _requestTimeoutSeconds = DefaultRequestTimeoutSeconds;

    public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption>
    {
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
            Key = "Model",
            DisplayLabel = "Default model",
            DisplayDescription = "Model name as shown by Ollama (for example qwen3:14b). You can override it per query with > model > prompt.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = DefaultModel,
        },
        new()
        {
            Key = "Thinking",
            DisplayLabel = "Thinking / think",
            DisplayDescription = "Use auto, true, false, or a model-specific value such as low. Auto uses low for gpt-oss, false for Qwen, and omits the field for other models.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = DefaultThinking,
        },
        new()
        {
            Key = "KeepAliveSeconds",
            DisplayLabel = "Keep model loaded (seconds)",
            DisplayDescription = "Value sent as Ollama keep_alive. Use 0 to unload the model after the request. Range: 0–86400 seconds.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = DefaultKeepAliveSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        },
        new()
        {
            Key = "RequestTimeoutSeconds",
            DisplayLabel = "Request timeout (seconds)",
            DisplayDescription = "Maximum time for model discovery or generation. Range: 30–3600 seconds.",
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
        string model = DefaultModel;
        string thinking = DefaultThinking;
        int keepAliveSeconds = DefaultKeepAliveSeconds;
        int requestTimeoutSeconds = DefaultRequestTimeoutSeconds;

        if (settings?.AdditionalOptions is not null)
        {
            var options = settings.AdditionalOptions;
            endpoint = GetTextOption(options, "LLMEndpoint", endpoint);
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
                    ? "Ollama returned an empty model list."
                    : $"Available models: {string.Join(", ", models)}";

                return new List<Result>
                {
                    CreateResponseResult(
                        model,
                        $"Model '{model}' was not found. {available}",
                        isError: true),
                };
            }

            var response = QueryLLMStreamAsync(prompt, model, settings).GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(response))
            {
                response = "The model completed the request but returned no text.";
            }

            return new List<Result> { CreateResponseResult(model, response, isError: false) };
        }
        catch (Exception ex)
        {
            var message = GetUsefulExceptionMessage(ex);
            return new List<Result>
            {
                CreateResponseResult(model, $"Request failed: {message}", isError: true),
            };
        }
    }

    /// <summary>
    /// Sends the query unchanged to Ollama, using the configured default model.
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

    private async Task<string> QueryLLMStreamAsync(
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
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"),
        };

        using var response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {TrimForError(body)}";
            throw new HttpRequestException(
                $"Ollama returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{detail}",
                null,
                response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        var output = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;

            if (root.TryGetProperty("error", out var errorElement)
                && errorElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(errorElement.GetString()))
            {
                throw new InvalidOperationException($"Ollama: {errorElement.GetString()}");
            }

            if (root.TryGetProperty("response", out var chunk)
                && chunk.ValueKind == JsonValueKind.String)
            {
                output.Append(chunk.GetString());
            }

            if (root.TryGetProperty("done", out var doneElement)
                && doneElement.ValueKind == JsonValueKind.True)
            {
                break;
            }
        }

        return output.ToString();
    }

    private async Task<List<string>> FetchModelsFromEndpointAsync(SettingsSnapshot settings)
    {
        var tagsEndpoint = BuildTagsUri(settings.Endpoint);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var response = await HttpClient.GetAsync(tagsEndpoint, timeout.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {TrimForError(body)}";
            throw new HttpRequestException(
                $"Could not query the Ollama model list (HTTP {(int)response.StatusCode} {response.ReasonPhrase}).{detail}",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);

        if (!json.RootElement.TryGetProperty("models", out var modelsElement)
            || modelsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The model-list response did not contain a 'models' array. Check that the endpoint is an Ollama API endpoint.");
        }

        var modelNames = new List<string>();
        foreach (var model in modelsElement.EnumerateArray())
        {
            if (model.TryGetProperty("name", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(nameElement.GetString()))
            {
                modelNames.Add(nameElement.GetString()!);
            }
        }

        return modelNames;
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

    private Result CreateResponseResult(string model, string response, bool isError)
    {
        var title = isError ? $"LocalLLM · {model} · Request failed" : $"LocalLLM · {model}";
        var preview = CreatePreview(response);
        var subtitle = isError
            ? preview
            : $"{preview}  ·  Enter to read  ·  Right-click to copy";

        return new Result
        {
            Title = title,
            SubTitle = subtitle,
            IcoPath = _iconPath,
            ContextData = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["copy"] = response,
                ["model"] = model,
            },
            Action = _ =>
            {
                try
                {
                    OpenResponseInBrowser(title, model, response, isError);
                }
                catch (Exception ex)
                {
                    _context?.API.ShowMsg("Could not open the LLM response", ex.Message);
                }

                return true;
            },
        };
    }

    public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
    {
        var results = new List<ContextMenuResult>();
        if (selectedResult?.ContextData is not Dictionary<string, string> data
            || !data.TryGetValue("copy", out var textToCopy))
        {
            return results;
        }

        results.Add(new ContextMenuResult
        {
            PluginName = Name,
            Title = "Copy response (Markdown)",
            FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
            Glyph = "\xE8C8",
            AcceleratorKey = Key.Enter,
            Action = _ =>
            {
                try
                {
                    Clipboard.SetText(textToCopy ?? string.Empty);
                }
                catch (Exception ex)
                {
                    _context?.API.ShowMsg("Could not copy response", ex.Message);
                }

                return true;
            },
        });

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
        var preview = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        const int maxLength = 155;
        if (preview.Length > maxLength)
        {
            preview = preview[..(maxLength - 1)] + "…";
        }

        return preview;
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
            return "The connection was refused. Check that Ollama is running and the endpoint URL is correct.";
        }

        return exceptionChain.LastOrDefault()?.Message ?? exception.Message;
    }

    private static string TrimForError(string text)
    {
        var singleLine = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        const int maxLength = 500;
        return singleLine.Length <= maxLength ? singleLine : singleLine[..(maxLength - 1)] + "…";
    }

    private static void OpenResponseInBrowser(string title, string model, string response, bool isError)
    {
        var markdownPipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .DisableHtml()
            .Build();

        var markdownHtml = Markdown.ToHtml(response ?? string.Empty, markdownPipeline);
        var document = new HtmlDocument();
        document.LoadHtml(markdownHtml);
        SanitizeMarkdownHtml(document);
        AddCodeCopyButtons(document);

        var safeTitle = WebUtility.HtmlEncode(title);
        var safeModel = WebUtility.HtmlEncode(model);
        var safeRawResponse = WebUtility.HtmlEncode(response ?? string.Empty);
        var safeBody = document.DocumentNode.InnerHtml;
        var stateLabel = isError ? "REQUEST DETAILS" : "MODEL RESPONSE";
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <meta name="color-scheme" content="light dark">
                <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src 'none'; connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'">
                <title>{{safeTitle}}</title>
                <style>
                    :root {
                        color-scheme: light dark;
                        --page: #f5f6f8;
                        --surface: #ffffff;
                        --surface-soft: #f0f2f5;
                        --text: #20242b;
                        --muted: #687180;
                        --border: #dce1e8;
                        --accent: #4263eb;
                        --accent-soft: #e8edff;
                        --code: #171b24;
                        --code-text: #e8edf5;
                        --quote: #8995a7;
                        --shadow: 0 12px 36px rgba(25, 35, 55, .07);
                    }
                    @media (prefers-color-scheme: dark) {
                        :root {
                            --page: #111318;
                            --surface: #191c23;
                            --surface-soft: #222630;
                            --text: #e8eaf0;
                            --muted: #a0a8b7;
                            --border: #303542;
                            --accent: #9aafff;
                            --accent-soft: #252d49;
                            --code: #0d0f14;
                            --code-text: #e8edf5;
                            --quote: #7a879b;
                            --shadow: 0 12px 36px rgba(0, 0, 0, .18);
                        }
                    }
                    * { box-sizing: border-box; }
                    html { min-height: 100%; scroll-behavior: smooth; }
                    body {
                        margin: 0;
                        min-height: 100vh;
                        overflow-wrap: anywhere;
                        background: var(--page);
                        color: var(--text);
                        font-family: "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
                        font-size: 16px;
                        line-height: 1.72;
                        -webkit-font-smoothing: antialiased;
                    }
                    .shell { width: min(100%, 980px); margin: 0 auto; padding: 28px clamp(16px, 4vw, 42px) 56px; }
                    .topbar { display: flex; align-items: center; justify-content: space-between; gap: 16px; padding-bottom: 22px; border-bottom: 1px solid var(--border); }
                    .identity { display: flex; align-items: center; gap: 12px; min-width: 0; }
                    .mark { display: grid; place-items: center; flex: 0 0 38px; width: 38px; height: 38px; border-radius: 13px; color: var(--accent); background: var(--accent-soft); font-size: 19px; font-weight: 750; }
                    .brand { font-size: 13px; font-weight: 750; letter-spacing: .13em; text-transform: uppercase; line-height: 1.3; }
                    .state { color: var(--muted); margin-top: 4px; font-size: 11px; letter-spacing: .12em; font-weight: 650; }
                    .model { display: inline-flex; align-items: center; max-width: min(42vw, 420px); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; padding: 7px 11px; border: 1px solid var(--border); border-radius: 999px; color: var(--muted); background: var(--surface); font-size: 12px; }
                    button { font: inherit; cursor: pointer; }
                    .copy-all { flex: 0 0 auto; border: 1px solid var(--border); border-radius: 10px; background: var(--surface); color: var(--text); padding: 9px 13px; font-size: 12px; font-weight: 650; transition: background .15s ease, border-color .15s ease; }
                    .copy-all:hover, .copy-code:hover { border-color: var(--accent); background: var(--accent-soft); }
                    .page-title { margin: 34px 0 23px; font-size: clamp(22px, 3vw, 31px); font-weight: 700; line-height: 1.28; letter-spacing: -.035em; }
                    main { padding: clamp(20px, 4vw, 34px); border: 1px solid var(--border); border-radius: 18px; background: var(--surface); box-shadow: var(--shadow); overflow-wrap: anywhere; }
                    main > :first-child { margin-top: 0; }
                    main > :last-child { margin-bottom: 0; }
                    h1, h2, h3, h4, h5, h6 { line-height: 1.35; margin: 1.65em 0 .7em; letter-spacing: -.02em; }
                    h1 { font-size: 1.65em; } h2 { font-size: 1.4em; } h3 { font-size: 1.17em; }
                    p, ul, ol, blockquote, table, pre { margin: 1.05em 0; }
                    li + li { margin-top: .32em; }
                    a { color: var(--accent); text-decoration: underline; text-decoration-thickness: 1px; text-underline-offset: 3px; }
                    a:hover { text-decoration-thickness: 2px; }
                    hr { margin: 2em 0; border: 0; border-top: 1px solid var(--border); }
                    blockquote { margin-left: 0; padding: 2px 0 2px 18px; border-left: 3px solid var(--quote); color: var(--muted); }
                    :not(pre) > code { padding: .16em .42em; border: 1px solid var(--border); border-radius: 6px; background: var(--surface-soft); font-size: .88em; font-family: Consolas, "Cascadia Code", monospace; }
                    pre { position: relative; max-width: 100%; overflow: auto; padding: 42px 17px 17px; border: 1px solid #2a2f3b; border-radius: 12px; background: var(--code); color: var(--code-text); line-height: 1.55; tab-size: 4; }
                    pre code { display: block; min-width: max-content; white-space: pre; font: 13px/1.6 Consolas, "Cascadia Code", monospace; }
                    .copy-code { position: absolute; top: 8px; right: 8px; padding: 5px 9px; border: 1px solid #414959; border-radius: 7px; background: #262c38; color: #e8edf5; font-size: 11px; line-height: 1.4; }
                    table { display: block; width: 100%; overflow-x: auto; border-collapse: collapse; border-spacing: 0; }
                    th, td { padding: 9px 12px; border: 1px solid var(--border); text-align: left; vertical-align: top; }
                    th { background: var(--surface-soft); font-weight: 700; }
                    tr:nth-child(even) td { background: color-mix(in srgb, var(--surface-soft) 42%, transparent); }
                    img { display: none; }
                    .footer { display: flex; justify-content: space-between; flex-wrap: wrap; gap: 8px; margin-top: 18px; color: var(--muted); font-size: 11px; }
                    #copy-status { min-height: 1.4em; color: var(--accent); }
                    @media (max-width: 560px) {
                        .shell { padding-top: 16px; }
                        .topbar { align-items: flex-start; flex-wrap: wrap; }
                        .model { max-width: 100%; }
                        main { padding: 18px; border-radius: 13px; }
                        .page-title { margin-top: 26px; }
                    }
                    @media print {
                        body { background: #fff; color: #111; }
                        .shell { width: 100%; padding: 0; }
                        .topbar, .footer, .copy-code, .copy-all { display: none !important; }
                        main { padding: 0; box-shadow: none; border: 0; }
                        pre { white-space: pre-wrap; overflow-wrap: anywhere; }
                    }
                </style>
            </head>
            <body>
                <div class="shell">
                    <header class="topbar">
                        <div class="identity">
                            <div class="mark" aria-hidden="true">L</div>
                            <div>
                                <div class="brand">LocalLLM</div>
                                <div class="state">{{stateLabel}}</div>
                            </div>
                            <span class="model" title="{{safeModel}}">{{safeModel}}</span>
                        </div>
                        <button class="copy-all" id="copy-all" type="button">Copy Markdown</button>
                    </header>
                    <h1 class="page-title">{{safeTitle}}</h1>
                    <main id="answer">{{safeBody}}</main>
                    <footer class="footer">
                        <span>Text is selectable · Links open in your browser</span>
                        <span id="copy-status" role="status" aria-live="polite"></span>
                    </footer>
                    <textarea id="raw-response" hidden aria-hidden="true">{{safeRawResponse}}</textarea>
                </div>
                <script>
                    async function copyText(text) {
                        try {
                            if (navigator.clipboard && window.isSecureContext) {
                                await navigator.clipboard.writeText(text);
                                return true;
                            }
                        } catch (_) { }
                        const area = document.createElement('textarea');
                        area.value = text;
                        area.setAttribute('readonly', '');
                        area.style.position = 'fixed';
                        area.style.left = '-9999px';
                        document.body.appendChild(area);
                        area.select();
                        let ok = false;
                        try { ok = document.execCommand('copy'); } catch (_) { ok = false; }
                        area.remove();
                        return ok;
                    }
                    function announce(message) {
                        const status = document.getElementById('copy-status');
                        status.textContent = message;
                    }
                    document.getElementById('copy-all').addEventListener('click', async () => {
                        const raw = document.getElementById('raw-response').value;
                        announce(await copyText(raw) ? 'Copied response.' : 'Copy was blocked by the browser. Select the text and press Ctrl+C.');
                    });
                    document.querySelectorAll('.copy-code').forEach(button => {
                        button.addEventListener('click', async () => {
                            const code = button.parentElement.querySelector('code');
                            const ok = await copyText(code ? code.innerText : '');
                            button.textContent = ok ? 'Copied' : 'Copy failed';
                            announce(ok ? 'Code block copied.' : 'Copy was blocked by the browser.');
                        });
                    });
                </script>
            </body>
            </html>
            """;

        var directory = Path.Combine(Path.GetTempPath(), "PowerToysRun-LocalLLM");
        Directory.CreateDirectory(directory);
        RemoveOldResponseFiles(directory);

        var path = Path.Combine(directory, $"response-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    private static void SanitizeMarkdownHtml(HtmlDocument document)
    {
        // Markdig's DisableHtml means model-supplied raw HTML is rendered as text. Keep
        // a second defensive layer in case the pipeline or extensions change later.
        var dangerousNodes = document.DocumentNode.SelectNodes(
            "//script|//iframe|//object|//embed|//svg|//audio|//video|//source|//form|//img");
        if (dangerousNodes is not null)
        {
            foreach (var node in dangerousNodes.ToList())
            {
                node.Remove();
            }
        }

        var allNodes = document.DocumentNode.Descendants().ToList();
        foreach (var node in allNodes)
        {
            foreach (var attribute in node.Attributes.ToList())
            {
                if (attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                    || attribute.Name.Equals("style", StringComparison.OrdinalIgnoreCase)
                    || attribute.Name.Equals("src", StringComparison.OrdinalIgnoreCase)
                    || attribute.Name.Equals("srcset", StringComparison.OrdinalIgnoreCase)
                    || attribute.Name.Equals("formaction", StringComparison.OrdinalIgnoreCase)
                    || attribute.Name.Equals("xlink:href", StringComparison.OrdinalIgnoreCase))
                {
                    node.Attributes.Remove(attribute);
                }
            }
        }

        var links = document.DocumentNode.SelectNodes("//a[@href]");
        if (links is null)
        {
            return;
        }

        foreach (var link in links)
        {
            var href = link.GetAttributeValue("href", string.Empty).Trim();
            var isLocalFragment = href.StartsWith("#", StringComparison.Ordinal) && href.Length > 1;
            var isAllowedAbsoluteUrl = Uri.TryCreate(href, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp
                    || uri.Scheme == Uri.UriSchemeHttps
                    || uri.Scheme == Uri.UriSchemeMailto);

            if (!isLocalFragment && !isAllowedAbsoluteUrl)
            {
                link.Attributes.Remove("href");
                link.Attributes.Remove("target");
                link.Attributes.Remove("rel");
                continue;
            }

            if (isAllowedAbsoluteUrl && uri is not null
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                link.SetAttributeValue("target", "_blank");
                link.SetAttributeValue("rel", "noopener noreferrer");
            }
        }
    }

    private static void AddCodeCopyButtons(HtmlDocument document)
    {
        var codeBlocks = document.DocumentNode.SelectNodes("//pre[code]");
        if (codeBlocks is null)
        {
            return;
        }

        foreach (var codeBlock in codeBlocks.ToList())
        {
            var button = HtmlNode.CreateNode(
                "<button type=\"button\" class=\"copy-code\" aria-label=\"Copy code block\">Copy code</button>");
            codeBlock.AppendChild(button);
        }
    }

    private static void RemoveOldResponseFiles(string directory)
    {
        var expiration = DateTime.UtcNow.AddDays(-7);
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "response-*.html"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < expiration)
                    {
                        File.Delete(file);
                    }
                }
                catch (IOException)
                {
                    // A browser may still have a file open; skip it and continue.
                }
                catch (UnauthorizedAccessException)
                {
                    // Ignore files that the current user cannot remove.
                }
            }
        }
        catch (IOException)
        {
            // Cleanup is best-effort and must not prevent opening the current response.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup is best-effort and must not prevent opening the current response.
        }
    }

    private sealed record SettingsSnapshot(
        string Endpoint,
        string DefaultModel,
        string Thinking,
        int KeepAliveSeconds,
        int RequestTimeoutSeconds);
}
