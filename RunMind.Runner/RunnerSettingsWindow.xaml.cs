using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using RunMind.Shared;

namespace RunMind.Runner;

public partial class RunnerSettingsWindow : Window
{
    private readonly RunnerSettings _currentSettings;
    private readonly RunnerInlineSettings _currentInlineSettings;
    private readonly RunnerInlineCredentials _currentCredentials;
    private readonly RunnerInlineModelClient _modelClient = new();

    internal RunnerSettings Settings { get; private set; }
    internal RunnerInlineSettings InlineSettings { get; private set; }
    internal RunnerInlineCredentials Credentials { get; private set; }

    internal RunnerSettingsWindow(
        RunnerSettings currentSettings,
        RunnerInlineSettings currentInlineSettings,
        RunnerInlineCredentials currentCredentials)
    {
        InitializeComponent();
        _currentSettings = currentSettings;
        _currentInlineSettings = currentInlineSettings;
        _currentCredentials = currentCredentials;
        Settings = currentSettings;
        InlineSettings = currentInlineSettings;
        Credentials = currentCredentials;
        RunnerThemeManager.Apply(this);

        HotkeyBox.Text = currentSettings.Hotkey;
        StartWithWindowsCheckBox.IsChecked = currentSettings.StartWithWindows;
        ApplicationsPluginCheck.IsChecked = currentSettings.EnabledPlugins.Contains("applications", StringComparer.Ordinal);
        FilesPluginCheck.IsChecked = currentSettings.EnabledPlugins.Contains("files", StringComparer.Ordinal);
        UrlsPluginCheck.IsChecked = currentSettings.EnabledPlugins.Contains("urls", StringComparer.Ordinal);
        CalculatorPluginCheck.IsChecked = currentSettings.EnabledPlugins.Contains("calculator", StringComparer.Ordinal);
        RunMindPluginCheck.IsChecked = currentSettings.EnabledPlugins.Contains("runmind", StringComparer.Ordinal);

        InlineEnabledCheck.IsChecked = currentInlineSettings.Enabled;
        SelectTag(ProviderBox, currentInlineSettings.Provider);
        UpdateTemperatureLabel();
        OllamaEndpointBox.Text = currentInlineSettings.OllamaEndpoint;
        LMStudioBaseUrlBox.Text = currentInlineSettings.LMStudioBaseUrl;
        ModelBox.Text = currentInlineSettings.Model;
        SelectTag(ThinkingBox, currentInlineSettings.Thinking);
        KeepAliveBox.Text = currentInlineSettings.KeepAliveSeconds.ToString(CultureInfo.InvariantCulture);
        RequestTimeoutBox.Text = currentInlineSettings.RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        DiscoveryTimeoutBox.Text = currentInlineSettings.ModelDiscoveryTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        CpuThreadsBox.Text = currentInlineSettings.InlineCpuThreads.ToString(CultureInfo.InvariantCulture);
        TemperatureBox.Text = currentInlineSettings.Temperature?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        TopPBox.Text = currentInlineSettings.TopP?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        MaxOutputTokensBox.Text = currentInlineSettings.MaxOutputTokens?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        SeedBox.Text = currentInlineSettings.Seed?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        SystemPromptBox.Text = currentInlineSettings.SystemPrompt;
        NemotronModeCheck.IsChecked = currentInlineSettings.Nemotron35BoltE4BConversationalMode;

        OllamaApiKeyBox.Password = string.Empty;
        OllamaAuthHeaderBox.Password = string.Empty;
        LMStudioApiKeyBox.Password = string.Empty;
        LMStudioAuthHeaderBox.Password = string.Empty;
        OllamaApiKeyBox.ToolTip = string.IsNullOrEmpty(currentCredentials.OllamaApiKey) ? "No Ollama API key saved." : "A key is saved. Enter a new key to replace it; leave blank to keep it.";
        OllamaAuthHeaderBox.ToolTip = string.IsNullOrEmpty(currentCredentials.OllamaAuthHeader) ? "No custom Ollama header saved." : "A custom header is saved. Enter a new value to replace it; leave blank to keep it.";
        LMStudioApiKeyBox.ToolTip = string.IsNullOrEmpty(currentCredentials.LMStudioApiKey) ? "No LM Studio API key saved." : "A key is saved. Enter a new key to replace it; leave blank to keep it.";
        LMStudioAuthHeaderBox.ToolTip = string.IsNullOrEmpty(currentCredentials.LMStudioAuthHeader) ? "No custom LM Studio header saved." : "A custom header is saved. Enter a new value to replace it; leave blank to keep it.";

        var appearance = RunnerThemeManager.CurrentAppearance;
        SelectTag(AppearanceModeBox, appearance.Mode);
        var themes = RunnerThemeManager.Themes;
        LightThemeBox.ItemsSource = themes.Where(theme => theme.Mode == "light").ToArray();
        DarkThemeBox.ItemsSource = themes.Where(theme => theme.Mode == "dark").ToArray();
        LightThemeBox.SelectedValuePath = "Id";
        DarkThemeBox.SelectedValuePath = "Id";
        LightThemeBox.SelectedValue = appearance.LightTheme;
        DarkThemeBox.SelectedValue = appearance.DarkTheme;
    }

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemperatureLabel is not null) UpdateTemperatureLabel();
    }

    private void UpdateTemperatureLabel()
    {
        var isLmStudio = GetTag(ProviderBox, "ollama") == "lmstudio";
        TemperatureLabel.Text = isLmStudio
            ? "Temperature · optional (0–1; LM Studio)"
            : "Temperature · optional (0–2; Ollama)";
    }

    private async void RefreshModels_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = BuildInlineSettings(validate: true);
            var credentials = BuildCredentials();
            ModelDiscoveryStatus.Text = "Connecting and discovering models…";
            ModelDiscoveryStatus.Foreground = (System.Windows.Media.Brush)FindResource("RunnerMuted");
            var result = await _modelClient.DiscoverModelsAsync(settings, credentials);
            ModelBox.ItemsSource = result.Models;
            if (result.Models.Count > 0 && string.IsNullOrWhiteSpace(ModelBox.Text))
                ModelBox.Text = result.Models[0];
            ModelDiscoveryStatus.Text = result.Message;
            ModelDiscoveryStatus.Foreground = (System.Windows.Media.Brush)FindResource(result.Succeeded ? "RunnerSuccess" : "RunnerDanger");
        }
        catch (Exception exception)
        {
            ModelDiscoveryStatus.Text = exception.Message;
            ModelDiscoveryStatus.Foreground = (System.Windows.Media.Brush)FindResource("RunnerDanger");
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var hotkey = HotkeyBox.Text.Trim();
            if (hotkey.Length is < 3 or > 64 || !hotkey.Contains("Ctrl", StringComparison.OrdinalIgnoreCase)
                && !hotkey.Contains("Alt", StringComparison.OrdinalIgnoreCase)
                && !hotkey.Contains("Shift", StringComparison.OrdinalIgnoreCase)
                && !hotkey.Contains("Win", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Enter a valid global shortcut with a modifier, such as Ctrl+Alt+Space.");

            var enabledPlugins = new List<string>();
            if (ApplicationsPluginCheck.IsChecked == true) enabledPlugins.Add("applications");
            if (FilesPluginCheck.IsChecked == true) enabledPlugins.Add("files");
            if (UrlsPluginCheck.IsChecked == true) enabledPlugins.Add("urls");
            if (CalculatorPluginCheck.IsChecked == true) enabledPlugins.Add("calculator");
            if (RunMindPluginCheck.IsChecked == true) enabledPlugins.Add("runmind");
            if (enabledPlugins.Count == 0)
                throw new InvalidOperationException("Enable at least one launcher plugin.");

            Settings = new RunnerSettings
            {
                Hotkey = hotkey,
                StartWithWindows = StartWithWindowsCheckBox.IsChecked == true,
                EnabledPlugins = enabledPlugins,
            };
            InlineSettings = BuildInlineSettings(validate: true);
            Credentials = BuildCredentials();

            var mode = GetTag(AppearanceModeBox, "system");
            var lightTheme = (LightThemeBox.SelectedItem as ThemeDefinition)?.Id ?? "classic-light";
            var darkTheme = (DarkThemeBox.SelectedItem as ThemeDefinition)?.Id ?? "warm-graphite";
            if (!RunnerThemeManager.SaveAppearance(mode, lightTheme, darkTheme))
                throw new InvalidOperationException("The selected appearance preferences could not be saved.");

            DialogResult = true;
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "RunMind Runner Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private RunnerInlineSettings BuildInlineSettings(bool validate)
    {
        var provider = GetTag(ProviderBox, "ollama");
        var model = ModelBox.Text.Trim();
        if (model.Length > 512) throw new InvalidOperationException("Model identifiers must not exceed 512 characters.");
        if (SystemPromptBox.Text.Length > 12_000) throw new InvalidOperationException("Inline system instructions must not exceed 12,000 characters.");

        var result = RunnerInlineSettingsStore.Normalize(new RunnerInlineSettings
        {
            Enabled = InlineEnabledCheck.IsChecked == true,
            Provider = provider,
            OllamaEndpoint = OllamaEndpointBox.Text.Trim(),
            LMStudioBaseUrl = LMStudioBaseUrlBox.Text.Trim(),
            Model = model,
            Thinking = GetTag(ThinkingBox, "auto"),
            KeepAliveSeconds = ParseInt(KeepAliveBox.Text, 300, "Keep model loaded", 0, 86400),
            RequestTimeoutSeconds = ParseInt(RequestTimeoutBox.Text, 600, "Request timeout", 30, 3600),
            ModelDiscoveryTimeoutSeconds = ParseInt(DiscoveryTimeoutBox.Text, 8, "Model discovery timeout", 1, 300),
            InlineCpuThreads = ParseInt(CpuThreadsBox.Text, 0, "Ollama CPU threads", 0, 128),
            SystemPrompt = SystemPromptBox.Text,
            Temperature = ParseOptionalDouble(TemperatureBox.Text, "Temperature", 0, provider == "lmstudio" ? 1 : 2),
            TopP = ParseOptionalDouble(TopPBox.Text, "Top P", 0, 1),
            MaxOutputTokens = ParseOptionalInt(MaxOutputTokensBox.Text, "Maximum output tokens", 1, 1_000_000),
            Seed = ParseOptionalInt(SeedBox.Text, "Seed", 0, int.MaxValue),
            Nemotron35BoltE4BConversationalMode = NemotronModeCheck.IsChecked == true,
        });

        if (validate)
        {
            if (provider == "ollama" && (!Uri.TryCreate(result.OllamaEndpoint, UriKind.Absolute, out var ollama)
                || ollama.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(ollama.Host)
                || !string.IsNullOrEmpty(ollama.UserInfo) || !string.IsNullOrEmpty(ollama.Query) || !string.IsNullOrEmpty(ollama.Fragment)
                || !ollama.AbsolutePath.TrimEnd('/').EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Ollama endpoint must end in /api/generate, for example http://localhost:11434/api/generate.");
            if (provider == "lmstudio" && (!Uri.TryCreate(result.LMStudioBaseUrl, UriKind.Absolute, out var lmStudio)
                || lmStudio.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(lmStudio.Host)
                || !string.IsNullOrEmpty(lmStudio.UserInfo) || !string.IsNullOrEmpty(lmStudio.Query) || !string.IsNullOrEmpty(lmStudio.Fragment)))
                throw new InvalidOperationException("LM Studio base URL must be an absolute HTTP(S) URL, for example http://localhost:1234/v1.");
        }
        return result;
    }

    private RunnerInlineCredentials BuildCredentials()
    {
        var credentials = new RunnerInlineCredentials(
            ClearOllamaCredentialsCheck.IsChecked == true ? string.Empty : (OllamaApiKeyBox.Password.Length > 0 ? OllamaApiKeyBox.Password : _currentCredentials.OllamaApiKey),
            ClearOllamaCredentialsCheck.IsChecked == true ? string.Empty : (OllamaAuthHeaderBox.Password.Length > 0 ? OllamaAuthHeaderBox.Password : _currentCredentials.OllamaAuthHeader),
            ClearLMStudioCredentialsCheck.IsChecked == true ? string.Empty : (LMStudioApiKeyBox.Password.Length > 0 ? LMStudioApiKeyBox.Password : _currentCredentials.LMStudioApiKey),
            ClearLMStudioCredentialsCheck.IsChecked == true ? string.Empty : (LMStudioAuthHeaderBox.Password.Length > 0 ? LMStudioAuthHeaderBox.Password : _currentCredentials.LMStudioAuthHeader));
        if (credentials.OllamaApiKey.Length > 4096 || credentials.LMStudioApiKey.Length > 4096
            || credentials.OllamaAuthHeader.Length > 8192 || credentials.LMStudioAuthHeader.Length > 8192)
            throw new InvalidOperationException("API keys are limited to 4,096 characters and custom authentication headers to 8,192 characters.");
        return credentials;
    }

    private static int ParseInt(string text, int fallback, string label, int minimum, int maximum)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
            throw new InvalidOperationException($"{label} must be an integer from {minimum.ToString("N0", CultureInfo.InvariantCulture)} to {maximum.ToString("N0", CultureInfo.InvariantCulture)}.");
        return value;
    }

    private static int? ParseOptionalInt(string text, string label, int minimum, int maximum)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return ParseInt(text, minimum, label, minimum, maximum);
    }

    private static double? ParseOptionalDouble(string text, string label, double minimum, double maximum)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value) || value < minimum || value > maximum)
            throw new InvalidOperationException($"{label} must be a number from {minimum.ToString(CultureInfo.InvariantCulture)} to {maximum.ToString(CultureInfo.InvariantCulture)}.");
        return value;
    }

    private static void SelectTag(ComboBox combo, string tag)
    {
        combo.SelectedItem = combo.Items.Cast<object>().OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetTag(ComboBox combo, string fallback)
        => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
