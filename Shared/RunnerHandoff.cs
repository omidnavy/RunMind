#nullable enable

using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows.Threading;

namespace RunMind.Shared;

/// <summary>Bounded context transferred from Runner's inline answer into the real Standalone chat.</summary>
internal sealed class RunnerHandoffPayload
{
    public string? Prompt { get; set; }
    public string? Response { get; set; }
    public bool IsError { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

internal static class RunnerHandoffStore
{
    public const int MaximumPayloadBytes = 256 * 1024;
    private const int MaximumPromptCharacters = 12_000;
    private const int MaximumResponseCharacters = 180_000;

    public static string HandoffDirectory
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                throw new InvalidOperationException("Windows did not provide a Local AppData path.");
            return Path.GetFullPath(Path.Combine(localAppData, "RunMind", "Runner", "Handoffs"));
        }
    }

    public static string Write(RunnerHandoffPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Validate(payload);
        Directory.CreateDirectory(HandoffDirectory);
        EnsureNotReparsePoint(HandoffDirectory);

        var path = Path.Combine(HandoffDirectory, Guid.NewGuid().ToString("N") + ".json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        if (bytes.Length is < 2 or > MaximumPayloadBytes)
            throw new InvalidDataException("The Runner handoff exceeds its allowed size.");

        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                   4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        return path;
    }

    public static bool TryConsume(string? path, out RunnerHandoffPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(path)) return false;

        string? fullPath = null;
        try
        {
            fullPath = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(fullPath), HandoffDirectory, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
                return false;

            EnsureNotReparsePoint(HandoffDirectory);
            EnsureNotReparsePoint(fullPath);
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length is < 2 or > MaximumPayloadBytes) return false;

            var bytes = File.ReadAllBytes(fullPath);
            if (bytes.Length is < 2 or > MaximumPayloadBytes) return false;
            var candidate = JsonSerializer.Deserialize<RunnerHandoffPayload>(bytes);
            if (candidate is null) return false;
            Validate(candidate);
            if (candidate.CreatedUtc > DateTimeOffset.UtcNow.AddMinutes(2)
                || candidate.CreatedUtc < DateTimeOffset.UtcNow.AddMinutes(-10))
                return false;

            payload = candidate;
            return true;
        }
        catch
        {
            payload = null;
            return false;
        }
        finally
        {
            if (fullPath is not null) TryDelete(fullPath);
        }
    }

    public static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!string.Equals(Path.GetDirectoryName(fullPath), HandoffDirectory, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
        catch { }
    }

    internal static void Validate(RunnerHandoffPayload payload)
    {
        if ((payload.Prompt?.Length ?? 0) > MaximumPromptCharacters)
            throw new InvalidDataException("The Runner handoff prompt exceeds 12,000 characters.");
        if ((payload.Response?.Length ?? 0) > MaximumResponseCharacters)
            throw new InvalidDataException("The Runner handoff response exceeds 180,000 characters.");
        if (payload.Prompt is null && payload.Response is null)
            payload.Prompt = string.Empty;
    }

    private static void EnsureNotReparsePoint(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Runner handoff paths cannot be symbolic links or reparse points.");
    }
}


internal static class StandalonePathStore
{
    private const string ExecutableName = "RunMind.exe";

