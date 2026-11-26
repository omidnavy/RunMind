#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RunMind.Shared;

internal sealed record ThemeDefinition(string Id, string Name, string Mode, string FileName, string Version, string BackgroundColor, IReadOnlyDictionary<string, string> Palette);

/// <summary>
/// Discovers self-contained theme files. Each file declares its identity and supported
/// appearance modes in a RunMind-Theme comment; malformed files/modes are ignored.
/// </summary>
internal static class ThemeCatalog
{
    private const int MaximumThemeBytes = 512 * 1024;
    private static readonly Regex ThemeFilePattern = new(
        "^(?<id>[a-z0-9]+(?:-[a-z0-9]+)*)\\.css$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex MetadataPattern = new(
        @"\A\s*/\*\s*RunMind-Theme:\s*(?<json>\{[\s\S]{1,2048}?\})\s*\*/",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CommentPattern = new(
        @"/\*[\s\S]*?\*/",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PaletteRulePattern = new(
        @":root\s*\[\s*data-theme\s*=\s*""(?<mode>light|dark)""\s*\]\s*\[\s*data-color-theme\s*=\s*""(?<id>[a-z0-9]+(?:-[a-z0-9]+)*)""\s*\]\s*\{(?<body>[^{}]*)\}",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] RequiredPaletteProperties =
    {
        "--color-bg", "--color-surface", "--color-surface-raised", "--color-surface-hover",
        "--color-input", "--color-text", "--color-muted", "--color-accent",
        "--color-accent-contrast", "--color-border", "--color-user-bubble",
        "--color-assistant-bubble", "--color-danger", "--color-success",
        "--shadow-panel", "--shadow-float",
    };

    public static IReadOnlyList<ThemeDefinition> Discover()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(ThemeCatalog).Assembly.Location);
        if (string.IsNullOrWhiteSpace(assemblyDirectory)) return Array.Empty<ThemeDefinition>();

        var themeDirectory = Path.Combine(assemblyDirectory, "ChatWindow", "theme");
        if (!Directory.Exists(themeDirectory) || IsReparsePoint(themeDirectory))
            return Array.Empty<ThemeDefinition>();

        string[] paths;
        try { paths = Directory.EnumerateFiles(themeDirectory, "*.css", SearchOption.TopDirectoryOnly).ToArray(); }
        catch { return Array.Empty<ThemeDefinition>(); }

        var themes = new List<ThemeDefinition>();
        foreach (var path in paths)
        {
            try
            {
                if (IsReparsePoint(path)) continue;
                var fileName = Path.GetFileName(path);
                var fileMatch = ThemeFilePattern.Match(fileName);
                if (!fileMatch.Success) continue;

                var info = new FileInfo(path);
                if (info.Length is <= 0 or > MaximumThemeBytes) continue;
                var css = File.ReadAllText(path, new UTF8Encoding(false, true));
                if (!TryReadTheme(css, fileMatch.Groups["id"].Value, out var id, out var name, out var palettes)) continue;

                var version = $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}";
                foreach (var palette in palettes)
                {
                    if (palette.Value.TryGetValue("--color-bg", out var backgroundColor))
                        themes.Add(new ThemeDefinition(id, name, palette.Key, fileName, version, backgroundColor, palette.Value));
                }
            }
            catch
            {
                // A malformed, unreadable, or concurrently removed theme never blocks discovery.
            }
        }

        return themes
            .GroupBy(theme => $"{theme.Mode}:{theme.Id}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(theme => theme.Mode, StringComparer.Ordinal)
            .ThenBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryReadTheme(
        string css,
        string expectedId,
        out string id,
        out string name,
        out IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> palettes)
    {
        id = string.Empty;
        name = string.Empty;
        palettes = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var metadataMatch = MetadataPattern.Match(css);
        if (!metadataMatch.Success) return false;

        try
        {
            using var document = JsonDocument.Parse(metadataMatch.Groups["json"].Value, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("modes", out var modesElement) || modesElement.ValueKind != JsonValueKind.Array)
                return false;

            id = idElement.GetString() ?? string.Empty;
            name = nameElement.GetString() ?? string.Empty;
            if (!string.Equals(id, expectedId, StringComparison.Ordinal)
                || name.Length is < 1 or > 64
                || name.Any(char.IsControl)) return false;

            var declaredModes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in modesElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) return false;
                var mode = item.GetString();
                if (mode is not ("light" or "dark") || !declaredModes.Add(mode)) return false;
            }
            if (declaredModes.Count is < 1 or > 2) return false;

            // Validate each declared palette block. Only self-scoped root palette rules
            // are accepted; extra selectors or malformed blocks invalidate that file.
            var withoutComments = CommentPattern.Replace(css, string.Empty);
            var rules = PaletteRulePattern.Matches(withoutComments);
            var foundModes = new HashSet<string>(StringComparer.Ordinal);
            var invalidModes = new HashSet<string>(StringComparer.Ordinal);
            var paletteValues = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (Match rule in rules)
            {
                var mode = rule.Groups["mode"].Value.ToLowerInvariant();
                var ruleId = rule.Groups["id"].Value;
                if (!declaredModes.Contains(mode) || !string.Equals(ruleId, id, StringComparison.Ordinal))
                    return false;
                var body = rule.Groups["body"].Value;
                if (!foundModes.Add(mode) || !HasRequiredProperties(body, mode)
                    || !TryReadPaletteValues(body, out var colors))
                {
                    invalidModes.Add(mode);
                    continue;
                }
                paletteValues[mode] = colors;
            }

            // A syntactically complete but incomplete palette only disables that mode;
            // a malformed/unexpected remainder invalidates the file to avoid CSS injection.
            var remainder = PaletteRulePattern.Replace(withoutComments, string.Empty);
            if (!string.IsNullOrWhiteSpace(remainder)) return false;
            var validModes = foundModes.Except(invalidModes)
                .Where(paletteValues.ContainsKey)
                .OrderBy(mode => mode, StringComparer.Ordinal)
                .ToArray();
            if (validModes.Length == 0) return false;

            palettes = validModes.ToDictionary(mode => mode, mode => paletteValues[mode], StringComparer.Ordinal);
            return true;
        }
        catch
        {
            id = string.Empty;
            name = string.Empty;
            palettes = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            return false;
        }
    }

    private static bool HasRequiredProperties(string body, string mode)
    {
        foreach (var property in RequiredPaletteProperties)
        {
            var pattern = $@"(?:^|;)\s*{Regex.Escape(property)}\s*:\s*[^;{{}}]+;?";
            if (!Regex.IsMatch(body, pattern, RegexOptions.CultureInvariant | RegexOptions.Multiline)) return false;
        }
        var colorScheme = Regex.Match(body, @"(?:^|;)\s*color-scheme\s*:\s*(?<value>light|dark)\s*;?", RegexOptions.CultureInvariant | RegexOptions.Multiline);
        return colorScheme.Success && string.Equals(colorScheme.Groups["value"].Value, mode, StringComparison.OrdinalIgnoreCase);
    }


    private static bool TryReadPaletteValues(string body, out IReadOnlyDictionary<string, string> palette)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in RequiredPaletteProperties)
        {
            var match = Regex.Match(
                body,
                $@"(?:^|;)\s*{Regex.Escape(property)}\s*:\s*(?<value>[^;{{}}]+)\s*;?",
                RegexOptions.CultureInvariant | RegexOptions.Multiline);
            if (!match.Success)
            {
                palette = new Dictionary<string, string>(StringComparer.Ordinal);
                return false;
            }
            values[property] = match.Groups["value"].Value.Trim();
        }
        palette = values;
        return true;
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }
    }
}
