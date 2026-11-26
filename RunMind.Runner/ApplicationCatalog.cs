using System.IO;

namespace RunMind.Runner;

internal sealed class ApplicationCatalog
{
    private const int MaximumShortcuts = 3_500;
    private const int MaximumSearchResults = 9;

    public IReadOnlyList<RunnerItem> Load()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
        };

        var results = new List<RunnerItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
                continue;

            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    ReturnSpecialDirectories = false,
                    MaxRecursionDepth = 8,
                };

                foreach (var shortcutPath in Directory.EnumerateFiles(root, "*.lnk", options))
                {
                    if (results.Count >= MaximumShortcuts)
                        return SortAndReturn(results);
                    if (!seenPaths.Add(shortcutPath))
                        continue;

                    var title = Path.GetFileNameWithoutExtension(shortcutPath).Trim();
                    if (title.Length == 0)
                        continue;

                    var relativePath = Path.GetRelativePath(root, shortcutPath);
                    var group = Path.GetDirectoryName(relativePath);
                    var subtitle = string.IsNullOrWhiteSpace(group) || group == "."
                        ? "Application · Start menu"
                        : $"Application · {group}";
                    results.Add(new RunnerItem(title, subtitle, shortcutPath, RunnerItemKind.ApplicationShortcut));
                }
            }
            catch (IOException)
            {
                // A Start Menu entry can disappear while the catalog is being read.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep the catalog usable when a profile or machine folder is restricted.
            }
        }

        return SortAndReturn(results);
    }

    public IReadOnlyList<RunnerItem> Search(IReadOnlyList<RunnerItem> catalog, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<RunnerItem>();

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return catalog
            .Select(item => new { Item = item, Score = Score(item.Title, terms) })
            .Where(match => match.Score >= 0)
            .OrderBy(match => match.Score)
            .ThenBy(match => match.Item.Title, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumSearchResults)
            .Select(match => match.Item)
            .ToArray();
    }

    private static int Score(string title, IReadOnlyList<string> terms)
    {
        var normalizedTitle = title.Trim();
        var score = 0;
        foreach (var term in terms)
        {
            var index = normalizedTitle.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return -1;

            score += index == 0 ? 0 : 10 + index;
        }

        return score;
    }

    private static IReadOnlyList<RunnerItem> SortAndReturn(List<RunnerItem> items) => items
        .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
