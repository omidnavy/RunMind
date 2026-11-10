namespace RunMind.CommandPalette;

internal static class RunMindLauncher
{
    internal const int MaximumPromptCharacters = 12_000;

    internal static LaunchResult Open()
    {
        try
        {
            RunMindCommandPaletteDiagnostics.Write("Open chat with empty context requested.");
            CommandPaletteChatHost.OpenEmptyContext();
            return LaunchResult.Success("RunMind chat opened.");
        }
        catch (Exception exception)
        {
            RunMindCommandPaletteDiagnostics.Write("Could not open RunMind chat with empty context.", exception);
            return LaunchResult.Failure("Could not open RunMind chat: " + exception.Message);
        }
    }

    internal static LaunchResult OpenWithConversation(string conversationJson)
    {
        if (string.IsNullOrWhiteSpace(conversationJson))
            return LaunchResult.Failure("There is no inline conversation to transfer.");

        try
        {
            RunMindCommandPaletteDiagnostics.Write(
                $"Launching chat with complete inline conversation; payload length={conversationJson.Length}.");
            CommandPaletteChatHost.OpenConversation(conversationJson);
            return LaunchResult.Success("RunMind chat opened with the complete inline conversation.");
        }
        catch (Exception exception)
        {
            RunMindCommandPaletteDiagnostics.Write("Could not open RunMind chat with the complete inline conversation.", exception);
            return LaunchResult.Failure("Could not open RunMind chat with context: " + exception.Message);
        }
    }
}

internal readonly record struct LaunchResult(string Message)
{
    internal static LaunchResult Success(string message) => new(message);
    internal static LaunchResult Failure(string message) => new(message);
}
