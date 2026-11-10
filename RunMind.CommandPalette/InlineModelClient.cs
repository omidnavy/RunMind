using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using RunMind.Shared;

namespace RunMind.CommandPalette;

internal sealed record InlineConversationTurn(
    string Prompt,
    string Response,
    string Provider = "",
    string Model = "",
    long TimestampMilliseconds = 0);

/// <summary>
/// Streaming client used by the Command Palette inline conversation page.
/// It reads the same shared provider/model settings as PowerToys Run.
/// </summary>
internal static class InlineModelClient
{
    private const int MaximumResponseCharacters = 8 * 1024 * 1024;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    internal static async Task<string> GenerateAsync(
        InlineModelSettings rawSettings,
        IReadOnlyList<InlineConversationTurn> history,
        string prompt,
        Action<string> onChunk,
        Action<string> onThinkingAutoFallback,
        CancellationToken cancellationToken)
    {
        var settings = InlineModelSettingsStore.Normalize(rawSettings);
        if (string.IsNullOrWhiteSpace(settings.Model))
            throw new InvalidOperationException("No model is selected. Open Command Palette → Installed Extensions → RunMind → Settings, refresh the model list, and select one.");

        // Model discovery is a convenience for the selector. The provider remains
        // authoritative about whether an arbitrary model ID exists and can be used.
        var emittedContent = false;
        void TrackChunk(string chunk)
        {
            if (!string.IsNullOrEmpty(chunk)) emittedContent = true;
            onChunk(chunk);
        }

        try
        {
            return await GenerateWithSettingsAsync(settings, history, prompt, TrackChunk, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && !emittedContent
            && IsUnsupportedThinkingError(settings.Thinking, exception))
        {
            // A provider may reject the explicitly selected reasoning mode for a
            // model that does not expose that feature. Persist Auto to the shared
            // configuration, notify the user, and retry exactly once without an
            // explicit reasoning parameter. Never retry after answer text streamed.
            var autoSettings = InlineModelSettingsStore.Normalize(settings with { Thinking = "auto" });
            InlineModelSettingsStore.Save(autoSettings);
            var persisted = InlineModelSettingsStore.Load();
            if (!string.Equals(persisted.Thinking, "auto", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The model rejected the selected reasoning mode, and RunMind could not persist Auto to the shared settings file.", exception);

            RunMindCommandPaletteDiagnostics.Write(
                $"The selected model rejected Thinking='{settings.Thinking}'. Saved Thinking='auto' to the shared settings; retrying once without an explicit reasoning parameter.", exception);
            onThinkingAutoFallback("Think mode was changed to Auto because this model does not support the selected mode. The change was saved for PowerToys Run and Command Palette.");

            return await GenerateWithSettingsAsync(persisted, history, prompt, onChunk, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task<string> GenerateWithSettingsAsync(
        InlineModelSettings settings,
        IReadOnlyList<InlineConversationTurn> history,
        string prompt,
        Action<string> onChunk,
        CancellationToken cancellationToken)
        => settings.Provider == "lmstudio"
            ? GenerateLMStudioAsync(settings, history, prompt, onChunk, cancellationToken)
            : GenerateOllamaAsync(settings, history, prompt, onChunk, cancellationToken);

    private static bool IsUnsupportedThinkingError(string? thinking, Exception exception)
    {
        if (string.IsNullOrWhiteSpace(thinking)
            || string.Equals(thinking.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
            return false;

        var message = exception.ToString();
        if (exception is HttpRequestException requestException
            && requestException.StatusCode != System.Net.HttpStatusCode.BadRequest)
            return false;

        var normalized = message.ToLowerInvariant();
        var mentionsReasoning = normalized.Contains("reasoning", StringComparison.Ordinal)
            || normalized.Contains("think", StringComparison.Ordinal);
        var rejectsSetting = normalized.Contains("does not expose", StringComparison.Ordinal)
            || normalized.Contains("doesn't support", StringComparison.Ordinal)
            || normalized.Contains("does not support", StringComparison.Ordinal)
            || normalized.Contains("unsupported", StringComparison.Ordinal)
            || normalized.Contains("invalid_value", StringComparison.Ordinal)
            || normalized.Contains("invalid value", StringComparison.Ordinal)
            || normalized.Contains("unknown parameter", StringComparison.Ordinal)
            || normalized.Contains("not supported", StringComparison.Ordinal);
        return mentionsReasoning && rejectsSetting;
    }

    private static async Task<string> GenerateOllamaAsync(
        InlineModelSettings settings,
        IReadOnlyList<InlineConversationTurn> history,
        string prompt,
        Action<string> onChunk,
        CancellationToken cancellationToken)
    {
        var endpoint = ValidateHttpUri(settings.OllamaEndpoint, "Ollama endpoint");
        var requestBody = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["model"] = settings.Model,
            ["prompt"] = BuildOllamaPrompt(history, prompt),
            ["stream"] = true,
            ["keep_alive"] = settings.KeepAliveSeconds,
        };
        if (!string.IsNullOrWhiteSpace(settings.CommandPaletteSystemPrompt))
            requestBody["system"] = settings.CommandPaletteSystemPrompt;

        var options = new Dictionary<string, object>(StringComparer.Ordinal);
        if (settings.InlineCpuThreads > 0) options["num_thread"] = settings.InlineCpuThreads;
        if (settings.InlineTemperature is double temperature) options["temperature"] = temperature;
        if (settings.InlineTopP is double topP) options["top_p"] = topP;
        if (settings.InlineMaxOutputTokens is int maxTokens) options["num_predict"] = maxTokens;
        if (settings.InlineSeed is int seed) options["seed"] = seed;
        if (options.Count > 0) requestBody["options"] = options;
        if (TryResolveOllamaThinking(settings.Thinking, out var thinking))
            requestBody["think"] = thinking;

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };
        InlineModelSettingsStore.ApplyCommandPaletteAuthentication(request, settings);
        using var totalTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(totalTimeout.Token, cancellationToken);
        // The configured request timeout covers model loading, response headers,
        // and the complete streamed response. A shorter connect timeout would
        // cancel local models while LM Studio/Ollama is still loading them.
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw await CreateHttpExceptionAsync("Ollama", response, requestTimeout.Token).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(requestTimeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var output = new StringBuilder();
        var recordNumber = 0;
        var completed = false;
        while (true)
        {
            var line = await reader.ReadLineAsync(requestTimeout.Token).ConfigureAwait(false);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            recordNumber++;
            using var json = ParseJson(line, $"Ollama NDJSON record {recordNumber}");
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                throw new InvalidOperationException("Ollama: " + error.GetString());
            if (root.TryGetProperty("response", out var content) && content.ValueKind == JsonValueKind.String)
                AppendChunk(output, content.GetString(), "Ollama", onChunk);
            if (root.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True)
            {
                completed = true;
                break;
            }
        }

        if (!completed)
            throw new InvalidDataException("Ollama closed its response stream before sending done:true. The answer may be incomplete.");
        return output.ToString();
    }

    private static async Task<string> GenerateLMStudioAsync(
        InlineModelSettings settings,
        IReadOnlyList<InlineConversationTurn> history,
        string prompt,
        Action<string> onChunk,
        CancellationToken cancellationToken)
    {
        // The current LM Studio REST API exposes a provider-native reasoning
        // setting and can load an installed model by its model key. Use it for
        // normal conversations. Keep the OpenAI-compatible route for the two
        // optional legacy parameters that route currently supports (seed and
        // Nemotron's custom chat-template flag).
        if (settings.InlineSeed.HasValue
            || (settings.InlineConversationalMode && IsNemotron35BoltE4B(settings.Model)))
        {
            return await GenerateLMStudioOpenAICompatAsync(settings, history, prompt, onChunk, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await GenerateLMStudioNativeAsync(settings, history, prompt, onChunk, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed)
        {
            var thinking = string.IsNullOrWhiteSpace(settings.Thinking) ? "auto" : settings.Thinking.Trim().ToLowerInvariant();
            if (thinking != "auto")
                throw new InvalidOperationException(
                    "This LM Studio server does not expose its native /api/v1/chat endpoint, which RunMind needs to apply the shared Thinking / reasoning setting. Upgrade LM Studio or set Thinking / reasoning to Auto.", exception);

            return await GenerateLMStudioOpenAICompatAsync(settings, history, prompt, onChunk, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> GenerateLMStudioNativeAsync(
        InlineModelSettings settings,
        IReadOnlyList<InlineConversationTurn> history,
        string prompt,
        Action<string> onChunk,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildLMStudioNativeUri(settings.LMStudioBaseUrl, "chat");
        var requestBody = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["model"] = settings.Model,
            ["input"] = BuildLMStudioInput(history, prompt),
            ["stream"] = true,
            ["store"] = false,
        };
        if (!string.IsNullOrWhiteSpace(settings.CommandPaletteSystemPrompt))
            requestBody["system_prompt"] = settings.CommandPaletteSystemPrompt;
        if (settings.InlineTemperature is double temperature) requestBody["temperature"] = Math.Min(temperature, 1.0);
        if (settings.InlineTopP is double topP) requestBody["top_p"] = topP;
        if (settings.InlineMaxOutputTokens is int maxTokens) requestBody["max_output_tokens"] = maxTokens;

        var thinking = string.IsNullOrWhiteSpace(settings.Thinking) ? "auto" : settings.Thinking.Trim().ToLowerInvariant();
        if (thinking != "auto") requestBody["reasoning"] = thinking;

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };
        InlineModelSettingsStore.ApplyCommandPaletteAuthentication(request, settings);
        using var totalTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(totalTimeout.Token, cancellationToken);
        // The configured request timeout covers model loading, response headers,
        // and the complete streamed response. A shorter connect timeout would
        // cancel local models while LM Studio/Ollama is still loading them.
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw await CreateHttpExceptionAsync("LM Studio", response, requestTimeout.Token).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(requestTimeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var output = new StringBuilder();
        var eventName = string.Empty;
        var eventNumber = 0;
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

            eventNumber++;
            using var json = ParseJson(data, $"LM Studio REST SSE event {eventNumber}");
            var root = json.RootElement;
            var eventType = root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString() ?? eventName
                : eventName;

            if (string.Equals(eventType, "error", StringComparison.OrdinalIgnoreCase))
            {
                var detail = root.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var message)
                    ? message.GetString()
                    : root.ToString();
                throw new InvalidOperationException("LM Studio: " + (string.IsNullOrWhiteSpace(detail) ? "The model request failed." : detail));
            }

            if (string.Equals(eventType, "message.delta", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                AppendChunk(output, content.GetString(), "LM Studio", onChunk);
            }
            else if (string.Equals(eventType, "chat.end", StringComparison.OrdinalIgnoreCase))
            {
                completed = true;
                if (output.Length == 0 && root.TryGetProperty("result", out var result)
                    && result.TryGetProperty("output", out var outputItems)
                    && outputItems.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in outputItems.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object
                            && item.TryGetProperty("type", out var itemType)
                            && itemType.ValueKind == JsonValueKind.String
                            && itemType.GetString() == "message"
                            && item.TryGetProperty("content", out var itemContent)
                            && itemContent.ValueKind == JsonValueKind.String)
                            AppendChunk(output, itemContent.GetString(), "LM Studio", onChunk);
                    }
                }
                break;
            }
        }

        if (!completed)
            throw new InvalidDataException("LM Studio closed its REST response stream before chat.end. The answer may be incomplete.");
        return output.ToString();
    }

    private static async Task<string> GenerateLMStudioOpenAICompatAsync(
        InlineModelSettings settings,
        IReadOnlyList<InlineConversationTurn> history,
        string prompt,
        Action<string> onChunk,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildLMStudioUri(settings.LMStudioBaseUrl, "chat/completions");
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(settings.CommandPaletteSystemPrompt))
            messages.Add(new { role = "system", content = settings.CommandPaletteSystemPrompt });
        foreach (var turn in history)
        {
            messages.Add(new { role = "user", content = turn.Prompt });
            messages.Add(new { role = "assistant", content = turn.Response });
        }
        messages.Add(new { role = "user", content = prompt });

        var requestBody = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["model"] = settings.Model,
            ["messages"] = messages,
            ["stream"] = true,
        };
        if (settings.InlineTemperature is double temperature) requestBody["temperature"] = temperature;
        if (settings.InlineTopP is double topP) requestBody["top_p"] = topP;
        if (settings.InlineMaxOutputTokens is int maxTokens) requestBody["max_tokens"] = maxTokens;
        if (settings.InlineSeed is int seed) requestBody["seed"] = seed;
        ApplyLMStudioThinking(requestBody, settings.Thinking);

        if (IsNemotron35BoltE4B(settings.Model))
        {
            var templateOptions = requestBody.TryGetValue("chat_template_kwargs", out var existing)
                && existing is Dictionary<string, object> existingOptions
                    ? existingOptions
                    : new Dictionary<string, object>(StringComparer.Ordinal);
            templateOptions["conversational_mode"] = settings.InlineConversationalMode;
            requestBody["chat_template_kwargs"] = templateOptions;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
        };
        InlineModelSettingsStore.ApplyCommandPaletteAuthentication(request, settings);
        using var totalTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(totalTimeout.Token, cancellationToken);
        // The configured request timeout covers model loading, response headers,
        // and the complete streamed response. A shorter connect timeout would
        // cancel local models while LM Studio/Ollama is still loading them.
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw await CreateHttpExceptionAsync("LM Studio", response, requestTimeout.Token).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(requestTimeout.Token).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var output = new StringBuilder();
        var eventNumber = 0;
        var completed = false;
        while (true)
        {
            var line = await reader.ReadLineAsync(requestTimeout.Token).ConfigureAwait(false);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].Trim();
            if (data.Length == 0) continue;
            if (data.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                completed = true;
                break;
            }
            eventNumber++;
            using var json = ParseJson(data, $"LM Studio compatibility SSE event {eventNumber}");
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error))
                throw new InvalidOperationException("LM Studio: " + (error.ValueKind == JsonValueKind.String ? error.GetString() : error.ToString()));
            if (root.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta)
                    && delta.ValueKind == JsonValueKind.Object
                    && delta.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.String)
                    AppendChunk(output, content.GetString(), "LM Studio", onChunk);
                else if (choice.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.Object
                    && message.TryGetProperty("content", out content)
                    && content.ValueKind == JsonValueKind.String)
                    AppendChunk(output, content.GetString(), "LM Studio", onChunk);
                if (choice.TryGetProperty("finish_reason", out var finishReason)
                    && finishReason.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(finishReason.GetString()))
                    completed = true;
            }
        }

        if (!completed)
            throw new InvalidDataException("LM Studio closed its compatibility stream without [DONE] or a final finish_reason.");
        return output.ToString();
    }

    private static string BuildLMStudioInput(IReadOnlyList<InlineConversationTurn> history, string prompt)
    {
        if (history.Count == 0) return prompt;
        var builder = new StringBuilder();
        foreach (var turn in history)
        {
            builder.Append("User: ").AppendLine(turn.Prompt);
            builder.Append("Assistant: ").AppendLine(turn.Response).AppendLine();
        }
        builder.Append("User: ").AppendLine(prompt);
        builder.Append("Assistant:");
        return builder.ToString();
    }

    private static async Task<Exception> CreateHttpExceptionAsync(string provider, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (body.Length > 16_384) body = body[..16_384] + "…";
        var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : " Response: " + body;
        return new HttpRequestException($"{provider} returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{detail}", null, response.StatusCode);
    }

    private static JsonDocument ParseJson(string value, string source)
    {
        try { return JsonDocument.Parse(value); }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{source} contained invalid JSON: {exception.Message}", exception);
        }
    }

