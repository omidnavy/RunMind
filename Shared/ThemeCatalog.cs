#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RunMind.Shared;

internal sealed record ThemeDefinition(string Id, string Name, string Mode, string FileName, string Version);

/// <summary>
/// Discovers self-contained light/dark CSS themes deployed beside the chat page.
/// Theme filenames are the catalog: light-&lt;id&gt;.css and dark-&lt;id&gt;.css.
/// </summary>
internal static class ThemeCatalog
{
    private static readonly Regex ThemeFilePattern = new(
        "^(?<mode>light|dark)-(?<id>[a-z0-9]+(?:-[a-z0-9]+)*)\\.css$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
                var fileName = Path.GetFileName(path);
                var match = ThemeFilePattern.Match(fileName);
                if (!match.Success || IsReparsePoint(path)) continue;

                var mode = match.Groups["mode"].Value.ToLowerInvariant();
                var id = match.Groups["id"].Value.ToLowerInvariant();
                var info = new FileInfo(path);
                if (info.Length is <= 0 or > 512 * 1024) continue;

                themes.Add(new ThemeDefinition(
                    id,
                    Humanize(id),
                    mode,
                    fileName,
                    $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}"));
            }
            catch
            {
                // Ignore an individual theme that disappears or becomes unreadable during discovery.
            }
        }

        return themes
            .GroupBy(theme => $"{theme.Mode}:{theme.Id}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(theme => theme.Mode, StringComparer.Ordinal)
            .ThenBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }
    }

    private static string Humanize(string id)
    {
        return string.Join(" ", id.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 0 ? part : char.ToUpper(part[0], CultureInfo.InvariantCulture) + part[1..]));
    }
}
