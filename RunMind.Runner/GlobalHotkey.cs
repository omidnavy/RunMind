using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;

namespace RunMind.Runner;

internal sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x524D;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly HwndSource _source;
    private bool _registered;

    public event Action? Pressed;

    public GlobalHotkey(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("The Runner window handle is not available.");
        _source.AddHook(WindowProcedure);
    }

    public bool TryRegister(string description, out string error)
    {
        Unregister();
        if (!TryParse(description, out var modifiers, out var virtualKey, out error))
            return false;

        _registered = RegisterHotKey(_source.Handle, HotkeyId, modifiers | ModNoRepeat, (uint)virtualKey);
        if (!_registered)
        {
            error = "Windows could not register that shortcut. Another application may already be using it.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WindowProcedure);
    }

    private void Unregister()
    {
        if (!_registered)
            return;

        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static bool TryParse(string description, out uint modifiers, out int virtualKey, out string error)
    {
        modifiers = 0;
        virtualKey = 0;
        error = "Use a shortcut such as Ctrl+Alt+Space, Ctrl+Shift+R or Win+Shift+R.";

        var parts = description.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        var keyToken = parts[^1];
        var seenModifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var modifier in parts[..^1])
        {
            if (!seenModifiers.Add(modifier))
                return false;

            var modifierFlag = modifier.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModControl,
                "alt" => ModAlt,
                "shift" => ModShift,
                "win" or "windows" => ModWin,
                _ => 0,
            };
            if (modifierFlag == 0)
                return false;
            modifiers |= modifierFlag;
        }

        if (!Enum.TryParse<Key>(keyToken, ignoreCase: true, out var key) || key is Key.None or Key.System)
            return false;

        var candidate = KeyInterop.VirtualKeyFromKey(key);
        if (candidate <= 0 || candidate > 0xFF)
            return false;

        var canonical = key.ToString();
        if (string.Equals(canonical, "LWin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(canonical, "RWin", StringComparison.OrdinalIgnoreCase))
            return false;

        var normalizedModifiers = modifiers;
        if ((normalizedModifiers == ModAlt && key == Key.F4)
            || (normalizedModifiers == ModWin && key == Key.L)
            || (normalizedModifiers == (ModControl | ModShift) && key == Key.Escape)
            || (normalizedModifiers == (ModControl | ModAlt) && key == Key.Delete))
            return false;

        virtualKey = candidate;
        error = string.Empty;
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
