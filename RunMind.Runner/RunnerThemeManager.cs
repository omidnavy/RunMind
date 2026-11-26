#nullable enable

using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using RunMind.Shared;

namespace RunMind.Runner;

internal static class RunnerThemeManager
{
    private static readonly AppearancePreferences DefaultAppearance = new("system", "classic-light", "warm-graphite");

    public static IReadOnlyList<ThemeDefinition> Themes => ThemeCatalog.Discover();

    public static AppearancePreferences CurrentAppearance => AppearancePreferenceStore.Load() ?? DefaultAppearance;

    public static string EffectiveMode(AppearancePreferences appearance)
    {
        if (appearance.Mode is "light" or "dark") return appearance.Mode;
        try
        {
            const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            var value = Microsoft.Win32.Registry.GetValue(key, "AppsUseLightTheme", 1);
            return value is not null && Convert.ToInt32(value) == 0 ? "dark" : "light";
        }
        catch { return "dark"; }
    }

    public static string ThemeName(string mode, string id)
        => Themes.FirstOrDefault(theme => theme.Mode == mode && theme.Id == id)?.Name ?? id;

    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var appearance = CurrentAppearance;
        var mode = EffectiveMode(appearance);
        var preferredId = mode == "dark" ? appearance.DarkTheme : appearance.LightTheme;
        var theme = Themes.FirstOrDefault(item => item.Mode == mode && item.Id == preferredId)
            ?? Themes.FirstOrDefault(item => item.Mode == mode)
            ?? Themes.FirstOrDefault();
        var palette = theme?.Palette ?? new Dictionary<string, string>(StringComparer.Ordinal);

        SetBrush("RunnerBackground", palette, "--color-bg", mode == "dark" ? "#171613" : "#F4F6FA");
        SetBrush("RunnerSurface", palette, "--color-surface", mode == "dark" ? "#201E19" : "#FFFFFF");
        SetBrush("RunnerSurfaceRaised", palette, "--color-surface-raised", mode == "dark" ? "#2A2721" : "#FFFFFF");
        SetBrush("RunnerSurfaceHover", palette, "--color-surface-hover", mode == "dark" ? "#373229" : "#E9EDF3");
        SetBrush("RunnerInput", palette, "--color-input", mode == "dark" ? "#191814" : "#FFFFFF");
        SetBrush("RunnerText", palette, "--color-text", mode == "dark" ? "#F1ECE2" : "#20242B");
        SetBrush("RunnerMuted", palette, "--color-muted", mode == "dark" ? "#B0A797" : "#697386");
        SetBrush("RunnerAccent", palette, "--color-accent", mode == "dark" ? "#D5A45F" : "#315F9D");
        SetBrush("RunnerAccentContrast", palette, "--color-accent-contrast", mode == "dark" ? "#21190E" : "#FFFFFF");
        SetBrush("RunnerBorder", palette, "--color-border", mode == "dark" ? "#464035" : "#D7DDE6");
        SetBrush("RunnerAssistantBubble", palette, "--color-assistant-bubble", mode == "dark" ? "#201E19" : "#FFFFFF");
        SetBrush("RunnerUserBubble", palette, "--color-user-bubble", mode == "dark" ? "#352A1E" : "#EAF1FA");
        SetBrush("RunnerDanger", palette, "--color-danger", mode == "dark" ? "#EF8B80" : "#B42318");
        SetBrush("RunnerSuccess", palette, "--color-success", mode == "dark" ? "#91C6A0" : "#16794B");

        window.Background = GetBrush("RunnerBackground");
    }

    public static bool SaveAppearance(string mode, string lightTheme, string darkTheme)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "runmind.appearanceChanged",
            mode,
            lightTheme,
            darkTheme,
        });
        return AppearancePreferenceStore.TryHandleMessage("https://localhost/Runner.html", json, out _);
    }

    private static void SetBrush(string key, IReadOnlyDictionary<string, string> palette, string property, string fallback)
    {
        var value = palette.TryGetValue(property, out var configured) ? configured : fallback;
        Application.Current.Resources[key] = new SolidColorBrush(ParseColor(value, fallback));
    }

    private static SolidColorBrush GetBrush(string key)
        => Application.Current.Resources[key] as SolidColorBrush ?? new SolidColorBrush(Colors.Transparent);

    private static Color ParseColor(string value, string fallback)
    {
        try
        {
            if (value.Length == 4 && value[0] == '#')
                value = "#" + string.Concat(value.Skip(1).SelectMany(character => new[] { character, character }));
            return (Color)ColorConverter.ConvertFromString(value)!;
        }
        catch
        {
            return (Color)ColorConverter.ConvertFromString(fallback)!;
        }
    }
}
