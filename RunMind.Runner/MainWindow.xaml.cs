using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace RunMind.Runner;

public partial class MainWindow : Window
{
    private readonly ApplicationCatalog _applicationCatalog = new();
    private readonly ObservableCollection<RunnerItem> _results = new();
    private IReadOnlyList<RunnerItem> _catalog = Array.Empty<RunnerItem>();
    private RunnerSettings _settings = RunnerSettings.Load();
    private GlobalHotkey? _globalHotkey;
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _trayIconImage;
    private bool _exitRequested;
    private bool _catalogLoaded;
    private string? _hotkeyStatus;

    public MainWindow()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _results;
    }

    public void HideAfterStartup()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Hide));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CreateTrayIcon();
        SearchBox.Focus();
        RefreshSearch();
        StatusText.Text = _hotkeyStatus ?? $"Esc to close  ·  Enter to open  ·  {_settings.Hotkey} to summon";

        try
        {
            _catalog = await Task.Run(_applicationCatalog.Load);
            _catalogLoaded = true;
            RefreshSearch();
        }
        catch (Exception exception)
        {
            StatusText.Text = "Application catalog could not be loaded: " + exception.Message;
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
            Hide();
            return;
        }

        _globalHotkey?.Dispose();
        _globalHotkey = null;
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
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Clear();
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

    private void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Clear();
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

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedResult();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RunnerSettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var candidate = dialog.Settings;
        if (_globalHotkey is not null && !_globalHotkey.TryRegister(candidate.Hotkey, out var error))
        {
            _globalHotkey.TryRegister(_settings.Hotkey, out _);
            MessageBox.Show(this, error, "RunMind Runner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        try
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new InvalidOperationException("The Runner executable path could not be determined.");

            candidate.ApplyStartupRegistration(executablePath);
            candidate.Save();
            _settings = candidate;
            _hotkeyStatus = null;
            StatusText.Text = $"Esc to close  ·  Enter to open  ·  {_settings.Hotkey} to summon";
        }
        catch (Exception exception)
        {
            try { _settings.ApplyStartupRegistration(executablePath ?? string.Empty); }
            catch { }
            _globalHotkey?.TryRegister(_settings.Hotkey, out _);
            MessageBox.Show(this, "Runner settings could not be saved. " + exception.Message,
                "RunMind Runner", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        Hide();
    }

    private void RefreshSearch()
    {
        if (_results is null || SearchBox is null)
            return;

        _results.Clear();
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            ShowEmptyState("Your shortcuts, one keystroke away", _catalogLoaded
                ? "Start typing to find an installed application"
                : "Loading installed application shortcuts…");
            return;
        }

        var directItem = RunnerCommandService.ResolveDirectInput(query);
        if (directItem is not null)
            _results.Add(directItem);

        foreach (var item in _applicationCatalog.Search(_catalog, query))
        {
            if (_results.Count >= 9)
                break;
            if (_results.Any(existing => string.Equals(existing.Target, item.Target, StringComparison.OrdinalIgnoreCase)))
                continue;
            _results.Add(item);
        }

        if (_results.Count == 0)
            ShowEmptyState("No matching application found", "Try an installed app name, a full file path, a folder path, or an HTTP/HTTPS URL");
        else
            EmptyState.Visibility = Visibility.Collapsed;

        ResultsList.SelectedIndex = _results.Count > 0 ? 0 : -1;
    }

    private void ShowEmptyState(string title, string description)
    {
        EmptyTitle.Text = title;
        EmptyDescription.Text = description;
        EmptyState.Visibility = Visibility.Visible;
    }

    private void OpenSelectedResult()
    {
        if (ResultsList.SelectedItem is not RunnerItem item)
            return;

        try
        {
            RunnerCommandService.Execute(item);
            SearchBox.Clear();
            Hide();
        }
        catch (Exception exception)
        {
            StatusText.Text = "Could not open " + item.Title + ": " + exception.Message;
        }
    }

    private void ShowLauncher()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Topmost = true;
        Activate();
        SearchBox.Focus();
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    private void CreateTrayIcon()
    {
        if (_trayIcon is not null)
            return;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "Icon.ico");
        _trayIconImage = File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open RunMind Runner", null, (_, _) => Dispatcher.BeginInvoke(new Action(ShowLauncher)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.BeginInvoke(new Action(ExitRunner)));

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayIconImage,
            Text = "RunMind Runner",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.MouseUp += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
                Dispatcher.BeginInvoke(new Action(ShowLauncher));
        };
    }

    private void ExitRunner() => ShutdownRunner();

    public void ShutdownRunner()
    {
        if (_exitRequested)
            return;

        _exitRequested = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }
}
