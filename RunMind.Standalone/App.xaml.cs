using System.IO;
using System.Text.Json;
using System.Windows;

namespace RunMind.Standalone;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sessionPath = ReadArgument(e.Args, "--session-file");
        MainWindow window;
        if (!string.IsNullOrWhiteSpace(sessionPath))
        {
            var fullPath = Path.GetFullPath(sessionPath);
            if (!string.Equals(Path.GetExtension(fullPath), ".runmind", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("RunMind session files must use the .runmind extension.");
            EnsureNoReparseAncestors(fullPath);
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length is < 2 or > 16 * 1024 * 1024)
                throw new InvalidDataException("The .runmind session file is missing or exceeds the 16 MB limit.");
            var json = File.ReadAllText(fullPath);
            using (var document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("The .runmind session must contain a JSON object.");
            }
            window = new MainWindow(json, fullPath, "Standalone");
        }
        else
        {
            window = new MainWindow(ReadInitialPrompt(e.Args));
        }
        MainWindow = window;
        window.Show();
    }

    private static void EnsureNoReparseAncestors(string path)
    {
        FileSystemInfo? current = new FileInfo(path);
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("RunMind session paths cannot traverse symbolic links or other reparse points.");
            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null,
            };
        }
    }

    private static string? ReadArgument(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        return null;
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
