using Microsoft.CommandPalette.Extensions.Toolkit;

namespace RunMind.CommandPalette;

internal sealed partial class OpenRunMindCommand : InvokableCommand
{
    public override string Name => "Open chat with empty context";
    public override IconInfo Icon => IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

    public override CommandResult Invoke()
    {
        var result = RunMindLauncher.Open();
        return CommandResult.ShowToast(result.Message);
    }
}
