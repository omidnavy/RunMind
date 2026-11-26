#nullable enable

using System.Globalization;

namespace RunMind.Runner;

internal enum RunnerPluginAction
{
    None,
    OpenTarget,
    CopyText,
    ShowInlineAnswer,
}

internal sealed record RunnerPluginResult(
    string Id,
    string PluginId,
    string Title,
    string Subtitle,
    string Icon,
    int Rank,
    RunnerPluginAction Action,
    RunnerItem? Target = null,
    string? Value = null);

internal interface IRunnerLauncherPlugin
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    IReadOnlyList<RunnerPluginResult> Search(string query);
}

internal sealed class RunnerPluginRegistry
{
    private readonly List<IRunnerLauncherPlugin> _plugins = new();
    private readonly ApplicationsPlugin _applications;

    public RunnerPluginRegistry(ApplicationCatalog applicationCatalog)
    {
        ArgumentNullException.ThrowIfNull(applicationCatalog);
        _applications = new ApplicationsPlugin(applicationCatalog);

        // Built-ins are registered through the same contract future first-party plugins use.
        Register(new DirectTargetPlugin());
        Register(new UrlPlugin());
        Register(new CalculatorPlugin());
        Register(_applications);
        Register(new RunMindInlinePlugin());
    }

    public void Register(IRunnerLauncherPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        var id = (plugin.Id ?? string.Empty).Trim();
        if (id.Length is < 2 or > 64 || id.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            throw new ArgumentException("Launcher plugin IDs must be 2–64 ASCII letters, digits, hyphens or underscores.", nameof(plugin));
        if (string.IsNullOrWhiteSpace(plugin.Name) || plugin.Name.Length > 80)
            throw new ArgumentException("Launcher plugins must have a name of 1–80 characters.", nameof(plugin));
        if (_plugins.Any(existing => string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A launcher plugin with ID '{id}' is already registered.");
        _plugins.Add(plugin);
    }

    public void SetApplicationCatalog(IReadOnlyList<RunnerItem> catalog) => _applications.SetCatalog(catalog);

    public IReadOnlyList<(string Id, string Name, string Description)> DescribePlugins()
        => _plugins.Select(plugin => (plugin.Id, plugin.Name, plugin.Description)).ToArray();

    public IReadOnlyList<RunnerPluginResult> Search(string query, IReadOnlySet<string>? enabledPlugins = null, bool runMindGlobalMode = false, string? runMindKeyword = null)
    {
        var normalized = (query ?? string.Empty).Trim();
        if (normalized.Length == 0) return Array.Empty<RunnerPluginResult>();

        var results = new List<RunnerPluginResult>();
        var runMindActivated = RunMindInlinePlugin.TryExtractPrompt(normalized, runMindGlobalMode, runMindKeyword, out _);
        foreach (var plugin in _plugins)
        {
            if (enabledPlugins is not null && !enabledPlugins.Contains(plugin.Id)) continue;
            // Match PowerToys Run's action-keyword behavior: an explicit llm query is
            // handled by RunMind, not mixed with unrelated application/path results.
            if (runMindActivated && !runMindGlobalMode && !string.Equals(plugin.Id, "runmind", StringComparison.Ordinal)) continue;
            try { results.AddRange(plugin is RunMindInlinePlugin runMindPlugin
                ? runMindPlugin.Search(normalized, runMindGlobalMode, runMindKeyword)
                : plugin.Search(normalized)); }
            catch { /* A single plugin must never prevent the remaining launcher plugins from responding. */ }
        }

        var ordered = results
            .OrderBy(result => result.Rank)
            .ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Keep RunMind discoverable even when several providers return a large result set.
        var runMind = ordered.FirstOrDefault(result => result.PluginId == "runmind");
        var capped = ordered.Where(result => result.PluginId != "runmind").Take(runMind is null ? 14 : 13).ToList();
        if (runMind is not null) capped.Add(runMind);
        return capped.OrderBy(result => result.Rank)
            .ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

internal sealed class DirectTargetPlugin : IRunnerLauncherPlugin
{
    public string Id => "files";
    public string Name => "Files & Folders";
    public string Description => "Open a supported local file or folder with its default Windows application.";

    public IReadOnlyList<RunnerPluginResult> Search(string query)
    {
        var target = RunnerCommandService.ResolveDirectInput(query);
        if (target is null || target.Kind == RunnerItemKind.Url) return Array.Empty<RunnerPluginResult>();
        var icon = target.Kind == RunnerItemKind.Folder ? "📁" : "📄";
        return new[]
        {
            new RunnerPluginResult(Guid.NewGuid().ToString("N"), Id, target.Title, target.Subtitle, icon, 0, RunnerPluginAction.OpenTarget, target),
        };
    }
}

internal sealed class UrlPlugin : IRunnerLauncherPlugin
{
    public string Id => "urls";
    public string Name => "URLs";
    public string Description => "Open an HTTP or HTTPS address in the default browser.";

    public IReadOnlyList<RunnerPluginResult> Search(string query)
    {
        var target = RunnerCommandService.ResolveDirectInput(query);
        if (target is null || target.Kind != RunnerItemKind.Url) return Array.Empty<RunnerPluginResult>();
        return new[]
        {
            new RunnerPluginResult(Guid.NewGuid().ToString("N"), Id, target.Title, target.Subtitle, "🌐", 0, RunnerPluginAction.OpenTarget, target),
        };
    }
}

internal sealed class ApplicationsPlugin : IRunnerLauncherPlugin
{
    private const int MaximumResults = 7;
    private readonly ApplicationCatalog _catalog;
    private IReadOnlyList<RunnerItem> _shortcuts = Array.Empty<RunnerItem>();

    public ApplicationsPlugin(ApplicationCatalog catalog) => _catalog = catalog;
    public string Id => "applications";
    public string Name => "Applications";
    public string Description => "Search Start Menu shortcuts and launch installed desktop applications.";

    public void SetCatalog(IReadOnlyList<RunnerItem> catalog) => _shortcuts = catalog ?? Array.Empty<RunnerItem>();

    public IReadOnlyList<RunnerPluginResult> Search(string query)
        => _catalog.Search(_shortcuts, query).Take(MaximumResults)
            .Select(item => new RunnerPluginResult(Guid.NewGuid().ToString("N"), Id, item.Title, item.Subtitle, "🪟", 10, RunnerPluginAction.OpenTarget, item))
            .ToArray();
}

internal sealed class CalculatorPlugin : IRunnerLauncherPlugin
{
    public string Id => "calculator";
    public string Name => "Calculator";
    public string Description => "Evaluate arithmetic expressions locally without executing code.";

    public IReadOnlyList<RunnerPluginResult> Search(string query)
    {
        if (query.Length > 128 || !SafeArithmetic.TryEvaluate(query, out var value)) return Array.Empty<RunnerPluginResult>();
        var formatted = value.ToString("G12", CultureInfo.InvariantCulture);
        return new[]
        {
            new RunnerPluginResult(Guid.NewGuid().ToString("N"), Id, formatted, $"Calculator · Copy result · {query}", "🧮", 5, RunnerPluginAction.CopyText, Value: formatted),
        };
    }
}

internal sealed class RunMindInlinePlugin : IRunnerLauncherPlugin
{
    public const string ActionKeyword = "llm";

    public string Id => "runmind";
    public string Name => "RunMind";
    public string Description => "Generate an inline answer globally or with the configured action keyword.";

    public static bool TryExtractPrompt(string? query, out string prompt)
        => TryExtractPrompt(query, false, ActionKeyword, out prompt);

    public static bool TryExtractPrompt(string? query, bool globalMode, string? actionKeyword, out string prompt)
    {
        prompt = string.Empty;
        var value = (query ?? string.Empty).Trim();
        if (value.Length == 0) return false;
        if (globalMode) { prompt = value; return true; }
        var keyword = string.IsNullOrWhiteSpace(actionKeyword) ? ActionKeyword : actionKeyword.Trim();
        if (value.Length <= keyword.Length || !value.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
            || !char.IsWhiteSpace(value[keyword.Length])) return false;
        prompt = value[keyword.Length..].Trim();
        return prompt.Length > 0;
    }

    public IReadOnlyList<RunnerPluginResult> Search(string query)
        => Search(query, false, ActionKeyword);

    public IReadOnlyList<RunnerPluginResult> Search(string query, bool globalMode, string? actionKeyword)
    {
        if (!TryExtractPrompt(query, globalMode, actionKeyword, out var prompt)) return Array.Empty<RunnerPluginResult>();
        var preview = prompt.Length <= 96 ? prompt : prompt[..93] + "…";
        return new[]
        {
            new RunnerPluginResult(Guid.NewGuid().ToString("N"), Id, "RunMind", $"Inline answer · {preview}", "✦", 90, RunnerPluginAction.ShowInlineAnswer, Value: prompt),
        };
    }
}

/// <summary>Small recursive-descent arithmetic evaluator. It never evaluates code or accesses the runtime.</summary>
internal sealed class SafeArithmetic
{
    private readonly string _source;
    private int _position;

    private SafeArithmetic(string source) => _source = source;

    public static bool TryEvaluate(string source, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(source) || source.Length > 128) return false;
        try
        {
            var parser = new SafeArithmetic(source);
            result = parser.ParseExpression();
            parser.SkipWhiteSpace();
            return parser._position == parser._source.Length && double.IsFinite(result);
        }
        catch (Exception exception) when (exception is FormatException or ArithmeticException or OverflowException)
        {
            result = 0;
            return false;
        }
    }

    private double ParseExpression()
    {
        var value = ParseTerm();
        while (true)
        {
            SkipWhiteSpace();
            if (Take('+')) value += ParseTerm();
            else if (Take('-')) value -= ParseTerm();
            else return EnsureFinite(value);
        }
    }

    private double ParseTerm()
    {
        var value = ParseUnary();
        while (true)
        {
            SkipWhiteSpace();
            if (Take('*')) value *= ParseUnary();
            else if (Take('/'))
            {
                var divisor = ParseUnary();
                if (divisor == 0) throw new ArithmeticException("Division by zero.");
                value /= divisor;
            }
            else if (Take('%'))
            {
                var divisor = ParseUnary();
                if (divisor == 0) throw new ArithmeticException("Modulo by zero.");
                value %= divisor;
            }
            else return EnsureFinite(value);
        }
    }

    private double ParsePower()
    {
        var value = ParsePrimary();
        SkipWhiteSpace();
        if (Take('^')) value = Math.Pow(value, ParseUnary());
        return EnsureFinite(value);
    }

    private double ParseUnary()
    {
        SkipWhiteSpace();
        if (Take('+')) return ParseUnary();
        if (Take('-')) return -ParseUnary();
        return ParsePower();
    }

    private double ParsePrimary()
    {
        SkipWhiteSpace();
        if (Take('('))
        {
            var parenthesizedValue = ParseExpression();
            SkipWhiteSpace();
            if (!Take(')')) throw new FormatException("Missing closing parenthesis.");
            return parenthesizedValue;
        }

        var start = _position;
        var hasDigit = false;
        while (_position < _source.Length && char.IsAsciiDigit(_source[_position])) { _position++; hasDigit = true; }
        if (_position < _source.Length && _source[_position] == '.')
        {
            _position++;
            while (_position < _source.Length && char.IsAsciiDigit(_source[_position])) { _position++; hasDigit = true; }
        }
        if (!hasDigit) throw new FormatException("Expected a number.");
        var token = _source[start.._position];
        if (!double.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            throw new FormatException("Invalid number.");
        return EnsureFinite(value);
    }

    private bool Take(char expected)
    {
        if (_position >= _source.Length || _source[_position] != expected) return false;
        _position++;
        return true;
    }

    private void SkipWhiteSpace()
    {
        while (_position < _source.Length && char.IsWhiteSpace(_source[_position])) _position++;
    }

    private static double EnsureFinite(double value)
    {
        if (!double.IsFinite(value)) throw new ArithmeticException("The result is outside the supported numeric range.");
        return value;
    }
}
