using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using RunMind.Shared;

namespace RunMind.CommandPalette;

/// <summary>
/// Edits the shared inline-model configuration used by PowerToys Run and
/// Command Palette. Model discovery is provider-backed; arbitrary model IDs
/// remain editable for models not returned by the provider's discovery API.
/// </summary>
internal sealed partial class InlineModelSettingsPage : ContentPage
{
    private readonly object _pageSync = new();
    private InlineSettingsForm? _settingsForm;
    private bool _modelDiscoveryStarted;

    public InlineModelSettingsPage()
    {
        Name = "Inline model settings";
        Title = "RunMind inline model settings";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
    }

    public override IContent[] GetContent()
    {
        InlineSettingsForm form;
        bool startDiscovery = false;
        lock (_pageSync)
        {
            if (_settingsForm is null)
            {
                InlineModelSettings settings;
                string? initialStatus = null;
                try
                {
                    settings = InlineModelSettingsStore.Load();
                }
                catch (Exception exception)
                {
                    settings = new InlineModelSettings();
                    initialStatus = "Saved inline settings could not be read. Review the values and save to replace the damaged settings file. " + exception.Message;
                }
                _settingsForm = new InlineSettingsForm(this, settings, initialStatus);
            }

            form = _settingsForm;
            if (!_modelDiscoveryStarted)
            {
                _modelDiscoveryStarted = true;
                startDiscovery = true;
            }
        }

        if (startDiscovery) form.RefreshModelsInBackground(form.ModelDiscoveryTimeoutSeconds);
        return [form];
    }

    private void NotifyContentChanged()
    {
        try { RaiseItemsChanged(1); }
        catch { /* The settings page may have been closed while discovery was running. */ }
    }

    private sealed partial class InlineSettingsForm : FormContent
    {
        private readonly InlineModelSettingsPage _owner;
        private readonly object _sync = new();
        private InlineModelSettings _settings;
        private IReadOnlyList<string> _models = [];
        private string _modelStatus = "Discovering models from the selected provider…";
        private string _customModelInput = string.Empty;
        private int _discoveryGeneration;

        public InlineSettingsForm(InlineModelSettingsPage owner, InlineModelSettings settings, string? initialStatus)
        {
            _owner = owner;
            _settings = InlineModelSettingsStore.Normalize(settings);
            _customModelInput = _settings.Model;
            if (!string.IsNullOrWhiteSpace(initialStatus)) _modelStatus = initialStatus;
            RebuildTemplate();
        }

        public int ModelDiscoveryTimeoutSeconds
        {
            get { lock (_sync) return _settings.ModelDiscoveryTimeoutSeconds; }
        }

        public void RefreshModelsInBackground(int timeoutSeconds)
        {
            int generation;
            InlineModelSettings snapshot;
            lock (_sync)
            {
                generation = ++_discoveryGeneration;
                snapshot = _settings;
                _modelStatus = $"Discovering models from {ProviderName(snapshot.Provider)}…";
                RebuildTemplateLocked();
            }
            _owner.NotifyContentChanged();

            _ = Task.Run(() => InlineModelSettingsStore.DiscoverModels(snapshot, timeoutSeconds))
                .ContinueWith(task =>
                {
                    var result = task.Status == TaskStatus.RanToCompletion
                        ? task.Result
                        : new InlineModelDiscoveryResult([], "Model discovery failed: " + (task.Exception?.GetBaseException().Message ?? "Unknown error."));
                    lock (_sync)
                    {
                        if (generation != _discoveryGeneration || !SameProviderEndpoint(snapshot, _settings)) return;
                        _models = result.Models;
                        _modelStatus = result.Message;
                        _customModelInput = _models.Contains(_settings.Model, StringComparer.OrdinalIgnoreCase)
                            ? string.Empty
                            : _settings.Model;
                        RebuildTemplateLocked();
                    }
                    _owner.NotifyContentChanged();
                }, TaskScheduler.Default);
        }

