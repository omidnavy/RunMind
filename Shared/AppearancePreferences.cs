#nullable enable

using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
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

    public static System.Drawing.Color GetInitialBackgroundColor()
    {
        var settings = Load() ?? new AppearancePreferences(DefaultMode, DefaultLightTheme, DefaultDarkTheme);
        var mode = ResolveEffectiveMode(settings.Mode);
        var themeId = mode == "dark" ? settings.DarkTheme : settings.LightTheme;
        var catalog = ThemeCatalog.Discover();
        var theme = catalog.FirstOrDefault(item =>
            item.Mode == mode && string.Equals(item.Id, themeId, StringComparison.OrdinalIgnoreCase))
            ?? catalog.FirstOrDefault(item => item.Mode == mode);

        if (theme is not null && TryParseThemeBackgroundColor(theme.BackgroundColor, out var backgroundColor))
            return backgroundColor;

        return mode == "light"
            ? System.Drawing.Color.FromArgb(244, 246, 250)
            : System.Drawing.Color.FromArgb(23, 22, 19);
    }

    private static bool TryParseThemeBackgroundColor(string value, out System.Drawing.Color color)
    {
        color = System.Drawing.Color.Empty;
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#') return false;
        try
        {
            if (value.Length == 4)
            {
                var red = Convert.ToInt32(new string(value[1], 2), 16);
                var green = Convert.ToInt32(new string(value[2], 2), 16);
                var blue = Convert.ToInt32(new string(value[3], 2), 16);
                color = System.Drawing.Color.FromArgb(red, green, blue);
                return true;
            }
            if (value.Length == 7)
            {
                var red = Convert.ToInt32(value.Substring(1, 2), 16);
                var green = Convert.ToInt32(value.Substring(3, 2), 16);
                var blue = Convert.ToInt32(value.Substring(5, 2), 16);
                color = System.Drawing.Color.FromArgb(red, green, blue);
                return true;
            }
        }
        catch { /* Invalid palette colors fall back to the built-in safe colors. */ }
        return false;
    }

    public static string CreateDocumentBootstrapScript()
    {
        var settings = Load() ?? new AppearancePreferences(DefaultMode, DefaultLightTheme, DefaultDarkTheme);
        var catalog = ThemeCatalog.Discover();
        string ResolveBackground(string mode, string id)
        {
            var theme = catalog.FirstOrDefault(item => item.Mode == mode
                && string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? catalog.FirstOrDefault(item => item.Mode == mode);
            return theme?.BackgroundColor ?? (mode == "light" ? "#f4f6fa" : "#171613");
        }

        var bootstrap = new
        {
            mode = settings.Mode,
            lightTheme = settings.LightTheme,
            darkTheme = settings.DarkTheme,
            lightBackground = ResolveBackground("light", settings.LightTheme),
            darkBackground = ResolveBackground("dark", settings.DarkTheme),
        };
        var json = JsonSerializer.Serialize(bootstrap);
        return "(() => { const a = " + json + "; "
            + "const mode = ['system','light','dark'].includes(a.mode) ? a.mode : 'system'; "
            + "const light = typeof a.lightTheme === 'string' ? a.lightTheme : 'classic-light'; "
            + "const dark = typeof a.darkTheme === 'string' ? a.darkTheme : 'warm-graphite'; "
            + "const safeColor = value => { if (typeof value !== 'string') return null; if (/^#[0-9a-fA-F]{3}$/.test(value)) return '#' + [...value.slice(1)].map(ch => ch + ch).join(''); return /^#[0-9a-fA-F]{6}$/.test(value) ? value : null; }; "
            + "const apply = () => { const root = document.documentElement; if (!root) return false; root.dataset.themeInitializing = 'true'; root.dataset.themeReady = 'false'; "
            + "const effective = mode === 'system' ? (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light') : mode; "
            + "root.dataset.appearanceMode = mode; root.dataset.theme = effective; root.dataset.colorTheme = effective === 'dark' ? dark : light; "
            + "const background = safeColor(effective === 'dark' ? a.darkBackground : a.lightBackground); if (background) root.style.backgroundColor = background; "
            + "return true; }; "
            + "if (!apply()) { const observer = new MutationObserver(() => { if (apply()) observer.disconnect(); }); observer.observe(document, { childList: true, subtree: true }); } })();";
    }

    private static string ResolveEffectiveMode(string? mode)
    {
        if (mode is "light" or "dark") return mode;
        try
        {
            const string personalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            var value = Registry.GetValue(personalizeKey, "AppsUseLightTheme", 1);
            return value is not null && Convert.ToInt32(value) == 0 ? "dark" : "light";
        }
        catch
        {
            return "light";
        }
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
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && uri.IsDefaultPort
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Fragment)
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
