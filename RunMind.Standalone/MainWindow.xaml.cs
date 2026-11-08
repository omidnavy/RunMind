using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using RunMind.Shared;

namespace RunMind.Standalone;

public partial class MainWindow : Window
{
    private readonly WindowThemeSynchronizer _windowThemeSynchronizer;
    private readonly NativePageReader _nativePageReader = new();
    private readonly AgentHostBridge _agentHostBridge = new();
    private readonly WebView2 _webView = new()
    {
        DefaultBackgroundColor = System.Drawing.Color.FromArgb(17, 19, 24),
        AllowExternalDrop = false,
    };
    private bool _closing;
    private bool _webViewDisposed;
    private string _initializationStage = "Prepare standalone window";
    private string _userDataFolder = "(not resolved)";
    private readonly string? _initialConversationJson;
    private readonly string? _initialConversationFileName;
    private bool _freshConversationForNextNavigation;
    private string? _freshConversationProvider;
    private string? _freshConversationModel;
    private string? _freshConversationConnectionId;
    private bool? _freshConversationThinkEnabled;

    public MainWindow() : this(null, null, false, null, null, null, null) { }

    private MainWindow(
        string? initialConversationJson,
        string? initialConversationFileName,
        bool startFreshConversation,
        string? initialProvider,
        string? initialModel,
        string? initialConnectionId,
        bool? initialThinkEnabled)
    {
        _initialConversationJson = initialConversationJson;
        _initialConversationFileName = initialConversationFileName;
        _freshConversationForNextNavigation = startFreshConversation;
        _freshConversationProvider = initialProvider;
        _freshConversationModel = initialModel;
        _freshConversationConnectionId = initialConnectionId;
        _freshConversationThinkEnabled = initialThinkEnabled;
        InitializeComponent();
        _windowThemeSynchronizer = new WindowThemeSynchronizer(this);
        Root.Children.Add(_webView);
        Closed += OnClosed;
        Loaded += OnLoaded;
        WindowLayout.ConfigureInitialPlacement(this);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _initializationStage = "Locate ChatWindow.html";
            var appFolder = AppContext.BaseDirectory;
            var htmlPath = Path.Combine(appFolder, "ChatWindow.html");
            if (!File.Exists(htmlPath))
                throw new FileNotFoundException("ChatWindow.html must be deployed beside RunMind.exe.", htmlPath);

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                throw new InvalidOperationException("Windows did not provide a Local AppData path.");

            // Each host owns a separate WebView2 profile. This prevents one host's
            // browser session from blocking the other host during concurrent startup.
            _initializationStage = "Create standalone WebView2 user-data folder";
            var profile = Path.Combine(localAppData, "LocalLLM", "Standalone", "WebView2");
            _userDataFolder = profile;
            Directory.CreateDirectory(profile);
            _initializationStage = "Ensure Microsoft Edge WebView2 Runtime";
            await WebView2RuntimeBootstrapper.EnsureInstalledAsync();
            if (_closing) return;
            _initializationStage = "Create standalone WebView2 environment";
            var environment = await CoreWebView2Environment.CreateAsync(
                null,
                profile,
                new CoreWebView2EnvironmentOptions());
            if (_closing) return;
            _initializationStage = "Initialize standalone WebView2 control";
            await _webView.EnsureCoreWebView2Async(environment);
            if (_closing) return;

            _initializationStage = "Configure standalone WebView2 navigation";
            var core = _webView.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("localhost", appFolder, CoreWebView2HostResourceAccessKind.DenyCors);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(AppearancePreferenceStore.CreateDocumentBootstrapScript());
            // Accept only bounded native operations: Save As, validated conversation-window
            // requests, and starting a new conversation. No host objects are exposed.
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.AreHostObjectsAllowed = false;
            core.WebMessageReceived += OnWebMessageReceived;
            core.DocumentTitleChanged += (_, _) =>
            {
                if (_closing) return;
                var documentTitle = core.DocumentTitle;
                if (!string.IsNullOrWhiteSpace(documentTitle) && documentTitle.Length <= 100)
                    Title = documentTitle;
            };
            core.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)) return;
                if (uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    && uri.AbsolutePath.Equals("/ChatWindow.html", StringComparison.OrdinalIgnoreCase)) return;
                if (uri.Scheme is "http" or "https" or "mailto")
                {
                    args.Cancel = true;
                    OpenExternal(uri);
                }
            };
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
                    OpenExternal(uri);
            };
            core.NavigationCompleted += async (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    ShowFailure("The RunMind chat page could not be loaded.");
                    return;
                }
                try
                {
                    var freshConversation = _freshConversationForNextNavigation;
                    _freshConversationForNextNavigation = false;
                    JsonElement? initialConversation = null;
                    if (!freshConversation && !string.IsNullOrWhiteSpace(_initialConversationJson))
                    {
                        using var sessionDocument = JsonDocument.Parse(_initialConversationJson);
                        initialConversation = sessionDocument.RootElement.Clone();
                    }

                    var title = freshConversation || string.IsNullOrWhiteSpace(_initialConversationFileName)
                        ? "New chat"
                        : Path.GetFileNameWithoutExtension(_initialConversationFileName);
                    var bootstrap = JsonSerializer.Serialize(new
                    {
                        title,
                        appearance = AppearancePreferenceStore.CreateBootstrapValue(),
                        windowInstanceId = Guid.NewGuid().ToString("N"),
                        standalone = true,
                        freshConversation,
                        generateTitleAfterFirstResponse = true,
                        initialProvider = freshConversation ? _freshConversationProvider : null,
                        initialModel = freshConversation ? _freshConversationModel : null,
                        initialConnectionId = freshConversation ? _freshConversationConnectionId : null,
                        initialThink = freshConversation ? _freshConversationThinkEnabled : null,
                        initialConversation,
                        initialConversationFileName = freshConversation ? null : _initialConversationFileName,
                    });
                    _freshConversationProvider = null;
                    _freshConversationModel = null;
                    _freshConversationConnectionId = null;
                    _freshConversationThinkEnabled = null;
                    await core.ExecuteScriptAsync($"window.initializeRunMind({bootstrap});");
                }
                catch (Exception ex)
                {
                    ShowFailure($"Could not initialize the RunMind chat session: {ex.Message}");
                }
            };
            _initializationStage = "Navigate to ChatWindow.html";
            core.Navigate("https://localhost/ChatWindow.html");
        }
        catch (Exception ex)
        {
            ShowFailure("RunMind could not initialize WebView2. " + ex.Message
                + "\nFailed stage: " + _initializationStage
                + "\nHRESULT: 0x" + ex.HResult.ToString("X8")
                + "\nUser-data folder: " + _userDataFolder
                + "\nException details: " + ex
                + "\nRunMind checks for WebView2 and installs Microsoft's Evergreen Runtime automatically when it is missing. If setup failed, check your internet connection and Windows permissions.");
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_closing) return;
        try
        {
            if (_windowThemeSynchronizer.TryHandleWebMessage(args.Source, args.WebMessageAsJson))
                return;
            if (_nativePageReader.TryHandleMessage(args.Source, args.WebMessageAsJson, _webView.CoreWebView2, Dispatcher))
                return;
            if (_agentHostBridge.TryHandleMessage(args.Source, args.WebMessageAsJson, _webView.CoreWebView2, Dispatcher))
                return;
            if (ConversationSaveDialog.TryHandleMessage(args.Source, args.WebMessageAsJson, this, _webView.CoreWebView2))
                return;
            if (!Uri.TryCreate(args.Source, UriKind.Absolute, out var source)
                || source.Scheme != Uri.UriSchemeHttps
                || !source.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || !source.AbsolutePath.Equals("/ChatWindow.html", StringComparison.OrdinalIgnoreCase))
                return;

            var messageJson = args.WebMessageAsJson;
            if (string.IsNullOrWhiteSpace(messageJson)
                || Encoding.UTF8.GetByteCount(messageJson) > 55 * 1024 * 1024)
                return;

            using var messageDocument = JsonDocument.Parse(messageJson);
            var root = messageDocument.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String)
                return;

            var messageType = type.GetString();
            var provider = root.TryGetProperty("provider", out var providerElement)
                && providerElement.ValueKind == JsonValueKind.String
                && providerElement.GetString() is "ollama" or "lmstudio"
                    ? providerElement.GetString()
                    : null;
            var model = root.TryGetProperty("model", out var modelElement)
                && modelElement.ValueKind == JsonValueKind.String
                    ? modelElement.GetString()?.Trim()
                    : null;
            if (string.IsNullOrWhiteSpace(model) || model.Length > 512) model = null;
            var connectionId = root.TryGetProperty("connectionId", out var connectionElement)
                && connectionElement.ValueKind == JsonValueKind.String
                    ? connectionElement.GetString()?.Trim()
                    : null;
            if (string.IsNullOrWhiteSpace(connectionId) || connectionId.Length > 128) connectionId = null;
            bool? thinkEnabled = root.TryGetProperty("thinkEnabled", out var thinkElement)
                && thinkElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? thinkElement.GetBoolean()
                    : null;

            if (messageType == "runmind.newConversationInCurrentWindow")
            {
                _freshConversationForNextNavigation = true;
                _freshConversationProvider = provider;
                _freshConversationModel = model;
                _freshConversationConnectionId = connectionId;
                _freshConversationThinkEnabled = thinkEnabled;
                _webView.CoreWebView2.Navigate($"https://localhost/ChatWindow.html?fresh={Guid.NewGuid():N}");
                return;
            }

            if (messageType == "runmind.newConversationInNewWindow")
            {
                var newWindow = new MainWindow(null, null, true, provider, model, connectionId, thinkEnabled);
                newWindow.Show();
                newWindow.Activate();
                return;
            }

            if (messageType != "runmind.openConversationInNewWindow"
                || !root.TryGetProperty("conversation", out var conversation)
                || conversation.ValueKind != JsonValueKind.Object
                || !IsSupportedConversation(conversation))
                return;

            var fileName = root.TryGetProperty("fileName", out var fileNameElement) && fileNameElement.ValueKind == JsonValueKind.String
                ? Path.GetFileName(fileNameElement.GetString() ?? string.Empty)
                : "Conversation.runmind";
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "Conversation.runmind";
            if (fileName.Length > 255) fileName = fileName[..255];
            if (!fileName.EndsWith(".runmind", StringComparison.OrdinalIgnoreCase)) return;

            var child = new MainWindow(conversation.GetRawText(), fileName, false, null, null, null, null);
            child.Show();
            child.Activate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RunMind could not handle a conversation window request: {ex}");
        }
    }

    private static bool IsSupportedConversation(JsonElement conversation)
    {
        if (!conversation.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String)
            return false;
        var formatName = format.GetString();
        if (!string.Equals(formatName, "runmind", StringComparison.Ordinal))
            return false;
        if (!conversation.TryGetProperty("version", out var version) || !version.TryGetInt32(out var versionNumber) || versionNumber is not (1 or 2))
            return false;
        return conversation.TryGetProperty("messages", out var messages)
            && messages.ValueKind == JsonValueKind.Array
            && messages.GetArrayLength() is > 0 and <= 10000;
    }

    private static void OpenExternal(Uri uri)
    {
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { /* The browser remains isolated; an unavailable external handler is non-fatal. */ }
    }

    private void ShowFailure(string message)
    {
        if (_closing) return;
        Root.Children.Clear();
        DisposeWebView();
        Root.Children.Add(new TextBlock
        {
            Text = message,
            Margin = new Thickness(24),
            Foreground = System.Windows.Media.Brushes.White,
            TextWrapping = TextWrapping.Wrap,
        });
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _closing = true;
        try
        {
            if (_webView.CoreWebView2 is { } core)
                await core.ExecuteScriptAsync("window.runMindCloseSession && window.runMindCloseSession();");
        }
        catch { /* Crash/reload cleanup uses the IndexedDB session lease. */ }
        _nativePageReader.Dispose();
        _agentHostBridge.Dispose();
        _windowThemeSynchronizer.Dispose();
        DisposeWebView();
    }

    private void DisposeWebView()
    {
        if (_webViewDisposed) return;
        _webViewDisposed = true;
        try { _webView.Dispose(); }
        catch { /* Do not allow a partially initialized WebView2 control to escape teardown. */ }
    }
}
