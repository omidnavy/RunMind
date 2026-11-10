#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace RunMind.Shared;

/// <summary>
/// Canonical configuration for inline model requests in PowerToys Run and
/// PowerToys Command Palette. Both hosts read and write this same file.
/// </summary>
public sealed record InlineModelSettings
{
    public string Provider { get; init; } = "ollama";
    public string OllamaEndpoint { get; init; } = "http://localhost:11434/api/generate";
    public string LMStudioBaseUrl { get; init; } = "http://localhost:1234/v1";
    public string Model { get; init; } = string.Empty;
    public string Thinking { get; init; } = "auto";
    public int KeepAliveSeconds { get; init; } = 300;
    public int RequestTimeoutSeconds { get; init; } = 600;
    public int ModelDiscoveryTimeoutSeconds { get; init; } = 8;
    public int InlineCpuThreads { get; init; }
    // Command Palette has its own prompt because its input is a launcher/search
    // query. Keep InlineSystemPrompt as the independent PowerToys Run setting.
    public string InlineSystemPrompt { get; init; } = InlineModelSettingsStore.DefaultSystemPrompt;
    public string CommandPaletteSystemPrompt { get; init; } = InlineModelSettingsStore.DefaultCommandPaletteSystemPrompt;
    // Command Palette can target authenticated local gateways. These credentials
    // are deliberately scoped to its inline client, not PowerToys Run.
    public string CommandPaletteOllamaApiKey { get; init; } = string.Empty;
    public string CommandPaletteOllamaAuthHeader { get; init; } = string.Empty;
    public string CommandPaletteLMStudioApiKey { get; init; } = string.Empty;
    public string CommandPaletteLMStudioAuthHeader { get; init; } = string.Empty;
    // PowerToys Run credentials are separate from Command Palette credentials.
    public string PowerToysRunOllamaApiKey { get; init; } = string.Empty;
    public string PowerToysRunOllamaAuthHeader { get; init; } = string.Empty;
    public string PowerToysRunLMStudioApiKey { get; init; } = string.Empty;
    public string PowerToysRunLMStudioAuthHeader { get; init; } = string.Empty;
    public double? InlineTemperature { get; init; }
    public double? InlineTopP { get; init; }
    public int? InlineMaxOutputTokens { get; init; }
    public int? InlineSeed { get; init; }
    public bool InlineConversationalMode { get; init; }
}

public sealed record InlineModelDiscoveryResult(IReadOnlyList<string> Models, string Message)
{
    public bool Succeeded => Models.Count > 0;
}

public static class InlineModelSettingsStore
{
    public const string DefaultSystemPrompt =
        "You are RunMind, a compact, proactive local AI living inside \"Microsoft PowerToys Run\".\n" +
        "The input is the user's current search-box text. It may be a single letter, partial word, application name, command, question, complete task or anything else. You can not execute commands directly so instead guide the user how to use that command.";

    private const string PreviousDefaultSystemPrompt =
        "You are RunMind, a helpful, concise local AI assistant available from PowerToys Run and PowerToys Command Palette.\n" +
        "The input may be a short search query, a question, a command, or a complete task. Respond directly and adapt the level of detail to the request.";

    private const string LegacyQuickAiSystemPrompt =
        "SYSTEM\n\n" +
        "You are QuickAI, a compact proactive local AI living inside \"Microsoft PowerToys Run\".\n" +
        "The input is the user's current search-box text. It may be a single letter, partial word, application name, command, question, complete task or anything else.\n" +
        "Answer immediately without introductions.";

    public const string DefaultCommandPaletteSystemPrompt =
        "You are RunMind, a compact, proactive local AI living inside \"Microsoft PowerToys Command Palette\".\n" +
        "The input is the user's current search-box text. It may be a single letter, partial word, application name, command, question, complete task or anything else. You can not execute commands directly so instead guide the user how to use that command.";

