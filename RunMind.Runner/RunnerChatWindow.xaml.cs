using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using RunMind.Shared;

namespace RunMind.Runner;

/// <summary>
/// Lazy chat host for Runner. It reuses RunMind's actual ChatWindow UI/theme catalog but
/// intentionally does not load AgentHostBridge, workspace, MCP, Skills or System Manager.
/// </summary>
public partial class RunnerChatWindow : Window
{
    private readonly WindowThemeSynchronizer _themeSynchronizer;
    private string? _initialPrompt;
    private string? _initialResponse;
    private bool _initialIsError;
    private readonly string _sourceProvider;
    private readonly string _sourceModel;
    private string _userDataFolder = "(not resolved)";
    private string _initializationStage = "Prepare Runner chat window";
    private bool _initializationStarted;
    private bool _closing;

    public RunnerChatWindow(string? initialPrompt = null, string? initialResponse = null, bool initialIsError = false,
        string? sourceProvider = null, string? sourceModel = null)
    {
        InitializeComponent();
        _initialPrompt = initialPrompt;
        _initialResponse = initialResponse;
        _initialIsError = initialIsError;
        _sourceProvider = sourceProvider == "lmstudio" ? "lmstudio" : "ollama";
        _sourceModel = (sourceModel ?? string.Empty).Trim();
        RunnerThemeManager.Apply(this);
        _themeSynchronizer = new WindowThemeSynchronizer(this);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initializationStarted) return;
        _initializationStarted = true;
        try
        {
            _initializationStage = "Locate the shared RunMind ChatWindow assets";
            var appFolder = AppContext.BaseDirectory;
            var htmlPath = Path.Combine(appFolder, "ChatWindow.html");
            if (!File.Exists(htmlPath))
                throw new FileNotFoundException("ChatWindow.html must be deployed beside RunMind Runner.", htmlPath);

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                throw new InvalidOperationException("Windows did not provide a Local AppData path for the Runner chat profile.");
            var profile = Path.Combine(localAppData, "RunMind", "Runner", "ChatWebView2");
            _userDataFolder = profile;
            Directory.CreateDirectory(profile);

            _initializationStage = "Ensure the Microsoft WebView2 Runtime is installed";
            await WebView2RuntimeBootstrapper.EnsureInstalledAsync();
            if (_closing) return;

            _initializationStage = "Initialize the Runner WebView2 profile";
            await using (var initializationLock = await WebView2RuntimeBootstrapper.AcquireUserDataFolderInitializationLockAsync(profile))
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, profile, new CoreWebView2EnvironmentOptions());
                if (_closing) return;
                await ChatWebView.EnsureCoreWebView2Async(environment);
            }
            if (_closing) return;

            _initializationStage = "Configure the restricted Runner chat bridge";
            var core = ChatWebView.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("localhost", appFolder, CoreWebView2HostResourceAccessKind.DenyCors);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(AppearancePreferenceStore.CreateDocumentBootstrapScript());
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.AreHostObjectsAllowed = false;
            core.WebMessageReceived += OnWebMessageReceived;
            core.DocumentTitleChanged += (_, _) =>
            {
                if (!_closing && !string.IsNullOrWhiteSpace(core.DocumentTitle) && core.DocumentTitle.Length <= 100)
                    Title = core.DocumentTitle;
            };
            core.NavigationStarting += (_, args) =>
            {
                args.Cancel = true;
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)) return;
                if (IsTrustedChatPageUri(uri))
                {
                    args.Cancel = false;
                    return;
                }
                OpenExternal(uri);
            };
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)) OpenExternal(uri);
            };
            core.NavigationCompleted += async (_, args) =>
            {
                if (_closing) return;
                if (!args.IsSuccess)
                {
                    ShowFailure("The RunMind chat page could not be loaded.");
                    return;
                }
                try
                {
                    _initializationStage = "Initialize RunMind in restricted Quick Host mode";
                    var bootstrap = JsonSerializer.Serialize(new
                    {
                        title = string.IsNullOrWhiteSpace(_initialPrompt) ? "RunMind Runner" : "RunMind Runner · Inline handoff",
                        appearance = AppearancePreferenceStore.CreateBootstrapValue(),
                        windowInstanceId = Guid.NewGuid().ToString("N"),
                        standalone = false,
                        quickHostMode = true,
                        quickHostName = "Runner",
                        generateTitleAfterFirstResponse = true,
                        sourceProvider = _sourceProvider,
                        sourceModel = _sourceModel,
                        prompt = _initialPrompt,
                        response = _initialResponse,
                        isError = _initialIsError,
                    });
                    await core.ExecuteScriptAsync($"window.initializeRunMind({bootstrap});");
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
                catch (Exception exception)
                {
                    ShowFailure("RunMind chat initialization failed: " + exception.Message);
                }
            };

            _initializationStage = "Load RunMind ChatWindow.html";
            core.Navigate("https://localhost/ChatWindow.html");
        }
        catch (Exception exception)
        {
            ShowFailure("RunMind chat could not initialize. " + exception.Message
                + "\nFailed stage: " + _initializationStage
                + "\nHRESULT: 0x" + exception.HResult.ToString("X8")
                + "\nWebView2 profile: " + _userDataFolder);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_closing || !IsTrustedChatPageSource(args.Source)) return;
        if (_themeSynchronizer.TryHandleWebMessage(args.Source, args.WebMessageAsJson))
        {
            RunnerThemeManager.Apply(this);
            return;
        }

        try
        {
            if (Encoding.UTF8.GetByteCount(args.WebMessageAsJson) > 64 * 1024) return;
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String) return;
            var type = typeElement.GetString();

            if (type == "runmind.agentHostRequest")
            {
                HandleRestrictedAgentHostRequest(root);
                return;
            }

            // Chat-only Quick Host: no native tools, MCP, workspace selection, Skills,
            // developer operations, arbitrary dispatch or native page reader are registered.
            if (type == "runmind.newConversationInCurrentWindow")
            {
                _initialPrompt = null;
                _initialResponse = null;
                _initialIsError = false;
                ChatWebView.CoreWebView2.Navigate("https://localhost/ChatWindow.html?fresh=" + Guid.NewGuid().ToString("N"));
                return;
            }
        }
        catch
        {
            // Malformed messages are ignored; the restricted chat page remains usable.
        }
    }

    private void HandleRestrictedAgentHostRequest(JsonElement root)
    {
        if (!root.TryGetProperty("requestId", out var requestIdElement)
            || requestIdElement.ValueKind != JsonValueKind.String) return;
        var requestId = requestIdElement.GetString();
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128) return;
        var operation = root.TryGetProperty("operation", out var operationElement)
            && operationElement.ValueKind == JsonValueKind.String ? operationElement.GetString() : string.Empty;

        if (operation == "themes.list")
        {
            var result = new
            {
                type = "runmind.agentHostResult",
                requestId,
                ok = true,
                result = new
                {
                    themes = RunnerThemeManager.Themes.Select(theme => new
                    {
                        theme.Id, theme.Name, theme.Mode, theme.FileName, theme.Version,
                        palette = theme.Palette,
                    }).ToArray(),
                },
            };
            ChatWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(result));
            return;
        }

        ChatWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "runmind.agentHostResult",
            requestId,
            ok = false,
            error = "Runner Quick Host does not expose native Agent or workspace capabilities.",
        }));
    }

    private static bool IsTrustedChatPageSource(string? source)
        => Uri.TryCreate(source, UriKind.Absolute, out var uri) && IsTrustedChatPageUri(uri);

    private static bool IsTrustedChatPageUri(Uri uri)
        => uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && uri.Port == 443
            && uri.AbsolutePath.Equals("/ChatWindow.html", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo);

    private static void OpenExternal(Uri uri)
    {
        if (!string.IsNullOrEmpty(uri.UserInfo)) return;
        if (uri.Scheme is not ("http" or "https" or "mailto")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { }
    }

    private void ShowFailure(string message)
    {
        LoadingText.Text = "RunMind chat could not start";
        LoadingDetail.Text = message;
        LoadingDetail.TextWrapping = TextWrapping.Wrap;
        LoadingDetail.MaxWidth = 620;
        LoadingDetail.TextAlignment = TextAlignment.Center;
        LoadingOverlay.Visibility = Visibility.Visible;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _themeSynchronizer.Dispose();
        try { ChatWebView.Dispose(); } catch { }
    }
}
