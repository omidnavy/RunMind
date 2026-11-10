using System.Windows;

namespace RunMind.Standalone;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow(ReadInitialPrompt(e.Args));
        MainWindow = window;
        window.Show();
    }

    private static string? ReadInitialPrompt(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], "--prompt", StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 1 >= args.Length)
                return null;

            var prompt = args[index + 1].Trim();
            return prompt.Length is > 0 and <= 12_000 ? prompt : null;
        }

        return null;
    }
}
