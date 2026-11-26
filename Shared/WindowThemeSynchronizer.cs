#nullable enable

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Interop;

namespace RunMind.Shared;

/// <summary>Synchronizes a RunMind WPF window's native caption with its saved appearance mode.</summary>
internal sealed class WindowThemeSynchronizer : IDisposable
{
    private const int WmSettingChange = 0x001A;
    private const int WmThemeChanged = 0x031A;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    private readonly Window _window;
    private HwndSource? _source;
    private AppearancePreferences _appearance;
    private bool _disposed;

    public WindowThemeSynchronizer(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _appearance = AppearancePreferenceStore.Load() ?? new AppearancePreferences("system", "classic-light", "warm-graphite");
        _window.SourceInitialized += OnSourceInitialized;
        _window.Closed += OnWindowClosed;
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd != IntPtr.Zero) Attach(hwnd);
    }

    public void RefreshAppearance()
    {
        if (_disposed) return;
        _appearance = AppearancePreferenceStore.Load() ?? new AppearancePreferences("system", "classic-light", "warm-graphite");
        Apply();
    }

    public bool TryHandleWebMessage(string source, string messageJson)
    {
        if (_disposed || !AppearancePreferenceStore.TryHandleMessage(source, messageJson, out var settings) || settings is null)
            return false;
        _appearance = settings;
        Apply();
        return true;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Attach(new WindowInteropHelper(_window).Handle);
    }

    private void Attach(IntPtr hwnd)
    {
        if (_disposed || hwnd == IntPtr.Zero || _source is not null) return;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(OnWindowMessage);
        Apply();
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((message is WmSettingChange or WmThemeChanged) && _appearance.Mode == "system")
            Apply();
        return IntPtr.Zero;
    }

    private void Apply()
    {
        if (_disposed) return;
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var darkMode = _appearance.Mode switch
        {
            "dark" => 1,
            "light" => 0,
            _ => IsSystemDarkMode() ? 1 : 0,
        };
        var result = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        if (result != 0)
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref darkMode, sizeof(int));
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            const string personalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            var value = Registry.GetValue(personalizeKey, "AppsUseLightTheme", 1);
            return value is not null && Convert.ToInt32(value) == 0;
        }
        catch
        {
            return false;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Closed -= OnWindowClosed;
        _source?.RemoveHook(OnWindowMessage);
        _source = null;
    }
}
