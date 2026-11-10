using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace RunMind.CommandPalette;

/// <summary>
/// Starts the Command Palette host's own chat process. It deliberately does not
/// launch the Standalone executable, and the chat process outlives palette reloads.
/// </summary>
internal static class CommandPaletteChatHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static void OpenEmptyContext() => Launch(new ChatLaunchRequest { Mode = "empty" });

    internal static void OpenContext(string? prompt, string? response = null, bool isError = false)
    {
        prompt = Normalize(prompt, 12_000);
        response = Normalize(response, 100_000);
        if (prompt is null && response is null)
        {
            OpenEmptyContext();
            return;
        }

        Launch(new ChatLaunchRequest
        {
            Mode = "context",
            Prompt = prompt,
            Response = response,
            IsError = isError,
        });
    }

    internal static void OpenConversation(string conversationJson)
    {
        if (string.IsNullOrWhiteSpace(conversationJson) || conversationJson.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("The inline conversation payload is empty or exceeds the 4 MB limit.");

        using var document = JsonDocument.Parse(conversationJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("format", out var format)
            || format.ValueKind != JsonValueKind.String
            || format.GetString() != "runmind"
            || !document.RootElement.TryGetProperty("messages", out var messages)
            || messages.ValueKind != JsonValueKind.Array
            || messages.GetArrayLength() == 0
            || messages.GetArrayLength() > 10_000)
            throw new InvalidDataException("The inline conversation payload is not a valid RunMind conversation.");

        Launch(new ChatLaunchRequest
        {
            Mode = "conversation",
            ConversationJson = conversationJson,
        });
    }

    internal static bool TryRunChatProcess(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[0], "--chat-launch", StringComparison.Ordinal))
            return false;

        var launchPath = args[1];
        var launchDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RunMind", "CommandPalette", "Launch");
        var fullDirectory = Path.GetFullPath(launchDirectory) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(launchPath);
        if (!fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            return true;

        ChatLaunchRequest? request = null;
        try
        {
            if (new FileInfo(fullPath).Length > 32L * 1024 * 1024)
                throw new InvalidDataException("The chat launch request exceeds the 32 MB limit.");
            request = JsonSerializer.Deserialize<ChatLaunchRequest>(File.ReadAllText(fullPath), JsonOptions);
        }
        finally
        {
            try { File.Delete(fullPath); } catch { }
        }

        if (request is null
            || request.Mode is not ("empty" or "context" or "conversation")
            || (request.Mode == "conversation" && string.IsNullOrWhiteSpace(request.ConversationJson)))
        {
            RunMindCommandPaletteDiagnostics.Write("Chat launch process received an invalid or unsupported launch request.");
            return true;
        }

        RunMindCommandPaletteDiagnostics.Write(
            $"Chat launch process received mode='{request.Mode}', prompt length={request.Prompt?.Length ?? 0}, response length={request.Response?.Length ?? 0}, conversation JSON length={request.ConversationJson?.Length ?? 0}.");
        Exception? uiError = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new System.Windows.Application
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnLastWindowClose,
                };
                var window = request.Mode switch
                {
                    "conversation" when !string.IsNullOrWhiteSpace(request.ConversationJson)
                        => new RunMind.Standalone.MainWindow(request.ConversationJson!, null, "CommandPalette"),
                    "context" => new RunMind.Standalone.MainWindow(request.Prompt, request.Response, request.IsError, "CommandPalette"),
                    _ => new RunMind.Standalone.MainWindow(null, "CommandPalette"),
                };
                RunMindCommandPaletteDiagnostics.Write(
                    $"Constructed Command Palette chat window for mode='{request.Mode}'.");
                app.Run(window);
            }
            catch (Exception exception)
            {
                uiError = exception;
                RunMindCommandPaletteDiagnostics.Write("Command Palette chat UI thread failed.", exception);
            }
        })
        {
            Name = "RunMind Command Palette chat",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (uiError is not null)
            throw new InvalidOperationException("The Command Palette chat window could not start.", uiError);
        RunMindCommandPaletteDiagnostics.Write("Command Palette chat UI message loop exited.");
        return true;
    }

    private static void Launch(ChatLaunchRequest request)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new InvalidOperationException("The RunMind Command Palette host executable could not be located.");

        var launchDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RunMind", "CommandPalette", "Launch");
        Directory.CreateDirectory(launchDirectory);
        var launchPath = Path.Combine(launchDirectory, Guid.NewGuid().ToString("N") + ".json");
        var temporaryPath = launchPath + ".tmp";

        try
        {
            var launchJson = JsonSerializer.Serialize(request, JsonOptions);
            if (Encoding.UTF8.GetByteCount(launchJson) > 32 * 1024 * 1024)
                throw new InvalidOperationException("The chat launch request exceeds the 32 MB transfer limit.");
            File.WriteAllText(temporaryPath, launchJson, Encoding.UTF8);
            File.Move(temporaryPath, launchPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("--chat-launch");
            startInfo.ArgumentList.Add(launchPath);

            RunMindCommandPaletteDiagnostics.Write(
                $"Starting packaged Command Palette chat process. Mode='{request.Mode}'; launch file='{launchPath}'.");
            using var process = Process.Start(startInfo);
            if (process is null)
                throw new InvalidOperationException("Windows did not start the Command Palette chat host.");
            RunMindCommandPaletteDiagnostics.Write(
                $"Started Command Palette chat process PID={process.Id}; mode='{request.Mode}'.");
        }
        catch (Exception exception)
        {
            RunMindCommandPaletteDiagnostics.Write("Failed to launch Command Palette chat process.", exception);
            try { File.Delete(temporaryPath); } catch { }
            try { File.Delete(launchPath); } catch { }
            throw;
        }
    }

    private static string? Normalize(string? value, int maximumLength)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private sealed class ChatLaunchRequest
    {
        public string Mode { get; set; } = "empty";
        public string? Prompt { get; set; }
        public string? Response { get; set; }
        public bool IsError { get; set; }
        public string? ConversationJson { get; set; }
    }
}
