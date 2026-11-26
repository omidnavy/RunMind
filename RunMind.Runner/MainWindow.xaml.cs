using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using RunMind.Shared;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfClipboard = System.Windows.Clipboard;

namespace RunMind.Runner;

public partial class MainWindow : Window
{
    private static readonly TimeSpan InlineDebounce = TimeSpan.FromMilliseconds(650);
    private const int MaximumInlineDisplayCharacters = 180_000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private readonly ApplicationCatalog _applicationCatalog = new();
    private readonly RunnerPluginRegistry _pluginRegistry;
    private readonly RunnerInlineModelClient _inlineClient = new();
    private readonly ObservableCollection<RunnerPluginResult> _results = new();
    private readonly WindowThemeSynchronizer _themeSynchronizer;
    private IReadOnlyList<RunnerItem> _catalog = Array.Empty<RunnerItem>();
    private RunnerSettings _settings = RunnerSettings.Load();
    private RunnerInlineSettings _inlineSettings = RunnerInlineSettingsStore.Load();
    private RunnerInlineCredentials _inlineCredentials = RunnerInlineCredentialsStore.Load();
    private GlobalHotkey? _globalHotkey;
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _trayIconImage;
    private CancellationTokenSource? _inlineGeneration;
    private bool _exitRequested;
    private bool _catalogLoaded;
    private string? _hotkeyStatus;
    private string _lastInlineQuery = string.Empty;
    private string _lastInlineResponse = string.Empty;
    private bool _lastInlineError;
    private bool _inlineIsRunning;

    public MainWindow()
    {
        InitializeComponent();
        _pluginRegistry = new RunnerPluginRegistry(_applicationCatalog);
        ResultsList.ItemsSource = _results;
        RunnerThemeManager.Apply(this);
        _themeSynchronizer = new WindowThemeSynchronizer(this);
    }

    internal void RefreshNativeTheme() => _themeSynchronizer.RefreshAppearance();

