#nullable enable

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RunMind.Runner;

internal sealed record RunnerModelDiscoveryResult(IReadOnlyList<string> Models, string Message)
{
    public bool Succeeded => Models.Count > 0;
}

/// <summary>Runner-only inline generation client for Ollama's native API and LM Studio's APIs.</summary>
internal sealed class RunnerInlineModelClient
{
    private const int MaximumResponseCharacters = 8 * 1024 * 1024;
    private const int MaximumStreamLineCharacters = 1024 * 1024;
    private const int MaximumErrorBytes = 64 * 1024;
    private const int MaximumModelListBytes = 4 * 1024 * 1024;
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt, IReadOnlyList<string> Models)> ModelCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<RunnerModelDiscoveryResult> DiscoverModelsAsync(
        RunnerInlineSettings rawSettings,
        RunnerInlineCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var settings = RunnerInlineSettingsStore.Normalize(rawSettings);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.ModelDiscoveryTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try
        {
            var endpoint = settings.Provider == "ollama"
                ? BuildOllamaTagsUri(settings.OllamaEndpoint).AbsoluteUri
                : BuildLMStudioNativeUri(settings.LMStudioBaseUrl, "models").AbsoluteUri;
            var apiKey = settings.Provider == "ollama" ? credentials.OllamaApiKey : credentials.LMStudioApiKey;
            var customHeader = settings.Provider == "ollama" ? credentials.OllamaAuthHeader : credentials.LMStudioAuthHeader;
            var credentialFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((apiKey ?? string.Empty) + "\0" + (customHeader ?? string.Empty))));
            var cacheKey = endpoint + "|" + credentialFingerprint;
            if (ModelCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
                return new RunnerModelDiscoveryResult(cached.Models, $"Found {cached.Models.Count} cached model(s).");

            IReadOnlyList<string> models;
            if (settings.Provider == "ollama")
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                ApplyAuthentication(request, credentials.OllamaApiKey, credentials.OllamaAuthHeader);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                await EnsureSuccessAsync(response, "Ollama model discovery", linked.Token).ConfigureAwait(false);
                var bytes = await ReadBoundedBytesAsync(response.Content, MaximumModelListBytes, linked.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(bytes);
                if (!document.RootElement.TryGetProperty("models", out var items) || items.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Ollama returned an unexpected model-list response (expected a 'models' array).");
                models = items.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetProperty("name").GetString()!.Trim())
                    .Where(name => name.Length is > 0 and <= 512)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Take(500)
                    .ToArray();
            }
            else
            {
                models = await DiscoverLMStudioModelsAsync(settings, credentials, linked.Token).ConfigureAwait(false);
            }

            ModelCache[cacheKey] = (DateTimeOffset.UtcNow.AddSeconds(8), models);
            return new RunnerModelDiscoveryResult(models,
                models.Count == 0 ? $"{ProviderName(settings)} is reachable, but no models were returned." : $"Found {models.Count} model(s) from {ProviderName(settings)}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return new RunnerModelDiscoveryResult([], $"Model discovery timed out after {settings.ModelDiscoveryTimeoutSeconds} seconds.");
        }
        catch (Exception exception)
        {
            return new RunnerModelDiscoveryResult([], $"Could not discover models: {UsefulMessage(exception)}");
        }
    }

    public async Task<string> GenerateAsync(
        string rawPrompt,
        RunnerInlineSettings rawSettings,
        RunnerInlineCredentials credentials,
        IProgress<string>? partialResponse = null,
        CancellationToken cancellationToken = default)
    {
        var settings = RunnerInlineSettingsStore.Normalize(rawSettings);
        if (!settings.Enabled) throw new InvalidOperationException("The RunMind inline plugin is disabled in Runner settings.");
        var (requestedModel, prompt) = ParseModelAndPrompt(rawPrompt, settings.Model);
        if (string.IsNullOrWhiteSpace(prompt)) throw new InvalidOperationException("Enter a prompt for RunMind.");
        if (string.IsNullOrWhiteSpace(requestedModel)) throw new InvalidOperationException("No default model is selected. Select a model in Runner Settings or use '> model > prompt'.");

        var discovery = await DiscoverModelsAsync(settings, credentials, cancellationToken).ConfigureAwait(false);
        if (!discovery.Succeeded)
            throw new InvalidOperationException(discovery.Message);
        if (!discovery.Models.Contains(requestedModel, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Model '{requestedModel}' was not found. Available models: {string.Join(", ", discovery.Models.Take(40))}");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        try
        {
            return settings.Provider == "ollama"
                ? await GenerateOllamaAsync(prompt, requestedModel, settings, credentials, partialResponse, linked.Token).ConfigureAwait(false)
                : await GenerateLMStudioAsync(prompt, requestedModel, settings, credentials, partialResponse, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"The inline model request exceeded the configured {settings.RequestTimeoutSeconds}-second request timeout.");
        }
    }

    private static async Task<IReadOnlyList<string>> DiscoverLMStudioModelsAsync(
        RunnerInlineSettings settings,
        RunnerInlineCredentials credentials,
        CancellationToken cancellationToken)
    {
        var nativeUri = BuildLMStudioNativeUri(settings.LMStudioBaseUrl, "models");
        using var nativeRequest = new HttpRequestMessage(HttpMethod.Get, nativeUri);
        ApplyAuthentication(nativeRequest, credentials.LMStudioApiKey, credentials.LMStudioAuthHeader);
        using var nativeResponse = await Http.SendAsync(nativeRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (nativeResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            using var compatibilityRequest = new HttpRequestMessage(HttpMethod.Get, BuildLMStudioUri(settings.LMStudioBaseUrl, "models"));
            ApplyAuthentication(compatibilityRequest, credentials.LMStudioApiKey, credentials.LMStudioAuthHeader);
            using var compatibilityResponse = await Http.SendAsync(compatibilityRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(compatibilityResponse, "LM Studio model discovery", cancellationToken).ConfigureAwait(false);
            var bytes = await ReadBoundedBytesAsync(compatibilityResponse.Content, MaximumModelListBytes, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("LM Studio returned an unexpected /v1/models response (expected a 'data' array).");
            return data.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                .Select(item => item.GetProperty("id").GetString()!.Trim())
                .Where(name => name.Length is > 0 and <= 512)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .ToArray();
        }

        await EnsureSuccessAsync(nativeResponse, "LM Studio model discovery", cancellationToken).ConfigureAwait(false);
        var nativeBytes = await ReadBoundedBytesAsync(nativeResponse.Content, MaximumModelListBytes, cancellationToken).ConfigureAwait(false);
        using var nativeDocument = JsonDocument.Parse(nativeBytes);
        if (!nativeDocument.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("LM Studio returned an unexpected /api/v1/models response (expected a 'models' array).");
        return models.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), "llm", StringComparison.OrdinalIgnoreCase)
                && item.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String)
            .Select(item => item.GetProperty("key").GetString()!.Trim())
            .Where(name => name.Length is > 0 and <= 512)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToArray();
    }

    private static async Task<string> GenerateOllamaAsync(
        string prompt,
        string model,
        RunnerInlineSettings settings,
        RunnerInlineCredentials credentials,
        IProgress<string>? partialResponse,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["stream"] = true,
            ["keep_alive"] = settings.KeepAliveSeconds,
        };
        if (!string.IsNullOrWhiteSpace(settings.SystemPrompt)) body["system"] = settings.SystemPrompt;
        var options = new Dictionary<string, object>(StringComparer.Ordinal);
        if (settings.InlineCpuThreads > 0) options["num_thread"] = settings.InlineCpuThreads;
        if (settings.Temperature is double temperature) options["temperature"] = temperature;
        if (settings.TopP is double topP) options["top_p"] = topP;
        if (settings.MaxOutputTokens is int maxTokens) options["num_predict"] = maxTokens;
        if (settings.Seed is int seed) options["seed"] = seed;
        if (options.Count > 0) body["options"] = options;
        if (TryResolveThinkValue(settings.Thinking, out var thinkValue)) body["think"] = thinkValue;

        using var request = new HttpRequestMessage(HttpMethod.Post, ValidateOllamaGenerateEndpoint(settings.OllamaEndpoint))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        ApplyAuthentication(request, credentials.OllamaApiKey, credentials.OllamaAuthHeader);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Ollama generation", cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var boundedReader = new BoundedStreamLineReader(reader, MaximumStreamLineCharacters);
        var output = new StringBuilder();
        var record = 0;
        var complete = false;
        while (true)
        {
            var line = await boundedReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            record++;
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(error.GetString()))
                throw new InvalidOperationException("Ollama: " + error.GetString());
            if (root.TryGetProperty("response", out var chunk) && chunk.ValueKind == JsonValueKind.String)
                AppendChunk(output, chunk.GetString(), partialResponse, "Ollama");
            if (root.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True)
            {
                complete = true;
                break;
            }
        }
        if (!complete) throw new InvalidDataException($"Ollama closed the response stream before its final done:true record (record {record}). The response may be incomplete.");
        return output.ToString();
    }

    private static async Task<string> GenerateLMStudioAsync(
        string prompt,
        string model,
        RunnerInlineSettings settings,
        RunnerInlineCredentials credentials,
        IProgress<string>? partialResponse,
        CancellationToken cancellationToken)
    {
        var compatibilityRequired = settings.Seed.HasValue || (settings.Nemotron35BoltE4BConversationalMode && IsNemotron35BoltE4BModel(model));
        if (compatibilityRequired)
            return await GenerateLMStudioOpenAIAsync(prompt, model, settings, credentials, partialResponse, cancellationToken).ConfigureAwait(false);

        try
        {
            return await GenerateLMStudioNativeAsync(prompt, model, settings, credentials, partialResponse, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
        {
            if (!string.Equals(settings.Thinking, "auto", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This LM Studio server does not expose /api/v1/chat, which RunMind needs to apply the selected Thinking / reasoning mode. Upgrade LM Studio or set Thinking / reasoning to Auto.", exception);
            return await GenerateLMStudioOpenAIAsync(prompt, model, settings, credentials, partialResponse, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> GenerateLMStudioNativeAsync(
        string prompt,
        string model,
        RunnerInlineSettings settings,
        RunnerInlineCredentials credentials,
        IProgress<string>? partialResponse,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["model"] = model,
            ["input"] = prompt,
            ["stream"] = true,
            ["store"] = false,
        };
        if (!string.IsNullOrWhiteSpace(settings.SystemPrompt)) body["system_prompt"] = settings.SystemPrompt;
        if (settings.Temperature is double temperature) body["temperature"] = Math.Min(temperature, 1.0);
        if (settings.TopP is double topP) body["top_p"] = topP;
        if (settings.MaxOutputTokens is int maxTokens) body["max_output_tokens"] = maxTokens;
        if (settings.Thinking != "auto") body["reasoning"] = settings.Thinking;

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildLMStudioNativeUri(settings.LMStudioBaseUrl, "chat"))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        ApplyAuthentication(request, credentials.LMStudioApiKey, credentials.LMStudioAuthHeader);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "LM Studio generation", cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var boundedReader = new BoundedStreamLineReader(reader, MaximumStreamLineCharacters);
        var output = new StringBuilder();
        var eventName = string.Empty;
        var completed = false;
        var record = 0;
        while (true)
        {
            var line = await boundedReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase)) { eventName = line[6..].Trim(); continue; }
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].Trim();
            if (data.Length == 0) continue;
            record++;
            using var json = JsonDocument.Parse(data);
            var root = json.RootElement;
            var type = root.TryGetProperty("type", out var typeValue) && typeValue.ValueKind == JsonValueKind.String ? typeValue.GetString() : eventName;
            if (string.Equals(type, "error", StringComparison.OrdinalIgnoreCase))
            {
                var detail = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message)
                    ? message.GetString() : root.ToString();
                throw new InvalidOperationException("LM Studio: " + (string.IsNullOrWhiteSpace(detail) ? "The model request failed." : detail));
            }
            if (string.Equals(type, "message.delta", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                AppendChunk(output, content.GetString(), partialResponse, "LM Studio");
            else if (string.Equals(type, "chat.end", StringComparison.OrdinalIgnoreCase))
            {
                completed = true;
                if (output.Length == 0 && root.TryGetProperty("result", out var result)
                    && result.TryGetProperty("output", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("type", out var itemType)
                            && itemType.ValueKind == JsonValueKind.String && itemType.GetString() == "message"
                            && item.TryGetProperty("content", out var messageContent) && messageContent.ValueKind == JsonValueKind.String)
                            AppendChunk(output, messageContent.GetString(), partialResponse, "LM Studio");
                }
                break;
            }
        }
        if (!completed) throw new InvalidDataException($"LM Studio closed its REST response stream before chat.end (event {record}). The response may be incomplete.");
        return output.ToString();
    }

    private static async Task<string> GenerateLMStudioOpenAIAsync(
        string prompt,
        string model,
        RunnerInlineSettings settings,
        RunnerInlineCredentials credentials,
        IProgress<string>? partialResponse,
        CancellationToken cancellationToken)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(settings.SystemPrompt)) messages.Add(new { role = "system", content = settings.SystemPrompt });
        messages.Add(new { role = "user", content = prompt });
        var body = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = true,
        };
        if (settings.Temperature is double temperature) body["temperature"] = temperature;
        if (settings.TopP is double topP) body["top_p"] = topP;
        if (settings.MaxOutputTokens is int maxTokens) body["max_tokens"] = maxTokens;
        if (settings.Seed is int seed) body["seed"] = seed;
        ApplyLMStudioThinkingOptions(body, settings.Thinking);
        if (IsNemotron35BoltE4BModel(model) && settings.Nemotron35BoltE4BConversationalMode)
        {
            var templateOptions = body.TryGetValue("chat_template_kwargs", out var existing) && existing is Dictionary<string, object> map
                ? map : new Dictionary<string, object>(StringComparer.Ordinal);
            templateOptions["conversational_mode"] = true;
            body["chat_template_kwargs"] = templateOptions;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildLMStudioUri(settings.LMStudioBaseUrl, "chat/completions"))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        ApplyAuthentication(request, credentials.LMStudioApiKey, credentials.LMStudioAuthHeader);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "LM Studio generation", cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var boundedReader = new BoundedStreamLineReader(reader, MaximumStreamLineCharacters);
        var output = new StringBuilder();
        var completed = false;
        var record = 0;
        while (true)
        {
            var line = await boundedReader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].Trim();
            if (data.Length == 0) continue;
            if (data.Equals("[DONE]", StringComparison.OrdinalIgnoreCase)) { completed = true; break; }
            record++;
            using var json = JsonDocument.Parse(data);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error))
                throw new InvalidOperationException("LM Studio: " + (error.ValueKind == JsonValueKind.String ? error.GetString() : error.ToString()));
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object
                    && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    AppendChunk(output, content.GetString(), partialResponse, "LM Studio");
                else if (choice.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
                    && message.TryGetProperty("content", out content) && content.ValueKind == JsonValueKind.String)
                    AppendChunk(output, content.GetString(), partialResponse, "LM Studio");
                if (choice.TryGetProperty("finish_reason", out var finishReason) && finishReason.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(finishReason.GetString())) completed = true;
            }
        }
        if (!completed) throw new InvalidDataException($"LM Studio closed the OpenAI-compatible stream without [DONE] or finish_reason (event {record}). The response may be incomplete.");
        return output.ToString();
    }

    private static void ApplyLMStudioThinkingOptions(Dictionary<string, object> body, string thinking)
    {
        if (thinking == "auto") return;
        if (thinking is "on" or "off")
        {
            var options = body.TryGetValue("chat_template_kwargs", out var existing) && existing is Dictionary<string, object> map
                ? map : new Dictionary<string, object>(StringComparer.Ordinal);
            options["enable_thinking"] = thinking == "on";
            body["chat_template_kwargs"] = options;
            if (thinking == "off") body["reasoning_effort"] = "none";
            return;
        }
        if (thinking is "low" or "medium" or "high")
        {
            body["reasoning_effort"] = thinking;
            var options = body.TryGetValue("chat_template_kwargs", out var existing) && existing is Dictionary<string, object> map
                ? map : new Dictionary<string, object>(StringComparer.Ordinal);
            options["enable_thinking"] = true;
            body["chat_template_kwargs"] = options;
            return;
        }
        throw new InvalidOperationException("Thinking / reasoning must be Auto, On, Off, Low, Medium, or High.");
    }

    private sealed class BoundedStreamLineReader
    {
        private readonly StreamReader _reader;
        private readonly int _maximumLineCharacters;
        private readonly char[] _buffer = new char[4096];
        private int _offset;
        private int _length;

        public BoundedStreamLineReader(StreamReader reader, int maximumLineCharacters)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _maximumLineCharacters = maximumLineCharacters > 0
                ? maximumLineCharacters
                : throw new ArgumentOutOfRangeException(nameof(maximumLineCharacters));
        }

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            var line = new StringBuilder();
            while (true)
            {
                if (_offset >= _length)
                {
                    _length = await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                    _offset = 0;
                    if (_length == 0) return line.Length == 0 ? null : FinishLine(line);
                }

                var newline = Array.IndexOf(_buffer, '\n', _offset, _length - _offset);
                var segmentEnd = newline >= 0 ? newline : _length;
                var count = segmentEnd - _offset;
                if (line.Length + count > _maximumLineCharacters)
                    throw new InvalidDataException($"The provider response contained a stream line longer than {_maximumLineCharacters:N0} characters.");
                if (count > 0) line.Append(_buffer, _offset, count);
                _offset = newline >= 0 ? newline + 1 : _length;
                if (newline >= 0) return FinishLine(line);
            }
        }

        private static string FinishLine(StringBuilder line)
        {
            if (line.Length > 0 && line[line.Length - 1] == '\r') line.Length--;
            return line.ToString();
        }
    }

    private static void AppendChunk(StringBuilder output, string? chunk, IProgress<string>? progress, string provider)
    {
        if (string.IsNullOrEmpty(chunk)) return;
        if (chunk.Length > MaximumResponseCharacters - output.Length)
            throw new InvalidDataException($"The {provider} response exceeded the {MaximumResponseCharacters:N0}-character inline output limit.");
        output.Append(chunk);
        progress?.Report(chunk);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await ReadBoundedTextAsync(response.Content, MaximumErrorBytes, cancellationToken).ConfigureAwait(false);
        var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : " Response: " + TrimForError(body);
        throw new HttpRequestException($"{operation} failed: HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{detail}", null, response.StatusCode);
    }

    private static async Task<byte[]> ReadBoundedBytesAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > maximumBytes) throw new InvalidDataException($"The provider response exceeded the {maximumBytes:N0}-byte limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return output.ToArray();
    }

    private static async Task<string> ReadBoundedTextAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
        => Encoding.UTF8.GetString(await ReadBoundedBytesAsync(content, maximumBytes, cancellationToken).ConfigureAwait(false));

    private static string TrimForError(string value)
    {
        var singleLine = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= 500 ? singleLine : singleLine[..499] + "…";
    }

    private static void ApplyAuthentication(HttpRequestMessage request, string? apiKey, string? customHeader)
    {
        var custom = (customHeader ?? string.Empty).Trim();
        var hasCustomAuthorization = false;
        if (custom.Length > 0)
        {
            var separator = custom.IndexOf(':');
            if (separator <= 0 || separator == custom.Length - 1)
                throw new InvalidOperationException("Custom auth header must use the form 'Header-Name: value'.");
            var name = custom[..separator].Trim();
            var value = custom[(separator + 1)..].Trim();
            if (name.Length is < 1 or > 128 || value.Length > 4096 || name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~'))
                || value.Any(character => character is '\r' or '\n'))
                throw new InvalidOperationException("The custom authentication header contains an invalid name or value.");
            hasCustomAuthorization = name.Equals("Authorization", StringComparison.OrdinalIgnoreCase);
            if (!request.Headers.TryAddWithoutValidation(name, value))
                throw new InvalidOperationException("The custom authentication header could not be added to the provider request.");
        }
        if (!hasCustomAuthorization && !string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static Uri ValidateOllamaGenerateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.AbsolutePath.TrimEnd('/').EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ollama endpoint must be an absolute HTTP(S) URL ending in /api/generate, for example http://localhost:11434/api/generate.");
        return uri;
    }

    private static Uri BuildOllamaTagsUri(string endpoint)
    {
        var generate = ValidateOllamaGenerateEndpoint(endpoint);
        var builder = new UriBuilder(generate);
        var path = builder.Path.TrimEnd('/');
        builder.Path = path[..^"generate".Length] + "tags";
        return builder.Uri;
    }

    private static Uri BuildLMStudioNativeUri(string baseUrl, string relativePath)
    {
        var uri = ValidateLMStudioBaseUrl(baseUrl);
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) path = path[..^3];
        var builder = new UriBuilder(uri) { Path = path + "/api/v1/" + relativePath.TrimStart('/'), Query = string.Empty, Fragment = string.Empty };
        return builder.Uri;
    }

    private static Uri BuildLMStudioUri(string baseUrl, string relativePath)
    {
        var uri = ValidateLMStudioBaseUrl(baseUrl);
        var builder = new UriBuilder(uri) { Path = uri.AbsolutePath.TrimEnd('/') + "/" + relativePath.TrimStart('/'), Query = string.Empty, Fragment = string.Empty };
        return builder.Uri;
    }

    private static Uri ValidateLMStudioBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("LM Studio base URL must be an absolute HTTP(S) URL without embedded credentials, query strings or fragments, for example http://localhost:1234/v1.");
        return uri;
    }

    private static bool TryResolveThinkValue(string setting, out object value)
    {
        switch (setting)
        {
            case "on": value = true; return true;
            case "off": value = false; return true;
            case "low":
            case "medium":
            case "high": value = setting; return true;
            default: value = false; return false;
        }
    }

    private static bool IsNemotron35BoltE4BModel(string model)
    {
        var normalized = new string((model ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalized.Contains("nemotron35bolte4b", StringComparison.Ordinal)
            && !normalized.Contains("dflash", StringComparison.Ordinal)
            && !normalized.Contains("74b", StringComparison.Ordinal);
    }

    private static (string Model, string Prompt) ParseModelAndPrompt(string search, string defaultModel)
    {
        var trimmed = (search ?? string.Empty).Trim();
        if (!trimmed.StartsWith('>')) return (defaultModel, trimmed);
        var segments = trimmed.Split('>', 3, StringSplitOptions.TrimEntries);
        if (segments.Length == 3 && segments[0].Length == 0 && segments[1].Length > 0 && segments[2].Length > 0)
            return (segments[1], segments[2]);
        return (defaultModel, trimmed);
    }

    private static string ProviderName(RunnerInlineSettings settings) => settings.Provider == "lmstudio" ? "LM Studio" : "Ollama";
    private static string UsefulMessage(Exception exception) => exception switch
    {
        HttpRequestException http when http.StatusCode is null => "Connection failed. Check that the selected provider is running and the endpoint is correct.",
        HttpRequestException http => $"HTTP {(int)http.StatusCode} {http.StatusCode}: {TrimForError(http.Message)}",
        OperationCanceledException => "The request was cancelled.",
        _ => TrimForError(exception.Message),
    };
}
