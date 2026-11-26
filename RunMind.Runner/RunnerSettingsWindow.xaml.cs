using System.Windows;

namespace RunMind.Runner;

internal partial class RunnerSettingsWindow : Window
{
    public RunnerSettings Settings { get; private set; }

    public RunnerSettingsWindow(RunnerSettings currentSettings)
    {
        InitializeComponent();
        Settings = new RunnerSettings
        {
            Hotkey = currentSettings.Hotkey,
            StartWithWindows = currentSettings.StartWithWindows,
        };
        HotkeyBox.Text = Settings.Hotkey;
        StartWithWindowsCheckBox.IsChecked = Settings.StartWithWindows;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var hotkey = HotkeyBox.Text.Trim();
        if (hotkey.Length is < 3 or > 64)
        {
            MessageBox.Show(this, "Enter a valid shortcut such as Ctrl+Alt+Space.",
                "RunMind Runner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Settings = new RunnerSettings
        {
            Hotkey = hotkey,
            StartWithWindows = StartWithWindowsCheckBox.IsChecked == true,
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
