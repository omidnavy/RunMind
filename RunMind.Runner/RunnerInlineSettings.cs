#nullable enable

using System.IO;
using System.Text.Json;

namespace RunMind.Runner;

internal sealed record RunnerInlineSettings
{
    public bool Enabled { get; init; } = true;
    public string Provider { get; init; } = "ollama";
    public string OllamaEndpoint { get; init; } = "http://localhost:11434/api/generate";
    public string LMStudioBaseUrl { get; init; } = "http://localhost:1234/v1";
    public string Model { get; init; } = string.Empty;
    public string Thinking { get; init; } = "auto";
    public int KeepAliveSeconds { get; init; } = 300;
    public int RequestTimeoutSeconds { get; init; } = 600;
    public int ModelDiscoveryTimeoutSeconds { get; init; } = 8;
    public int InlineCpuThreads { get; init; }
    public string SystemPrompt { get; init; } = RunnerInlineSettingsStore.DefaultSystemPrompt;
    public double? Temperature { get; init; }
    public double? TopP { get; init; }
    public int? MaxOutputTokens { get; init; }
    public int? Seed { get; init; }
    public bool Nemotron35BoltE4BConversationalMode { get; init; }
}

internal sealed record RunnerInlineCredentials(
    string OllamaApiKey,
    string OllamaAuthHeader,
    string LMStudioApiKey,
    string LMStudioAuthHeader)
{
    public static RunnerInlineCredentials Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
}

internal static class RunnerInlineSettingsStore
{
    public const string DefaultSystemPrompt =
        "You are RunMind, a compact, proactive local AI living inside RunMind Runner.\n" +
        "The user activated you with the 'llm' launcher keyword and supplied the text that follows it as their prompt. " +
        "Answer directly and concisely. You cannot execute actions from this inline-answer surface; describe the result or suggest the relevant Runner action. " +
        "Never claim that you have launched an application, changed a setting, or performed a system operation.";

    private const int MaximumSettingsFileBytes = 1024 * 1024;
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RunMind", "Runner");

    public static string SettingsPath => Path.Combine(DirectoryPath, "inline-model-settings.json");

    public static RunnerInlineSettings Load()
    {
        lock (Sync)
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new RunnerInlineSettings();
                var info = new FileInfo(SettingsPath);
                if (info.Length is <= 0 or > MaximumSettingsFileBytes) return new RunnerInlineSettings();
                var value = JsonSerializer.Deserialize<RunnerInlineSettings>(File.ReadAllText(SettingsPath), JsonOptions);
                return Normalize(value ?? new RunnerInlineSettings());
            }
            catch { return new RunnerInlineSettings(); }
        }
    }

    public static void Save(RunnerInlineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = Normalize(settings);
        lock (Sync)
        {
            Directory.CreateDirectory(DirectoryPath);
            var temporaryPath = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(normalized, JsonOptions));
                File.Move(temporaryPath, SettingsPath, true);
            }
            finally
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    public static RunnerInlineSettings Normalize(RunnerInlineSettings raw)
    {
        var provider = string.Equals(raw.Provider, "lmstudio", StringComparison.OrdinalIgnoreCase) ? "lmstudio" : "ollama";
        return raw with
        {
            Provider = provider,
            OllamaEndpoint = Limit(raw.OllamaEndpoint, 2048, "http://localhost:11434/api/generate"),
            LMStudioBaseUrl = Limit(raw.LMStudioBaseUrl, 2048, "http://localhost:1234/v1").TrimEnd('/'),
            Model = Limit(raw.Model, 512, string.Empty),
            Thinking = NormalizeThinking(raw.Thinking),
            KeepAliveSeconds = Clamp(raw.KeepAliveSeconds, 0, 86400, 300),
            RequestTimeoutSeconds = Clamp(raw.RequestTimeoutSeconds, 30, 3600, 600),
            ModelDiscoveryTimeoutSeconds = Clamp(raw.ModelDiscoveryTimeoutSeconds, 1, 300, 8),
            InlineCpuThreads = Clamp(raw.InlineCpuThreads, 0, 128, 0),
            SystemPrompt = LimitAllowEmpty(raw.SystemPrompt, 12000, DefaultSystemPrompt),
            Temperature = NormalizeDouble(raw.Temperature, 0, provider == "lmstudio" ? 1 : 2),
            TopP = NormalizeDouble(raw.TopP, 0, 1),
            MaxOutputTokens = NormalizeNullableInt(raw.MaxOutputTokens, 1, 1_000_000),
            Seed = NormalizeNullableInt(raw.Seed, 0, int.MaxValue),
        };
    }

    private static string NormalizeThinking(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim().ToLowerInvariant();
        return normalized is "auto" or "on" or "off" or "low" or "medium" or "high" ? normalized : "auto";
    }

    private static string Limit(string? value, int maximum, string fallback)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) normalized = fallback;
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }

    private static string LimitAllowEmpty(string? value, int maximum, string fallback)
    {
        var normalized = (value ?? fallback).Trim();
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }

    private static int Clamp(int value, int minimum, int maximum, int fallback)
        => value >= minimum && value <= maximum ? value : fallback;

    private static double? NormalizeDouble(double? value, double minimum, double maximum)
        => value.HasValue && double.IsFinite(value.Value) && value.Value >= minimum && value.Value <= maximum ? value : null;

    private static int? NormalizeNullableInt(int? value, int minimum, int maximum)
        => value.HasValue && value.Value >= minimum && value.Value <= maximum ? value : null;
}