    public void HideAfterStartup()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Hide));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CreateTrayIcon();
        SearchBox.Focus();
        RefreshSearch();
        await Dispatcher.InvokeAsync(() =>
        {
            UpdateWindowHeightForResults(SearchBox.Text.Length == 0);
            PositionOnConfiguredMonitor();
        }, DispatcherPriority.Loaded);
        StatusText.Text = _hotkeyStatus ?? $"Esc to hide  ·  Enter to open  ·  {_settings.Hotkey} to summon";

        // The launcher is usable immediately. Start-menu enumeration runs off the UI thread.
        try
        {
            var catalog = await Task.Run(_applicationCatalog.Load);
            _catalog = catalog;
            _pluginRegistry.SetApplicationCatalog(catalog);
            _catalogLoaded = true;
            RefreshSearch();
        }
        catch (Exception exception)
        {
            StatusText.Text = "Application search is unavailable: " + exception.Message;
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            _globalHotkey = new GlobalHotkey(this);
            _globalHotkey.Pressed += () => Dispatcher.BeginInvoke(new Action(ShowLauncher));
            if (!_globalHotkey.TryRegister(_settings.Hotkey, out var error))
                _hotkeyStatus = error + " Open Settings to choose another shortcut.";
        }
        catch (Exception exception)
        {
            _hotkeyStatus = "Global shortcut is unavailable: " + exception.Message;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            e.Cancel = true;
            CancelInlineGeneration();
            Hide();
            return;
        }

        CancelInlineGeneration();
        _globalHotkey?.Dispose();
        _globalHotkey = null;
        _themeSynchronizer.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayIconImage?.Dispose();
        _trayIconImage = null;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        RefreshSearch();
        ScheduleInlineGeneration(SearchBox.Text.Trim());
    }

    private void SearchBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Clear();
            CancelInlineGeneration();
            Hide();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && _results.Count > 0)
        {
            ResultsList.SelectedIndex = ResultsList.SelectedIndex < 0 ? 0 : Math.Min(ResultsList.SelectedIndex + 1, _results.Count - 1);
            ResultsList.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            OpenSelectedResult();
            e.Handled = true;
        }
    }

    private void ResultsList_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Clear();
            CancelInlineGeneration();
            Hide();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            OpenSelectedResult();
            e.Handled = true;
        }
        if (e.Key == Key.Up && ResultsList.SelectedIndex <= 0)
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
            e.Handled = true;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // RunMind already activates on the first click; do not create a second
        // Standalone conversation from the second half of a double-click.
        if (ResultsList.SelectedItem is RunnerPluginResult { PluginId: "runmind" }) return;
        OpenSelectedResult();
    }

    private async void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(ResultsList, source) is not ListBoxItem item
            || item.DataContext is not RunnerPluginResult result
            || !string.Equals(result.PluginId, "runmind", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(result.Value)
            || e.ClickCount > 1)
            return;

        e.Handled = true;
        await OpenStandaloneChatAsync(result.Value);
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is RunnerPluginResult { PluginId: "runmind" })
            StatusText.Text = "RunMind result · Enter to open or continue in chat";
    }

    private void RefreshSearch()
    {
        if (_results is null || SearchBox is null) return;
        _results.Clear();
        var query = SearchBox.Text.Trim();
        var enabled = _settings.EnabledPlugins.ToHashSet(StringComparer.Ordinal);
        if (!_inlineSettings.Enabled) enabled.Remove("runmind");
        foreach (var result in _pluginRegistry.Search(query, enabled, _settings.RunMindGlobalMode,
                     _settings.RunMindActionKeyword, _settings.PluginWeights)) _results.Add(result);

        var isInitialState = query.Length == 0;
        var hasResults = _results.Count > 0;
        ResultsPanel.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
        ResultsList.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
        ResultsList.SelectedIndex = hasResults ? 0 : -1;

        if (!isInitialState && !hasResults)
            StatusText.Text = _catalogLoaded ? "No matches · Try another search or press Esc to clear" : "Searching applications…";
        else if (isInitialState)
            StatusText.Text = _hotkeyStatus ?? $"Esc to hide  ·  Enter to open  ·  {_settings.Hotkey} to summon";

        UpdateWindowHeightForResults(isInitialState);
    }

    private void ScheduleInlineGeneration(string query)
    {
        CancelInlineGeneration();
        if (!RunMindInlinePlugin.TryExtractPrompt(query, _settings.RunMindGlobalMode, _settings.RunMindActionKeyword, out var prompt)
            || !_inlineSettings.Enabled
            || !_settings.EnabledPlugins.Contains("runmind", StringComparer.Ordinal))
        {
            ResetInlineAnswerForNonRunMindQuery();
            return;
        }

        InlineStatusText.Text = "Preparing inline answer…";
        InlineAnswerText.Text = "RunMind will answer after you finish typing.";
        InlineAnswerText.Foreground = (System.Windows.Media.Brush)FindResource("RunnerMuted");
        CopyAnswerButton.IsEnabled = false;
        StopButton.Visibility = Visibility.Collapsed;
        var cancellation = new CancellationTokenSource();
        _inlineGeneration = cancellation;
        _ = GenerateInlineAfterDelayAsync(query, prompt, cancellation);
    }

    private void ResetInlineAnswerForNonRunMindQuery()
    {
        if (InlineStatusText is null || InlineAnswerText is null) return;
        InlineStatusText.Text = "Inline assistant · Ollama / LM Studio";
        InlineAnswerText.Text = "Type llm followed by a question to get an inline answer. Ordinary searches stay local to the launcher.";
        InlineAnswerText.Foreground = (System.Windows.Media.Brush)FindResource("RunnerMuted");
        CopyAnswerButton.IsEnabled = false;
        StopButton.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(_lastInlineQuery)) return;
        _lastInlineQuery = string.Empty;
        _lastInlineResponse = string.Empty;
        _lastInlineError = false;
    }

    private async Task GenerateInlineAfterDelayAsync(string launcherQuery, string prompt, CancellationTokenSource owner)
    {
        try
        {
            await Task.Delay(InlineDebounce, owner.Token);
            if (owner.IsCancellationRequested || !string.Equals(SearchBox.Text.Trim(), launcherQuery, StringComparison.Ordinal)) return;
            await Dispatcher.InvokeAsync(() =>
            {
                _inlineIsRunning = true;
                UpdateRunMindResult("RunMind", "Generating inline answer…", prompt);
                _lastInlineQuery = prompt;
                _lastInlineResponse = string.Empty;
                _lastInlineError = false;
                CopyAnswerButton.IsEnabled = false;
                StopButton.Visibility = Visibility.Visible;
                InlineStatusText.Text = "Thinking…";
                InlineAnswerText.Text = string.Empty;
                InlineAnswerText.Foreground = (System.Windows.Media.Brush)FindResource("RunnerText");
                InlineModelText.Text = string.IsNullOrWhiteSpace(_inlineSettings.Model)
                    ? "No default model selected · Settings or > model > prompt"
                    : $"{(_inlineSettings.Provider == "lmstudio" ? "LM Studio" : "Ollama")} · {_inlineSettings.Model}";
            });

            var partial = new Progress<string>(chunk =>
            {
                if (owner.IsCancellationRequested || !ReferenceEquals(_inlineGeneration, owner)) return;
                if (InlineAnswerText.Text.Length >= MaximumInlineDisplayCharacters) return;
                var available = MaximumInlineDisplayCharacters - InlineAnswerText.Text.Length;
                InlineAnswerText.Text += chunk.Length > available ? chunk[..available] : chunk;
                InlineAnswerScroll.ScrollToEnd();
            });

            var response = await _inlineClient.GenerateAsync(prompt, _inlineSettings, _inlineCredentials, partial, owner.Token);
            if (owner.IsCancellationRequested || !ReferenceEquals(_inlineGeneration, owner)) return;
            _lastInlineResponse = response;
            UpdateRunMindResult("RunMind", "Inline answer · " + CreateAnswerPreview(response), prompt);
            InlineStatusText.Text = "Answer ready";
            StatusText.Text = "RunMind answer ready · select its result to continue in chat";
            InlineAnswerText.Text = response.Length > MaximumInlineDisplayCharacters
                ? response[..MaximumInlineDisplayCharacters] + "\n\n[Display limit reached. Open chat to continue.]"
                : response;
            InlineAnswerText.Foreground = (System.Windows.Media.Brush)FindResource("RunnerText");
            CopyAnswerButton.IsEnabled = !string.IsNullOrWhiteSpace(response);
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
            // A newer query owns the launcher. The old result is intentionally discarded.
        }
        catch (Exception exception)
        {
            if (!owner.IsCancellationRequested && ReferenceEquals(_inlineGeneration, owner))
            {
                _lastInlineError = true;
                _lastInlineResponse = exception.Message;
                UpdateRunMindResult("RunMind", "Could not answer · " + CreateAnswerPreview(exception.Message), prompt);
                InlineStatusText.Text = "RunMind could not answer";
                StatusText.Text = "RunMind request failed · see the RunMind result for details";
                InlineAnswerText.Text = exception.Message;
                InlineAnswerText.Foreground = (System.Windows.Media.Brush)FindResource("RunnerDanger");
                CopyAnswerButton.IsEnabled = true;
            }
        }
        finally
        {
            if (ReferenceEquals(_inlineGeneration, owner))
            {
                _inlineGeneration = null;
                _inlineIsRunning = false;
                StopButton.Visibility = Visibility.Collapsed;
            }
            owner.Dispose();
        }
    }

    private void UpdateRunMindResult(string title, string subtitle, string query)
    {
        for (var index = 0; index < _results.Count; index++)
        {
            var result = _results[index];
            if (result.PluginId != "runmind" || !string.Equals(result.Value, query, StringComparison.Ordinal)) continue;
            _results[index] = result with { Title = title, Subtitle = subtitle, Value = query };
            return;
        }
    }

    private static string CreateAnswerPreview(string value)
    {
        var normalized = string.Join(" ", (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        const int maximum = 104;
        return normalized.Length <= maximum ? normalized : normalized[..(maximum - 1)] + "…";
    }

    private void CancelInlineGeneration()
    {
        var active = _inlineGeneration;
        _inlineGeneration = null;
        _inlineIsRunning = false;
        if (active is null) return;
        try { active.Cancel(); } catch (ObjectDisposedException) { }
        StopButton.Visibility = Visibility.Collapsed;
    }

    private void StopInline_Click(object sender, RoutedEventArgs e)
    {
        CancelInlineGeneration();
        InlineStatusText.Text = "Stopped";
        StatusText.Text = "RunMind generation cancelled.";
    }

    private void CopyAnswer_Click(object sender, RoutedEventArgs e)
    {
        var value = _lastInlineResponse;
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            WpfClipboard.SetText(value);
            StatusText.Text = "RunMind answer copied to clipboard.";
        }
        catch (Exception exception) { StatusText.Text = "Could not copy the answer: " + exception.Message; }
    }

    private async void OpenSelectedResult()
    {
        if (ResultsList.SelectedItem is not RunnerPluginResult result) return;
        try
        {
            switch (result.Action)
            {
                case RunnerPluginAction.OpenTarget when result.Target is not null:
                    RunnerCommandService.Execute(result.Target);
                    SearchBox.Clear();
                    Hide();
                    break;
                case RunnerPluginAction.CopyText when result.Value is not null:
                    WpfClipboard.SetText(result.Value);
                    StatusText.Text = "Copied " + result.Value;
                    break;
                case RunnerPluginAction.ShowInlineAnswer:
                    await OpenStandaloneChatAsync(string.IsNullOrWhiteSpace(result.Value) ? _lastInlineQuery : result.Value);
                    break;
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = "Could not complete " + result.Title + ": " + exception.Message;
        }
    }

    private async void Chat_Click(object sender, RoutedEventArgs e)
    {
        var prompt = RunMindInlinePlugin.TryExtractPrompt(SearchBox.Text, _settings.RunMindGlobalMode, _settings.RunMindActionKeyword, out var currentPrompt)
            ? currentPrompt
            : (_lastInlineQuery.Length > 0 ? _lastInlineQuery : null);
        await OpenStandaloneChatAsync(prompt);
    }

    private async Task OpenStandaloneChatAsync(string? query)
    {
        var contextPrompt = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var wasGenerating = _inlineIsRunning;
        var partialInlineResponse = wasGenerating ? InlineAnswerText.Text : string.Empty;
        var responseMatchesPrompt = string.Equals(_lastInlineQuery, contextPrompt, StringComparison.Ordinal);
        CancelInlineGeneration();

        string? contextResponse = responseMatchesPrompt ? _lastInlineResponse : null;
        if (responseMatchesPrompt && wasGenerating && !string.IsNullOrWhiteSpace(partialInlineResponse))
            contextResponse = partialInlineResponse;
        if (string.IsNullOrWhiteSpace(contextResponse)) contextResponse = null;
        var contextError = contextResponse is not null && !wasGenerating && _lastInlineError;
        var payload = new RunnerHandoffPayload
        {
            Prompt = contextPrompt,
            Response = contextResponse,
            IsError = contextError,
            CreatedUtc = DateTimeOffset.UtcNow,
        };

        // Prefer the current-user-only pipe so an already-running Standalone receives
        // the context in a new full-agent conversation window without another process.
        if (await RunnerHandoffPipe.TrySendAsync(payload, 900))
        {
            Hide();
            return;
        }

        var executable = StandaloneLocator.FindExecutable();
        if (string.IsNullOrWhiteSpace(executable))
        {
            StatusText.Text = "RunMind Standalone was not found. Install or launch Standalone, then try again.";
            System.Windows.MessageBox.Show(this,
                "RunMind Standalone could not be found. Runner is an optional quick host; full chat and Full Agent capabilities belong to the separate RunMind Standalone application.",
                "RunMind Standalone not found", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string? handoffFile = null;
        try
        {
            handoffFile = RunnerHandoffStore.Write(payload);
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            };
            startInfo.ArgumentList.Add("--runner-handoff-file");
            startInfo.ArgumentList.Add(handoffFile);
            var process = Process.Start(startInfo);
            if (process is null)
                throw new InvalidOperationException("Windows did not start RunMind Standalone.");
            Hide();
        }
        catch (Exception exception)
        {
            RunnerHandoffStore.TryDelete(handoffFile);
            StatusText.Text = "RunMind Standalone could not be opened.";
            System.Windows.MessageBox.Show(this,
                "RunMind Standalone could not be opened. " + exception.Message,
                "RunMind Runner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RunnerSettingsWindow(_settings, _inlineSettings, _inlineCredentials) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var candidate = dialog.Settings;
        var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        var previous = _settings;
        try
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new InvalidOperationException("The Runner executable path could not be determined.");
            candidate.ApplyStartupRegistration(executablePath);
            candidate.Save();
            RunnerInlineSettingsStore.Save(dialog.InlineSettings);
            RunnerInlineCredentialsStore.Save(dialog.Credentials);
            if (_globalHotkey is not null && !_globalHotkey.TryRegister(candidate.Hotkey, out var error))
            {
                _globalHotkey.TryRegister(previous.Hotkey, out _);
                previous.ApplyStartupRegistration(executablePath);
                previous.Save();
                throw new InvalidOperationException(error);
            }
            _settings = candidate;
            _inlineSettings = RunnerInlineSettingsStore.Load();
            _inlineCredentials = RunnerInlineCredentialsStore.Load();
            RunnerThemeManager.ApplyToAllOpenWindows();
            PositionOnConfiguredMonitor();
            _hotkeyStatus = null;
            StatusText.Text = $"Esc to hide  ·  Enter to open  ·  {_settings.Hotkey} to summon";
            RefreshSearch();
            if (SearchBox.Text.Length > 0) ScheduleInlineGeneration(SearchBox.Text.Trim());
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, "Runner settings could not be saved. " + exception.Message,
                "RunMind Runner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        CancelInlineGeneration();
        Hide();
    }

    private void ShowLauncher()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Topmost = true;
        UpdateWindowHeightForResults(SearchBox.Text.Length == 0);
        PositionOnConfiguredMonitor();
        Activate();
        SearchBox.Focus();
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    private void PositionOnConfiguredMonitor()
    {
        if (!IsLoaded) return;
        try
        {
            var cursorPoint = Forms.Cursor.Position;
            var screen = _settings.OpenOnActiveMonitor
                ? Forms.Screen.FromPoint(cursorPoint)
                : Forms.Screen.PrimaryScreen ?? Forms.Screen.FromPoint(cursorPoint);
            var workingArea = screen.WorkingArea;
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            var dpi = GetMonitorDpi(screen);
            var scale = dpi / 96d;
            var widthPixels = (int)Math.Ceiling((ActualWidth > 0 ? ActualWidth : Width) * scale);
            var heightPixels = (int)Math.Ceiling((ActualHeight > 0 ? ActualHeight : Height) * scale);
            var x = workingArea.Left + Math.Max(0, (workingArea.Width - widthPixels) / 2);
            var y = workingArea.Top + Math.Max(0, (workingArea.Height - heightPixels) / 3);
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }
        catch
        {
            // Monitor discovery is best-effort; never prevent the launcher from opening.
        }
    }

    private void UpdateWindowHeightForResults(bool emptyQuery)
    {
        if (!_settings.AutoResizeToResults) return;
        try
        {
            var visibleRows = emptyQuery ? 0 : Math.Clamp(_results.Count, 0, 8);
            var contentHeight = visibleRows > 0 ? 16 + visibleRows * 56 : 0;
            const double compactWindowHeight = 184;
            var targetHeight = compactWindowHeight + contentHeight;

            var handle = new WindowInteropHelper(this).Handle;
            var screen = handle != IntPtr.Zero && IsVisible
                ? Forms.Screen.FromHandle(handle)
                : GetConfiguredTargetScreen();
            var workingArea = screen.WorkingArea;
            var scale = GetMonitorDpi(screen) / 96d;
            var maximumHeight = Math.Max(MinHeight, workingArea.Height / scale - 24);
            var targetHeightDip = Math.Min(targetHeight, maximumHeight);

            var previousBounds = default(NativeRect);
            var hasPreviousBounds = handle != IntPtr.Zero && GetWindowRect(handle, out previousBounds);
            var anchorToBottom = hasPreviousBounds &&
                previousBounds.Top + ((previousBounds.Bottom - previousBounds.Top) / 2) >=
                workingArea.Top + (workingArea.Height / 2);

            MaxHeight = maximumHeight;
            Height = targetHeightDip;
            UpdateLayout();

            // Preserve the top edge in the upper half of the display. In the lower half,
            // preserve the bottom edge so additional results expand upward instead.
            if (hasPreviousBounds && GetWindowRect(handle, out var resizedBounds))
            {
                var newHeightPixels = resizedBounds.Bottom - resizedBounds.Top;
                var newWidthPixels = resizedBounds.Right - resizedBounds.Left;
                var targetY = anchorToBottom
                    ? previousBounds.Bottom - newHeightPixels
                    : previousBounds.Top;
                targetY = Math.Clamp(targetY, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - newHeightPixels));
                var targetX = Math.Clamp(previousBounds.Left, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - newWidthPixels));
                SetWindowPos(handle, IntPtr.Zero, targetX, targetY, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
            }
        }
        catch
        {
            Height = Math.Clamp(Height, MinHeight, 700);
        }
    }

    private Forms.Screen GetConfiguredTargetScreen()
    {
        var cursorPoint = Forms.Cursor.Position;
        return _settings.OpenOnActiveMonitor
            ? Forms.Screen.FromPoint(cursorPoint)
            : Forms.Screen.PrimaryScreen ?? Forms.Screen.FromPoint(cursorPoint);
    }

    private static uint GetMonitorDpi(Forms.Screen screen)
    {
        try
        {
            var bounds = screen.Bounds;
            var monitor = MonitorFromPoint(new NativePoint(bounds.Left + 1, bounds.Top + 1), 2);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MonitorDpiType.Effective, out var dpiX, out _) == 0 && dpiX >= 96)
                return dpiX;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return 96;
    }

    private enum MonitorDpiType { Effective = 0, Angular = 1, Raw = 2 }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;
        public NativePoint(int x, int y) { X = x; Y = y; }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rectangle);

    private void CreateTrayIcon()
    {
        if (_trayIcon is not null) return;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "Icon.ico");
        _trayIconImage = File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open RunMind Runner", null, (_, _) => Dispatcher.BeginInvoke(new Action(ShowLauncher)));
        menu.Items.Add("Open RunMind chat", null, (_, _) => Dispatcher.BeginInvoke(new Action(async () => await OpenStandaloneChatAsync(null))));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.BeginInvoke(new Action(ExitRunner)));
        _trayIcon = new Forms.NotifyIcon { Icon = _trayIconImage, Text = "RunMind Runner", ContextMenuStrip = menu, Visible = true };
        _trayIcon.MouseUp += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
                Dispatcher.BeginInvoke(new Action(ShowLauncher));
        };
    }

    private void ExitRunner() => ShutdownRunner();

    public void ShutdownRunner()
    {
        if (_exitRequested) return;
        _exitRequested = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }
}
