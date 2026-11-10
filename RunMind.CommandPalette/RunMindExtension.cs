using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;

namespace RunMind.CommandPalette;

[ComVisible(true)]
[Guid("BFB8D8C5-1485-4E27-9FD8-67D3EAB3E21F")]
[ComDefaultInterface(typeof(IExtension))]
public sealed partial class RunMindExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;
    private readonly RunMindCommandsProvider _provider;
    private int _disposed;

    public RunMindExtension(ManualResetEvent extensionDisposedEvent)
    {
        _extensionDisposedEvent = extensionDisposedEvent;
        RunMindCommandPaletteDiagnostics.Write("RunMindExtension constructor entered.");
        _provider = new RunMindCommandsProvider();
        RunMindCommandPaletteDiagnostics.Write("RunMindCommandsProvider constructed.");
    }

    public object? GetProvider(ProviderType providerType)
    {
        RunMindCommandPaletteDiagnostics.Write($"GetProvider requested: {providerType}.");
        return providerType switch
        {
            ProviderType.Commands => _provider,
            _ => null,
        };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        RunMindCommandPaletteDiagnostics.Write("RunMindExtension.Dispose() signaled the disposal event.");
        _extensionDisposedEvent.Set();
    }
}
