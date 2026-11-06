#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Wox.Plugin;
using RunMind.Shared;
using Clipboard = System.Windows.Clipboard;

namespace Community.PowerToys.Run.Plugin.RunMind;

/// <summary>
/// PowerToys Run plugin that sends the user's query to the selected local LLM provider,
/// with optional plugin-local instructions and generation defaults.
/// </summary>
public sealed class Main : IPlugin, IDelayedExecutionPlugin, ISettingProvider, IContextMenu, IDisposable
{
    public static string PluginID => "550A34D0CFA845449989D581149B3D9C";
    public string Name => "RunMind";
    public string Description => "Query a local language model using Ollama or LM Studio.";

    private const string DefaultEndpoint = "http://localhost:11434/api/generate";
    private const string DefaultLMStudioBaseUrl = "http://localhost:1234/v1";
    private const string DefaultModel = "qwen3:14b";
    private const string DefaultThinking = "auto";
    private const int DefaultKeepAliveSeconds = 300;
    private const int DefaultRequestTimeoutSeconds = 600;
    private const int DefaultInlineCpuThreads = 0;
    private const int MaximumInlineSystemPromptCharacters = 12000;
    private const string PreviousDefaultInlineSystemPrompt =
        "SYSTEM\n\n" +
        "You are QuickAI, a compact proactive local AI living inside \"Microsoft PowerToys Run\".\n" +
        "The input is the user's current search-box text. It may be a single letter, partial word, application name, command, question, complete task or anything else.\n" +
        "Answer immediately without introductions.";
    private const string DefaultInlineSystemPrompt =
        "You are QuickAI, a compact, proactive local AI living inside \"Microsoft PowerToys Run\".\n" +
        "The input is the user's current search-box text. It may be a single letter, partial word, application name, command, question, complete task or anything else.";

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
    private static readonly ConditionalWeakTable<WebView2, object> DisposedWebViews = new();
    private static readonly ConditionalWeakTable<Window, WindowThemeSynchronizer> WindowThemeSynchronizers = new();
    private static CoreWebView2Environment? _localAppDataWebView2Environment;
    private static CoreWebView2Environment? _temporaryWebView2Environment;

