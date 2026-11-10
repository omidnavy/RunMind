using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace RunMind.CommandPalette;

internal sealed partial class ConfigureRunMindPage : ContentPage
{
    public ConfigureRunMindPage()
    {
        Name = "Configure";
        Title = "Configure RunMind";
        Icon = new IconInfo("\uE713");
    }

    public override IContent[] GetContent() => [new ConfigureRunMindForm()];

    private sealed partial class ConfigureRunMindForm : FormContent
    {
        public ConfigureRunMindForm()
        {
            var currentPath = RunMindLauncher.SuggestedExecutablePath;
            var currentPathJson = System.Text.Json.JsonSerializer.Serialize(currentPath);
            TemplateJson = $$"""
            {
              "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
              "type": "AdaptiveCard",
              "version": "1.6",
              "body": [
                {
                  "type": "TextBlock",
                  "text": "Configure RunMind",
                  "size": "Large",
                  "weight": "Bolder",
                  "wrap": true
                },
                {
                  "type": "TextBlock",
                  "text": "Enter or paste the full path to your standalone RunMind.exe. The path is stored in your per-user RunMind Command Palette settings.",
                  "wrap": true
                },
                {
                  "type": "Input.Text",
                  "id": "executablePath",
                  "label": "RunMind.exe path",
                  "value": {{currentPathJson}},
                  "isRequired": true,
                  "errorMessage": "Enter the path to RunMind.exe."
                }
              ],
              "actions": [
                {
                  "type": "Action.Submit",
                  "title": "Save path"
                }
              ]
            }
            """;
        }

        public override CommandResult SubmitForm(string payload)
        {
            try
            {
                var formInput = JsonNode.Parse(payload)?.AsObject();
                var executablePath = formInput?["executablePath"]?.GetValue<string>();
                var result = RunMindLauncher.ConfigureExecutable(executablePath);
                return CommandResult.ShowToast(result.Message);
            }
            catch (Exception exception)
            {
                return CommandResult.ShowToast("Could not save the RunMind path: " + exception.Message);
            }
        }
    }
}
