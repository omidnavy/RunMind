using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace LocalLLM.Standalone;

public partial class MainWindow : Window
{
    private const int WmSettingChange = 0x001A;
    private const int WmThemeChanged = 0x031A;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private readonly WebView2 _webView = new()
    {
        DefaultBackgroundColor = System.Drawing.Color.FromArgb(17, 19, 24),
        AllowExternalDrop = false,
    };
    private bool _closing;
    private bool _webViewDisposed;
    private string _initializationStage = "Prepare standalone window";
    private string _userDataFolder = "(not resolved)";
    private HwndSource? _windowSource;

    public MainWindow()
    {
        InitializeComponent();
        Root.Children.Add(_webView);
        Closed += OnClosed;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(hwnd);
        _windowSource?.AddHook(OnWindowMessage);
        ApplySystemTitleBarTheme(hwnd);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Windows broadcasts WM_SETTINGCHANGE when the user switches the system
        // app appearance between light and dark. WM_THEMECHANGED covers theme refreshes.
        if (message is WmSettingChange or WmThemeChanged)
            ApplySystemTitleBarTheme(hwnd);

        return IntPtr.Zero;
    }

    private static void ApplySystemTitleBarTheme(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        var prefersLight = true;
        try
        {
            const string personalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            var value = Registry.GetValue(personalizeKey, "AppsUseLightTheme", 1);
            prefersLight = value is int setting ? setting != 0 : Convert.ToInt32(value) != 0;
        }
        catch
        {
            // Keep the Windows default light caption if the preference is unavailable.
        }

        var darkMode = prefersLight ? 0 : 1;
        var result = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        if (result != 0)
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref darkMode, sizeof(int));
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
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
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
                    var bootstrap = JsonSerializer.Serialize(new
                    {
                        title = "New chat",
                        windowInstanceId = Guid.NewGuid().ToString("N"),
                        standalone = true,
                        generateTitleAfterFirstResponse = true,
                    });
                    await core.ExecuteScriptAsync($"window.initializeLocalLLM({bootstrap});");
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
                + "\nCheck that Microsoft Edge WebView2 Runtime is installed and that the profile folder is writable.");
        }
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

    private void OnClosed(object? sender, EventArgs e)
    {
        _closing = true;
        _windowSource?.RemoveHook(OnWindowMessage);
        _windowSource = null;
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
