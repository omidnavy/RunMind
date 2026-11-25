#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RunMind.Shared;

internal sealed record AppearancePreferences(string Mode, string LightTheme, string DarkTheme);

internal static class AppearancePreferenceStore
{
    private const string DefaultMode = "system";
    private const string DefaultLightTheme = "classic-light";
    private const string DefaultDarkTheme = "warm-graphite";
    private static readonly object Sync = new();

    public static AppearancePreferences? Load()
    {
        lock (Sync)
        {
            try
            {
                var path = GetFilePath();
                if (path is null || !File.Exists(path)) return null;
                var stored = JsonSerializer.Deserialize<StoredPreferences>(File.ReadAllText(path));
                return stored is null ? null : Normalize(stored.Mode, stored.LightTheme, stored.DarkTheme);
            }
            catch
            {
                return null;
            }
        }
    }

    public static object? CreateBootstrapValue()
    {
        var settings = Load();
        return settings is null ? null : new
        {
            mode = settings.Mode,
            lightTheme = settings.LightTheme,
            darkTheme = settings.DarkTheme,
        };
    }

    public static string CreateDocumentBootstrapScript()
    {
        var json = JsonSerializer.Serialize(CreateBootstrapValue());
        return "(() => { const a = " + json + "; if (!a) return; "
            + "const mode = ['system','light','dark'].includes(a.mode) ? a.mode : 'system'; "
            + "const light = typeof a.lightTheme === 'string' ? a.lightTheme : 'classic-light'; "
            + "const dark = typeof a.darkTheme === 'string' ? a.darkTheme : 'warm-graphite'; "
            + "const apply = () => { const root = document.documentElement; if (!root) return false; "
            + "const effective = mode === 'system' ? (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light') : mode; "
            + "root.dataset.appearanceMode = mode; root.dataset.theme = effective; root.dataset.colorTheme = effective === 'dark' ? dark : light; return true; }; "
            + "if (!apply()) { const observer = new MutationObserver(() => { if (apply()) observer.disconnect(); }); observer.observe(document, { childList: true, subtree: true }); } })();";
    }

    public static bool TryHandleMessage(string source, string messageJson, out AppearancePreferences? settings)
    {
        settings = null;
        if (!IsChatPage(source) || string.IsNullOrWhiteSpace(messageJson) || messageJson.Length > 4096)
            return false;

        try
        {
            using var document = JsonDocument.Parse(messageJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "runmind.appearanceChanged")
                return false;

            settings = Normalize(
                ReadString(root, "mode"),
                ReadString(root, "lightTheme"),
                ReadString(root, "darkTheme"));
            try { Save(settings); }
            catch { /* A read-only profile must not prevent the current window from changing themes. */ }
            return true;
        }
        catch
        {
            settings = null;
            return false;
        }
    }

    private static AppearancePreferences Normalize(string? mode, string? lightTheme, string? darkTheme)
    {
        var safeMode = mode is "system" or "light" or "dark" ? mode : DefaultMode;
        var available = ThemeCatalog.Discover();
        var lightIds = available.Where(theme => theme.Mode == "light").Select(theme => theme.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var darkIds = available.Where(theme => theme.Mode == "dark").Select(theme => theme.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var safeLightDefault = lightIds.Contains(DefaultLightTheme) ? DefaultLightTheme : lightIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? DefaultLightTheme;
        var safeDarkDefault = darkIds.Contains(DefaultDarkTheme) ? DefaultDarkTheme : darkIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? DefaultDarkTheme;
        var safeLightTheme = !string.IsNullOrWhiteSpace(lightTheme) && lightIds.Contains(lightTheme) ? lightTheme! : safeLightDefault;
        var safeDarkTheme = !string.IsNullOrWhiteSpace(darkTheme) && darkIds.Contains(darkTheme) ? darkTheme! : safeDarkDefault;
        return new AppearancePreferences(safeMode, safeLightTheme, safeDarkTheme);
    }

    private static void Save(AppearancePreferences settings)
    {
        lock (Sync)
        {
            var path = GetFilePath();
            if (path is null) return;
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory)) return;
            Directory.CreateDirectory(directory);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var stored = new StoredPreferences
            {
                Mode = settings.Mode,
                LightTheme = settings.LightTheme,
                DarkTheme = settings.DarkTheme,
            };
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(stored));
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }

    private static string? GetFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppData)
            ? null
            : Path.Combine(localAppData, "RunMind", "appearance.json");
    }

    private static bool IsChatPage(string source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Equals("/ChatWindow.html", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private sealed class StoredPreferences
    {
        public StoredPreferences() { }

        [JsonPropertyName("mode")]
        public string? Mode { get; init; }

        [JsonPropertyName("lightTheme")]
        public string? LightTheme { get; init; }

        [JsonPropertyName("darkTheme")]
        public string? DarkTheme { get; init; }
    }
}
