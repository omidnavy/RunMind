using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace RunMind.CommandPalette;

internal sealed partial class AskRunMindPage : ContentPage
{
    private readonly AskRunMindForm _form = new();

    public AskRunMindPage()
    {
        Name = "Ask";
        Title = "Ask RunMind";
        Icon = new IconInfo("\uE8A5");
    }

    public override IContent[] GetContent() => [_form];

    private sealed partial class AskRunMindForm : FormContent
    {
        public AskRunMindForm()
        {
            TemplateJson = """
            {
              "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
              "type": "AdaptiveCard",
              "version": "1.6",
              "body": [
                {
                  "type": "TextBlock",
                  "text": "Ask RunMind",
                  "size": "Large",
                  "weight": "Bolder",
                  "wrap": true
                },
                {
                  "type": "Input.Text",
                  "id": "prompt",
                  "label": "Prompt",
                  "placeholder": "What would you like RunMind to do?",
                  "isMultiline": true,
                  "isRequired": true,
                  "errorMessage": "Enter a prompt.",
                  "maxLength": 12000
                }
              ],
              "actions": [
                {
                  "type": "Action.Submit",
                  "title": "Ask RunMind"
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
                var prompt = formInput?["prompt"]?.GetValue<string>();
                var result = RunMindLauncher.OpenWithPrompt(prompt);
                return CommandResult.ShowToast(result.Message);
            }
            catch (Exception exception)
            {
                return CommandResult.ShowToast("Could not read the prompt: " + exception.Message);
            }
        }
    }
}
