using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace RunMind.CommandPalette;

internal sealed partial class RunMindCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;
    private readonly FallbackCommandItem _inlineFallback;
    private readonly InlineAnswerPage _dockPage;
    private readonly ICommandItem _dockItem;

    public RunMindCommandsProvider()
    {
        DisplayName = "RunMind";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Settings = new RunMindExtensionSettings(new InlineModelSettingsPage());

        var inlineAnswerPage = new InlineAnswerPage
        {
            Id = "runmind.inline-conversation",
        };
        // The current Command Palette SDK requires a display title and a stable
        // fallback ID. Explicitly assign FallbackHandler below so the root query
        // is always forwarded to the page instance that later generates the answer.
        _inlineFallback = new FallbackCommandItem(
            inlineAnswerPage,
            "RunMind",
            "runmind.inline-search-fallback")
        {
            // Set this explicitly instead of relying on toolkit constructor-side
            // inference. The current multi-argument constructor does not reliably
            // attach the page as the fallback query receiver in the SDK used here.
            FallbackHandler = inlineAnswerPage,
            Title = "RunMind",
            Subtitle = "Ask your selected local model inline",
            Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png"),
        };

        RunMindCommandPaletteDiagnostics.Write(
            "Registered RunMind search fallback with a unique ID and an explicit InlineAnswerPage fallback handler.");

        // Keep a dedicated Dock conversation page for the lifetime of this
        // provider. It is never the search-fallback page, so the Dock can continue
        // its own history independently. The page itself exposes Start fresh.
        _dockPage = new InlineAnswerPage
        {
            Id = "runmind.inline-conversation",
        };
        _dockItem = new CommandItem(_dockPage)
        {
            Title = "RunMind Dock",
            Subtitle = "Continue the Dock conversation or start fresh",
            Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png"),
        };
        RunMindCommandPaletteDiagnostics.Write(
            "Created a dedicated persistent RunMind Dock page, separate from the search fallback page.");

        _commands =
        [
            // Expose the persistent Dock page as a regular command too, so new
            // users can pin it. Its ID is distinct from the search fallback ID.
            _dockItem,
            new CommandItem(new OpenRunMindCommand())
            {
                Title = "Open chat with empty context",
                Subtitle = "Start a new chat without prior context",
                Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png"),
            },
        ];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[] FallbackCommands() => [_inlineFallback];

    // Command Palette uses ICommandProvider4.GetCommandItem to resolve items
    // already pinned to its Dock. Keep the Dock page separate and persistent so
    // users can continue its own thread or clear it using Start fresh.
    public override ICommandItem? GetCommandItem(string id)
    {
        if (string.Equals(id, "runmind.inline-conversation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, _dockItem.Command?.Id, StringComparison.OrdinalIgnoreCase))
        {
            // The Dock has its own persistent page instance. It never reuses the
            // search fallback's transcript, but it can continue its own Dock
            // conversation. Both entry points expose Start fresh to clear history.
            RunMindCommandPaletteDiagnostics.Write(
                $"GetCommandItem resolved pinned RunMind Dock command to its dedicated page; requested id='{id}'; search fallback id='{_inlineFallback.Command?.Id}'; dock item id='{_dockItem.Command?.Id}'; dock page id='{_dockPage.Id}'.");
            return _dockItem;
        }

        foreach (var item in _commands)
        {
            if (string.Equals(id, item.Command?.Id, StringComparison.OrdinalIgnoreCase))
            {
                RunMindCommandPaletteDiagnostics.Write(
                    $"GetCommandItem resolved top-level RunMind command; requested id='{id}'.");
                return item;
            }
        }

        RunMindCommandPaletteDiagnostics.Write($"GetCommandItem could not resolve requested id='{id}'.");
        return null;
    }

    private sealed partial class RunMindExtensionSettings(IContentPage settingsPage) : ICommandSettings
    {
        public IContentPage SettingsPage { get; } = settingsPage;
    }
}
