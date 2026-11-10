using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace RunMind.CommandPalette;

internal static class Program
{
    [MTAThread]
    private static void Main(string[] args)
    {
        // The package contains Microsoft.Windows.SDK.NET.dll, but it may not be
        // represented as a runtime asset in the generated deps.json. Register a
        // package-local resolver before any Command Palette WinRT types are used.
        RunMindWindowsSdkAssemblyResolver.Initialize();

        try
        {
            if (CommandPaletteChatHost.TryRunChatProcess(args))
                return;
        }
        catch (Exception exception)
        {
            RunMindCommandPaletteDiagnostics.Write("Command Palette chat-host startup failed.", exception);
            return;
        }

        RunMindCommandPaletteDiagnostics.Write(
            $"Process entered Main. Args=[{string.Join(" ", args)}]; " +
            $"Executable={Environment.ProcessPath ?? "<unknown>"}; " +
            $"BaseDirectory={AppContext.BaseDirectory}; " +
            $"CurrentDirectory={Environment.CurrentDirectory}; " +
            $"Architecture={RuntimeInformation.ProcessArchitecture}; " +
            $"Framework={RuntimeInformation.FrameworkDescription}.");

        if (args.Length == 0 || !string.Equals(args[0], "-RegisterProcessAsComServer", StringComparison.Ordinal))
        {
            RunMindCommandPaletteDiagnostics.Write(
                "No COM-server activation argument was supplied. The packaged Start-menu entry is not a standalone UI; exiting normally.");
            return;
        }

        // Keep WinRT extension types in a separate method. This ensures their
        // metadata is not resolved until after the package-local resolver above
        // has been registered.
        StartComServer();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartComServer()
    {
        ManualResetEvent? extensionDisposedEvent = null;
        RunMindExtension? extension = null;
        ComServer? server = null;

        try
        {
            // COM activation does not guarantee the process working directory. The
            // Command Palette toolkit resolves relative package assets from it.
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            RunMindCommandPaletteDiagnostics.Write(
                $"COM activation accepted. Working directory set to {Environment.CurrentDirectory}.");

            // Initialize the CsWinRT wrappers before registering any WinRT interfaces.
            WinRT.ComWrappersSupport.InitializeComWrappers();
            RunMindCommandPaletteDiagnostics.Write("CsWinRT ComWrappers initialized.");

            extensionDisposedEvent = new ManualResetEvent(false);
            RunMindCommandPaletteDiagnostics.Write("Created extension-disposal event.");

            extension = new RunMindExtension(extensionDisposedEvent);
            RunMindCommandPaletteDiagnostics.Write("Constructed RunMindExtension and its command provider.");

            server = new ComServer();
            RunMindCommandPaletteDiagnostics.Write("Created Shmuelie.WinRTServer.ComServer.");

            server.RegisterClass<RunMindExtension, IExtension>(() => extension!);
            RunMindCommandPaletteDiagnostics.Write("Registered RunMindExtension for IExtension.");

            RunMindCommandPaletteDiagnostics.Write("Calling ComServer.Start().");
            server.Start();
            RunMindCommandPaletteDiagnostics.Write("ComServer.Start() completed; waiting for Command Palette to dispose the extension.");

            extensionDisposedEvent.WaitOne();
            RunMindCommandPaletteDiagnostics.Write("Extension-disposal event was signaled by Command Palette.");
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            RunMindCommandPaletteDiagnostics.Write("Unhandled COM-server startup/runtime exception.", exception);
        }
        finally
        {
            if (server is not null)
            {
                try
                {
                    RunMindCommandPaletteDiagnostics.Write("Stopping COM server.");
                    server.Stop();
                    RunMindCommandPaletteDiagnostics.Write("COM server stopped.");
                }
                catch (Exception exception)
                {
                    RunMindCommandPaletteDiagnostics.Write("ComServer.Stop() failed.", exception);
                }

                try
                {
                    server.UnsafeDispose();
                    RunMindCommandPaletteDiagnostics.Write("COM server resources disposed.");
                }
                catch (Exception exception)
                {
                    RunMindCommandPaletteDiagnostics.Write("ComServer.UnsafeDispose() failed.", exception);
                }
            }

            if (extension is not null)
            {
                try
                {
                    extension.Dispose();
                    RunMindCommandPaletteDiagnostics.Write("RunMindExtension disposed.");
                }
                catch (Exception exception)
                {
                    RunMindCommandPaletteDiagnostics.Write("RunMindExtension.Dispose() failed.", exception);
                }
            }

            extensionDisposedEvent?.Dispose();
            RunMindCommandPaletteDiagnostics.Write("COM-server process is exiting.");
        }
    }
}

internal static class RunMindWindowsSdkAssemblyResolver
{
    private const string AssemblySimpleName = "Microsoft.Windows.SDK.NET";
    private static int _initialized;

    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        AssemblyLoadContext.Default.Resolving += ResolvePackageAssembly;
        RunMindCommandPaletteDiagnostics.Write(
            $"Registered package-local assembly resolver for {AssemblySimpleName}.dll. " +
            $"Package base directory: {AppContext.BaseDirectory}");
    }

    private static Assembly? ResolvePackageAssembly(AssemblyLoadContext context, AssemblyName requestedName)
    {
        if (!string.Equals(requestedName.Name, AssemblySimpleName, StringComparison.OrdinalIgnoreCase))
            return null;

        var assemblyPath = Path.Combine(AppContext.BaseDirectory, AssemblySimpleName + ".dll");
        if (!File.Exists(assemblyPath))
        {
            RunMindCommandPaletteDiagnostics.Write(
                $"Requested {requestedName.FullName}, but the package-local assembly was not found at '{assemblyPath}'.");
            return null;
        }

        try
        {
            var packagedName = AssemblyName.GetAssemblyName(assemblyPath);
            RunMindCommandPaletteDiagnostics.Write(
                $"Resolving {requestedName.FullName} from '{assemblyPath}'. " +
                $"Packaged assembly identity: {packagedName.FullName}.");

            return context.LoadFromAssemblyPath(assemblyPath);
        }
        catch (Exception exception)
        {
            RunMindCommandPaletteDiagnostics.Write(
                $"Failed to load package-local {AssemblySimpleName}.dll from '{assemblyPath}'.", exception);
            throw;
        }
    }
}


internal static class RunMindCommandPaletteDiagnostics
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RunMind",
        "CommandPalette",
        "Logs",
        "com-server.log");

    internal static void Write(string message, Exception? exception = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var entry = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" [PID ")
                .Append(Environment.ProcessId)
                .Append("] ")
                .AppendLine(message);

            if (exception is not null)
                entry.AppendLine(exception.ToString());

            File.AppendAllText(LogPath, entry.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Diagnostics must never prevent the extension COM server from starting.
        }
    }
}