    private static void AppendChunk(StringBuilder output, string? chunk, string provider, Action<string> onChunk)
    {
        if (string.IsNullOrEmpty(chunk)) return;
        if (chunk.Length > MaximumResponseCharacters - output.Length)
            throw new InvalidDataException($"The {provider} response exceeded RunMind's {MaximumResponseCharacters:N0}-character inline limit.");
        output.Append(chunk);
        onChunk(chunk);
    }

    private static string BuildOllamaPrompt(IReadOnlyList<InlineConversationTurn> history, string prompt)
    {
        if (history.Count == 0) return prompt;
        var builder = new StringBuilder();
        foreach (var turn in history)
        {
            builder.Append("User: ").Append(turn.Prompt).AppendLine();
            builder.Append("Assistant: ").Append(turn.Response).AppendLine().AppendLine();
        }
        builder.Append("User: ").Append(prompt).AppendLine();
        builder.Append("Assistant:");
        return builder.ToString();
    }

    private static void ApplyLMStudioThinking(IDictionary<string, object> body, string? rawSetting)
    {
        var setting = string.IsNullOrWhiteSpace(rawSetting) ? "auto" : rawSetting.Trim().ToLowerInvariant();
        if (setting == "auto") return;

        if (setting is "on" or "true" or "off" or "false")
        {
            var enabled = setting is "on" or "true";
            var templateOptions = body.TryGetValue("chat_template_kwargs", out var existing)
                && existing is Dictionary<string, object> existingOptions
                    ? existingOptions
                    : new Dictionary<string, object>(StringComparer.Ordinal);
            templateOptions["enable_thinking"] = enabled;
            body["chat_template_kwargs"] = templateOptions;
            if (!enabled) body["reasoning_effort"] = "none";
            return;
        }

        if (setting is "low" or "medium" or "high")
        {
            body["reasoning_effort"] = setting;
            var templateOptions = body.TryGetValue("chat_template_kwargs", out var existing)
                && existing is Dictionary<string, object> existingOptions
                    ? existingOptions
                    : new Dictionary<string, object>(StringComparer.Ordinal);
            templateOptions["enable_thinking"] = true;
            body["chat_template_kwargs"] = templateOptions;
            return;
        }

        throw new InvalidOperationException("Thinking / reasoning mode must be auto, on, off, low, medium, or high. Model support depends on the selected provider and model.");
    }