    private const int MaximumSettingsFileBytes = 1024 * 1024;
    private const int MaximumModelCount = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
    private static readonly HttpClient DiscoveryClient = new()
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
    };
    private const string MutexName = @"Local\RunMind.InlineModelSettings.v1";

    public static string FilePath
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                throw new InvalidOperationException("Windows did not provide a Local AppData path.");
            return Path.Combine(localAppData, "RunMind", "Shared", "inline-model-settings.json");
        }
    }

    public static InlineModelSettings Load()
    {
        var path = FilePath;
        if (!File.Exists(path)) return new InlineModelSettings();
        var info = new FileInfo(path);
        if (info.Length > MaximumSettingsFileBytes)
            throw new InvalidDataException("The shared inline model settings file exceeds 1 MB.");
        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<InlineModelSettings>(json, JsonOptions);
        return Normalize(settings ?? new InlineModelSettings());
    }

    public static DateTime Save(InlineModelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using var mutex = new Mutex(false, MutexName);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
                throw new TimeoutException("The shared inline model settings file is busy in another RunMind host.");

            var path = FilePath;
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Normalize(settings), JsonOptions));
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                try { File.Delete(temporaryPath); } catch { }
            }
            return File.GetLastWriteTimeUtc(path);
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    public static InlineModelSettings Normalize(InlineModelSettings settings)
    {
        var provider = string.Equals(settings.Provider, "lmstudio", StringComparison.OrdinalIgnoreCase)
            ? "lmstudio" : "ollama";
        return settings with
        {
            Provider = provider,
            OllamaEndpoint = Limit(settings.OllamaEndpoint, 2048, "http://localhost:11434/api/generate"),
            LMStudioBaseUrl = Limit(settings.LMStudioBaseUrl, 2048, "http://localhost:1234/v1").TrimEnd('/'),
            Model = Limit(settings.Model, 512, string.Empty),
            Thinking = NormalizeThinking(settings.Thinking),
            KeepAliveSeconds = Clamp(settings.KeepAliveSeconds, 0, 86400, 300),
            RequestTimeoutSeconds = Clamp(settings.RequestTimeoutSeconds, 30, 3600, 600),
            ModelDiscoveryTimeoutSeconds = Clamp(settings.ModelDiscoveryTimeoutSeconds, 1, 300, 8),
            InlineCpuThreads = Clamp(settings.InlineCpuThreads, 0, 128, 0),
            InlineSystemPrompt = NormalizeInlineSystemPrompt(settings.InlineSystemPrompt),
            CommandPaletteSystemPrompt = Limit(settings.CommandPaletteSystemPrompt, 12000, DefaultCommandPaletteSystemPrompt),
            CommandPaletteOllamaApiKey = Limit(settings.CommandPaletteOllamaApiKey, 4096, string.Empty),
            CommandPaletteOllamaAuthHeader = Limit(settings.CommandPaletteOllamaAuthHeader, 8192, string.Empty),
            CommandPaletteLMStudioApiKey = Limit(settings.CommandPaletteLMStudioApiKey, 4096, string.Empty),
            CommandPaletteLMStudioAuthHeader = Limit(settings.CommandPaletteLMStudioAuthHeader, 8192, string.Empty),
            PowerToysRunOllamaApiKey = Limit(settings.PowerToysRunOllamaApiKey, 4096, string.Empty),
            PowerToysRunOllamaAuthHeader = Limit(settings.PowerToysRunOllamaAuthHeader, 8192, string.Empty),
            PowerToysRunLMStudioApiKey = Limit(settings.PowerToysRunLMStudioApiKey, 4096, string.Empty),
            PowerToysRunLMStudioAuthHeader = Limit(settings.PowerToysRunLMStudioAuthHeader, 8192, string.Empty),
            InlineTemperature = NormalizeDouble(settings.InlineTemperature, 0, 2),
            InlineTopP = NormalizeDouble(settings.InlineTopP, 0, 1),
            InlineMaxOutputTokens = NormalizeNullableInt(settings.InlineMaxOutputTokens, 1, 1_000_000),
            InlineSeed = NormalizeNullableInt(settings.InlineSeed, 0, int.MaxValue),
        };
    }

    /// <summary>
    /// Adds Command Palette credentials to an outbound provider request.
    /// </summary>
    public static void ApplyCommandPaletteAuthentication(HttpRequestMessage request, InlineModelSettings rawSettings)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = Normalize(rawSettings);
        var apiKey = settings.Provider == "lmstudio"
            ? settings.CommandPaletteLMStudioApiKey
            : settings.CommandPaletteOllamaApiKey;
        var customHeader = settings.Provider == "lmstudio"
            ? settings.CommandPaletteLMStudioAuthHeader
            : settings.CommandPaletteOllamaAuthHeader;
        ApplyAuthentication(request, apiKey, customHeader);
    }

    /// <summary>
    /// Adds an optional Bearer API key and optional custom HTTP header.
    /// The custom header is applied last, so a custom Authorization header replaces the Bearer value.
    /// </summary>
    public static void ApplyAuthentication(HttpRequestMessage request, string? apiKey, string? customHeader)
    {
        ArgumentNullException.ThrowIfNull(request);
        apiKey = (apiKey ?? string.Empty).Trim();
        customHeader = (customHeader ?? string.Empty).Trim();

        if (apiKey.Length > 0)
        {
            if (apiKey.Any(char.IsControl))
                throw new InvalidOperationException("The API key must not contain control characters.");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
        }

        if (customHeader.Length == 0)
            return;

        var separator = customHeader.IndexOf(':');
        if (separator <= 0)
            throw new InvalidOperationException("Custom auth header must use the format 'Header-Name: value'.");

        var name = customHeader[..separator].Trim();
        var value = customHeader[(separator + 1)..].Trim();
        if (name.Length == 0 || value.Length == 0 || !IsValidHeaderName(name)
            || value.Any(char.IsControl))
            throw new InvalidOperationException("Custom auth header must contain an RFC-compatible header name and a single-line value.");

        if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The '{name}' header is reserved and cannot be set as a custom auth header.");

        request.Headers.Remove(name);
        if (!request.Headers.TryAddWithoutValidation(name, value))
            throw new InvalidOperationException($"'{name}' is not a valid request header for this provider.");
    }

    /// <summary>
    /// Normalizes the PowerToys Run inline prompt, migrating only known built-in defaults.
    /// A deliberately empty prompt remains empty.
    /// </summary>
    public static string NormalizeInlineSystemPrompt(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();

        if (string.Equals(normalized, PreviousDefaultSystemPrompt, StringComparison.Ordinal)
            || string.Equals(normalized, LegacyQuickAiSystemPrompt, StringComparison.Ordinal))
            return DefaultSystemPrompt;

        return normalized.Length <= 12000 ? normalized : normalized[..12000];
    }

    private static bool IsValidHeaderName(string name)
    {
        const string punctuation = "!#$%&'*+-.^_`|~";
        foreach (var character in name)
        {
            var isAsciiLetter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';
            if (!isAsciiLetter && !isDigit && !punctuation.Contains(character))
                return false;
        }
        return true;
    }

    public static InlineModelDiscoveryResult DiscoverModels(InlineModelSettings rawSettings, int timeoutSeconds = 4)
    {
        var settings = Normalize(rawSettings);
        var boundedTimeoutSeconds = Math.Clamp(timeoutSeconds, 1, 300);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(boundedTimeoutSeconds));
            if (settings.Provider == "lmstudio")
                return DiscoverLMStudioModels(settings, timeout.Token);

            var modelsUri = BuildOllamaTagsUri(settings.OllamaEndpoint);
            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUri);
            ApplyCommandPaletteAuthentication(request, settings);
            using var response = DiscoveryClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync(timeout.Token).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return new InlineModelDiscoveryResult([], $"Model discovery failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("models", out var items) || items.ValueKind != JsonValueKind.Array)
                return new InlineModelDiscoveryResult([], "Ollama returned an unexpected model-list response.");

            var models = items.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("name", out var name)
                    && name.ValueKind == JsonValueKind.String)
                .Select(item => item.GetProperty("name").GetString()!.Trim())
                .Where(name => name.Length is > 0 and <= 512)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(MaximumModelCount)
                .ToArray();

            return new InlineModelDiscoveryResult(models,
                models.Length == 0 ? "Ollama is reachable, but no local models were returned." : $"Found {models.Length} Ollama model(s).");
        }
        catch (OperationCanceledException)
        {
            return new InlineModelDiscoveryResult([], $"Model discovery timed out after {boundedTimeoutSeconds} seconds.");
        }
        catch (Exception exception)
        {
            return new InlineModelDiscoveryResult([], $"Could not discover models: {exception.Message}");
        }
    }

    private static InlineModelDiscoveryResult DiscoverLMStudioModels(InlineModelSettings settings, CancellationToken cancellationToken)
    {
        var nativeUri = BuildLMStudioNativeUri(settings.LMStudioBaseUrl, "models");
        using var nativeRequest = new HttpRequestMessage(HttpMethod.Get, nativeUri);
        ApplyCommandPaletteAuthentication(nativeRequest, settings);
        using var nativeResponse = DiscoveryClient.SendAsync(nativeRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).GetAwaiter().GetResult();
        if (nativeResponse.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed)
        {
            nativeResponse.Dispose();
            return DiscoverLMStudioOpenAIModels(settings, cancellationToken);
        }

        var nativeBody = nativeResponse.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        if (!nativeResponse.IsSuccessStatusCode)
            return new InlineModelDiscoveryResult([], $"LM Studio model discovery failed: HTTP {(int)nativeResponse.StatusCode} {nativeResponse.ReasonPhrase}.");

        using var document = JsonDocument.Parse(nativeBody);
        if (!document.RootElement.TryGetProperty("models", out var items) || items.ValueKind != JsonValueKind.Array)
            return new InlineModelDiscoveryResult([], "LM Studio returned an unexpected /api/v1/models response.");

        var models = items.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), "llm", StringComparison.OrdinalIgnoreCase)
                && item.TryGetProperty("key", out var key)
                && key.ValueKind == JsonValueKind.String)
            .Select(item => item.GetProperty("key").GetString()!.Trim())
            .Where(name => name.Length is > 0 and <= 512)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumModelCount)
            .ToArray();

        return new InlineModelDiscoveryResult(models,
            models.Length == 0 ? "LM Studio is reachable, but it returned no installed LLMs." : $"Found {models.Length} LM Studio model(s).");
    }

    private static InlineModelDiscoveryResult DiscoverLMStudioOpenAIModels(InlineModelSettings settings, CancellationToken cancellationToken)
    {
        var modelsUri = BuildLMStudioModelsUri(settings.LMStudioBaseUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, modelsUri);
        ApplyCommandPaletteAuthentication(request, settings);
        using var response = DiscoveryClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).GetAwaiter().GetResult();
        var body = response.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            return new InlineModelDiscoveryResult([], $"LM Studio model discovery failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var items) || items.ValueKind != JsonValueKind.Array)
            return new InlineModelDiscoveryResult([], "LM Studio returned an unexpected /v1/models response.");

        var models = items.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String)
            .Select(item => item.GetProperty("id").GetString()!.Trim())
            .Where(name => name.Length is > 0 and <= 512)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumModelCount)
            .ToArray();

        return new InlineModelDiscoveryResult(models,
            models.Length == 0 ? "LM Studio is reachable, but its compatibility endpoint returned no models." : $"Found {models.Length} loaded LM Studio model(s) through the compatibility endpoint.");
    }

    private static Uri BuildOllamaTagsUri(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Enter a valid Ollama HTTP endpoint, such as http://localhost:11434/api/generate.");

        var builder = new UriBuilder(uri);
        var path = builder.Path.TrimEnd('/');
        var slash = path.LastIndexOf('/');
        var lastSegment = slash >= 0 ? path[(slash + 1)..] : path;
        if (!lastSegment.Equals("generate", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Ollama endpoint must end with /api/generate so RunMind can discover models through /api/tags.");
        builder.Path = (slash >= 0 ? path[..(slash + 1)] : string.Empty) + "tags";
        builder.Fragment = string.Empty;
        return builder.Uri;
    }

    private static Uri BuildLMStudioNativeUri(string baseUrl, string relativePath)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Enter a valid LM Studio base URL, such as http://localhost:1234/v1.");

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            path = path[..^3];
        var builder = new UriBuilder(uri)
        {
            Path = path + "/api/v1/" + relativePath.TrimStart('/'),
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri;
    }

    private static Uri BuildLMStudioModelsUri(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Enter a valid LM Studio base URL, such as http://localhost:1234/v1.");
        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/models",
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri;
    }


    private static string NormalizeThinking(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "true" => "on",
            "false" => "off",
            "auto" or "on" or "off" or "low" or "medium" or "high" => normalized,
            _ => "auto",
        };
    }

    private static string Limit(string? value, int maximum, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) normalized = fallback;
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }

    private static int Clamp(int value, int minimum, int maximum, int fallback)
        => value >= minimum && value <= maximum ? value : fallback;

    private static double? NormalizeDouble(double? value, double minimum, double maximum)
        => value.HasValue && double.IsFinite(value.Value) && value.Value >= minimum && value.Value <= maximum ? value : null;

    private static int? NormalizeNullableInt(int? value, int minimum, int maximum)
        => value.HasValue && value.Value >= minimum && value.Value <= maximum ? value : null;
}