        // Current Command Palette sends Adaptive Card input values and the
        // Action.Submit data object separately. Keep the one-argument overload
        // for compatibility, but handle the two-argument contract explicitly so
        // the save/refresh action is not silently discarded.
        public override CommandResult SubmitForm(string payload)
            => SubmitForm(payload, "{}");

        public override CommandResult SubmitForm(string inputs, string data)
        {
            try
            {
                RunMindCommandPaletteDiagnostics.Write(
                    $"Inline settings form submitted; inputs length={inputs?.Length ?? 0}; action-data length={data?.Length ?? 0}.");
                var values = FlattenPayload(inputs ?? "{}", data ?? "{}");
                var action = ReadString(values, "action");
                RunMindCommandPaletteDiagnostics.Write(
                    $"Inline settings form action='{action}'; provider='{ReadString(values, "provider")}'; model-input-present={!string.IsNullOrWhiteSpace(ReadString(values, "model"))}; custom-model-present={!string.IsNullOrWhiteSpace(ReadString(values, "customModel"))}.");

                InlineModelSettings snapshot;
                bool providerOrEndpointChanged;
                lock (_sync)
                {
                    var previousSettings = _settings;
                    _settings = ReadSettings(values, _settings, out _customModelInput);
                    snapshot = _settings;
                    providerOrEndpointChanged = !SameProviderEndpoint(previousSettings, snapshot);
                    if (providerOrEndpointChanged)
                    {
                        _models = [];
                        _discoveryGeneration++;
                        _modelStatus = $"Provider endpoint changed to {ProviderName(snapshot.Provider)}. Refreshing its model list…";
                    }
                    RebuildTemplateLocked();
                }

                if (action.Equals("useProvider", StringComparison.OrdinalIgnoreCase))
                {
                    var providerSavedAt = InlineModelSettingsStore.Save(snapshot);
                    var providerPersisted = InlineModelSettingsStore.Load();
                    if (!string.Equals(snapshot.Provider, providerPersisted.Provider, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The selected provider did not persist to the shared settings file.");

                    lock (_sync)
                    {
                        _settings = providerPersisted;
                        _models = [];
                        _customModelInput = string.Empty;
                        _modelStatus = $"Switched to {ProviderName(providerPersisted.Provider)}. Discovering its models…";
                        _discoveryGeneration++;
                        RebuildTemplateLocked();
                    }
                    RunMindCommandPaletteDiagnostics.Write(
                        $"Provider switched and saved; provider='{providerPersisted.Provider}'; model cleared for the new provider; write-time-utc={providerSavedAt:O}. Starting automatic model discovery.");
                    RefreshModelsInBackground(providerPersisted.ModelDiscoveryTimeoutSeconds);
                    return CommandResult.KeepOpen();
                }

                if (action.Equals("refresh", StringComparison.OrdinalIgnoreCase))
                {
                    RefreshModelsInBackground(snapshot.ModelDiscoveryTimeoutSeconds);
                    return CommandResult.KeepOpen();
                }

                if (!action.Equals("save", StringComparison.OrdinalIgnoreCase))
                    return CommandResult.ShowToast("Unknown RunMind settings action.");

                var savedAt = InlineModelSettingsStore.Save(snapshot);
                var persisted = InlineModelSettingsStore.Load();
                RunMindCommandPaletteDiagnostics.Write(
                    $"Inline settings saved and read back; provider='{persisted.Provider}'; model-set={!string.IsNullOrWhiteSpace(persisted.Model)}; model-length={persisted.Model.Length}; write-time-utc={savedAt:O}.");
                if (!string.Equals(snapshot.Model, persisted.Model, StringComparison.Ordinal))
                    throw new InvalidOperationException("The selected model did not persist to the shared inline settings file. Reopen Settings and save again; see the Command Palette log for details.");
                if (providerOrEndpointChanged) _owner.NotifyContentChanged();
                if (providerOrEndpointChanged)
                    RefreshModelsInBackground(snapshot.ModelDiscoveryTimeoutSeconds);
                var modelStatus = string.IsNullOrWhiteSpace(snapshot.Model)
                    ? " No model is selected yet; choose one from the discovered list or enter a model ID."
                    : string.Empty;
                return CommandResult.ShowToast("Shared RunMind inline settings saved for PowerToys Run and Command Palette." + modelStatus);
            }
            catch (Exception exception)
            {
                return CommandResult.ShowToast("RunMind settings were not saved: " + exception.Message);
            }
        }

        private void RebuildTemplate()
        {
            lock (_sync) RebuildTemplateLocked();
        }

        private void RebuildTemplateLocked()
        {
            var models = _models
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(model => model, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var choices = models.Count == 0
                ? new[] { new { title = "No models discovered — refresh or enter a model ID below", value = string.Empty } }
                : models.Select(model => new { title = model, value = model }).ToArray();
            var selectedModel = models.Contains(_settings.Model, StringComparer.OrdinalIgnoreCase)
                ? _settings.Model
                : string.Empty;

            var body = new List<object>
            {
                new { type = "TextBlock", text = "RunMind inline model", size = "Large", weight = "Bolder", wrap = true },
                new { type = "TextBlock", text = "These settings are shared by PowerToys Run and Command Palette. The model list is discovered from the provider you select; you can also enter any model ID manually.", wrap = true },
                new { type = "TextBlock", text = _modelStatus, isSubtle = true, wrap = true },
                new { type = "TextBlock", text = "Provider — choose one to save it and refresh its models automatically", weight = "Bolder", spacing = "Medium" },
                new { type = "ActionSet", actions = new object[]
                    {
                        new { type = "Action.Submit", title = _settings.Provider == "ollama" ? "✓ Ollama" : "Use Ollama", data = new { action = "useProvider", provider = "ollama" } },
                        new { type = "Action.Submit", title = _settings.Provider == "lmstudio" ? "✓ LM Studio" : "Use LM Studio", data = new { action = "useProvider", provider = "lmstudio" } },
                    }
                },
                new { type = "Input.Text", id = "ollamaEndpoint", label = "Ollama API endpoint", value = _settings.OllamaEndpoint, placeholder = "http://localhost:11434/api/generate" },
                new { type = "Input.Text", id = "commandPaletteOllamaApiKey", label = "Ollama API key (optional; sent as Bearer token)", style = "password", value = _settings.CommandPaletteOllamaApiKey, placeholder = "Leave empty if authentication is not required", maxLength = 4096 },
                new { type = "Input.Text", id = "commandPaletteOllamaAuthHeader", label = "Ollama custom auth header (optional; Name: value)", style = "password", value = _settings.CommandPaletteOllamaAuthHeader, placeholder = "For example: X-API-Key: your-token", maxLength = 8192 },
                new { type = "Input.Text", id = "lmStudioBaseUrl", label = "LM Studio base URL", value = _settings.LMStudioBaseUrl, placeholder = "http://localhost:1234/v1" },
                new { type = "Input.Text", id = "commandPaletteLMStudioApiKey", label = "LM Studio API key (optional; sent as Bearer token)", style = "password", value = _settings.CommandPaletteLMStudioApiKey, placeholder = "Leave empty if authentication is not required", maxLength = 4096 },
                new { type = "Input.Text", id = "commandPaletteLMStudioAuthHeader", label = "LM Studio custom auth header (optional; Name: value)", style = "password", value = _settings.CommandPaletteLMStudioAuthHeader, placeholder = "For example: X-API-Key: your-token", maxLength = 8192 },
                new { type = "TextBlock", text = "Credentials are used for both model discovery and generation. A custom header with the same name overrides the API-key header. These Command Palette credentials are separate from PowerToys Run. They are stored in the local RunMind settings JSON and are not written to logs; the file is not DPAPI-encrypted, so protect your Windows user account.", isSubtle = true, wrap = true },
                new { type = "Input.ChoiceSet", id = "model", label = "Models discovered from this provider", style = "compact", value = selectedModel, choices },
                new { type = "Input.Text", id = "customModel", label = "Or enter any model ID", value = _customModelInput, placeholder = "Custom Ollama tag or LM Studio model key", maxLength = 512 },
                new { type = "TextBlock", text = "Generation", weight = "Bolder", spacing = "Medium" },
                new { type = "Input.ChoiceSet", id = "thinking", label = "Thinking / reasoning mode (both providers)", style = "compact", value = _settings.Thinking, choices = new[] { new { title = "Auto — use model default", value = "auto" }, new { title = "On", value = "on" }, new { title = "Off", value = "off" }, new { title = "Low", value = "low" }, new { title = "Medium", value = "medium" }, new { title = "High", value = "high" } } },
                new { type = "TextBlock", text = "This is one shared control for Ollama and LM Studio. RunMind maps the selected mode to each provider's request format. Not every model supports every mode; Auto leaves model defaults untouched.", isSubtle = true, wrap = true },
                new { type = "Input.Text", id = "keepAliveSeconds", label = "Keep model loaded (seconds; Ollama only, 0–86400)", value = Number(_settings.KeepAliveSeconds), isVisible = _settings.Provider == "ollama" },
                new { type = "Input.Text", id = "requestTimeoutSeconds", label = "Request timeout including model loading and generation (seconds, 30–3600)", value = Number(_settings.RequestTimeoutSeconds) },
                new { type = "Input.Text", id = "modelDiscoveryTimeoutSeconds", label = "Model discovery timeout (seconds, 1–300)", value = Number(_settings.ModelDiscoveryTimeoutSeconds) },
                new { type = "Input.Text", id = "inlineCpuThreads", label = "Maximum CPU threads (Ollama only; 0 = automatic, 1–128)", value = Number(_settings.InlineCpuThreads), isVisible = _settings.Provider == "ollama" },
                new { type = "Input.Text", id = "inlineTemperature", label = _settings.Provider == "lmstudio" ? "Temperature (optional, 0–1 for LM Studio)" : "Temperature (optional, 0–2 for Ollama)", value = OptionalNumber(_settings.InlineTemperature), placeholder = "Provider default" },
                new { type = "Input.Text", id = "inlineTopP", label = "Top P (optional, 0–1)", value = OptionalNumber(_settings.InlineTopP), placeholder = "Provider default" },
                new { type = "Input.Text", id = "inlineMaxOutputTokens", label = "Maximum output tokens (optional)", value = OptionalNumber(_settings.InlineMaxOutputTokens), placeholder = "Provider default" },
                new { type = "Input.Text", id = "inlineSeed", label = "Seed (optional; Ollama / LM Studio compatibility mode)", value = OptionalNumber(_settings.InlineSeed), placeholder = "Random / provider default" },
                new { type = "Input.Toggle", id = "inlineConversationalMode", title = "Nemotron 3.5 Bolt E4B conversational mode (LM Studio compatibility mode)", value = _settings.InlineConversationalMode ? "true" : "false", isVisible = _settings.Provider == "lmstudio" },
                new { type = "TextBlock", text = "Command Palette system prompt", weight = "Bolder", spacing = "Medium" },
                new { type = "Input.Text", id = "commandPaletteSystemPrompt", label = "System prompt", value = _settings.CommandPaletteSystemPrompt, isMultiline = true, maxLength = 12000, placeholder = "Instructions for Command Palette queries…" },
            };

            var actions = new object[]
            {
                new { type = "Action.Submit", title = "Refresh models", data = new { action = "refresh" } },
                new { type = "Action.Submit", title = "Save settings", data = new { action = "save" } },
            };

            TemplateJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
                ["type"] = "AdaptiveCard",
                ["version"] = "1.6",
                ["body"] = body,
                ["actions"] = actions,
            });
        }

        private static bool SameProviderEndpoint(InlineModelSettings left, InlineModelSettings right)
            => string.Equals(left.Provider, right.Provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.OllamaEndpoint, right.OllamaEndpoint, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.LMStudioBaseUrl, right.LMStudioBaseUrl, StringComparison.OrdinalIgnoreCase);

        private static string ProviderName(string provider) => provider == "lmstudio" ? "LM Studio" : "Ollama";

        private static JsonObject FlattenPayload(string inputs, string data)
        {
            var flattened = new JsonObject();
            var inputRoot = ParsePayloadObject(inputs, "form inputs");
            var dataRoot = ParsePayloadObject(data, "form action data");

            // Accept both the current SDK contract (separate inputs/data JSON)
            // and older combined payloads used by earlier Command Palette builds.
            Merge(inputRoot["data"] as JsonObject, flattened);
            Merge(inputRoot["inputs"] as JsonObject, flattened);
            Merge(inputRoot, flattened);
            Merge(dataRoot["data"] as JsonObject, flattened);
            Merge(dataRoot["inputs"] as JsonObject, flattened);
            Merge(dataRoot, flattened);
            return flattened;
        }

        private static JsonObject ParsePayloadObject(string? json, string description)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
            try
            {
                return JsonNode.Parse(json) as JsonObject
                    ?? throw new InvalidDataException($"Command Palette returned invalid {description} JSON.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"Command Palette returned invalid {description} JSON.", exception);
            }
        }

        private static void Merge(JsonObject? source, JsonObject destination)
        {
            if (source is null) return;
            foreach (var pair in source)
                destination[pair.Key] = pair.Value?.DeepClone();
        }

        private InlineModelSettings ReadSettings(JsonObject values, InlineModelSettings current, out string customModelInput)
        {
            var provider = ReadStringOrCurrent(values, "provider", current.Provider);
            if (provider is not ("ollama" or "lmstudio"))
                throw new InvalidDataException("Choose Ollama or LM Studio as the provider.");

            var providerChanged = !string.Equals(provider, current.Provider, StringComparison.OrdinalIgnoreCase);
            var selectedModel = ReadString(values, "model");
            customModelInput = ReadString(values, "customModel");
            var customModelChanged = !string.Equals(customModelInput, _customModelInput, StringComparison.Ordinal);
            if (providerChanged)
            {
                // A selected model belongs to its provider. Do not carry it across
                // provider changes, but keep a new manual ID entered in this form.
                selectedModel = string.Empty;
                if (!customModelChanged) customModelInput = string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(selectedModel)
                && !string.Equals(selectedModel, current.Model, StringComparison.OrdinalIgnoreCase)
                && !customModelChanged)
            {
                // Choosing a discovered model clears a stale manual override. This
                // keeps a previously saved custom ID from shadowing the dropdown.
                customModelInput = string.Empty;
            }
            var model = string.IsNullOrWhiteSpace(customModelInput) ? selectedModel : customModelInput;

            return InlineModelSettingsStore.Normalize(current with
            {
                Provider = provider,
                OllamaEndpoint = ReadStringOrCurrent(values, "ollamaEndpoint", current.OllamaEndpoint),
                LMStudioBaseUrl = ReadStringOrCurrent(values, "lmStudioBaseUrl", current.LMStudioBaseUrl),
                Model = model,
                Thinking = ReadStringOrCurrent(values, "thinking", current.Thinking),
                KeepAliveSeconds = provider == "ollama"
                    ? ReadIntOrCurrent(values, "keepAliveSeconds", 0, 86400, "Keep model loaded", current.KeepAliveSeconds)
                    : current.KeepAliveSeconds,
                RequestTimeoutSeconds = ReadInt(values, "requestTimeoutSeconds", 30, 3600, "Request timeout"),
                ModelDiscoveryTimeoutSeconds = ReadInt(values, "modelDiscoveryTimeoutSeconds", 1, 300, "Model discovery timeout"),
                InlineCpuThreads = provider == "ollama"
                    ? ReadIntOrCurrent(values, "inlineCpuThreads", 0, 128, "Maximum CPU threads", current.InlineCpuThreads)
                    : current.InlineCpuThreads,
                InlineTemperature = ReadOptionalDouble(values, "inlineTemperature", 0, provider == "lmstudio" ? 1 : 2, "Temperature"),
                InlineTopP = ReadOptionalDouble(values, "inlineTopP", 0, 1, "Top P"),
                InlineMaxOutputTokens = ReadOptionalInt(values, "inlineMaxOutputTokens", 1, 1_000_000, "Maximum output tokens"),
                InlineSeed = ReadOptionalInt(values, "inlineSeed", 0, int.MaxValue, "Seed"),
                InlineConversationalMode = provider == "lmstudio"
                    ? ReadBoolean(values, "inlineConversationalMode", current.InlineConversationalMode)
                    : current.InlineConversationalMode,
                CommandPaletteSystemPrompt = ReadStringOrCurrent(values, "commandPaletteSystemPrompt", current.CommandPaletteSystemPrompt),
                CommandPaletteOllamaApiKey = ReadStringOrCurrent(values, "commandPaletteOllamaApiKey", current.CommandPaletteOllamaApiKey),
                CommandPaletteOllamaAuthHeader = ReadStringOrCurrent(values, "commandPaletteOllamaAuthHeader", current.CommandPaletteOllamaAuthHeader),
                CommandPaletteLMStudioApiKey = ReadStringOrCurrent(values, "commandPaletteLMStudioApiKey", current.CommandPaletteLMStudioApiKey),
                CommandPaletteLMStudioAuthHeader = ReadStringOrCurrent(values, "commandPaletteLMStudioAuthHeader", current.CommandPaletteLMStudioAuthHeader),
            });
        }

        private static string ReadString(JsonObject values, string key)
        {
            var value = values[key];
            return value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) ? text?.Trim() ?? string.Empty : string.Empty;
        }