    public static string CacheFilePath
    {
        get
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                throw new InvalidOperationException("Windows did not provide a Local AppData path.");
            return Path.Combine(localAppData, "RunMind", "Standalone", "standalone-path.txt");
        }
    }

    public static void RememberCurrentExecutable()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)
                || !string.Equals(Path.GetFileName(executable), ExecutableName, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(executable))
                return;

            var path = Path.GetFullPath(CacheFilePath);
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            EnsureNotReparsePoint(directory);
            EnsureNotReparsePoint(path);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                           1024, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
                {
                    writer.Write(Path.GetFullPath(executable));
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally { TryDelete(temporary); }
        }
        catch { /* Path caching is a convenience; the app must still start if it fails. */ }
    }

    public static string? ReadCachedExecutable()
    {
        try
        {
            var path = CacheFilePath;
            EnsureNotReparsePoint(Path.GetDirectoryName(path)!);
            EnsureNotReparsePoint(path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is < 1 or > 4096) return null;
            var executable = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(executable)
                || !Path.IsPathFullyQualified(executable)
                || !string.Equals(Path.GetFileName(executable), ExecutableName, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(executable))
                return null;
            return Path.GetFullPath(executable);
        }
        catch { return null; }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Standalone path cache cannot use a reparse point.");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

/// <summary>
/// Current-user-only IPC between Runner and an already-running Standalone process.
/// The payload is deliberately limited to prompt/response context; no tool invocation is transferred.
/// </summary>
internal static class RunnerHandoffPipe
{
    private const string PipeName = "RunMind.Standalone.Handoff.v1";
    private const int MaximumPayloadBytes = RunnerHandoffStore.MaximumPayloadBytes;

    public static async Task<bool> TrySendAsync(RunnerHandoffPayload payload, int timeoutMilliseconds,
        CancellationToken cancellationToken = default)
    {
        try
        {
            RunnerHandoffStore.Validate(payload);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            if (bytes.Length is < 2 or > MaximumPayloadBytes) return false;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Math.Clamp(timeoutMilliseconds, 100, 5000));
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(Math.Clamp(timeoutMilliseconds, 100, 5000), timeout.Token).ConfigureAwait(false);

            var length = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            await client.WriteAsync(length.AsMemory(), timeout.Token).ConfigureAwait(false);
            await client.WriteAsync(bytes.AsMemory(), timeout.Token).ConfigureAwait(false);
            await client.FlushAsync(timeout.Token).ConfigureAwait(false);

            var acknowledgement = new byte[1];
            await ReadExactlyAsync(client, acknowledgement, timeout.Token).ConfigureAwait(false);
            return acknowledgement[0] == 1;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> TrySendWithRetryAsync(RunnerHandoffPayload payload, int totalTimeoutMilliseconds,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Clamp(totalTimeoutMilliseconds, 250, 8000));
        while (!timeout.IsCancellationRequested)
        {
            if (await TrySendAsync(payload, 350, timeout.Token).ConfigureAwait(false)) return true;
            try { await Task.Delay(125, timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { break; }
        }
        return false;
    }

    public static async Task RunServerAsync(Dispatcher dispatcher, Action<RunnerHandoffPayload> onPayload,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance,
                    4096, 4096);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Another Standalone process owns the first pipe instance. Retry if it exits.
                try { await Task.Delay(750, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            await using var ownedServer = server.ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    var payload = await ReadPayloadAsync(server, cancellationToken).ConfigureAwait(false);
                    var accepted = false;
                    if (payload is not null)
                    {
                        try
                        {
                            await dispatcher.InvokeAsync(() => onPayload(payload),
                                DispatcherPriority.Normal, cancellationToken).Task.ConfigureAwait(false);
                            accepted = true;
                        }
                        catch { }
                    }

                    await server.WriteAsync(new[] { accepted ? (byte)1 : (byte)0 }.AsMemory(), cancellationToken).ConfigureAwait(false);
                    await server.FlushAsync(cancellationToken).ConfigureAwait(false);
                    if (server.IsConnected) server.Disconnect();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                catch (IOException)
                {
                    if (server.IsConnected) server.Disconnect();
                }
                catch (InvalidDataException)
                {
                    if (server.IsConnected) server.Disconnect();
                }
                catch (JsonException)
                {
                    if (server.IsConnected) server.Disconnect();
                }
            }
        }
    }

    private static async Task<RunnerHandoffPayload?> ReadPayloadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, lengthBytes, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length is < 2 or > MaximumPayloadBytes) throw new InvalidDataException("Invalid Runner handoff size.");
        var bytes = new byte[length];
        await ReadExactlyAsync(stream, bytes, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Deserialize<RunnerHandoffPayload>(bytes);
        if (payload is null) return null;
        RunnerHandoffStore.Validate(payload);
        if (payload.CreatedUtc > DateTimeOffset.UtcNow.AddMinutes(2)
            || payload.CreatedUtc < DateTimeOffset.UtcNow.AddMinutes(-10)) return null;
        return payload;
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException("The Runner handoff pipe closed unexpectedly.");
            offset += count;
        }
    }
}
