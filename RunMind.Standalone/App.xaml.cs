using System.Diagnostics;
using System.IO;
using System.Text.Json;
using RunMind.Shared;
using System.Windows;

namespace RunMind.Standalone;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource _handoffServerCancellation = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StandalonePathStore.RememberCurrentExecutable();

        RunnerHandoffPayload? runnerHandoff = null;
        var handoffPath = ReadArgument(e.Args, "--runner-handoff-file");
        if (RunnerHandoffStore.TryConsume(handoffPath, out var incomingHandoff)
            && incomingHandoff is not null)
        {
            runnerHandoff = incomingHandoff;
            // Only wait for another process when one is actually present. A first launch
            // proceeds directly to creating its own Full Agent window; a second launch
            // forwards the handoff into the already-running Standalone process.
            if (HasOtherStandaloneProcess()
                && await RunnerHandoffPipe.TrySendWithRetryAsync(runnerHandoff, 4000))
            {
                Shutdown();
                return;
            }
        }

        var sessionPath = ReadArgument(e.Args, "--session-file");
        MainWindow window;
        if (runnerHandoff is not null)
        {
            // This is the real Standalone host: Full Agent, workspace intelligence,
            // MCP, Skills and the normal standalone provider/conversation state are enabled.
            window = new MainWindow(runnerHandoff.Prompt, runnerHandoff.Response,
                runnerHandoff.IsError, "Standalone");
        }
        else if (!string.IsNullOrWhiteSpace(sessionPath))
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
        // Start the current-user-only handoff listener as soon as the first window is
        // visible. A second launch retries briefly while this listener is coming up.
        _ = RunnerHandoffPipe.RunServerAsync(Dispatcher, OpenHandoffConversation, _handoffServerCancellation.Token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _handoffServerCancellation.Cancel();
        _handoffServerCancellation.Dispose();
        base.OnExit(e);
    }

    private static bool HasOtherStandaloneProcess()
    {
        var currentProcessId = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName("RunMind"))
        {
            using (process)
            {
                try
                {
                    if (process.Id != currentProcessId && !process.HasExited) return true;
                }
                catch { }
            }
        }
        return false;
    }

    private void OpenHandoffConversation(RunnerHandoffPayload payload)
    {
        if (MainWindow is Window existing && existing.WindowState == WindowState.Minimized)
            existing.WindowState = WindowState.Normal;

        // Preserve any existing chat by opening the handoff as a new conversation
        // window inside the already-running Standalone process. This is the real
        // Full Agent host, not a duplicated Runner chat surface or process.
        var window = new MainWindow(payload.Prompt, payload.Response, payload.IsError, "Standalone");
        window.Show();
        window.Activate();
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
