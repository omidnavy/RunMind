using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace RunMind.Runner;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\RunMind.Runner.SingleInstance";
    private Mutex? _instanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        SessionEnding += (_, _) => window.ShutdownRunner();
        window.Show();
        if (e.Args.Any(argument => string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase)))
            window.HideAfterStartup();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            try { _instanceMutex?.ReleaseMutex(); }
            catch (ApplicationException) { }
        }

        _instanceMutex?.Dispose();
        _instanceMutex = null;
        base.OnExit(e);
    }

    private static void SignalExistingInstance()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var handle = FindWindow(null, "RunMind Runner");
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, 9);
                SetForegroundWindow(handle);
                return;
            }

            Thread.Sleep(100);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
