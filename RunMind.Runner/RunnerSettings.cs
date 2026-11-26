using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace RunMind.Runner;

internal sealed class RunnerSettings
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunRegistryValue = "RunMindRunner";

    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public bool StartWithWindows { get; set; }
    public bool RunMindGlobalMode { get; set; }
    public string RunMindActionKeyword { get; set; } = "llm";
    public List<string> EnabledPlugins { get; set; } = new() { "applications", "files", "urls", "calculator", "runmind" };

    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RunMind", "Runner");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static RunnerSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new RunnerSettings();

            var settings = JsonSerializer.Deserialize<RunnerSettings>(File.ReadAllText(SettingsPath));
            return settings is null ? new RunnerSettings() : Normalize(settings);
        }
        catch
        {
            return new RunnerSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        var temporaryPath = SettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(Normalize(this), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }

    public void ApplyStartupRegistration(string executablePath)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunRegistryKey, writable: true)
            ?? throw new InvalidOperationException("The current-user startup registry key could not be opened.");
        if (StartWithWindows)
        {
            var quotedExecutable = "\"" + Path.GetFullPath(executablePath) + "\"";
            runKey.SetValue(RunRegistryValue, quotedExecutable + " --background", RegistryValueKind.String);
        }
        else
        {
            runKey.DeleteValue(RunRegistryValue, throwOnMissingValue: false);
        }
    }

    private static RunnerSettings Normalize(RunnerSettings settings)
    {
        var hotkey = string.IsNullOrWhiteSpace(settings.Hotkey) ? "Ctrl+Alt+Space" : settings.Hotkey.Trim();
        return new RunnerSettings
        {
            Hotkey = hotkey,
            StartWithWindows = settings.StartWithWindows,
            RunMindGlobalMode = settings.RunMindGlobalMode,
            RunMindActionKeyword = NormalizeKeyword(settings.RunMindActionKeyword),
            EnabledPlugins = NormalizePluginIds(settings.EnabledPlugins),
        };
    }

    private static string NormalizeKeyword(string? value)
    {
        var keyword = (value ?? "llm").Trim();
        if (keyword.Length is < 1 or > 32 || keyword.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))) return "llm";
        return keyword;
    }

    private static List<string> NormalizePluginIds(List<string>? ids)
    {
        var result = new List<string>();
        foreach (var id in ids ?? new List<string>())
        {
            if (id == "paths")
            {
                result.Add("files");
                result.Add("urls");
            }
            else if (id is "applications" or "files" or "urls" or "calculator" or "runmind")
                result.Add(id);
        }
        if (ids is null) return new List<string> { "applications", "files", "urls", "calculator", "runmind" };
        return result.Distinct(StringComparer.Ordinal).ToList();
    }
}
