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
using System.Text.Json.Nodes;
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
    private const string DefaultModel = "";
    private const string DefaultThinking = "auto";
    private const int DefaultKeepAliveSeconds = 300;
    private const int DefaultRequestTimeoutSeconds = 600;
    private const int DefaultModelDiscoveryTimeoutSeconds = 8;
    private const int MinimumStageTimeoutSeconds = 1;
    private const int MaximumStageTimeoutSeconds = 300;
    private const int DefaultInlineCpuThreads = 0;
    private const int MaximumInlineSystemPromptCharacters = 12000;
    private const int MaximumInlineResponseCharacters = 8 * 1024 * 1024;

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
    private string _iconPath = @"Images\256x256.png";
    private string _endpoint = DefaultEndpoint;
    private string _lmStudioBaseUrl = DefaultLMStudioBaseUrl;
    private string _defaultModel = DefaultModel;
    private string _thinking = DefaultThinking;
    private int _provider;
    private long _settingsVersion;
    private long _sharedSettingsWriteTimeUtcTicks;
    private int _keepAliveSeconds = DefaultKeepAliveSeconds;
    private int _requestTimeoutSeconds = DefaultRequestTimeoutSeconds;
    private int _modelDiscoveryTimeoutSeconds = DefaultModelDiscoveryTimeoutSeconds;
    private int _inlineCpuThreads = DefaultInlineCpuThreads;
    private string _inlineSystemPrompt = string.Empty;
    private string _ollamaApiKey = string.Empty;
    private string _ollamaAuthHeader = string.Empty;
    private string _lmStudioApiKey = string.Empty;
    private string _lmStudioAuthHeader = string.Empty;
    private double? _inlineTemperature;
    private double? _inlineTopP;
    private int? _inlineMaxOutputTokens;
    private int? _inlineSeed;
    private bool _inlineConversationalMode;

    public IEnumerable<PluginAdditionalOption> AdditionalOptions
    {
        get
        {
            var shared = LoadSharedInlineSettingsForOptions();
            return new List<PluginAdditionalOption>
            {
        new()
        {
            Key = "InlineProvider",
            DisplayLabel = "Inline model provider",
            DisplayDescription = "Shared by PowerToys Run and Command Palette inline answers. Full chat-window provider settings remain separate.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Combobox,
            ComboBoxItems = new List<KeyValuePair<string, string>>
            {
                new("Ollama", "0"),
                new("LM Studio", "1"),
            },
            ComboBoxValue = shared.Provider == "lmstudio" ? 1 : 0,
        },
        new()
        {
            Key = "LLMEndpoint",
            DisplayLabel = "Ollama API endpoint",
            DisplayDescription = "Full Ollama generate endpoint, for example http://localhost:11434/api/generate.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.OllamaEndpoint,
        },
        new()
        {
            Key = "OllamaApiKey",
            DisplayLabel = "Ollama API key (optional)",
            DisplayDescription = "PowerToys Run only. Sends Authorization: Bearer <key> to Ollama model discovery and generation requests. Stored in the local per-user RunMind settings file; leave empty when not required.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.PowerToysRunOllamaApiKey,
            TextBoxMaxLength = 4096,
            PlaceholderText = "Optional Ollama API key...",
        },
        new()
        {
            Key = "OllamaAuthHeader",
            DisplayLabel = "Ollama custom auth header (optional)",
            DisplayDescription = "PowerToys Run only. Full header in the form Header-Name: value. A custom Authorization header replaces the Bearer API key header.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.PowerToysRunOllamaAuthHeader,
            TextBoxMaxLength = 8192,
            PlaceholderText = "Header-Name: value",
        },
        new()
        {
            Key = "LMStudioBaseUrl",
            DisplayLabel = "LM Studio base URL",
            DisplayDescription = "OpenAI-compatible LM Studio base URL used by both inline-answer hosts, for example http://localhost:1234/v1.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.LMStudioBaseUrl,
        },
        new()
        {
            Key = "LMStudioApiKey",
            DisplayLabel = "LM Studio API key (optional)",
            DisplayDescription = "PowerToys Run only. Sends Authorization: Bearer <key> to LM Studio model discovery and generation requests. Stored in the local per-user RunMind settings file; leave empty when not required.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.PowerToysRunLMStudioApiKey,
            TextBoxMaxLength = 4096,
            PlaceholderText = "Optional LM Studio API key...",
        },
        new()
        {
            Key = "LMStudioAuthHeader",
            DisplayLabel = "LM Studio custom auth header (optional)",
            DisplayDescription = "PowerToys Run only. Full header in the form Header-Name: value. A custom Authorization header replaces the Bearer API key header.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.PowerToysRunLMStudioAuthHeader,
            TextBoxMaxLength = 8192,
            PlaceholderText = "Header-Name: value",
        },
        new()
        {
            Key = "Model",
            DisplayLabel = "Default model",
            DisplayDescription = "Model identifier as shown by the selected provider (Ollama model name or LM Studio model ID). You can override it per query with > model > prompt.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.Model,
        },
        new()
        {
            Key = "Thinking",
            DisplayLabel = "Thinking / reasoning",
            DisplayDescription = "Shared mode for Ollama and LM Studio. Auto uses the model default; the other choices are mapped to each provider's API. Model support varies.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Combobox,
            ComboBoxItems = new List<KeyValuePair<string, string>>
            {
                new("Auto — model default", "0"),
                new("On", "1"),
                new("Off", "2"),
                new("Low", "3"),
                new("Medium", "4"),
                new("High", "5"),
            },
            ComboBoxValue = GetThinkingChoiceIndex(shared.Thinking),
        },
        new()
        {
            Key = "KeepAliveSeconds",
            DisplayLabel = "Keep model loaded (seconds)",
            DisplayDescription = "Ollama only: value sent as keep_alive. Use 0 to unload the model after the request. Range: 0–86400 seconds.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.KeepAliveSeconds.ToString(CultureInfo.InvariantCulture),
        },
        new()
        {
            Key = "RequestTimeoutSeconds",
            DisplayLabel = "Request timeout (seconds)",
            DisplayDescription = "Maximum time for an inline generation request in either PowerToys Run or Command Palette (30–3600 seconds). Does not affect full chat sessions.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
        },
        new()
        {
            Key = "ModelDiscoveryTimeoutSeconds",
            DisplayLabel = "Model discovery timeout (seconds)",
            DisplayDescription = "Maximum time to request and read the selected provider's model list before generation starts. Default: 8 seconds. Range: 1–300 seconds.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.ModelDiscoveryTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
        },
        new()
        {
            Key = "InlineCpuThreads",
            DisplayLabel = "Ollama max CPU threads (optional)",
            DisplayDescription = "Optional Ollama-only CPU thread limit shared by both inline-answer hosts. Use 0 for automatic selection or 1–128 to set a maximum. Does not affect LM Studio or full chat sessions.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.InlineCpuThreads.ToString(CultureInfo.InvariantCulture),
        },
        new()
        {
            Key = "InlineSystemPrompt",
            DisplayLabel = "Inline system instructions",
            DisplayDescription = "System instructions shared by PowerToys Run and Command Palette inline answers. Edit or clear them freely; an empty value disables the additional prompt. Maximum 12,000 characters.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.MultilineTextbox,
            TextValue = shared.InlineSystemPrompt,
            TextBoxMaxLength = MaximumInlineSystemPromptCharacters,
            PlaceholderText = "Enter shared inline-answer system instructions...",
        },
        new()
        {
            Key = "InlineTemperature",
            DisplayLabel = "Temperature (optional)",
            DisplayDescription = shared.Provider == "lmstudio"
                ? "Shared inline generation setting. Leave empty for the model default. LM Studio range: 0–1."
                : "Shared inline generation setting. Leave empty for the model default. Ollama range: 0–2.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.InlineTemperature?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        },
        new()
        {
            Key = "InlineTopP",
            DisplayLabel = "Top P (optional)",
            DisplayDescription = "Shared inline generation setting. Leave empty for the model default. Range: 0–1.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.InlineTopP?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        },
        new()
        {
            Key = "InlineMaxOutputTokens",
            DisplayLabel = "Max output tokens (optional)",
            DisplayDescription = "Shared inline output limit. Leave empty for the model default. Range: 1–1,000,000.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.InlineMaxOutputTokens?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        },
        new()
        {
            Key = "InlineSeed",
            DisplayLabel = "Seed (optional; Ollama / LM Studio compatibility mode)",
            DisplayDescription = "Shared inline reproducibility setting. Leave empty for a random/provider default seed. Range: 0–2,147,483,647.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = shared.InlineSeed?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        },
        new()
        {
            Key = "InlineConversationalMode",
            DisplayLabel = "Nemotron 3.5 Bolt E4B conversational mode",
            DisplayDescription = "Optional and OFF by default. When enabled, sends chat_template_kwargs.conversational_mode=true for matching NVIDIA Nemotron 3.5 Bolt E4B model IDs through the LM Studio / OpenAI-compatible provider only. Ollama's native API does not expose this template parameter.",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Checkbox,
            Value = shared.InlineConversationalMode,
        },
            };
        }
    }

    public void Init(PluginInitContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _context.API.ThemeChanged += OnThemeChanged;
        UpdateIconPath(_context.API.GetCurrentTheme());
    }

    public void UpdateSettings(PowerLauncherPluginSettings settings)
    {
        // Use the shared file as the baseline so a partial/older PowerToys settings
        // payload cannot reset values recently changed from Command Palette.
        var initial = LoadSharedInlineSettingsForOptions();
        string endpoint = initial.OllamaEndpoint;
        string lmStudioBaseUrl = initial.LMStudioBaseUrl;
        string model = initial.Model;
        string thinking = initial.Thinking;
        int provider = initial.Provider == "lmstudio" ? 1 : 0;
        int keepAliveSeconds = initial.KeepAliveSeconds;
        int requestTimeoutSeconds = initial.RequestTimeoutSeconds;
        int modelDiscoveryTimeoutSeconds = initial.ModelDiscoveryTimeoutSeconds;
        int inlineCpuThreads = initial.InlineCpuThreads;
        string inlineSystemPrompt = initial.InlineSystemPrompt;
        string ollamaApiKey = initial.PowerToysRunOllamaApiKey;
        string ollamaAuthHeader = initial.PowerToysRunOllamaAuthHeader;
        string lmStudioApiKey = initial.PowerToysRunLMStudioApiKey;
        string lmStudioAuthHeader = initial.PowerToysRunLMStudioAuthHeader;
        double? inlineTemperature = initial.InlineTemperature;
        double? inlineTopP = initial.InlineTopP;
        int? inlineMaxOutputTokens = initial.InlineMaxOutputTokens;
        int? inlineSeed = initial.InlineSeed;
        bool inlineConversationalMode = initial.InlineConversationalMode;

        if (settings?.AdditionalOptions is not null)
        {
            var options = settings.AdditionalOptions;
            endpoint = GetTextOption(options, "LLMEndpoint", endpoint);
            lmStudioBaseUrl = GetTextOption(options, "LMStudioBaseUrl", lmStudioBaseUrl);
            var providerOption = options.FirstOrDefault(option => option.Key == "InlineProvider");
            if (providerOption is not null)
                provider = providerOption.ComboBoxValue == 1 ? 1 : 0;
            model = GetTextOption(options, "Model", model);
            ollamaApiKey = ReadOptionalTextOption(options, "OllamaApiKey", ollamaApiKey, 4096);
            ollamaAuthHeader = ReadOptionalTextOption(options, "OllamaAuthHeader", ollamaAuthHeader, 8192);
            lmStudioApiKey = ReadOptionalTextOption(options, "LMStudioApiKey", lmStudioApiKey, 4096);
            lmStudioAuthHeader = ReadOptionalTextOption(options, "LMStudioAuthHeader", lmStudioAuthHeader, 8192);
            thinking = ReadThinkingOption(options, thinking);
            var inlinePromptOption = options.FirstOrDefault(option => option.Key == "InlineSystemPrompt");
            if (inlinePromptOption is null)
            {
                inlineSystemPrompt = initial.InlineSystemPrompt;
            }
            else
            {
                inlineSystemPrompt = NormalizeInlineSystemPrompt(inlinePromptOption.TextValue ?? string.Empty);
            }
            if (options.Any(option => option.Key == "InlineTemperature"))
                inlineTemperature = ParseOptionalDouble(options, "InlineTemperature", 0, provider == 1 ? 1 : 2);
            if (options.Any(option => option.Key == "InlineTopP"))
                inlineTopP = ParseOptionalDouble(options, "InlineTopP", 0, 1);
            if (options.Any(option => option.Key == "InlineMaxOutputTokens"))
                inlineMaxOutputTokens = ParseOptionalInt(options, "InlineMaxOutputTokens", 1, 1000000);
            if (options.Any(option => option.Key == "InlineSeed"))
                inlineSeed = ParseOptionalInt(options, "InlineSeed", 0, int.MaxValue);
            var conversationalOption = options.FirstOrDefault(option => option.Key == "InlineConversationalMode");
            if (conversationalOption?.Value is bool enabled)
                inlineConversationalMode = enabled;

            if (int.TryParse(GetTextOption(options, "KeepAliveSeconds", initial.KeepAliveSeconds.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedKeepAlive)
                && parsedKeepAlive is >= 0 and <= 86400)
            {
                keepAliveSeconds = parsedKeepAlive;
            }

            if (int.TryParse(GetTextOption(options, "RequestTimeoutSeconds", initial.RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTimeout)
                && parsedTimeout is >= 30 and <= 3600)
            {
                requestTimeoutSeconds = parsedTimeout;
            }

            if (int.TryParse(GetTextOption(options, "ModelDiscoveryTimeoutSeconds", initial.ModelDiscoveryTimeoutSeconds.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDiscoveryTimeout)
                && parsedDiscoveryTimeout is >= MinimumStageTimeoutSeconds and <= MaximumStageTimeoutSeconds)
            {
                modelDiscoveryTimeoutSeconds = parsedDiscoveryTimeout;
            }

            if (int.TryParse(GetTextOption(options, "InlineCpuThreads", initial.InlineCpuThreads.ToString(CultureInfo.InvariantCulture)), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCpuThreads)
                && parsedCpuThreads is >= 0 and <= 128)
            {
                inlineCpuThreads = parsedCpuThreads;
            }
        }

        QueryOperation? previous;
        lock (_settingsLock)
        {
            _endpoint = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint.Trim();
            _provider = provider;
            _lmStudioBaseUrl = string.IsNullOrWhiteSpace(lmStudioBaseUrl) ? DefaultLMStudioBaseUrl : lmStudioBaseUrl.Trim().TrimEnd('/');
            _defaultModel = string.IsNullOrWhiteSpace(model) ? string.Empty : model.Trim();
            _thinking = string.IsNullOrWhiteSpace(thinking) ? DefaultThinking : thinking.Trim();
            _keepAliveSeconds = keepAliveSeconds;
            _requestTimeoutSeconds = requestTimeoutSeconds;
            _modelDiscoveryTimeoutSeconds = modelDiscoveryTimeoutSeconds;
            _inlineCpuThreads = inlineCpuThreads;
            _inlineSystemPrompt = inlineSystemPrompt;
            _ollamaApiKey = ollamaApiKey;
            _ollamaAuthHeader = ollamaAuthHeader;
            _lmStudioApiKey = lmStudioApiKey;
            _lmStudioAuthHeader = lmStudioAuthHeader;
            _inlineTemperature = inlineTemperature;
            _inlineTopP = inlineTopP;
            _inlineMaxOutputTokens = inlineMaxOutputTokens;
            _inlineSeed = inlineSeed;
            _inlineConversationalMode = inlineConversationalMode;
            _settingsVersion++;
            lock (ModelCacheLock)
            {
                _modelCacheEndpoint = null;
                _modelCache.Clear();
            }

            // Publish the new settings and invalidate work using the old snapshot as one
            // coordinated operation. BeginQuery/GetOrBeginQuery use the same lock order.
            lock (_queryLock)
            {
                previous = _activeQuery;
                _activeQuery = null;
                _latestFastQuery = null;
                _hasReceivedFastQuery = true;
            }
        }

        // Cancellation callbacks may re-enter plugin code; never invoke them under either lock.
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var savedAt = InlineModelSettingsStore.Save(new InlineModelSettings
            {
                Provider = provider == 1 ? "lmstudio" : "ollama",
                OllamaEndpoint = endpoint,
                LMStudioBaseUrl = lmStudioBaseUrl,
                Model = model,
                Thinking = thinking,
                KeepAliveSeconds = keepAliveSeconds,
                RequestTimeoutSeconds = requestTimeoutSeconds,
                ModelDiscoveryTimeoutSeconds = modelDiscoveryTimeoutSeconds,
                InlineCpuThreads = inlineCpuThreads,
                InlineSystemPrompt = inlineSystemPrompt,
                CommandPaletteSystemPrompt = initial.CommandPaletteSystemPrompt,
                CommandPaletteOllamaApiKey = initial.CommandPaletteOllamaApiKey,
                CommandPaletteOllamaAuthHeader = initial.CommandPaletteOllamaAuthHeader,
                CommandPaletteLMStudioApiKey = initial.CommandPaletteLMStudioApiKey,
                CommandPaletteLMStudioAuthHeader = initial.CommandPaletteLMStudioAuthHeader,
                PowerToysRunOllamaApiKey = ollamaApiKey,
                PowerToysRunOllamaAuthHeader = ollamaAuthHeader,
                PowerToysRunLMStudioApiKey = lmStudioApiKey,
                PowerToysRunLMStudioAuthHeader = lmStudioAuthHeader,
                InlineTemperature = inlineTemperature,
                InlineTopP = inlineTopP,
                InlineMaxOutputTokens = inlineMaxOutputTokens,
                InlineSeed = inlineSeed,
                InlineConversationalMode = inlineConversationalMode,
            });
            Interlocked.Exchange(ref _sharedSettingsWriteTimeUtcTicks, savedAt.Ticks);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[RunMind] Could not persist shared inline model settings: {exception.Message}");
        }
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

        if (!TryGetValidSearchText(query, out var search))
        {
            CancelActiveQuery();
            return new List<Result>();
        }

        // Global mode accepts even a one-character prompt. PowerToys may use short
        // prefixes while typing; the normal delayed-execution path handles throttling.

#if DEBUG
        var fastQueryStartedAt = Stopwatch.GetTimestamp();
#endif
        var settings = GetSettingsSnapshot();
        var (model, prompt) = ParseModelAndPrompt(search, settings.DefaultModel);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            CancelActiveQuery();
            return new List<Result>();
        }

        var operation = BeginQuery(search, model, prompt, settings);
        var results = operation is null
            ? new List<Result>()
            : new List<Result> { CreatePendingResult(prompt) };
#if DEBUG
        Debug.WriteLine($"[RunMind] PowerToys fast query prepared in {Stopwatch.GetElapsedTime(fastQueryStartedAt).TotalMilliseconds:F1} ms.");
#endif
        return results;
    }

    // PowerToys holds a per-plugin execution gate while calling IDelayedExecutionPlugin.
    // Start generation in the background and return immediately so new keystrokes can
    // reach Query() and supersede the previous operation while the model is still running.
    public List<Result> Query(Query query, bool delayedExecution)
    {
        if (!delayedExecution || query is null)
            return new List<Result>();

        if (!TryGetValidSearchText(query, out var search))
        {
            CancelActiveQuery();
            return new List<Result>();
        }

        var currentSettings = GetSettingsSnapshot();
        var (currentModel, currentPrompt) = ParseModelAndPrompt(search, currentSettings.DefaultModel);
        if (string.IsNullOrWhiteSpace(currentPrompt))
        {
            CancelActiveQuery();
            return new List<Result>();
        }

        var operation = GetOrBeginQuery(search, currentModel, currentPrompt, currentSettings);
        if (operation is null)
            return new List<Result>();

        if (operation.TryGetCompletion(out var completedResponse, out var isError))
        {
            return new List<Result>
            {
                CreateResponseResult(operation.Model, completedResponse, isError, operation.Prompt, operation.Settings),
            };
        }

        switch (operation.TryStart())
        {
            case QueryOperation.StartResult.Started:
                // Task.Run also moves synchronous setup before the first HTTP await off the
                // PowerToys callback. The plugin gate is released as soon as this method returns.
                _ = Task.Run(() => ExecuteQueryAndRefreshAsync(operation, query));
                break;

            case QueryOperation.StartResult.AlreadyRunning:
                // Another delayed callback already owns this operation. Its completion event
                // will publish the result; never hold the plugin gate waiting for it.
                break;

            case QueryOperation.StartResult.Terminal:
                // Re-check in case the operation completed between the first check and TryStart.
                if (operation.TryGetCompletion(out completedResponse, out isError))
                {
                    return new List<Result>
                    {
                        CreateResponseResult(operation.Model, completedResponse, isError, operation.Prompt, operation.Settings),
                    };
                }
                break;
        }

        // The fast phase normally supplied the persistent "Open in chat" result. Hosts that
        // invoke only the delayed interface still receive that entry while generation runs.
        return HasFastQueryResult(search)
            ? new List<Result>()
            : new List<Result> { CreatePendingResult(currentPrompt) };
    }

    private async Task ExecuteQueryAndRefreshAsync(QueryOperation operation, Query query)
    {
        try
        {
            await ExecuteQueryAsync(operation).ConfigureAwait(false);
            if (!IsCurrentQuery(operation) || !operation.TryGetCompletion(out _, out _))
                return;

            // Do not depend on IResultUpdated being wired by every supported PowerToys build.
            // Force one normal query cycle for the same search. The completed operation is
            // already cached, so the delayed Query() path returns the answer immediately
            // without starting another model request or holding the plugin execution gate.
            DispatchToUi(() =>
            {
                // A newer keystroke may arrive between model completion and this dispatcher
                // callback. Never replace the user's newer search with an obsolete query.
                if (!IsCurrentQuery(operation))
                    return;

                _context?.API.ChangeQuery(query.RawQuery, requery: true);
            }, "Could not refresh the completed RunMind result");
        }
        catch (Exception ex)
        {
            // Keep fire-and-forget task failures from becoming unobserved exceptions.
            Debug.WriteLine($"[RunMind] Could not refresh the completed inline result: {ex}");
        }
    }

    private async Task ExecuteQueryAsync(QueryOperation operation)
    {
        try
        {
            var cancellationToken = operation.Token;
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(operation.Model))
            {
                operation.SetCompletion("No default model is selected. Set a model ID in the shared RunMind inline settings, or use the > model > prompt override.", isError: true);
                return;
            }

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
                operation.SetCompletion($"Request failed: {GetUsefulExceptionMessage(ex, operation.Settings)}", isError: true);
        }
        finally
        {
            operation.MarkFinished();
        }

    }

    private static bool TryGetValidSearchText(Query query, out string search)
    {
        search = (query.Search ?? string.Empty).Trim();
        if (search.Length == 0
            || search.Equals("llm", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(query.ActionKeyword)
                && search.Equals(query.ActionKeyword.Trim(), StringComparison.OrdinalIgnoreCase)))
            return false;

        // Let PowerToys' normal application indexer handle the standalone app name.
        if (string.IsNullOrWhiteSpace(query.ActionKeyword)
            && (search.Equals("RunMind", StringComparison.OrdinalIgnoreCase)
                || search.Equals("LocalLLM", StringComparison.OrdinalIgnoreCase)
                || search.Equals("Local LLM", StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
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
        lock (_settingsLock)
        {
            if (settings.Version != _settingsVersion) return null;
            lock (_queryLock)
            {
                if (_disposed) return null;
                if (_activeQuery is not null
                    && string.Equals(_activeQuery.Search, search, StringComparison.Ordinal)
                    && (!_activeQuery.IsCancellationRequested || _activeQuery.WasHandedOff))
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
        }

        // Cancel outside both locks: CancellationToken callbacks may run inline.
        previous?.Cancel();
        previous?.Dispose();
        return operation;
    }

    private QueryOperation? GetOrBeginQuery(string search, string model, string prompt, SettingsSnapshot settings)
    {
        lock (_settingsLock)
        {
            if (settings.Version != _settingsVersion) return null;
            lock (_queryLock)
            {
                if (_disposed) return null;

                // A delayed callback for an obsolete query must never restart cancelled work.
                if (_hasReceivedFastQuery && !string.Equals(_latestFastQuery, search, StringComparison.Ordinal))
                    return null;

                if (_activeQuery is not null)
                {
                    if (!string.Equals(_activeQuery.Search, search, StringComparison.Ordinal)
                        || (_activeQuery.IsCancellationRequested && !_activeQuery.WasHandedOff))
                        return null;
                    return _activeQuery;
                }

                // Supports launcher configurations which invoke only the delayed path first.
                var operation = new QueryOperation(search, model, prompt, settings);
                _activeQuery = operation;
                return operation;
            }
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

        if (TryResolveThinkValue(settings.Thinking, out var thinkValue))
        {
            requestBody["think"] = thinkValue;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };
        ApplyProviderAuthentication(request, settings);

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token).ConfigureAwait(false);
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
        var receivedCompletion = false;
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
                    AppendBoundedResponseChunk(output, chunk.GetString(), "Ollama");
                if (root.TryGetProperty("done", out var doneElement) && doneElement.ValueKind == JsonValueKind.True)
                {
                    receivedCompletion = true;
                    break;
                }
            }
        }
        if (!receivedCompletion)
            throw new InvalidDataException("Ollama closed the response stream before its final done:true record. The response may be incomplete; retry the request.");
        return output.ToString();
    }

    private async Task<string> QueryLMStudioStreamAsync(string input, string model, SettingsSnapshot settings, CancellationToken cancellationToken)
    {
        if (settings.InlineSeed.HasValue || (settings.InlineConversationalMode && IsNemotron35BoltE4BModel(model)))
            return await QueryLMStudioOpenAICompatStreamAsync(input, model, settings, cancellationToken).ConfigureAwait(false);

        try
        {
            return await QueryLMStudioNativeStreamAsync(input, model, settings, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed)
        {
            if (!string.Equals(settings.Thinking, "auto", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "This LM Studio server does not expose /api/v1/chat, which RunMind needs to apply the shared Thinking / reasoning setting. Upgrade LM Studio or set Thinking / reasoning to Auto.", exception);
            return await QueryLMStudioOpenAICompatStreamAsync(input, model, settings, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string> QueryLMStudioNativeStreamAsync(string input, string model, SettingsSnapshot settings, CancellationToken cancellationToken)
    {
        var endpoint = BuildLMStudioNativeUri(settings.LMStudioBaseUrl, "chat");
        var requestBody = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["model"] = model,
            ["input"] = input,
            ["stream"] = true,
            ["store"] = false,
        };
        if (!string.IsNullOrWhiteSpace(settings.InlineSystemPrompt)) requestBody["system_prompt"] = settings.InlineSystemPrompt;
        if (settings.InlineTemperature is double temperature) requestBody["temperature"] = Math.Min(temperature, 1.0);
        if (settings.InlineTopP is double topP) requestBody["top_p"] = topP;
        if (settings.InlineMaxOutputTokens is int maxTokens) requestBody["max_output_tokens"] = maxTokens;
        var thinking = string.IsNullOrWhiteSpace(settings.Thinking) ? "auto" : settings.Thinking.Trim().ToLowerInvariant();
        if (thinking != "auto") requestBody["reasoning"] = thinking;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };
        ApplyProviderAuthentication(request, settings);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await ReadBoundedContentTextAsync(response.Content, 64 * 1024, requestTimeout.Token).ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {TrimForError(body)}";
            throw new HttpRequestException($"LM Studio returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{detail}", null, response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(requestTimeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        var output = new StringBuilder();
        var eventName = string.Empty;
        var recordNumber = 0;
        var completed = false;
        while (true)
        {
            var line = await reader.ReadLineAsync(requestTimeout.Token).ConfigureAwait(false);
            if (line is null) break;
            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line[6..].Trim();
                continue;
            }
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].Trim();
            if (data.Length == 0) continue;
            recordNumber++;
            JsonDocument json;
            try { json = JsonDocument.Parse(data); }
            catch (JsonException exception) { throw new InvalidDataException($"LM Studio returned malformed REST SSE JSON at event {recordNumber}: {exception.Message}", exception); }
            using (json)
            {
                var root = json.RootElement;
                var eventType = root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString() ?? eventName : eventName;
                if (string.Equals(eventType, "error", StringComparison.OrdinalIgnoreCase))
                {
                    var detail = root.TryGetProperty("error", out var errorElement)
                        && errorElement.ValueKind == JsonValueKind.Object
                        && errorElement.TryGetProperty("message", out var messageElement)
                        ? messageElement.GetString() : root.ToString();
                    throw new InvalidOperationException("LM Studio: " + (string.IsNullOrWhiteSpace(detail) ? "The model request failed." : detail));
                }
                if (string.Equals(eventType, "message.delta", StringComparison.OrdinalIgnoreCase)
                    && root.TryGetProperty("content", out var contentElement)
                    && contentElement.ValueKind == JsonValueKind.String)
                    AppendBoundedResponseChunk(output, contentElement.GetString(), "LM Studio");
                else if (string.Equals(eventType, "chat.end", StringComparison.OrdinalIgnoreCase))
                {
                    completed = true;
                    if (output.Length == 0 && root.TryGetProperty("result", out var resultElement)
                        && resultElement.TryGetProperty("output", out var outputItems)
                        && outputItems.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in outputItems.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.Object
                                && item.TryGetProperty("type", out var itemType) && itemType.ValueKind == JsonValueKind.String && itemType.GetString() == "message"
                                && item.TryGetProperty("content", out var messageContent) && messageContent.ValueKind == JsonValueKind.String)
                                AppendBoundedResponseChunk(output, messageContent.GetString(), "LM Studio");
                    }
                    break;
                }
            }
        }
        if (!completed)
            throw new InvalidDataException("LM Studio closed its REST response stream before chat.end. The response may be incomplete; retry the request.");
        return output.ToString();
    }

    private async Task<string> QueryLMStudioOpenAICompatStreamAsync(string input, string model, SettingsSnapshot settings, CancellationToken cancellationToken)
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
        ApplyLMStudioThinkingOptions(requestBody, settings.Thinking);
        if (IsNemotron35BoltE4BModel(model))
        {
            // Merge the model-specific conversational switch with the generic
            // thinking option so enabling one does not overwrite the other.
            Dictionary<string, object> templateOptions;
            if (requestBody.TryGetValue("chat_template_kwargs", out var rawTemplateOptions)
                && rawTemplateOptions is Dictionary<string, object> existingTemplateOptions)
            {
                templateOptions = existingTemplateOptions;
            }
            else
            {
                templateOptions = new Dictionary<string, object>(StringComparer.Ordinal);
                requestBody["chat_template_kwargs"] = templateOptions;
            }
            templateOptions["conversational_mode"] = settings.InlineConversationalMode;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };
        ApplyProviderAuthentication(request, settings);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token).ConfigureAwait(false);
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
        var receivedCompletion = false;
        while (true)
        {
            var line = await reader.ReadLineAsync(requestTimeout.Token).ConfigureAwait(false);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].Trim();
            if (data.Length == 0) continue;
            if (data.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                receivedCompletion = true;
                break;
            }
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
                        AppendBoundedResponseChunk(output, content.GetString(), "LM Studio");
                    else if (choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
                        && message.TryGetProperty("content", out content) && content.ValueKind == JsonValueKind.String)
                        AppendBoundedResponseChunk(output, content.GetString(), "LM Studio");

                    // Some OpenAI-compatible servers omit [DONE] but send the standard
                    // non-null finish_reason on their final choice. Treat that as an
                    // explicit completion signal, not an arbitrary EOF.
                    if (choice.TryGetProperty("finish_reason", out var finishReason)
                        && finishReason.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(finishReason.GetString()))
                        receivedCompletion = true;
                }
            }
        }
        if (!receivedCompletion)
            throw new InvalidDataException("LM Studio closed the response stream without [DONE] or a final finish_reason. The response may be incomplete; retry the request.");
        return output.ToString();
    }

    private static void AppendBoundedResponseChunk(StringBuilder output, string? chunk, string providerName)
    {
        if (string.IsNullOrEmpty(chunk)) return;
        if (chunk.Length > MaximumInlineResponseCharacters - output.Length)
            throw new InvalidDataException($"The {providerName} response exceeded RunMind's {MaximumInlineResponseCharacters:N0}-character inline output limit. Use a smaller request or open the full chat window for longer work.");
        output.Append(chunk);
    }

    private static void ApplyProviderAuthentication(HttpRequestMessage request, SettingsSnapshot settings)
    {
        var apiKey = settings.Provider == 1 ? settings.LMStudioApiKey : settings.OllamaApiKey;
        var customHeader = settings.Provider == 1 ? settings.LMStudioAuthHeader : settings.OllamaAuthHeader;
        InlineModelSettingsStore.ApplyAuthentication(request, apiKey, customHeader);
    }

    private async Task<List<string>> FetchModelsFromEndpointAsync(SettingsSnapshot settings, CancellationToken cancellationToken = default)
    {
        var modelsUri = settings.Provider == 1
            ? BuildLMStudioNativeUri(settings.LMStudioBaseUrl, "models")
            : BuildTagsUri(settings.Endpoint);
        var cacheKey = modelsUri.AbsoluteUri;
        cancellationToken.ThrowIfCancellationRequested();
        lock (ModelCacheLock)
        {
            if (string.Equals(_modelCacheEndpoint, cacheKey, StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.UtcNow - _modelCacheTimestamp < TimeSpan.FromSeconds(5))
                return new List<string>(_modelCache);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.ModelDiscoveryTimeoutSeconds));
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUri);
            ApplyProviderAuthentication(request, settings);
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token).ConfigureAwait(false);
            JsonDocument json;
            bool lmStudioCompatibilityFallback = false;
            if (settings.Provider == 1 && (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed))
            {
                response.Dispose();
                lmStudioCompatibilityFallback = true;
                using var compatibilityRequest = new HttpRequestMessage(HttpMethod.Get, BuildLMStudioUri(settings.LMStudioBaseUrl, "models"));
                ApplyProviderAuthentication(compatibilityRequest, settings);
                using var compatibilityResponse = await HttpClient.SendAsync(compatibilityRequest, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token).ConfigureAwait(false);
                if (!compatibilityResponse.IsSuccessStatusCode)
                {
                    var body = await ReadBoundedContentTextAsync(compatibilityResponse.Content, 64 * 1024, requestTimeout.Token).ConfigureAwait(false);
                    var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {TrimForError(body)}";
                    throw new HttpRequestException($"Could not query the LM Studio model list (HTTP {(int)compatibilityResponse.StatusCode} {compatibilityResponse.ReasonPhrase}).{detail}", null, compatibilityResponse.StatusCode);
                }
                var bytes = await ReadBoundedContentBytesAsync(compatibilityResponse.Content, 4 * 1024 * 1024, requestTimeout.Token).ConfigureAwait(false);
                json = JsonDocument.Parse(bytes);
            }
            else
            {
                if (!response.IsSuccessStatusCode)
                {
                    var body = await ReadBoundedContentTextAsync(response.Content, 64 * 1024, requestTimeout.Token).ConfigureAwait(false);
                    var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : $" Response: {TrimForError(body)}";
                    throw new HttpRequestException($"Could not query the {GetProviderName(settings)} model list (HTTP {(int)response.StatusCode} {response.ReasonPhrase}).{detail}", null, response.StatusCode);
                }
                var bytes = await ReadBoundedContentBytesAsync(response.Content, 4 * 1024 * 1024, requestTimeout.Token).ConfigureAwait(false);
                json = JsonDocument.Parse(bytes);
            }

            using (json)
            {
                var modelNames = new List<string>();
                var arrayName = settings.Provider == 1 && !lmStudioCompatibilityFallback ? "models" : settings.Provider == 1 ? "data" : "models";
                if (!json.RootElement.TryGetProperty(arrayName, out var modelsElement) || modelsElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"The {GetProviderName(settings)} model-list response did not contain a '{arrayName}' array. Check the configured API URL.");

                foreach (var model in modelsElement.EnumerateArray())
                {
                    if (model.ValueKind != JsonValueKind.Object) continue;
                    if (settings.Provider == 1 && !lmStudioCompatibilityFallback)
                    {
                        if (!model.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String
                            || !string.Equals(typeElement.GetString(), "llm", StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    var nameProperty = settings.Provider == 1 ? (lmStudioCompatibilityFallback ? "id" : "key") : "name";
                    if (model.TryGetProperty(nameProperty, out var nameElement)
                        && nameElement.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(nameElement.GetString()))
                        modelNames.Add(nameElement.GetString()!.Trim());
                }

                lock (ModelCacheLock)
                {
                    _modelCacheEndpoint = cacheKey;
                    _modelCacheTimestamp = DateTimeOffset.UtcNow;
                    _modelCache = new List<string>(modelNames);
                }
                return modelNames;
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"Model discovery timed out after {settings.ModelDiscoveryTimeoutSeconds} seconds while contacting {GetProviderName(settings)}. Check the provider endpoint and whether the service is responding. You can increase Model discovery timeout in the PowerToys Run plugin settings.", ex);
        }
    }

    private static void ApplyLMStudioThinkingOptions(Dictionary<string, object> requestBody, string? rawSetting)
    {
        var setting = string.IsNullOrWhiteSpace(rawSetting) ? "auto" : rawSetting.Trim().ToLowerInvariant();
        if (setting == "auto") return;

        if (setting is "on" or "true" or "off" or "false")
        {
            var enabled = setting is "on" or "true";
            var templateOptions = requestBody.TryGetValue("chat_template_kwargs", out var existing)
                && existing is Dictionary<string, object> existingOptions
                    ? existingOptions
                    : new Dictionary<string, object>(StringComparer.Ordinal);
            templateOptions["enable_thinking"] = enabled;
            requestBody["chat_template_kwargs"] = templateOptions;
            if (!enabled) requestBody["reasoning_effort"] = "none";
            return;
        }

        if (setting is "low" or "medium" or "high")
        {
            requestBody["reasoning_effort"] = setting;
            var templateOptions = requestBody.TryGetValue("chat_template_kwargs", out var existing)
                && existing is Dictionary<string, object> existingOptions
                    ? existingOptions
                    : new Dictionary<string, object>(StringComparer.Ordinal);
            templateOptions["enable_thinking"] = true;
            requestBody["chat_template_kwargs"] = templateOptions;
            return;
        }

        throw new InvalidOperationException("Thinking / reasoning mode must be auto, on, off, low, medium, or high.");
    }

    private static string ReadThinkingOption(IEnumerable<PluginAdditionalOption> options, string fallback)
    {
        var option = options.FirstOrDefault(candidate => candidate.Key == "Thinking");
        if (option is null) return fallback;

        // TextValue preserves settings created by earlier plugin versions, where
        // Thinking was a textbox. New settings use the shared provider-neutral list.
        var text = (option.TextValue ?? string.Empty).Trim().ToLowerInvariant();
        return text switch
        {
            "auto" or "0" => "auto",
            "on" or "true" or "1" => "on",
            "off" or "false" or "2" => "off",
            "low" or "3" => "low",
            "medium" or "4" => "medium",
            "high" or "5" => "high",
            _ => option.ComboBoxValue switch
            {
                1 => "on",
                2 => "off",
                3 => "low",
                4 => "medium",
                5 => "high",
                _ => fallback,
            },
        };
    }

    private static int GetThinkingChoiceIndex(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "on" => 1,
            "off" => 2,
            "low" => 3,
            "medium" => 4,
            "high" => 5,
            _ => 0,
        };

    private static string GetProviderName(SettingsSnapshot settings) => settings.Provider == 1 ? "LM Studio" : "Ollama";

    private static Uri BuildLMStudioNativeUri(string baseUrl, string relativePath)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(baseUri.Host)
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
            throw new InvalidOperationException("The LM Studio base URL must be an absolute HTTP or HTTPS URL without embedded credentials, query strings, or fragments, for example http://localhost:1234/v1.");
        var path = baseUri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) path = path[..^3];
        var builder = new UriBuilder(baseUri)
        {
            Path = path + "/api/v1/" + relativePath.TrimStart('/'),
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri;
    }

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

    private static bool TryResolveThinkValue(string? thinkingSetting, out object value)
    {
        var setting = string.IsNullOrWhiteSpace(thinkingSetting) ? "auto" : thinkingSetting.Trim().ToLowerInvariant();
        if (setting == "auto")
        {
            // Leave the model's own default intact. Do not infer behavior from a model name.
            value = false;
            return false;
        }
        if (setting is "on" or "true")
        {
            value = true;
            return true;
        }
        if (setting is "off" or "false")
        {
            value = false;
            return true;
        }
        if (setting is "low" or "medium" or "high")
        {
            value = setting;
            return true;
        }
        throw new InvalidOperationException("Thinking / reasoning mode must be auto, on, off, low, medium, or high.");
    }

    private Result CreatePendingResult(string prompt)
    {
        var queryPreview = CreateInlineQueryPreview(prompt);
        var subtitle = string.IsNullOrEmpty(queryPreview)
            ? "Open in chat · Answer generates in background"
            : $"{queryPreview} · Open in chat · Answer generates in background";

        return new Result
        {
            Title = "Ask RunMind",
            SubTitle = subtitle,
            IcoPath = _iconPath,
            Score = 45,
            Action = _ =>
            {
                DispatchToUi(() =>
                {
                    try
                    {
                        if (!TryResolvePendingOperation(out var currentOperation, out var completedResponse, out var isError, out var needsHandoff))
                            return;

                        // PowerToys can briefly leave an older result visible while a newer
                        // fast query is being published. Resolve the click against the latest
                        // operation RunMind has received, and use that operation's prompt,
                        // model, and settings as one consistent snapshot.
                        if (needsHandoff)
                        {
                            // Cancel the inline HTTP request before starting the chat request.
                            currentOperation.Cancel();
                            ShowPromptChatWindow(currentOperation.Settings, currentOperation.Model, currentOperation.Prompt);
                        }
                        else
                        {
                            ShowNativeResponseWindow(currentOperation.Settings, currentOperation.Model, currentOperation.Prompt, completedResponse, isError);
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

    private bool TryResolvePendingOperation(
        out QueryOperation currentOperation,
        out string response,
        out bool isError,
        out bool needsHandoff)
    {
        lock (_queryLock)
        {
            // The clicked Result may belong to an earlier render of the same retained Run
            // query. Keep resolving it against the active snapshot so each activation can
            // open an independent chat. A genuinely newer search still invalidates the old one.
            currentOperation = _activeQuery!;
            if (_disposed
                || currentOperation is null
                || (_hasReceivedFastQuery && !string.Equals(_latestFastQuery, currentOperation.Search, StringComparison.Ordinal)))
            {
                currentOperation = null!;
                response = string.Empty;
                isError = false;
                needsHandoff = false;
                return false;
            }

            // A result from an older generation intentionally resolves to the latest active
            // operation. If there is no newer operation, this is the original clicked one.
            var completed = currentOperation.TryGetCompletionOrMarkHandoff(out response, out isError);
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

    private static InlineModelSettings LoadSharedInlineSettingsForOptions()
    {
        try { return InlineModelSettingsStore.Load(); }
        catch { return new InlineModelSettings(); }
    }

    private void ApplySharedInlineSettingsIfChanged()
    {
        InlineModelSettings shared;
        DateTime writeTime;
        try
        {
            var path = InlineModelSettingsStore.FilePath;
            if (!File.Exists(path)) return;
            writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime.Ticks <= Interlocked.Read(ref _sharedSettingsWriteTimeUtcTicks)) return;
            shared = InlineModelSettingsStore.Load();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[RunMind] Could not read shared inline model settings: {exception.Message}");
            return;
        }

        QueryOperation? previous;
        lock (_settingsLock)
        {
            if (writeTime.Ticks <= _sharedSettingsWriteTimeUtcTicks) return;
            _endpoint = shared.OllamaEndpoint;
            _lmStudioBaseUrl = shared.LMStudioBaseUrl;
            _provider = shared.Provider == "lmstudio" ? 1 : 0;
            _defaultModel = shared.Model;
            _thinking = shared.Thinking;
            _keepAliveSeconds = shared.KeepAliveSeconds;
            _requestTimeoutSeconds = shared.RequestTimeoutSeconds;
            _modelDiscoveryTimeoutSeconds = shared.ModelDiscoveryTimeoutSeconds;
            _inlineCpuThreads = shared.InlineCpuThreads;
            _inlineSystemPrompt = shared.InlineSystemPrompt;
            _ollamaApiKey = shared.PowerToysRunOllamaApiKey;
            _ollamaAuthHeader = shared.PowerToysRunOllamaAuthHeader;
            _lmStudioApiKey = shared.PowerToysRunLMStudioApiKey;
            _lmStudioAuthHeader = shared.PowerToysRunLMStudioAuthHeader;
            _inlineTemperature = shared.InlineTemperature;
            _inlineTopP = shared.InlineTopP;
            _inlineMaxOutputTokens = shared.InlineMaxOutputTokens;
            _inlineSeed = shared.InlineSeed;
            _inlineConversationalMode = shared.InlineConversationalMode;
            _settingsVersion++;
            _sharedSettingsWriteTimeUtcTicks = writeTime.Ticks;
            lock (ModelCacheLock)
            {
                _modelCacheEndpoint = null;
                _modelCache.Clear();
            }
            lock (_queryLock)
            {
                previous = _activeQuery;
                _activeQuery = null;
                _latestFastQuery = null;
                _hasReceivedFastQuery = true;
            }
        }

        previous?.Cancel();
        previous?.Dispose();
    }

    private SettingsSnapshot GetSettingsSnapshot()
    {
        ApplySharedInlineSettingsIfChanged();
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
                _modelDiscoveryTimeoutSeconds,
                _inlineCpuThreads,
                _inlineSystemPrompt,
                _ollamaApiKey,
                _ollamaAuthHeader,
                _lmStudioApiKey,
                _lmStudioAuthHeader,
                _inlineTemperature,
                _inlineTopP,
                _inlineMaxOutputTokens,
                _inlineSeed,
                _inlineConversationalMode,
                _settingsVersion);
        }
    }

    private static string NormalizeInlineSystemPrompt(string? value)
        => InlineModelSettingsStore.NormalizeInlineSystemPrompt(value);

    private static string ReadOptionalTextOption(IEnumerable<PluginAdditionalOption> options, string key, string fallback, int maximumLength)
    {
        var option = options.FirstOrDefault(candidate => candidate.Key == key);
        if (option is null) return fallback;
        var value = (option.TextValue ?? string.Empty).Trim();
        return value.Length <= maximumLength ? value : value[..maximumLength];
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
        // Preserve query parameters (for example, gateway/API-key parameters) when
        // switching from /api/generate to /api/tags.
        builder.Fragment = string.Empty;
        return builder.Uri;
    }

    // Keep the fast result visibly tied to the current input without doing regex,
    // Markdown, or unbounded formatting work on PowerToys' keystroke path.
    private static string CreateInlineQueryPreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        const int maxLength = 100;
        var preview = new StringBuilder(Math.Min(text.Length, maxLength));
        var pendingSpace = false;
        var truncated = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = preview.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                if (preview.Length >= maxLength)
                {
                    truncated = true;
                    break;
                }

                preview.Append(' ');
                pendingSpace = false;
            }

            if (preview.Length >= maxLength)
            {
                truncated = true;
                break;
            }

            preview.Append(character);
        }

        // Avoid leaving half of a UTF-16 surrogate pair at the display boundary.
        if (truncated && preview.Length > 0 && char.IsHighSurrogate(preview[preview.Length - 1]))
            preview.Length--;

        return truncated ? preview.ToString().TrimEnd() + "…" : preview.ToString();
    }

    private static string CreatePreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var preview = System.Text.RegularExpressions.Regex.Replace(
            text.Trim(), @"\r?\n\s*\r?\n+", "\n");
        const int maxLength = 420;
        return preview.Length <= maxLength ? preview : preview[..(maxLength - 1)].TrimEnd() + "…";
    }

    private static string GetUsefulExceptionMessage(Exception exception, SettingsSnapshot settings)
    {
        var exceptionChain = new List<Exception>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            exceptionChain.Add(current);
        }

        var explicitTimeout = exceptionChain.OfType<TimeoutException>().FirstOrDefault();
        if (explicitTimeout is not null)
            return explicitTimeout.Message;

        if (exceptionChain.Any(item => item is OperationCanceledException))
        {
            return $"The inline provider request exceeded the configured {settings.RequestTimeoutSeconds}-second Request timeout, which includes model loading, initial response headers, and response generation. Model discovery has its separate {settings.ModelDiscoveryTimeoutSeconds}-second timeout.";
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

    private static async Task DisposeWebViewWithSessionCleanupAsync(WebView2 webView)
    {
        try
        {
            if (webView.CoreWebView2 is { } core)
                await core.ExecuteScriptAsync("window.runMindCloseSession && window.runMindCloseSession();");
        }
        catch { /* Crash/reload cleanup uses the IndexedDB session lease. */ }
        DisposeWebViewSafely(webView);
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
            // Keep inline provenance for the initial prompt, but do not pass inline
            // selection or endpoints: the chat window uses its own saved configuration.
            sourceProvider = settings.Provider == 1 ? "lmstudio" : "ollama",
            sourceModel = model,
            autoSendPromptText = prompt,
            generateTitleAfterFirstResponse = true,
            keepAliveSeconds = settings.KeepAliveSeconds,
        };
        var webView = CreateChatWebView();
        var appWindow = new Window
        {
            Title = "RunMind", Width = 1120, Height = 836, MinWidth = 680, MinHeight = 520,
            Icon = LoadWindowIcon(assemblyDirectory),
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = webView,
        };
        appWindow.Closed += async (_, _) => await DisposeWebViewWithSessionCleanupAsync(webView);
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

        appWindow.Closed += async (_, _) => await DisposeWebViewWithSessionCleanupAsync(webView);
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

        appWindow.Closed += async (_, _) => await DisposeWebViewWithSessionCleanupAsync(webView);
        WindowLayout.ConfigureInitialPlacement(appWindow);
        appWindow.Show();
        appWindow.Activate();
        _ = InitializeChatWebViewAsync(appWindow, webView, assemblyDirectory, JsonSerializer.Serialize(bootstrap));
    }

    private void ShowNewConversationWindow(string assemblyDirectory, string? provider, string? model, string? connectionId, bool? thinkEnabled)
    {
        var htmlPath = Path.Combine(assemblyDirectory, "ChatWindow.html");
        if (!File.Exists(htmlPath))
            throw new FileNotFoundException("ChatWindow.html was not found beside the RunMind plugin assembly.", htmlPath);

        var bootstrap = new
        {
            title = "New chat",
            appearance = AppearancePreferenceStore.CreateBootstrapValue(),
            windowInstanceId = Guid.NewGuid().ToString("N"),
            freshConversation = true,
            generateTitleAfterFirstResponse = true,
            initialProvider = provider,
            initialModel = model,
            initialConnectionId = connectionId,
            initialThink = thinkEnabled,
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

        appWindow.Closed += async (_, _) => await DisposeWebViewWithSessionCleanupAsync(webView);
        WindowLayout.ConfigureInitialPlacement(appWindow);
        appWindow.Show();
        appWindow.Activate();
        _ = InitializeChatWebViewAsync(appWindow, webView, assemblyDirectory, JsonSerializer.Serialize(bootstrap));
    }

    private static bool IsTrustedChatPageSource(string source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var sourceUri)
            && IsTrustedChatPageUri(sourceUri);
    }

    private static bool IsTrustedChatPageUri(Uri uri)
    {
        return uri.IsAbsoluteUri
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && uri.IsDefaultPort
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.AbsolutePath.Equals("/ChatWindow.html", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryHandleNewConversationMessage(
        string source,
        string messageJson,
        string assemblyDirectory,
        Action<string?, string?, string?, bool?> resetCurrentWindow)
    {
        try
        {
            if (!IsTrustedChatPageSource(source))
                return false;

            if (string.IsNullOrWhiteSpace(messageJson) || Encoding.UTF8.GetByteCount(messageJson) > 16 * 1024)
                return false;

            using var document = JsonDocument.Parse(messageJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
                return false;

            var type = typeElement.GetString();
            if (type is not ("runmind.newConversationInCurrentWindow" or "runmind.newConversationInNewWindow"))
                return false;

            var provider = root.TryGetProperty("provider", out var providerElement)
                && providerElement.ValueKind == JsonValueKind.String
                && providerElement.GetString() is "ollama" or "lmstudio"
                    ? providerElement.GetString()
                    : null;
            var model = root.TryGetProperty("model", out var modelElement)
                && modelElement.ValueKind == JsonValueKind.String
                    ? modelElement.GetString()?.Trim()
                    : null;
            if (string.IsNullOrWhiteSpace(model) || model.Length > 512) model = null;
            var connectionId = root.TryGetProperty("connectionId", out var connectionElement)
                && connectionElement.ValueKind == JsonValueKind.String
                    ? connectionElement.GetString()?.Trim()
                    : null;
            if (string.IsNullOrWhiteSpace(connectionId) || connectionId.Length > 128) connectionId = null;
            bool? thinkEnabled = root.TryGetProperty("thinkEnabled", out var thinkElement)
                && thinkElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? thinkElement.GetBoolean()
                    : null;

            if (type == "runmind.newConversationInNewWindow")
            {
                DispatchToUi(
                    () => ShowNewConversationWindow(assemblyDirectory, provider, model, connectionId, thinkEnabled),
                    "Could not open a new RunMind conversation window");
            }
            else
            {
                DispatchToUi(
                    () => resetCurrentWindow(provider, model, connectionId, thinkEnabled),
                    "Could not start a new conversation in this window");
            }
            return true;
        }
        catch (Exception ex)
        {
            _context?.API.ShowMsg("Could not start a new conversation", ex.Message);
            return true;
        }
    }

    private static string CreateFreshConversationBootstrap(string currentBootstrapJson, string? provider, string? model, string? connectionId, bool? thinkEnabled)
    {
        var bootstrap = JsonNode.Parse(currentBootstrapJson) as JsonObject ?? new JsonObject();
        foreach (var property in new[]
        {
            "prompt", "response", "isError", "initialConversation", "initialConversationFileName",
            "autoSendPromptText", "initialProvider", "initialModel", "initialConnectionId", "initialThink", "initialTitleGenerated"
        })
            bootstrap.Remove(property);

        bootstrap["title"] = "New chat";
        bootstrap["appearance"] = JsonNode.Parse(JsonSerializer.Serialize(AppearancePreferenceStore.CreateBootstrapValue()));
        bootstrap["windowInstanceId"] = Guid.NewGuid().ToString("N");
        bootstrap["freshConversation"] = true;
        bootstrap["generateTitleAfterFirstResponse"] = true;
        if (provider is "ollama" or "lmstudio") bootstrap["initialProvider"] = provider;
        if (!string.IsNullOrWhiteSpace(model) && model.Length <= 512) bootstrap["initialModel"] = model;
        if (!string.IsNullOrWhiteSpace(connectionId) && connectionId.Length <= 128) bootstrap["initialConnectionId"] = connectionId;
        if (thinkEnabled.HasValue) bootstrap["initialThink"] = thinkEnabled.Value;
        return bootstrap.ToJsonString();
    }

    private void HandleOpenConversationInNewWindow(string source, string messageJson, string assemblyDirectory)
    {
        try
        {
            if (!IsTrustedChatPageSource(source))
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
        if (!conversation.TryGetProperty("version", out var version) || !version.TryGetInt32(out var versionNumber) || versionNumber is not (1 or 2))
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
                ApplyProviderAuthentication(request, settings);
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
            ApplyProviderAuthentication(request, settings);
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
            await core.AddScriptToExecuteOnDocumentCreatedAsync(AppearancePreferenceStore.CreateDocumentBootstrapScript());

            // Native Page Reader remains available for bounded public-page retrieval.
            // AgentHostBridge exposes only the explicitly enumerated native capabilities.
            var nativePageReader = new NativePageReader();
            var agentHostBridge = new AgentHostBridge();
            appWindow.Closed += (_, _) => { nativePageReader.Dispose(); agentHostBridge.Dispose(); };
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.AreHostObjectsAllowed = false;
            var activeBootstrapJson = bootstrapJson;
            core.WebMessageReceived += (_, messageArgs) =>
            {
                if (!IsTrustedChatPageSource(messageArgs.Source)) return;
                if (windowThemeSynchronizer.TryHandleWebMessage(messageArgs.Source, messageArgs.WebMessageAsJson))
                    return;
                if (nativePageReader.TryHandleMessage(messageArgs.Source, messageArgs.WebMessageAsJson, core, appWindow.Dispatcher))
                    return;
                if (agentHostBridge.TryHandleMessage(messageArgs.Source, messageArgs.WebMessageAsJson, core, appWindow.Dispatcher))
                    return;
                if (ConversationSaveDialog.TryHandleMessage(
                    messageArgs.Source, messageArgs.WebMessageAsJson, appWindow, core))
                    return;

                if (TryHandleNewConversationMessage(
                    messageArgs.Source,
                    messageArgs.WebMessageAsJson,
                    appFolderPath,
                    (provider, model, connectionId, thinkEnabled) =>
                    {
                        activeBootstrapJson = CreateFreshConversationBootstrap(activeBootstrapJson, provider, model, connectionId, thinkEnabled);
                        core.Navigate($"https://localhost/ChatWindow.html?fresh={Guid.NewGuid():N}");
                    }))
                    return;

                HandleOpenConversationInNewWindow(messageArgs.Source, messageArgs.WebMessageAsJson, appFolderPath);
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
                // Navigation is denied by default. Only the exact mapped app page may
                // remain in WebView2; supported external links are handed to Windows.
                e.Cancel = true;
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
                if (IsTrustedChatPageUri(uri))
                {
                    e.Cancel = false;
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
                    await core.ExecuteScriptAsync("window.initializeRunMind(" + activeBootstrapJson + ");");
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
            appWindow.Closed += async (_, _) => await DisposeWebViewWithSessionCleanupAsync(fallbackWebView);
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

        var thinkInitiallyEnabled = TryResolveThinkValue(settings.Thinking, out var thinkValue)
            && (thinkValue is bool boolValue
                ? boolValue
                : !string.Equals(Convert.ToString(thinkValue, System.Globalization.CultureInfo.InvariantCulture), "false", StringComparison.OrdinalIgnoreCase));
        var bootstrap = new
        {
            title,
            appearance = AppearancePreferenceStore.CreateBootstrapValue(),
            windowInstanceId = Guid.NewGuid().ToString("N"),
            // Keep inline response provenance separate from the chat model selection.
            // The chat window must use its own configured provider/model connections.
            sourceProvider = settings.Provider == 1 ? "lmstudio" : "ollama",
            sourceModel = model,
            prompt = prompt ?? string.Empty,
            response = response ?? string.Empty,
            isError,
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
        // This bootstrap value is a path base: ChatWindow.html appends API routes to it.
        // Keeping a query string here would turn the appended route into query text.
        // Native model discovery preserves endpoint query parameters in BuildTagsUri;
        // chat-window query forwarding needs a path-aware URL builder before it can do so.
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

        public enum StartResult
        {
            Started,
            AlreadyRunning,
            Terminal,
        }

        public StartResult TryStart()
        {
            lock (_sync)
            {
                if (_disposed || _finished || IsCancellationRequested || WasHandedOff)
                    return StartResult.Terminal;
                if (_started != 0)
                    return StartResult.AlreadyRunning;
                _started = 1;
                return StartResult.Started;
            }
        }

        public void Cancel()
        {
            CancellationTokenSource? sourceToCancel = null;
            lock (_sync)
            {
                if (!_disposed && Interlocked.Exchange(ref _cancelRequested, 1) == 0)
                {
                    // Reserve the source until CancelAsync has completed every registered
                    // callback. This flag also prevents Dispose/MarkFinished from disposing
                    // the source while those callbacks are still running.
                    _cancelInProgress = true;
                    sourceToCancel = _cancellation;
                }

                if (_started == 0)
                {
                    // No background worker exists to finish this operation.
                    _finished = true;
                    _disposeRequested = true;
                }
                else if (_finished)
                {
                    _disposeRequested = true;
                }
                TryDisposeSourceLocked();
            }

            if (sourceToCancel is null) return;

            // CancelAsync sets IsCancellationRequested synchronously, so obsolete work is
            // invalidated before Cancel returns. Registered callbacks finish asynchronously,
            // keeping PowerToys' fast Query() callback independent of HTTP cancellation work.
#if DEBUG
            var cancellationStartedAt = Stopwatch.GetTimestamp();
#endif
            try
            {
                var cancellationTask = sourceToCancel.CancelAsync();
                _ = CompleteCancellationAsync(cancellationTask
#if DEBUG
                    , cancellationStartedAt
#endif
                );
            }
            catch (Exception ex)
            {
                // Do not let an exceptional cancellation callback block or fail a newer
                // PowerToys query. The source still needs the same deferred-disposal path.
                Debug.WriteLine($"[RunMind] Could not start asynchronous query cancellation: {ex}");
                CompleteCancellationReservation();
            }
        }

        private async Task CompleteCancellationAsync(Task cancellationTask
#if DEBUG
            , long cancellationStartedAt
#endif
        )
        {
            try
            {
                await cancellationTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // CancelAsync reports callback exceptions through its task. Observe them so
                // a fire-and-forget cancellation cannot become an unobserved task failure.
                Debug.WriteLine($"[RunMind] A query cancellation callback failed: {ex}");
            }
            finally
            {
                CompleteCancellationReservation();
#if DEBUG
                Debug.WriteLine($"[RunMind] Previous query cancellation callbacks completed in {Stopwatch.GetElapsedTime(cancellationStartedAt).TotalMilliseconds:F1} ms.");
#endif
            }
        }

        private void CompleteCancellationReservation()
        {
            lock (_sync)
            {
                _cancelInProgress = false;
                TryDisposeSourceLocked();
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
                    // A completed inline answer is an immutable launch snapshot. Do not
                    // consume it: every activation can open another independent chat window.
                    response = _response;
                    isError = _isError;
                    return true;
                }

                response = string.Empty;
                isError = false;
                // A still-running inline request is handed to chat only once. Keep the
                // original prompt/settings as a reusable snapshot so later activations
                // can open sibling chats without starting another inline generation.
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
        int ModelDiscoveryTimeoutSeconds,
        int InlineCpuThreads,
        string InlineSystemPrompt,
        string OllamaApiKey,
        string OllamaAuthHeader,
        string LMStudioApiKey,
        string LMStudioAuthHeader,
        double? InlineTemperature,
        double? InlineTopP,
        int? InlineMaxOutputTokens,
        int? InlineSeed,
        bool InlineConversationalMode,
        long Version);
}