        private static string ReadStringOrCurrent(JsonObject values, string key, string fallback)
            => values.ContainsKey(key) ? ReadString(values, key) : fallback;

        private static bool ReadBoolean(JsonObject values, string key, bool fallback)
        {
            if (values[key] is not JsonValue value) return fallback;
            if (value.TryGetValue<bool>(out var boolean)) return boolean;
            if (value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed)) return parsed;
            return fallback;
        }

        private static int ReadInt(JsonObject values, string key, int minimum, int maximum, string label)
        {
            var raw = ReadString(values, key);
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
                throw new InvalidDataException($"{label} must be between {minimum} and {maximum}.");
            return value;
        }

        private static int ReadIntOrCurrent(JsonObject values, string key, int minimum, int maximum, string label, int fallback)
        {
            var raw = ReadString(values, key);
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
                throw new InvalidDataException($"{label} must be between {minimum} and {maximum}.");
            return value;
        }

        private static int? ReadOptionalInt(JsonObject values, string key, int minimum, int maximum, string label)
        {
            var raw = ReadString(values, key);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
                throw new InvalidDataException($"{label} must be between {minimum} and {maximum}.");
            return value;
        }

        private static double? ReadOptionalDouble(JsonObject values, string key, double minimum, double maximum, string label)
        {
            var raw = ReadString(values, key);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value) || value < minimum || value > maximum)
                throw new InvalidDataException($"{label} must be between {minimum} and {maximum}.");
            return value;
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string OptionalNumber<T>(T? value) where T : struct, IFormattable
            => value.HasValue ? value.Value.ToString(null, CultureInfo.InvariantCulture) : string.Empty;
    }
}