    private readonly object _settingsLock = new();
    private readonly object _queryLock = new();
    private QueryOperation? _activeQuery;
    private string? _latestFastQuery;
    private bool _hasReceivedFastQuery;
    private bool _disposed;
    private PluginInitContext? _context;
    private string _iconPath = @"Images\icon.png";
    private string _endpoint = DefaultEndpoint;
    private string _lmStudioBaseUrl = DefaultLMStudioBaseUrl;
    private string _defaultModel = DefaultModel;
    private string _thinking = DefaultThinking;
    private int _provider;
    private int _keepAliveSeconds = DefaultKeepAliveSeconds;
    private int _requestTimeoutSeconds = DefaultRequestTimeoutSeconds;
    private int _inlineCpuThreads = DefaultInlineCpuThreads;
    private string _inlineSystemPrompt = string.Empty;
    private double? _inlineTemperature;
    private double? _inlineTopP;
    private int? _inlineMaxOutputTokens;
    private int? _inlineSeed;
    private bool _inlineConversationalMode;

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
        new()
        {
            Key = "InlineCpuThreads",
            DisplayLabel = "Ollama max CPU threads (optional)",
            DisplayDescription = "Optional Ollama-only CPU thread limit for inline PowerToys Run requests. Leave blank or set to 0 to let Ollama choose automatically. Set 1–128 to use that maximum. Does not affect LM Studio or standalone chat.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = DefaultInlineCpuThreads.ToString(CultureInfo.InvariantCulture),
        },
        new()
        {
            Key = "InlineSystemPrompt",
            DisplayLabel = "Custom system instructions (editable QuickAI default)",
            DisplayDescription = "Pre-filled QuickAI instructions for PowerToys Run. Edit or clear them freely; an empty value disables the additional prompt. Maximum 12,000 characters. Does not affect the standalone chat app.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.MultilineTextbox,
            TextValue = DefaultInlineSystemPrompt,
            TextBoxMaxLength = MaximumInlineSystemPromptCharacters,
            PlaceholderText = "Enter your PowerToys Run system instructions...",
        },
        new()
        {
            Key = "InlineTemperature",
            DisplayLabel = "Temperature (optional)",
            DisplayDescription = "Inline PowerToys Run generation setting. Leave empty for the model default. Range: 0–2.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = string.Empty,
        },
        new()
        {
            Key = "InlineTopP",
            DisplayLabel = "Top P (optional)",
            DisplayDescription = "Inline PowerToys Run generation setting. Leave empty for the model default. Range: 0–1.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = string.Empty,
        },
        new()
        {
            Key = "InlineMaxOutputTokens",
            DisplayLabel = "Max output tokens (optional)",
            DisplayDescription = "Inline PowerToys Run output limit. Leave empty for the model default. Range: 1–1,000,000.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = string.Empty,
        },
        new()
        {
            Key = "InlineSeed",
            DisplayLabel = "Seed (optional)",
            DisplayDescription = "Inline PowerToys Run reproducibility setting. Leave empty for a random/provider default seed. Range: 0–2,147,483,647.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = string.Empty,
        },
        new()
        {
            Key = "InlineConversationalMode",
            DisplayLabel = "Nemotron 3.5 Bolt E4B conversational mode",
            DisplayDescription = "Optional and OFF by default. When enabled, sends chat_template_kwargs.conversational_mode=true for matching NVIDIA Nemotron 3.5 Bolt E4B model IDs through the LM Studio / OpenAI-compatible provider only. Ollama's native API does not expose this template parameter.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Checkbox,
            Value = false,
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
        int inlineCpuThreads = DefaultInlineCpuThreads;
        string inlineSystemPrompt = DefaultInlineSystemPrompt;
        double? inlineTemperature = null;
        double? inlineTopP = null;
        int? inlineMaxOutputTokens = null;
        int? inlineSeed = null;
        bool inlineConversationalMode = false;

        if (settings?.AdditionalOptions is not null)
        {
            var options = settings.AdditionalOptions;
            endpoint = GetTextOption(options, "LLMEndpoint", endpoint);
            lmStudioBaseUrl = GetTextOption(options, "LMStudioBaseUrl", lmStudioBaseUrl);
            provider = options.FirstOrDefault(option => option.Key == "InlineProvider")?.ComboBoxValue == 1 ? 1 : 0;
            model = GetTextOption(options, "Model", model);
            thinking = GetTextOption(options, "Thinking", thinking);
            var inlinePromptOption = options.FirstOrDefault(option => option.Key == "InlineSystemPrompt");
            if (inlinePromptOption is null)
            {
                inlineSystemPrompt = DefaultInlineSystemPrompt;
            }
            else
            {
                var savedPrompt = NormalizeInlineSystemPrompt(inlinePromptOption.TextValue ?? string.Empty);
                var previousDefault = NormalizeInlineSystemPrompt(PreviousDefaultInlineSystemPrompt);
                // Migrate only the exact previous built-in starter text. Preserve custom prompts
                // and an explicitly empty value (which disables the additional instructions).
                inlineSystemPrompt = string.Equals(savedPrompt, previousDefault, StringComparison.Ordinal)
                    ? DefaultInlineSystemPrompt
                    : savedPrompt;
            }
            inlineTemperature = ParseOptionalDouble(options, "InlineTemperature", 0, 2);
            inlineTopP = ParseOptionalDouble(options, "InlineTopP", 0, 1);
            inlineMaxOutputTokens = ParseOptionalInt(options, "InlineMaxOutputTokens", 1, 1000000);
            inlineSeed = ParseOptionalInt(options, "InlineSeed", 0, int.MaxValue);
            inlineConversationalMode = options.FirstOrDefault(option => option.Key == "InlineConversationalMode")?.Value is bool enabled && enabled;

            if (int.TryParse(GetTextOption(options, "KeepAliveSeconds", DefaultKeepAliveSeconds.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedKeepAlive)
                && parsedKeepAlive is >= 0 and <= 86400)
            {
                keepAliveSeconds = parsedKeepAlive;
            }

            if (int.TryParse(GetTextOption(options, "RequestTimeoutSeconds", DefaultRequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTimeout)
                && parsedTimeout is >= 30 and <= 3600)
            {
                requestTimeoutSeconds = parsedTimeout;
            }

            if (int.TryParse(GetTextOption(options, "InlineCpuThreads", DefaultInlineCpuThreads.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCpuThreads)
                && parsedCpuThreads is >= 0 and <= 128)
            {
                inlineCpuThreads = parsedCpuThreads;
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
            _inlineCpuThreads = inlineCpuThreads;
            _inlineSystemPrompt = inlineSystemPrompt;
            _inlineTemperature = inlineTemperature;
            _inlineTopP = inlineTopP;
            _inlineMaxOutputTokens = inlineMaxOutputTokens;
            _inlineSeed = inlineSeed;
            _inlineConversationalMode = inlineConversationalMode;
        }

        CancelActiveQuery();
    }

    // PowerToys calls this fast path as the user types. It must never perform network I/O.
    // Starting a new fast query cancels the previous delayed LLM request immediately.
    public List<Result> Query(Query query)
    {
        if (query is null)
        {
            CancelActiveQuery();
            return new List<Result>();
        }

        var search = (query.Search ?? string.Empty).Trim();
        if (search.Length == 0
            || search.Equals("llm", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(query.ActionKeyword)
                && search.Equals(query.ActionKeyword.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            // `llm` by itself is intentionally not a launcher command. Windows Search /
            // PowerToys' normal application indexer launches RunMind.exe by its app name.
            CancelActiveQuery();
            return new List<Result>();
        }

        // Let PowerToys' normal application indexer handle the standalone app name.
        if (string.IsNullOrWhiteSpace(query.ActionKeyword)
            && (search.Equals("RunMind", StringComparison.OrdinalIgnoreCase)
                || search.Equals("LocalLLM", StringComparison.OrdinalIgnoreCase)
                || search.Equals("Local LLM", StringComparison.OrdinalIgnoreCase)))
        {
            CancelActiveQuery();
            return new List<Result>();
        }

        // Global mode accepts even a one-character prompt. PowerToys may use short
        // prefixes while typing; the normal delayed-execution path handles throttling.

        var settings = GetSettingsSnapshot();
        var (model, prompt) = ParseModelAndPrompt(search, settings.DefaultModel);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            CancelActiveQuery();
            return new List<Result>();
        }

        var operation = BeginQuery(search, model, prompt, settings);
        return operation is null
            ? new List<Result>()
            : new List<Result> { CreatePendingResult(operation) };
    }

    // PowerToys invokes the delayed phase from a background query worker. The fast Query()
    // above has already returned immediately; this phase can wait for model I/O and return
    // the completed result directly to the host's current query generation.
    public List<Result> Query(Query query, bool delayedExecution)
    {
        if (!delayedExecution || query is null)
            return new List<Result>();

        var search = (query.Search ?? string.Empty).Trim();
        if (search.Length == 0)
        {
            CancelActiveQuery();
            return new List<Result>();
        }

        var currentSettings = GetSettingsSnapshot();
        var (currentModel, currentPrompt) = ParseModelAndPrompt(search, currentSettings.DefaultModel);
        if (string.IsNullOrWhiteSpace(currentPrompt)) return new List<Result>();

        var operation = GetOrBeginQuery(search, currentModel, currentPrompt, currentSettings);
        if (operation is null) return new List<Result>();

        if (!operation.TryGetCompletion(out _, out _))
        {
            if (operation.TryStart())
            {
                // PowerToys invokes IDelayedExecutionPlugin.Query from its background
                // query phase and awaits the returned result. Keep HTTP asynchronous
                // internally, but wait here so the host can publish this response as part
                // of the exact query generation without rewriting the search-box text.
                ExecuteQueryAsync(operation).GetAwaiter().GetResult();
            }
            else
            {
                // Defensive path for duplicate delayed callbacks for the same operation.
                operation.WaitForFinish();
            }
        }

        if (!IsCurrentQuery(operation)) return new List<Result>();
        if (operation.TryGetCompletion(out var completedResponse, out var isError))
        {
            return new List<Result>
            {
                CreateResponseResult(operation.Model, completedResponse, isError, operation.Prompt, operation.Settings),
            };
        }

        // The fast Query() path already supplied the pending item in the normal flow.
        // Return it only for hosts that invoke the delayed interface without the fast path.
        return HasFastQueryResult(search)
            ? new List<Result>()
            : new List<Result> { CreatePendingResult(operation) };
    }

    private async Task ExecuteQueryAsync(QueryOperation operation)
    {
        try
        {
            var cancellationToken = operation.Token;
            cancellationToken.ThrowIfCancellationRequested();

            var models = await FetchModelsFromEndpointAsync(operation.Settings, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentQuery(operation)) return;

            if (!models.Contains(operation.Model, StringComparer.OrdinalIgnoreCase))
            {
                var available = models.Count == 0
                    ? $"{GetProviderName(operation.Settings)} returned an empty model list."
                    : $"Available models: {string.Join(", ", models)}";
                operation.SetCompletion($"Model '{operation.Model}' was not found. {available}", isError: true);
            }
            else
            {
                var response = await QueryLLMStreamAsync(
                    operation.Prompt,
                    operation.Model,
                    operation.Settings,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentQuery(operation)) return;

                if (string.IsNullOrWhiteSpace(response))
                    response = "The model completed the request but returned no text.";

                operation.SetCompletion(response, isError: false);
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            // A newer query or an explicit hand-off to chat owns the user's intent now.
            // Do not publish stale results back into PowerToys.
        }
        catch (Exception ex)
        {
            if (IsCurrentQuery(operation) && !operation.IsCancellationRequested)
                operation.SetCompletion($"Request failed: {GetUsefulExceptionMessage(ex)}", isError: true);
        }
        finally
        {
            operation.MarkFinished();
        }

    }

    private bool HasFastQueryResult(string search)
    {
        lock (_queryLock)
        {
            return _hasReceivedFastQuery
                && string.Equals(_latestFastQuery, search, StringComparison.Ordinal);
        }
    }

    private QueryOperation? BeginQuery(string search, string model, string prompt, SettingsSnapshot settings)
    {
        QueryOperation? previous;
        QueryOperation operation;
        lock (_queryLock)
        {
            if (_disposed) return null;
            if (_activeQuery is not null
                && string.Equals(_activeQuery.Search, search, StringComparison.Ordinal)
                && !_activeQuery.IsCancellationRequested
                && !_activeQuery.WasHandedOff)
            {
                _latestFastQuery = search;
                _hasReceivedFastQuery = true;
                return _activeQuery;
            }

            previous = _activeQuery;
            operation = new QueryOperation(search, model, prompt, settings);
            _activeQuery = operation;
            _latestFastQuery = search;
            _hasReceivedFastQuery = true;
        }

        // Cancel outside the state lock: CancellationToken callbacks may run inline.
        previous?.Cancel();
        previous?.Dispose();
        return operation;
    }

    private QueryOperation? GetOrBeginQuery(string search, string model, string prompt, SettingsSnapshot settings)
    {
        lock (_queryLock)
        {
            if (_disposed) return null;

            // A delayed callback for an obsolete query must never restart cancelled work.
            if (_hasReceivedFastQuery && !string.Equals(_latestFastQuery, search, StringComparison.Ordinal))
                return null;

            if (_activeQuery is not null)
            {
                if (!string.Equals(_activeQuery.Search, search, StringComparison.Ordinal)
                    || _activeQuery.IsCancellationRequested || _activeQuery.WasHandedOff)
                    return null;
                return _activeQuery;
            }

            // Supports launcher configurations which invoke only the delayed path first.
            var operation = new QueryOperation(search, model, prompt, settings);
            _activeQuery = operation;
            return operation;
        }
    }

    private bool IsCurrentQuery(QueryOperation operation)
    {
        lock (_queryLock)
        {
            return ReferenceEquals(_activeQuery, operation)
                && !operation.IsCancellationRequested
                && !operation.WasHandedOff
                && !_disposed;
        }
    }

    private void CancelActiveQuery()
    {
        QueryOperation? previous;
        lock (_queryLock)
        {
            previous = _activeQuery;
            _activeQuery = null;
            _latestFastQuery = null;
            _hasReceivedFastQuery = true;
        }

        // Invalidate the query before triggering cancellation callbacks.
        previous?.Cancel();
        previous?.Dispose();
    }

    public void Dispose()
    {
        QueryOperation? previous;
        lock (_queryLock)
        {
            if (_disposed) return;
            _disposed = true;
            previous = _activeQuery;
            _activeQuery = null;
            _latestFastQuery = null;
            _hasReceivedFastQuery = true;
        }

        previous?.Cancel();
        previous?.Dispose();

        if (_context is not null)
        {
            _context.API.ThemeChanged -= OnThemeChanged;
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
        SettingsSnapshot settings,
        CancellationToken cancellationToken = default)
    {
        return settings.Provider == 1
            ? QueryLMStudioStreamAsync(input, model, settings, cancellationToken)
            : QueryOllamaStreamAsync(input, model, settings, cancellationToken);
    }

    private async Task<string> QueryOllamaStreamAsync(
        string input,
        string model,
        SettingsSnapshot settings,
        CancellationToken cancellationToken)
    {
        var endpoint = ValidateGenerateEndpoint(settings.Endpoint);
        var requestBody = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["model"] = model,
            ["prompt"] = input,
            ["stream"] = true,
            ["keep_alive"] = settings.KeepAliveSeconds,
        };
        if (!string.IsNullOrWhiteSpace(settings.InlineSystemPrompt))
            requestBody["system"] = settings.InlineSystemPrompt;

        var generationOptions = new Dictionary<string, object>(StringComparer.Ordinal);
        if (settings.InlineCpuThreads > 0) generationOptions["num_thread"] = settings.InlineCpuThreads;
        if (settings.InlineTemperature is double temperature) generationOptions["temperature"] = temperature;
        if (settings.InlineTopP is double topP) generationOptions["top_p"] = topP;
        if (settings.InlineMaxOutputTokens is int maxOutputTokens) generationOptions["num_predict"] = maxOutputTokens;
        if (settings.InlineSeed is int seed) generationOptions["seed"] = seed;
        if (generationOptions.Count > 0) requestBody["options"] = generationOptions;

        if (TryResolveThinkValue(model, settings.Thinking, out var thinkValue))
        {
            requestBody["think"] = thinkValue;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var connectionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(requestTimeout.Token, connectionTimeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, sendTimeout.Token).ConfigureAwait(false);
        connectionTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
        if (!response.IsSuccessStatusCode)
        {
            var body = await ReadBoundedContentTextAsync(response.Content, 64 * 1024, requestTimeout.Token).ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {TrimForError(body)}";
            throw new HttpRequestException($"Ollama returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{detail}", null, response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(requestTimeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        var output = new StringBuilder();
        var recordNumber = 0;
        while (true)
        {
            var line = await reader.ReadLineAsync(requestTimeout.Token).ConfigureAwait(false);
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

    private async Task<string> QueryLMStudioStreamAsync(string input, string model, SettingsSnapshot settings, CancellationToken cancellationToken)
    {
        var endpoint = BuildLMStudioUri(settings.LMStudioBaseUrl, "chat/completions");
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(settings.InlineSystemPrompt))
            messages.Add(new { role = "system", content = settings.InlineSystemPrompt });
        messages.Add(new { role = "user", content = input });
        var requestBody = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = true,
        };
        if (settings.InlineTemperature is double temperature) requestBody["temperature"] = temperature;
        if (settings.InlineTopP is double topP) requestBody["top_p"] = topP;
        if (settings.InlineMaxOutputTokens is int maxOutputTokens) requestBody["max_tokens"] = maxOutputTokens;
        if (settings.InlineSeed is int seed) requestBody["seed"] = seed;
        if (IsNemotron35BoltE4BModel(model))
        {
            // Send both states explicitly so PowerToys can enable and disable the
            // chat-template mode for this model without changing other model requests.
            requestBody["chat_template_kwargs"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["conversational_mode"] = settings.InlineConversationalMode,
            };
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var connectionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(requestTimeout.Token, connectionTimeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, sendTimeout.Token).ConfigureAwait(false);
        connectionTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
        if (!response.IsSuccessStatusCode)
        {
            var body = await ReadBoundedContentTextAsync(response.Content, 64 * 1024, requestTimeout.Token).ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {TrimForError(body)}";
            throw new HttpRequestException($"LM Studio returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{detail}", null, response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(requestTimeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        var output = new StringBuilder();
        var recordNumber = 0;
        while (true)
        {
            var line = await reader.ReadLineAsync(requestTimeout.Token).ConfigureAwait(false);
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

    private async Task<List<string>> FetchModelsFromEndpointAsync(SettingsSnapshot settings, CancellationToken cancellationToken = default)
    {
        var modelsUri = settings.Provider == 1
            ? BuildLMStudioUri(settings.LMStudioBaseUrl, "models")
            : BuildTagsUri(settings.Endpoint);
        var cacheKey = modelsUri.AbsoluteUri;
        cancellationToken.ThrowIfCancellationRequested();
        lock (ModelCacheLock)
        {
            if (string.Equals(_modelCacheEndpoint, cacheKey, StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.UtcNow - _modelCacheTimestamp < TimeSpan.FromSeconds(5))
                return new List<string>(_modelCache);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, modelsUri);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await ReadBoundedContentTextAsync(response.Content, 64 * 1024, requestTimeout.Token).ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {TrimForError(body)}";
            throw new HttpRequestException($"Could not query the {GetProviderName(settings)} model list (HTTP {(int)response.StatusCode} {response.ReasonPhrase}).{detail}", null, response.StatusCode);
        }

        var modelListBytes = await ReadBoundedContentBytesAsync(response.Content, 4 * 1024 * 1024, requestTimeout.Token).ConfigureAwait(false);
        using var json = JsonDocument.Parse(modelListBytes);
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
            || string.IsNullOrWhiteSpace(baseUri.Host)
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
            throw new InvalidOperationException("The LM Studio base URL must be an absolute HTTP or HTTPS URL without embedded credentials, query strings, or fragments, for example http://localhost:1234/v1.");
        var builder = new UriBuilder(baseUri) { Path = baseUri.AbsolutePath.TrimEnd('/') + "/" + relativePath.TrimStart('/'), Query = string.Empty, Fragment = string.Empty };
        return builder.Uri;
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

    private Result CreatePendingResult(QueryOperation operation)
    {
        var model = operation.Model;
        var prompt = operation.Prompt;
        var settings = operation.Settings;
        return new Result
        {
            Title = "Ask RunMind",
            SubTitle = $"{CreatePreview(prompt)} · Generating… · Open in chat",
            IcoPath = _iconPath,
            Score = 45,
            ContextData = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["prompt"] = prompt,
                ["model"] = model,
                ["mode"] = "pending",
            },
            Action = _ =>
            {
                DispatchToUi(() =>
                {
                    try
                    {
                        if (!TryResolvePendingOperation(operation, out var completedResponse, out var isError, out var needsHandoff))
                            return;
                        if (needsHandoff)
                        {
                            // Cancel the inline HTTP request before starting the chat request.
                            operation.Cancel();
                            ShowPromptChatWindow(settings, model, prompt);
                        }
                        else
                        {
                            ShowNativeResponseWindow(settings, model, prompt, completedResponse, isError);
                        }
                    }
                    catch (Exception ex)
                    {
                        _context?.API.ShowMsg("Could not open the RunMind chat window", ex.ToString());
                    }
                }, "Could not open the RunMind chat window");
                return true;
            },
        };
    }

    private bool TryResolvePendingOperation(QueryOperation operation, out string response, out bool isError, out bool needsHandoff)
    {
        lock (_queryLock)
        {
            if (_disposed || !ReferenceEquals(_activeQuery, operation) || operation.IsCancellationRequested || operation.WasHandedOff)
            {
                response = string.Empty;
                isError = false;
                needsHandoff = false;
                return false;
            }

            var completed = operation.TryGetCompletionOrMarkHandoff(out response, out isError);
            needsHandoff = !completed;
            return true;
        }
    }

    private Result CreateResponseResult(string model, string response, bool isError, string prompt, SettingsSnapshot settings)
    {
        var title = isError ? "RunMind · Request failed" : "RunMind response";
        var preview = CreatePreview(response);

        return new Result
        {
            Title = title,
            SubTitle = preview,
            IcoPath = _iconPath,
            Score = 45,
            ContextData = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["copy"] = response,
                ["model"] = model,
                ["prompt"] = prompt ?? string.Empty,
                ["isError"] = isError.ToString(),
            },
            Action = _ =>
            {
                DispatchToUi(() =>
                {
                    try
                    {
                        ShowNativeResponseWindow(settings, model, prompt, response, isError);
                    }
                    catch (Exception ex)
                    {
                        _context?.API.ShowMsg("Could not display the LLM response", ex.ToString());
                    }
                }, "Could not display the LLM response");
                return true;
            },
        };
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
                DispatchToUi(() =>
                {
                    try { Clipboard.SetText(section ?? fullResponse); }
                    catch (Exception ex) { _context?.API.ShowMsg("Could not copy response", ex.Message); }
                }, "Could not copy response");
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
                    DispatchToUi(() =>
                    {
                        try { Clipboard.SetText(fullResponse); }
                        catch (Exception ex) { _context?.API.ShowMsg("Could not copy response", ex.Message); }
                    }, "Could not copy response");
                    return true;
                },
            });
        }

        return results;
    }

    public Control CreateSettingPanel()
    {
        // RunMind exposes its settings through PowerToys AdditionalOptions.
        // This legacy custom-panel path is intentionally not implemented.
        throw new NotImplementedException("RunMind settings are supplied through PowerToys AdditionalOptions.");
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

    private static bool IsNemotron35BoltE4BModel(string model)
    {
        var normalized = new string((model ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalized.Contains("nemotron35bolte4b", StringComparison.Ordinal)
            && !normalized.Contains("dflash", StringComparison.Ordinal)
            && !normalized.Contains("74b", StringComparison.Ordinal);
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
                _requestTimeoutSeconds,
                _inlineCpuThreads,
                _inlineSystemPrompt,
                _inlineTemperature,
                _inlineTopP,
                _inlineMaxOutputTokens,
                _inlineSeed,
                _inlineConversationalMode);
        }
    }

    private static string NormalizeInlineSystemPrompt(string value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();
        return normalized.Length > MaximumInlineSystemPromptCharacters
            ? normalized[..MaximumInlineSystemPromptCharacters]
            : normalized;
    }

    private static double? ParseOptionalDouble(IEnumerable<PluginAdditionalOption> options, string key, double minimum, double maximum)
    {
        var raw = GetTextOption(options, key, string.Empty);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value) && value >= minimum && value <= maximum ? value : null;
    }

    private static int? ParseOptionalInt(IEnumerable<PluginAdditionalOption> options, string key, int minimum, int maximum)
    {
        var raw = GetTextOption(options, key, string.Empty);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && value >= minimum && value <= maximum ? value : null;
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
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var preview = System.Text.RegularExpressions.Regex.Replace(
            text.Trim(), @"\r?\n\s*\r?\n+", "\n");
        const int maxLength = 420;
        return preview.Length <= maxLength ? preview : preview[..(maxLength - 1)].TrimEnd() + "…";
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

    private static async Task<byte[]> ReadBoundedContentBytesAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (content.Headers.ContentLength is long declaredLength && declaredLength > maximumBytes)
            throw new InvalidDataException($"HTTP response exceeded the {maximumBytes:N0}-byte size limit.");

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > maximumBytes)
                throw new InvalidDataException($"HTTP response exceeded the {maximumBytes:N0}-byte size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return output.ToArray();
    }

    private static async Task<string> ReadBoundedContentTextAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        var bytes = await ReadBoundedContentBytesAsync(content, maximumBytes, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string TrimForError(string text)
    {
        var singleLine = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        const int maxLength = 500;
        return singleLine.Length <= maxLength ? singleLine : singleLine[..(maxLength - 1)] + "…";
    }

    private void DispatchToUi(Action action, string failureTitle)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            _context?.API.ShowMsg(failureTitle, "PowerToys' WPF UI dispatcher is unavailable or shutting down.");
            return;
        }

        void SafeInvoke()
        {
            try
            {
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
                if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                    throw new InvalidOperationException("The PowerToys UI dispatcher is not running on an STA thread; WPF/WebView2 windows cannot be created safely.");
                action();
            }
            catch (Exception ex)
            {
                _context?.API.ShowMsg(failureTitle, ex.ToString());
            }
        }

        if (dispatcher.CheckAccess())
        {
            SafeInvoke();
        }
        else
        {
            _ = dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(SafeInvoke));
        }
    }

    private static void DisposeWebViewSafely(WebView2 webView)
    {
        lock (DisposedWebViews)
        {
            if (DisposedWebViews.TryGetValue(webView, out _)) return;
            DisposedWebViews.Add(webView, new object());
        }

        try { webView.Dispose(); }
        catch { /* Partially initialized WebView2 controls can throw during teardown. */ }
    }

    private static BitmapFrame? LoadWindowIcon(string assemblyDirectory)
    {
        var iconPath = Path.Combine(assemblyDirectory, "Assets", "Brand", "Icon.ico");
        if (!File.Exists(iconPath)) return null;

        try
        {
            var frame = BitmapFrame.Create(
                new Uri(iconPath, UriKind.Absolute),
                BitmapCreateOptions.None,
                BitmapCacheOption.OnLoad);
            if (frame.CanFreeze) frame.Freeze();
            return frame;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ShowPromptChatWindow(SettingsSnapshot settings, string model, string prompt)
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(Main).Assembly.Location) ?? AppContext.BaseDirectory;
        var htmlPath = Path.Combine(assemblyDirectory, "ChatWindow.html");
        if (!File.Exists(htmlPath)) throw new FileNotFoundException("ChatWindow.html was not found beside the RunMind plugin assembly.", htmlPath);
        var bootstrap = new
        {
            title = CreateFallbackConversationTitle(prompt),
            appearance = AppearancePreferenceStore.CreateBootstrapValue(),
            windowInstanceId = Guid.NewGuid().ToString("N"),
            initialModel = model,
            initialProvider = settings.Provider == 1 ? "lmstudio" : "ollama",
            autoSendPromptText = prompt,
            generateTitleAfterFirstResponse = true,
            ollamaBaseUrl = BuildOllamaBaseUrl(settings.Endpoint),
            lmStudioBaseUrl = RewriteLocalhostToLoopback(settings.LMStudioBaseUrl),
            keepAliveSeconds = settings.KeepAliveSeconds,
        };
        var webView = CreateChatWebView();
        var appWindow = new Window
        {
            Title = "RunMind", Width = 1120, Height = 836, MinWidth = 680, MinHeight = 520,
            Icon = LoadWindowIcon(assemblyDirectory),
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = webView,
        };
        appWindow.Closed += (_, _) => DisposeWebViewSafely(webView);
        WindowLayout.ConfigureInitialPlacement(appWindow);
        appWindow.Show();
        appWindow.Activate();
        _ = InitializeChatWebViewAsync(appWindow, webView, assemblyDirectory, JsonSerializer.Serialize(bootstrap));
    }

    private void ShowNativeResponseWindow(SettingsSnapshot settings, string model, string? prompt, string response, bool isError)
    {
        // Title generation is deliberately deferred until the user explicitly opens Chat.
        // Inline PowerToys Run queries never make this additional model request.
        var safePrompt = prompt ?? string.Empty;
        var fallbackTitle = CreateFallbackConversationTitle(safePrompt);
        var navigation = BuildChatNavigation(fallbackTitle, model, safePrompt, response, isError, settings);
        var webView = CreateChatWebView();

        var appWindow = new Window
        {
            Title = "RunMind",
            Width = 1120,
            Height = 836,
            MinWidth = 680,
            MinHeight = 520,
            Icon = LoadWindowIcon(Path.GetDirectoryName(typeof(Main).Assembly.Location) ?? AppContext.BaseDirectory),
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = webView,
        };

        appWindow.Closed += (_, _) => DisposeWebViewSafely(webView);
        WindowLayout.ConfigureInitialPlacement(appWindow);
        appWindow.Show();
        appWindow.Activate();
        // The page's initialization sets its fallback title. Run title generation only
        // after that initialization has completed; starting both tasks concurrently can
        // let the fallback title overwrite a generated title when the model responds first.
        _ = InitializeNativeResponseWindowAsync(
            appWindow, webView, navigation.FolderPath, navigation.BootstrapJson,
            model, safePrompt, response, settings, fallbackTitle, !isError);
    }

    private async Task InitializeNativeResponseWindowAsync(
        Window appWindow,
        WebView2 webView,
        string appFolderPath,
        string bootstrapJson,
        string model,
        string prompt,
        string responseText,
        SettingsSnapshot settings,
        string fallbackTitle,
        bool generateTitle)
    {
        await InitializeChatWebViewAsync(appWindow, webView, appFolderPath, bootstrapJson);
        if (!generateTitle) return;

        // InitializeChatWebViewAsync handles its own errors. Do not spend another model
        // request if the window was closed or WebView2 did not finish initializing.
        var pageReady = await appWindow.Dispatcher.InvokeAsync(() =>
            appWindow.IsVisible
            && appWindow.Content is WebView2 activeWebView
            && activeWebView.CoreWebView2 is not null).Task.ConfigureAwait(false);
        if (!pageReady) return;

        await GenerateAndApplyConversationTitleAsync(
            appWindow, model, prompt, responseText, settings, fallbackTitle).ConfigureAwait(false);
    }

    private void ShowSavedConversationWindow(string assemblyDirectory, string conversationJson, string fileName)
    {
        var htmlPath = Path.Combine(assemblyDirectory, "ChatWindow.html");
        if (!File.Exists(htmlPath))
            throw new FileNotFoundException("ChatWindow.html was not found beside the RunMind plugin assembly.", htmlPath);

        using var sessionDocument = JsonDocument.Parse(conversationJson);
        var initialConversation = sessionDocument.RootElement.Clone();
        var title = Path.GetFileNameWithoutExtension(fileName);
        var bootstrap = new
        {
            title = string.IsNullOrWhiteSpace(title) ? "RunMind Chat" : title,
            appearance = AppearancePreferenceStore.CreateBootstrapValue(),
            windowInstanceId = Guid.NewGuid().ToString("N"),
            initialConversation,
            initialConversationFileName = fileName,
        };

        var webView = CreateChatWebView();
        var appWindow = new Window
        {
            Title = "RunMind",
            Width = 1120,
            Height = 836,
            MinWidth = 680,
            MinHeight = 520,
            Icon = LoadWindowIcon(assemblyDirectory),
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = webView,
        };

        appWindow.Closed += (_, _) => DisposeWebViewSafely(webView);
        WindowLayout.ConfigureInitialPlacement(appWindow);
        appWindow.Show();
        appWindow.Activate();
        _ = InitializeChatWebViewAsync(appWindow, webView, assemblyDirectory, JsonSerializer.Serialize(bootstrap));
    }

    private void HandleOpenConversationInNewWindow(string source, string messageJson, string assemblyDirectory)
    {
        try
        {
            if (!Uri.TryCreate(source, UriKind.Absolute, out var sourceUri)
                || sourceUri.Scheme != Uri.UriSchemeHttps
                || !sourceUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || !sourceUri.AbsolutePath.Equals("/ChatWindow.html", StringComparison.OrdinalIgnoreCase))
                return;

            if (string.IsNullOrWhiteSpace(messageJson)
                || Encoding.UTF8.GetByteCount(messageJson) > 55 * 1024 * 1024)
                return;

            using var messageDocument = JsonDocument.Parse(messageJson);
            var root = messageDocument.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "runmind.openConversationInNewWindow"
                || !root.TryGetProperty("conversation", out var conversation)
                || conversation.ValueKind != JsonValueKind.Object
                || !IsSupportedConversation(conversation))
                return;

            var fileName = root.TryGetProperty("fileName", out var fileNameElement) && fileNameElement.ValueKind == JsonValueKind.String
                ? Path.GetFileName(fileNameElement.GetString() ?? string.Empty)
                : "Conversation.runmind";
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "Conversation.runmind";
            if (fileName.Length > 255) fileName = fileName[..255];
            if (!fileName.EndsWith(".runmind", StringComparison.OrdinalIgnoreCase)) return;

            var conversationJson = conversation.GetRawText();
            DispatchToUi(
                () => ShowSavedConversationWindow(assemblyDirectory, conversationJson, fileName),
                "Could not open the saved conversation in a new window");
        }
        catch (Exception ex)
        {
            _context?.API.ShowMsg("Could not open the saved conversation in a new window", ex.Message);
        }
    }

    private static bool IsSupportedConversation(JsonElement conversation)
    {
        if (!conversation.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String)
            return false;
        var formatName = format.GetString();
        if (!string.Equals(formatName, "runmind", StringComparison.Ordinal))
            return false;
        if (!conversation.TryGetProperty("version", out var version) || !version.TryGetInt32(out var versionNumber) || versionNumber != 1)
            return false;
        return conversation.TryGetProperty("messages", out var messages)
            && messages.ValueKind == JsonValueKind.Array
            && messages.GetArrayLength() is > 0 and <= 10000;
    }

    private static string CreateFallbackConversationTitle(string prompt)
    {
        var value = string.Join(" ", (prompt ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(value)) return "RunMind Chat";
        value = value.Trim().TrimEnd('.', '?', '!');
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(7);
        var title = string.Join(" ", words);
        if (title.Length > 58) title = title[..55].TrimEnd() + "…";
        return char.ToUpperInvariant(title[0]) + title[1..];
    }

    private static string NormalizeGeneratedConversationTitle(string? value, string fallback)
    {
        var title = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        title = title.Trim('"', '\'', '`', ' ', ':', '-', '—');
        title = System.Text.RegularExpressions.Regex.Replace(
            title,
            @"^(?:title|conversation title)\s*:\s*",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        title = string.Join(" ", title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(title)) return fallback;
        if (title.Length > 64) title = title[..61].TrimEnd() + "…";
        return title;
    }

    private async Task GenerateAndApplyConversationTitleAsync(
        Window appWindow,
        string model,
        string prompt,
        string responseText,
        SettingsSnapshot settings,
        string fallbackTitle)
    {
        using var lifetime = new CancellationTokenSource();
        EventHandler closedHandler = (_, _) => lifetime.Cancel();
        appWindow.Closed += closedHandler;

        try
        {
            var dispatcher = appWindow.Dispatcher;
            var pageReady = false;

            // The title setter exists as soon as the HTML script is parsed, before
            // initializeRunMind has finished and applied the fallback title. Wait for
            // the explicit completion signal so the generated title cannot be overwritten.
            for (var attempt = 0; attempt < 120; attempt++)
            {
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || lifetime.IsCancellationRequested)
                    return;

                try
                {
                    var readyScriptTask = await dispatcher.InvokeAsync(() =>
                    {
                        // InitializeChatWebViewAsync can replace the original control with
                        // a fresh WebView2 instance when it retries using a temporary profile.
                        // Always target the control currently hosted by the window.
                        if (!appWindow.IsVisible || appWindow.Content is not WebView2 activeWebView
                            || activeWebView.CoreWebView2 is null)
                            return Task.FromResult("false");
                        return activeWebView.CoreWebView2.ExecuteScriptAsync(
                            "window.__runMindInitialized === true && typeof window.setConversationTitle === 'function'");
                    }).Task.ConfigureAwait(false);
                    var ready = await readyScriptTask.ConfigureAwait(false);
                    if (string.Equals(ready, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        pageReady = true;
                        break;
                    }
                }
                catch
                {
                    // Navigation, WebView disposal, or window closure can race readiness checks.
                }

                try
                {
                    await Task.Delay(250, lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
                {
                    return;
                }
            }

            // Do not spend a title-generation request if the chat page never became ready.
            if (!pageReady || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || lifetime.IsCancellationRequested)
                return;

            string title;
            try
            {
                title = await GenerateConversationTitleAsync(model, prompt, responseText, settings, lifetime.Token).ConfigureAwait(false);
                title = NormalizeGeneratedConversationTitle(title, fallbackTitle);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The fallback title is already visible. Keep it rather than silently
                // re-applying it, and retain a diagnostic explaining why generation failed.
                System.Diagnostics.Trace.WriteLine($"RunMind conversation title generation failed: {ex}");
                return;
            }

            if (string.IsNullOrWhiteSpace(title)
                || string.Equals(title, fallbackTitle, StringComparison.Ordinal)
                || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished
                || lifetime.IsCancellationRequested)
                return;

            var serializedTitle = JsonSerializer.Serialize(title);
            var expectedDocumentTitle = $"RunMind — {title}";
            var applied = false;

            // Resolve the currently hosted WebView on every attempt. A failed WebView2
            // initialization can replace the original control with a temporary-profile
            // control; applying the result to the disposed original leaves the UI unchanged.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || lifetime.IsCancellationRequested)
                    return;

                try
                {
                    var applyTitleTask = await dispatcher.InvokeAsync(() =>
                    {
                        if (!appWindow.IsVisible || appWindow.Content is not WebView2 activeWebView
                            || activeWebView.CoreWebView2 is null)
                            return Task.FromResult(string.Empty);

                        return activeWebView.CoreWebView2.ExecuteScriptAsync(
                            $"(() => {{ if (typeof window.setConversationTitle !== 'function') return ''; window.setConversationTitle({serializedTitle}); return document.title; }})()");
                    }).Task.ConfigureAwait(false);
                    var scriptResult = await applyTitleTask.ConfigureAwait(false);
                    string? appliedDocumentTitle = null;
                    try { appliedDocumentTitle = JsonSerializer.Deserialize<string>(scriptResult); }
                    catch (JsonException) { /* The page did not return a JSON string. */ }
                    applied = string.Equals(appliedDocumentTitle, expectedDocumentTitle, StringComparison.Ordinal);
                    if (applied) break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"RunMind could not apply the generated conversation title (attempt {attempt + 1}): {ex.Message}");
                }

                if (attempt < 4)
                {
                    try { await Task.Delay(200, lifetime.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                }
            }

            if (!applied)
            {
                System.Diagnostics.Trace.WriteLine("RunMind generated a conversation title, but the active chat page did not confirm applying it.");
                return;
            }

            // Keep the native window title synchronized with the confirmed page title.
            await dispatcher.InvokeAsync(() =>
            {
                if (appWindow.IsVisible) appWindow.Title = expectedDocumentTitle;
            }).Task.ConfigureAwait(false);
        }
        finally
        {
            appWindow.Closed -= closedHandler;
        }
    }

    private static string ExtractOpenAIChatCompletionText(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
            return string.Empty;

        var choice = choices[0];
        var output = new StringBuilder();
        if (choice.ValueKind == JsonValueKind.Object
            && choice.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.Object
            && message.TryGetProperty("content", out var content))
        {
            AppendOpenAITextParts(content, output);
        }

        // A few OpenAI-compatible servers still expose legacy completion text here.
        if (output.Length == 0 && choice.ValueKind == JsonValueKind.Object
            && choice.TryGetProperty("text", out var legacyText)
            && legacyText.ValueKind == JsonValueKind.String)
            output.Append(legacyText.GetString());

        return output.ToString();
    }

    private static void AppendOpenAITextParts(JsonElement value, StringBuilder output)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                output.Append(value.GetString());
                break;
            case JsonValueKind.Array:
                foreach (var part in value.EnumerateArray())
                    AppendOpenAITextParts(part, output);
                break;
            case JsonValueKind.Object:
                if (value.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    output.Append(text.GetString());
                else if (value.TryGetProperty("content", out var nestedContent))
                    AppendOpenAITextParts(nestedContent, output);
                break;
        }
    }

    private static string TryReadOpenAIFinishReason(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].ValueKind == JsonValueKind.Object
            && choices[0].TryGetProperty("finish_reason", out var finishReason))
            return finishReason.ValueKind == JsonValueKind.String ? finishReason.GetString() ?? "unknown" : finishReason.ToString();
        return "missing";
    }

    private static string TryReadOpenAIMessageFieldNames(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].ValueKind == JsonValueKind.Object
            && choices[0].TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.Object)
            return string.Join(",", message.EnumerateObject().Select(property => property.Name));
        return "missing";
    }

    private static async Task<string> GenerateConversationTitleAsync(
        string model,
        string prompt,
        string responseText,
        SettingsSnapshot settings,
        CancellationToken cancellationToken)
    {
        var titlePrompt = "Create a concise chat title of 3 to 7 words. Use the user's request and the assistant's response to identify the actual topic. Return only the title, without quotes, markdown, or explanation.\n\nUSER REQUEST:\n"
            + (prompt ?? string.Empty)[..Math.Min((prompt ?? string.Empty).Length, 1800)]
            + "\n\nASSISTANT RESPONSE:\n"
            + (responseText ?? string.Empty)[..Math.Min((responseText ?? string.Empty).Length, 1800)];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        if (settings.Provider == 1)
        {
            var payload = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["model"] = model,
                ["messages"] = new object[]
                {
                    new { role = "system", content = "You generate short, accurate conversation titles. Return only a title." },
                    new { role = "user", content = titlePrompt },
                },
                ["stream"] = false,
                ["temperature"] = 0.2,
                ["max_tokens"] = 32,
                // As with the normal LM Studio chat path, don't spend this short
                // title-generation budget on reasoning tokens.
                ["reasoning_effort"] = "none",
            };
            // LM Studio templates can expose thinking controls through
            // chat_template_kwargs. Disable thinking for this short one-off request;
            // preserve the optional Nemotron conversational-mode preference too.
            var chatTemplateParameters = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["enable_thinking"] = false,
            };
            if (IsNemotron35BoltE4BModel(model))
                chatTemplateParameters["conversational_mode"] = settings.InlineConversationalMode;
            payload["chat_template_kwargs"] = chatTemplateParameters;

            byte[]? responseBytes = null;
            var optionalTitleParameters = new (string Key, string Pattern)[]
            {
                ("reasoning_effort", @"(?:unknown|unsupported|unrecognized|unexpected|not supported|does not support)[^\r\n]{0,120}reasoning_effort|reasoning_effort[^\r\n]{0,120}(?:unknown|unsupported|unrecognized|unexpected|not supported|does not support)"),
                ("chat_template_kwargs", @"(?:unknown|unsupported|unrecognized|unexpected|not supported|does not support)[^\r\n]{0,120}(?:chat_template_kwargs|enable_thinking|conversational_mode)|(?:chat_template_kwargs|enable_thinking|conversational_mode)[^\r\n]{0,120}(?:unknown|unsupported|unrecognized|unexpected|not supported|does not support)"),
                ("max_tokens", @"(?:unknown|unsupported|unrecognized|unexpected|not supported|does not support)[^\r\n]{0,120}max_tokens|max_tokens[^\r\n]{0,120}(?:unknown|unsupported|unrecognized|unexpected|not supported|does not support)"),
            };

            for (var attempt = 0; attempt <= optionalTitleParameters.Length; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, BuildLMStudioUri(settings.LMStudioBaseUrl, "chat/completions"))
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                };
                using var result = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (result.IsSuccessStatusCode)
                {
                    responseBytes = await ReadBoundedContentBytesAsync(result.Content, 512 * 1024, timeout.Token).ConfigureAwait(false);
                    break;
                }

                var errorBody = await ReadBoundedContentTextAsync(result.Content, 64 * 1024, timeout.Token).ConfigureAwait(false);
                string? rejectedParameter = null;
                if ((int)result.StatusCode is 400 or 422)
                {
                    foreach (var optionalParameter in optionalTitleParameters)
                    {
                        if (payload.ContainsKey(optionalParameter.Key)
                            && System.Text.RegularExpressions.Regex.IsMatch(errorBody, optionalParameter.Pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        {
                            rejectedParameter = optionalParameter.Key;
                            break;
                        }
                    }
                }

                if (rejectedParameter is not null)
                {
                    payload.Remove(rejectedParameter);
                    continue;
                }

                var detail = string.IsNullOrWhiteSpace(errorBody) ? string.Empty : $" Response: {TrimForError(errorBody)}";
                throw new HttpRequestException($"LM Studio title request returned HTTP {(int)result.StatusCode}.{detail}", null, result.StatusCode);
            }

            if (responseBytes is null)
                throw new InvalidDataException("LM Studio title request did not return a response after compatibility retries.");
            using var document = JsonDocument.Parse(responseBytes);
            var title = ExtractOpenAIChatCompletionText(document.RootElement);
            if (string.IsNullOrWhiteSpace(title))
            {
                var finishReason = TryReadOpenAIFinishReason(document.RootElement);
                var messageFields = TryReadOpenAIMessageFieldNames(document.RootElement);
                throw new InvalidDataException($"LM Studio title response contained no assistant text (finish_reason: {finishReason}; message fields: {messageFields}).");
            }
            return title;
        }
        else
        {
            var payload = new
            {
                model,
                prompt = titlePrompt,
                stream = false,
                think = false,
                keep_alive = settings.KeepAliveSeconds,
                options = new { temperature = 0.2, num_predict = 32 },
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, ValidateGenerateEndpoint(settings.Endpoint))
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            using var result = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!result.IsSuccessStatusCode) throw new HttpRequestException($"Ollama title request returned HTTP {(int)result.StatusCode}.");
            var responseBytes = await ReadBoundedContentBytesAsync(result.Content, 512 * 1024, timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(responseBytes);
            return document.RootElement.TryGetProperty("response", out var response) ? response.GetString() ?? string.Empty : string.Empty;
        }
    }

    private async Task InitializeChatWebViewAsync(
        Window appWindow,
        WebView2 webView,
        string appFolderPath,
        string bootstrapJson,
        bool useTemporaryUserDataFolder = false,
        Exception? primaryInitializationException = null)
    {
        var windowThemeSynchronizer = WindowThemeSynchronizers.GetValue(appWindow, static window => new WindowThemeSynchronizer(window));
        string userDataFolder = "(not resolved)";
        string stage = "Resolve WebView2 user-data folder";
        try
        {
            // PowerToys and Standalone use separate profiles, avoiding cross-host profile
            // contention while preserving a deterministic profile location per host.
            userDataFolder = GetWebView2UserDataFolder(useTemporaryUserDataFolder);
            stage = "Ensure Microsoft Edge WebView2 Runtime";
            await WebView2RuntimeBootstrapper.EnsureInstalledAsync();
            if (!appWindow.IsVisible) return;
            stage = "Create WebView2 environment";
            var environment = await GetWebView2EnvironmentAsync(useTemporaryUserDataFolder, userDataFolder);
            if (!appWindow.IsVisible) return;
            stage = "Initialize WebView2 control";
            await webView.EnsureCoreWebView2Async(environment);
            if (!appWindow.IsVisible) return;

            stage = "Configure WebView2 origin and navigation policy";
            var core = webView.CoreWebView2;
            // Give the static page a stable localhost origin so browser CORS policies can
            // recognize the page. API requests are still issued directly by JavaScript.
            core.SetVirtualHostNameToFolderMapping(
                "localhost",
                appFolderPath,
                CoreWebView2HostResourceAccessKind.DenyCors);

            // Expose only the bounded native public-page reader, conversation Save As,
            // and validated open-in-new-window operations. General host objects stay disabled.
            var nativePageReader = new NativePageReader();
            appWindow.Closed += (_, _) => nativePageReader.Dispose();
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.AreHostObjectsAllowed = false;
            core.WebMessageReceived += (_, messageArgs) =>
            {
                if (windowThemeSynchronizer.TryHandleWebMessage(messageArgs.Source, messageArgs.WebMessageAsJson))
                    return;
                if (nativePageReader.TryHandleMessage(
                    messageArgs.Source, messageArgs.WebMessageAsJson, core, appWindow.Dispatcher))
                    return;
                if (!ConversationSaveDialog.TryHandleMessage(
                    messageArgs.Source, messageArgs.WebMessageAsJson, appWindow, core))
                {
                    HandleOpenConversationInNewWindow(messageArgs.Source, messageArgs.WebMessageAsJson, appFolderPath);
                }
            };
            core.DocumentTitleChanged += (_, _) =>
            {
                // An initialization retry can leave the original control's events queued.
                // Only the WebView2 currently hosted in this window owns its native title.
                if (!appWindow.IsVisible || !ReferenceEquals(appWindow.Content, webView)) return;
                var documentTitle = core.DocumentTitle;
                if (!string.IsNullOrWhiteSpace(documentTitle) && documentTitle.Length <= 100)
                    appWindow.Title = documentTitle;
            };
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
                if (!appWindow.IsVisible || !ReferenceEquals(appWindow.Content, webView)) return;
                if (!e.IsSuccess)
                {
                    if (ReferenceEquals(appWindow.Content, webView)) appWindow.Content = null;
                    DisposeWebViewSafely(webView);
                    appWindow.Content = new TextBlock
                    {
                        Margin = new Thickness(24),
                        TextWrapping = TextWrapping.Wrap,
                        Text = "The RunMind chat page could not be loaded. Check that ChatWindow.html and Assets/Brand/Icon.svg are deployed beside the plugin DLL.",
                    };
                    return;
                }

                try
                {
                    // This one-way call supplies only the original query/result and settings.
                    // All model discovery and subsequent chat turns stay in page JavaScript.
                    await core.ExecuteScriptAsync("window.initializeRunMind(" + bootstrapJson + ");");
                }
                catch (Exception ex)
                {
                    _context?.API.ShowMsg("Could not initialize the RunMind chat session", ex.Message);
                }
            };
            stage = "Navigate to ChatWindow.html";
            core.Navigate("https://localhost/ChatWindow.html");
        }
        catch (Exception ex) when (!useTemporaryUserDataFolder && ShouldRetryWebViewWithTemporaryProfile(ex))
        {
            // Access-denied and WebView2 invalid-state failures can be associated with the
            // selected user-data folder/session. Retry once with a separate profile and a
            // fresh control; never reuse a control whose initialization already failed.
            if (!appWindow.IsVisible)
            {
                DisposeWebViewSafely(webView);
                return;
            }

            if (ReferenceEquals(appWindow.Content, webView))
                appWindow.Content = null;
            DisposeWebViewSafely(webView);
            var fallbackWebView = CreateChatWebView();
            appWindow.Content = fallbackWebView;
            appWindow.Closed += (_, _) => DisposeWebViewSafely(fallbackWebView);
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
            if (!appWindow.IsVisible) return;
            var priorFailure = primaryInitializationException is null
                ? string.Empty
                : $"\nPrimary profile attempt also failed: {primaryInitializationException.Message}";
            var message = "The RunMind chat window could not initialize WebView2. "
                + ex.Message
                + $"\nFailed stage: {stage}"
                + $"\nHRESULT: 0x{ex.HResult:X8}"
                + $"\nUser-data folder: {userDataFolder}"
                + priorFailure
                + $"\nException details: {ex}"
                + "\nRunMind checks for WebView2 and installs Microsoft's Evergreen Runtime automatically when it is missing. If setup failed, check your internet connection and Windows permissions.";
            if (ReferenceEquals(appWindow.Content, webView)) appWindow.Content = null;
            DisposeWebViewSafely(webView);
            appWindow.Content = new TextBlock
            {
                Margin = new Thickness(24),
                TextWrapping = TextWrapping.Wrap,
                Text = message,
            };
            _context?.API.ShowMsg("Could not initialize the RunMind chat window", message);
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
            var environmentOptions = new CoreWebView2EnvironmentOptions();
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: environmentOptions);

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
            return Path.Combine(Path.GetTempPath(), "PowerToys-Run-LocalLLM", "PowerToys", "WebView2");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("Windows did not provide a Local AppData path for the current user.");
        }

        return Path.Combine(localAppData, "Microsoft", "PowerToys", "LocalLLM", "PowerToys", "WebView2");
    }

    private static bool ShouldRetryWebViewWithTemporaryProfile(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException
                || current.HResult == unchecked((int)0x80070005) // E_ACCESSDENIED
                || current.HResult == unchecked((int)0x8007139F)) // ERROR_INVALID_STATE
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
                "ChatWindow.html was not found beside the RunMind plugin assembly. Ensure the file is copied to the plugin output directory.",
                htmlPath);
        }

        var thinkInitiallyEnabled = TryResolveThinkValue(model, settings.Thinking, out var thinkValue)
            && (thinkValue is bool boolValue
                ? boolValue
                : !string.Equals(Convert.ToString(thinkValue, System.Globalization.CultureInfo.InvariantCulture), "false", StringComparison.OrdinalIgnoreCase));
        var bootstrap = new
        {
            title,
            appearance = AppearancePreferenceStore.CreateBootstrapValue(),
            windowInstanceId = Guid.NewGuid().ToString("N"),
            initialModel = model,
            initialProvider = settings.Provider == 1 ? "lmstudio" : "ollama",
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

    private sealed class QueryOperation : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly TaskCompletionSource<bool> _finishedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _sync = new();
        private int _started;
        private int _cancelRequested;
        private int _handedOff;
        private bool _finished;
        private bool _disposed;
        private bool _cancelInProgress;
        private bool _disposeRequested;
        private bool _hasCompletion;
        private string _response = string.Empty;
        private bool _isError;

        public QueryOperation(string search, string model, string prompt, SettingsSnapshot settings)
        {
            Search = search;
            Model = model;
            Prompt = prompt;
            Settings = settings;
        }

        public string Search { get; }
        public string Model { get; }
        public string Prompt { get; }
        public SettingsSnapshot Settings { get; }
        public CancellationToken Token
        {
            get
            {
                lock (_sync) return _cancellation.Token;
            }
        }
        public bool IsCancellationRequested => Volatile.Read(ref _cancelRequested) != 0;
        public bool WasHandedOff => Volatile.Read(ref _handedOff) != 0;

        public bool TryStart()
        {
            lock (_sync)
            {
                if (_disposed || IsCancellationRequested || WasHandedOff || _started != 0) return false;
                _started = 1;
                return true;
            }
        }

        public void WaitForFinish()
        {
            _finishedSignal.Task.GetAwaiter().GetResult();
        }

        public void Cancel()
        {
            CancellationTokenSource? sourceToCancel = null;
            lock (_sync)
            {
                if (!_disposed && Interlocked.Exchange(ref _cancelRequested, 1) == 0)
                {
                    _cancelInProgress = true;
                    sourceToCancel = _cancellation;
                }

                if (_started == 0 || _finished) _disposeRequested = true;
                TryDisposeSourceLocked();
            }

            // Cancellation callbacks may execute synchronously and can re-enter plugin
            // code. Never invoke CancellationTokenSource.Cancel while holding _sync.
            if (sourceToCancel is not null)
            {
                try { sourceToCancel.Cancel(); }
                catch (ObjectDisposedException) { }
                finally
                {
                    lock (_sync)
                    {
                        _cancelInProgress = false;
                        TryDisposeSourceLocked();
                    }
                }
            }
        }

        public bool TryGetCompletion(out string response, out bool isError)
        {
            lock (_sync)
            {
                if (_hasCompletion && !IsCancellationRequested && !WasHandedOff)
                {
                    response = _response;
                    isError = _isError;
                    return true;
                }

                response = string.Empty;
                isError = false;
                return false;
            }
        }

        public bool TryGetCompletionOrMarkHandoff(out string response, out bool isError)
        {
            lock (_sync)
            {
                if (_hasCompletion && !IsCancellationRequested && !WasHandedOff)
                {
                    response = _response;
                    isError = _isError;
                    // Claim the pending result exactly once. A double-click must not open
                    // multiple windows from the same query operation.
                    Interlocked.Exchange(ref _handedOff, 1);
                    return true;
                }

                response = string.Empty;
                isError = false;
                // Mark handoff before releasing the lock. SetCompletion and IsCurrentQuery
                // will reject the operation immediately, even before HTTP cancellation runs.
                Interlocked.Exchange(ref _handedOff, 1);
                return false;
            }
        }

        public void SetCompletion(string response, bool isError)
        {
            lock (_sync)
            {
                if (_finished || _disposed || IsCancellationRequested || WasHandedOff) return;
                _response = response;
                _isError = isError;
                _hasCompletion = true;
            }
        }

        public void MarkFinished()
        {
            lock (_sync)
            {
                _finished = true;
                _disposeRequested = true;
                TryDisposeSourceLocked();
            }

            _finishedSignal.TrySetResult(true);
        }

        public void Dispose()
        {
            Cancel();
            lock (_sync)
            {
                _disposeRequested = true;
                TryDisposeSourceLocked();
            }
        }

        private void TryDisposeSourceLocked()
        {
            if (!_disposeRequested || _cancelInProgress || _disposed) return;
            if (_started != 0 && !_finished) return;
            _disposed = true;
            _cancellation.Dispose();
        }
    }

    private sealed record SettingsSnapshot(
        string Endpoint,
        string LMStudioBaseUrl,
        int Provider,
        string DefaultModel,
        string Thinking,
        int KeepAliveSeconds,
        int RequestTimeoutSeconds,
        int InlineCpuThreads,
        string InlineSystemPrompt,
        double? InlineTemperature,
        double? InlineTopP,
        int? InlineMaxOutputTokens,
        int? InlineSeed,
        bool InlineConversationalMode);
}