    private static bool TryResolveOllamaThinking(string? rawSetting, out object value)
    {
        var setting = string.IsNullOrWhiteSpace(rawSetting) ? "auto" : rawSetting.Trim().ToLowerInvariant();
        if (setting == "auto")
        {
            // Let the selected model use its native default. Auto must not infer
            // behavior from a model name or silently disable reasoning.
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

    private static bool IsNemotron35BoltE4B(string model)
        => model.Contains("nemotron-3.5-bolt-e4b", StringComparison.OrdinalIgnoreCase)
            || model.Contains("nemotron-3.5-bolt", StringComparison.OrdinalIgnoreCase);

    private static Uri ValidateHttpUri(string value, string label)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException($"Enter a valid {label} using HTTP or HTTPS.");
        return uri;
    }

    private static Uri BuildLMStudioNativeUri(string baseUrl, string relativePath)
    {
        var uri = ValidateHttpUri(baseUrl, "LM Studio base URL");
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("The LM Studio base URL must not include a query string or fragment.");
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) path = path[..^3];
        var builder = new UriBuilder(uri)
        {
            Path = path + "/api/v1/" + relativePath.TrimStart('/'),
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri;
    }

    private static Uri BuildLMStudioUri(string baseUrl, string relativePath)
    {
        var uri = ValidateHttpUri(baseUrl, "LM Studio base URL");
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("The LM Studio base URL must not include a query string or fragment.");
        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/" + relativePath.TrimStart('/'),
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri;
    }
}
