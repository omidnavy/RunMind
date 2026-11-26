#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RunMind.Shared;

public sealed partial class AgentHostBridge
{
    private const int MaximumNodeDebugSessions = 8;
    private readonly ConcurrentDictionary<string, NodeDebuggerSession> _nodeDebugSessions = new(StringComparer.Ordinal);
    private static readonly HttpClient NodeInspectorHttpClient = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(4) };

    private async Task<object> NodeDebuggerLaunchAsync(JsonElement args, CancellationToken cancellationToken)
    {
        EnsureDebuggerSessionCapacity();
        var working = GetProcessWorkingDirectory(args);
        var scriptPath = ReadString(args, "script_path", 2048).Trim();
        if (scriptPath.Length == 0 || Path.IsPathRooted(scriptPath)) throw new InvalidDataException("script_path must be a workspace-relative JavaScript entry file.");
        if (!new[] { ".js", ".mjs", ".cjs" }.Contains(Path.GetExtension(scriptPath), StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Node debugger launch currently supports JavaScript entry files ending in .js, .mjs, or .cjs.");
        var fullScript = ResolveProcessWorkspacePath(scriptPath, "script_path", requireDirectory: false, ReadString(args, "contextId", 64));
        EnsureNoReparseAncestors(fullScript);
        if ((File.GetAttributes(fullScript) & FileAttributes.ReparsePoint) != 0) throw new FileNotFoundException("The JavaScript debug target is a reparse point.", fullScript);

        var node = FindExecutable("node.exe") ?? throw new FileNotFoundException("Node.js was not found on PATH.");
        var start = new ProcessStartInfo(node)
        {
            WorkingDirectory = working,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("--inspect-brk=127.0.0.1:0");
        start.ArgumentList.Add(fullScript);
        AddDebuggerArguments(start, args);

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start()) { process.Dispose(); throw new InvalidOperationException("Node.js could not be started."); }
        var session = new NodeDebuggerSession(process, startedByRunMind: true);
        try
        {
            await session.ConnectFromInspectorOutputAsync(cancellationToken).ConfigureAwait(false);
            await session.InitializeAsync(cancellationToken, waitForInitialPause: true).ConfigureAwait(false);
            var id = Guid.NewGuid().ToString("N");
            session.Id = id;
            if (!_nodeDebugSessions.TryAdd(id, session)) throw new InvalidOperationException("Could not register the Node.js debug session.");
            return session.GetSummary();
        }
        catch
        {
            await session.StopAsync(terminateProcess: true).ConfigureAwait(false);
            session.Dispose();
            throw;
        }
    }

    private async Task<object> NodeDebuggerAttachAsync(JsonElement args, CancellationToken cancellationToken)
    {
        EnsureDebuggerSessionCapacity();
        _ = GetProcessWorkingDirectory(args); // Bind attachment to the workspace context selected in this chat.
        if (!args.TryGetProperty("port", out var portValue) || !portValue.TryGetInt32(out var port) || port is < 1 or > 65535)
            throw new InvalidDataException("Provide a valid local Node Inspector TCP port (1–65535).");

        using var response = await NodeInspectorHttpClient.GetAsync($"http://127.0.0.1:{port}/json/list", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Node Inspector returned HTTP {(int)response.StatusCode}.");
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (payload.Length > 256 * 1024) throw new InvalidDataException("Node Inspector discovery response exceeded the safety limit.");
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Node Inspector returned an unexpected target list.");
        string? endpoint = null;
        foreach (var target in document.RootElement.EnumerateArray())
        {
            if (!target.TryGetProperty("type", out var typeValue) || !string.Equals(typeValue.GetString(), "node", StringComparison.OrdinalIgnoreCase)) continue;
            if (!target.TryGetProperty("webSocketDebuggerUrl", out var urlValue) || urlValue.ValueKind != JsonValueKind.String) continue;
            var candidate = urlValue.GetString();
            if (IsAllowedInspectorEndpoint(candidate, port)) { endpoint = candidate; break; }
        }
        if (endpoint is null) throw new InvalidOperationException("No Node.js Inspector target was found on that loopback port.");

        var session = new NodeDebuggerSession(process: null, startedByRunMind: false);
        try
        {
            await session.ConnectAsync(new Uri(endpoint), cancellationToken).ConfigureAwait(false);
            await session.InitializeAsync(cancellationToken, waitForInitialPause: false).ConfigureAwait(false);
            var id = Guid.NewGuid().ToString("N");
            session.Id = id;
            if (!_nodeDebugSessions.TryAdd(id, session)) throw new InvalidOperationException("Could not register the attached Node.js debug session.");
            return session.GetSummary();
        }
        catch
        {
            await session.StopAsync(terminateProcess: false).ConfigureAwait(false);
            session.Dispose();
            throw;
        }
    }

    private static void AddDebuggerArguments(ProcessStartInfo start, JsonElement args)
    {
        if (!args.TryGetProperty("arguments", out var values) || values.ValueKind != JsonValueKind.Array) return;
        if (values.GetArrayLength() > 50) throw new InvalidDataException("A maximum of 50 Node.js script arguments is allowed.");
        var total = 0;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Every Node.js script argument must be a string.");
            var argument = value.GetString() ?? string.Empty;
            total += argument.Length;
            if (argument.Length > 4096 || total > 16 * 1024) throw new InvalidDataException("Node.js script arguments exceed the size limit.");
            start.ArgumentList.Add(argument);
        }
    }

    private void EnsureDebuggerSessionCapacity()
    {
        foreach (var pair in _nodeDebugSessions.ToArray())
        {
            if (!pair.Value.CanPrune) continue;
            if (_nodeDebugSessions.TryRemove(pair.Key, out var closedSession)) closedSession.Dispose();
        }
        if (_nodeDebugSessions.Count >= MaximumNodeDebugSessions) throw new InvalidOperationException($"RunMind supports at most {MaximumNodeDebugSessions} concurrent Node.js debugger sessions per host.");
    }

    private static bool IsAllowedInspectorEndpoint(string? value, int expectedPort)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "ws" || uri.Port != expectedPort || string.IsNullOrEmpty(uri.AbsolutePath)) return false;
        var host = uri.Host.Trim('[', ']');
        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }

    private NodeDebuggerSession GetNodeDebuggerSession(JsonElement args)
    {
        var id = ReadString(args, "session_id", 80);
        if (id.Length == 0 || !_nodeDebugSessions.TryGetValue(id, out var session))
            throw new InvalidOperationException("The requested Node.js debug session does not exist or has already been removed.");
        return session;
    }

    private object NodeDebuggerListSessions()
    {
        return new { sessions = _nodeDebugSessions.Values.Select(session => session.GetSummary()).ToArray(), maximumSessions = MaximumNodeDebugSessions };
    }

    private object NodeDebuggerReadEvents(JsonElement args)
    {
        var session = GetNodeDebuggerSession(args);
        var maximum = args.TryGetProperty("maximum", out var value) && value.TryGetInt32(out var requested) ? Math.Clamp(requested, 1, 100) : 50;
        return new { sessionId = session.Id, events = session.DrainEvents(maximum), stdout = session.ReadOutput("stdout"), stderr = session.ReadOutput("stderr"), state = session.State };
    }

    private async Task<object> NodeDebuggerCommandAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var session = GetNodeDebuggerSession(args);
        var action = ReadString(args, "action", 40).Trim().ToLowerInvariant();
        switch (action)
        {
            case "pause": return await session.SendCommandAsync("Debugger.pause", new JsonObject(), cancellationToken).ConfigureAwait(false);
            case "resume": return await session.ResumeAsync(cancellationToken).ConfigureAwait(false);
            case "step_over": return await session.SendCommandAsync("Debugger.stepOver", new JsonObject(), cancellationToken).ConfigureAwait(false);
            case "step_into": return await session.SendCommandAsync("Debugger.stepInto", new JsonObject(), cancellationToken).ConfigureAwait(false);
            case "step_out": return await session.SendCommandAsync("Debugger.stepOut", new JsonObject(), cancellationToken).ConfigureAwait(false);
            case "pause_on_exceptions":
            {
                var state = ReadString(args, "exception_state", 20).Trim().ToLowerInvariant();
                if (state is not ("none" or "uncaught" or "all")) throw new InvalidDataException("exception_state must be none, uncaught, or all.");
                return await session.SendCommandAsync("Debugger.setPauseOnExceptions", new JsonObject { ["state"] = state }, cancellationToken).ConfigureAwait(false);
            }
            case "set_breakpoint":
            {
                var path = ReadString(args, "path", 2048).Trim();
                if (path.Length == 0 || Path.IsPathRooted(path)) throw new InvalidDataException("Breakpoint path must be workspace-relative.");
                _ = GetProcessWorkingDirectory(args); // Validate the active workspace context.
                var root = Path.GetFullPath(RequireWorkspaceRoot(ReadString(args, "contextId", 64)));
                var fullPath = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
                var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath)) throw new InvalidDataException("Breakpoint target must be an existing file inside the selected workspace.");
                EnsureNoReparseAncestors(fullPath);
                var line = args.TryGetProperty("line", out var lineValue) && lineValue.TryGetInt32(out var requestedLine) ? requestedLine : 0;
                if (line < 1 || line > 1_000_000) throw new InvalidDataException("Breakpoint line must be between 1 and 1,000,000.");
                var column = args.TryGetProperty("column", out var columnValue) && columnValue.TryGetInt32(out var requestedColumn) ? Math.Clamp(requestedColumn, 1, 1_000_000) - 1 : 0;
                var parameters = new JsonObject { ["url"] = new Uri(fullPath).AbsoluteUri, ["lineNumber"] = line - 1, ["columnNumber"] = column };
                return await session.SendCommandAsync("Debugger.setBreakpointByUrl", parameters, cancellationToken).ConfigureAwait(false);
            }
            case "remove_breakpoint":
            {
                var id = ReadString(args, "breakpoint_id", 256);
                if (id.Length == 0) throw new InvalidDataException("breakpoint_id is required.");
                return await session.SendCommandAsync("Debugger.removeBreakpoint", new JsonObject { ["breakpointId"] = id }, cancellationToken).ConfigureAwait(false);
            }
            case "stack": return session.GetPausedCallFrames();
            case "inspect_scope":
            {
                var objectId = ReadString(args, "object_id", 512);
                if (objectId.Length == 0) throw new InvalidDataException("object_id from a paused call-frame scope is required.");
                var parameters = new JsonObject { ["objectId"] = objectId, ["ownProperties"] = true, ["accessorPropertiesOnly"] = false, ["generatePreview"] = true };
                return await session.SendCommandAsync("Runtime.getProperties", parameters, cancellationToken).ConfigureAwait(false);
            }
            case "evaluate":
            {
                var expression = ReadString(args, "expression", 8192);
                if (expression.Trim().Length == 0) throw new InvalidDataException("Provide an expression to evaluate.");
                var frameId = ReadString(args, "call_frame_id", 512);
                if (frameId.Length == 0) frameId = session.GetTopPausedCallFrameId();
                if (frameId.Length == 0) throw new InvalidOperationException("The target is not paused. Pause execution or hit a breakpoint before evaluating an expression.");
                var parameters = new JsonObject
                {
                    ["callFrameId"] = frameId,
                    ["expression"] = expression,
                    ["returnByValue"] = true,
                    ["awaitPromise"] = false,
                    ["throwOnSideEffect"] = true,
                    ["silent"] = false,
                    ["includeCommandLineAPI"] = false,
                };
                return await session.SendCommandAsync("Debugger.evaluateOnCallFrame", parameters, cancellationToken).ConfigureAwait(false);
            }
            case "scripts": return session.GetScripts();
            default: throw new InvalidDataException("Unsupported debugger action. Allowed: pause, resume, step_over, step_into, step_out, pause_on_exceptions, set_breakpoint, remove_breakpoint, stack, inspect_scope, evaluate, scripts.");
        }
    }

    private async Task<object> NodeRuntimeDiagnosticsAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var session = GetNodeDebuggerSession(args);
        var heap = await session.SendCommandAsync("Runtime.getHeapUsage", new JsonObject(), cancellationToken).ConfigureAwait(false);
        return new { sessionId = session.Id, state = session.State, paused = session.IsPaused, heap };
    }

    private async Task<object> NodeRuntimeCpuProfileAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var session = GetNodeDebuggerSession(args);
        var action = ReadString(args, "action", 16).Trim().ToLowerInvariant();
        if (action == "start")
            return await session.StartCpuProfileAsync(cancellationToken).ConfigureAwait(false);
        if (action != "stop") throw new InvalidDataException("CPU profile action must be start or stop.");

        var profile = await session.StopCpuProfileAsync(cancellationToken).ConfigureAwait(false);
        var json = profile.GetRawText();
        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > 32 * 1024 * 1024) throw new InvalidDataException("CPU profile exceeded the 32 MB artifact limit and was not saved.");
        var path = CreateRuntimeArtifactPath("cpu-profile", ".cpuprofile");
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return BuildCpuProfileSummary(session.Id, profile, path, bytes);
    }

    private async Task<object> NodeRuntimeHeapSnapshotAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var session = GetNodeDebuggerSession(args);
        var path = CreateRuntimeArtifactPath("heap-snapshot", ".heapsnapshot");
        await session.SendCommandAsync("HeapProfiler.enable", new JsonObject(), cancellationToken).ConfigureAwait(false);
        session.BeginHeapSnapshotCapture(path);
        try
        {
            await session.SendCommandAsync("HeapProfiler.takeHeapSnapshot", new JsonObject
            {
                ["reportProgress"] = false,
                ["captureNumericValue"] = true,
            }, cancellationToken, TimeSpan.FromMinutes(3)).ConfigureAwait(false);
            var result = session.CompleteHeapSnapshotCapture();
            if (result.Truncated) throw new InvalidDataException("Heap snapshot exceeded the 128 MB artifact limit. The incomplete snapshot was discarded.");
            if (result.Bytes == 0)
            {
                try { File.Delete(result.Path); } catch { }
                throw new InvalidDataException("Node Inspector completed the heap snapshot without returning any snapshot data.");
            }
            return new { sessionId = session.Id, artifactPath = result.Path, bytes = result.Bytes, format = "V8 heap snapshot", complete = true };
        }
        catch
        {
            session.AbortHeapSnapshotCapture();
            throw;
        }
    }

    private async Task<object> NodeRuntimeMemoryMonitorAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var session = GetNodeDebuggerSession(args);
        var action = ReadString(args, "action", 16).Trim().ToLowerInvariant();
        if (action == "start")
        {
            var interval = args.TryGetProperty("interval_ms", out var intervalValue) && intervalValue.TryGetInt32(out var requestedInterval) ? requestedInterval : 2000;
            var duration = args.TryGetProperty("duration_seconds", out var durationValue) && durationValue.TryGetInt32(out var requestedDuration) ? requestedDuration : 60;
            return await session.StartMemoryMonitorAsync(interval, duration, cancellationToken).ConfigureAwait(false);
        }
        if (action == "stop") return await session.StopMemoryMonitorAsync().ConfigureAwait(false);
        if (action == "read") return session.ReadMemoryMonitor();
        throw new InvalidDataException("Memory monitor action must be start, stop, or read.");
    }

    private static string CreateRuntimeArtifactPath(string prefix, string extension)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local)) throw new InvalidOperationException("Local AppData is unavailable; runtime artifacts cannot be saved.");
        var runMindDirectory = Path.Combine(local, "RunMind");
        Directory.CreateDirectory(runMindDirectory);
        if ((File.GetAttributes(runMindDirectory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("The RunMind local data directory cannot be a symbolic link or reparse point.");
        var directory = Path.Combine(runMindDirectory, "RuntimeArtifacts");
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("The RunMind runtime artifact directory cannot be a symbolic link or reparse point.");
        var files = new DirectoryInfo(directory).GetFiles().Where(file => file.Extension is ".cpuprofile" or ".heapsnapshot").OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        foreach (var old in files.Skip(19)) { try { old.Delete(); } catch { } }
        return Path.Combine(directory, $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{extension}");
    }

    private static object BuildCpuProfileSummary(string sessionId, JsonElement profile, string path, int bytes)
    {
        var nodes = profile.TryGetProperty("nodes", out var nodeArray) && nodeArray.ValueKind == JsonValueKind.Array ? nodeArray.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
        var samples = profile.TryGetProperty("samples", out var sampleArray) && sampleArray.ValueKind == JsonValueKind.Array ? sampleArray.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
        var deltas = profile.TryGetProperty("timeDeltas", out var deltaArray) && deltaArray.ValueKind == JsonValueKind.Array ? deltaArray.EnumerateArray().ToArray() : Array.Empty<JsonElement>();
        var nodeById = nodes.Where(node => node.TryGetProperty("id", out _)).ToDictionary(node => node.GetProperty("id").GetInt32(), node => node);
        var totals = new Dictionary<int, long>();
        for (var index = 0; index < samples.Length; index++)
        {
            if (!samples[index].TryGetInt32(out var id)) continue;
            var delta = index < deltas.Length && deltas[index].TryGetInt64(out var micros) ? micros : 0;
            totals[id] = totals.TryGetValue(id, out var current) ? current + delta : delta;
        }
        var hotspots = totals.OrderByDescending(pair => pair.Value).Take(20).Select(pair =>
        {
            var node = nodeById[pair.Key];
            var frame = node.TryGetProperty("callFrame", out var callFrame) ? callFrame : default;
            return new
            {
                function = frame.ValueKind == JsonValueKind.Object && frame.TryGetProperty("functionName", out var functionName) ? functionName.GetString() : "(anonymous)",
                url = frame.ValueKind == JsonValueKind.Object && frame.TryGetProperty("url", out var url) ? url.GetString() : string.Empty,
                line = frame.ValueKind == JsonValueKind.Object && frame.TryGetProperty("lineNumber", out var line) ? line.GetInt32() + 1 : 0,
                sampledMicroseconds = pair.Value,
                samples = samples.Count(sample => sample.TryGetInt32(out var id) && id == pair.Key),
            };
        }).ToArray();
        var start = profile.TryGetProperty("startTime", out var startTime) && startTime.TryGetInt64(out var startValue) ? startValue : 0;
        var end = profile.TryGetProperty("endTime", out var endTime) && endTime.TryGetInt64(out var endValue) ? endValue : 0;
        return new { sessionId, artifactPath = path, bytes, format = "V8 CPU profile (.cpuprofile)", nodeCount = nodes.Length, sampleCount = samples.Length, durationMilliseconds = Math.Max(0, end - start) / 1000.0, topSelfTime = hotspots };
    }

    private async Task<object> NodeDebuggerStopAsync(JsonElement args)
    {
        var session = GetNodeDebuggerSession(args);
        var terminate = args.TryGetProperty("terminate_process", out var value) && value.ValueKind == JsonValueKind.True;
        if (terminate && !session.StartedByRunMind) throw new InvalidOperationException("RunMind can only terminate a Node.js process that it started itself. Attached processes can only be detached.");
        if (!terminate && session.IsPaused) throw new InvalidOperationException("The Node.js process is paused. Resume it before detaching, or explicitly terminate it if RunMind launched it.");
        await session.StopAsync(terminate).ConfigureAwait(false);
        if ((!session.StartedByRunMind || terminate || session.ProcessExited == true) && _nodeDebugSessions.TryRemove(session.Id, out _)) session.Dispose();
        return new { sessionId = session.Id, detached = true, processTerminated = terminate, processContinues = session.StartedByRunMind && !terminate && session.ProcessExited == false, verified = terminate ? session.ProcessExited == true : true };
    }

    private void StopNodeDebuggerSessions()
    {
        foreach (var pair in _nodeDebugSessions.ToArray())
        {
            if (!_nodeDebugSessions.TryRemove(pair.Key, out var session)) continue;
            try { session.Dispose(); } catch { }
        }
    }
}

internal sealed class NodeDebuggerSession : IDisposable
{
    private const int MaximumEventCount = 256;
    private const int MaximumMessageBytes = 36 * 1024 * 1024;
    private const int MaximumOutputCharacters = 64 * 1024;
    private const int MaximumEventBytes = 64 * 1024;
    private readonly Process? _process;
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentQueue<JsonElement> _events = new();
    private readonly ConcurrentQueue<JsonElement> _scripts = new();
    private readonly object _outputLock = new();
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly TaskCompletionSource<string> _endpointReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _firstPause = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StringBuilder _stderrProbe = new();
    private readonly object _snapshotLock = new();
    private FileStream? _heapSnapshotStream;
    private string _heapSnapshotPath = string.Empty;
    private long _heapSnapshotBytes;
    private bool _heapSnapshotTruncated;
    private readonly object _profileLock = new();
    private bool _cpuProfileActive;
    private bool _cpuProfileStarting;
    private bool _cpuProfileStopping;
    private CancellationTokenSource? _memoryMonitorCancellation;
    private Task? _memoryMonitorTask;
    private readonly ConcurrentQueue<JsonElement> _memorySamples = new();
    private int _memorySampleCount;
    private int _nextCommandId;
    private int _eventCount;
    private int _scriptCount;
    private string _topCallFrameId = string.Empty;
    private JsonElement _lastPausedEvent;
    private volatile bool _paused;
    private volatile bool _closed;
    private int _stopStarted;
    private int _socketDisposeStarted;
    private int _disposeStarted;

    public string Id { get; set; } = string.Empty;
    public bool StartedByRunMind { get; }
    public bool IsClosed => _closed || (_process is not null && SafeHasExited(_process));
    public bool CanPrune => IsClosed && (_process is null || SafeHasExited(_process));
    public bool? ProcessExited => _process is null ? null : SafeHasExited(_process);
    public bool IsPaused => _paused;
    public string State => _closed && _process is not null && !SafeHasExited(_process) ? "detached" : IsClosed ? "closed" : _paused ? "paused" : "running";

    public NodeDebuggerSession(Process? process, bool startedByRunMind)
    {
        _process = process;
        StartedByRunMind = startedByRunMind;
        if (_process is not null)
        {
            _ = DrainOutputAsync(_process.StandardOutput, _stdout, detectInspectorEndpoint: false);
            _ = DrainOutputAsync(_process.StandardError, _stderr, detectInspectorEndpoint: true);
        }
    }

    public async Task ConnectFromInspectorOutputAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        string endpoint;
        try { endpoint = await _endpointReady.Task.WaitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Node.js did not publish its local Inspector WebSocket endpoint within 20 seconds. Check that the target starts under the installed Node.js runtime.");
        }
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "ws" || uri.Port is < 1 or > 65535 || !(uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address))))
            throw new InvalidOperationException("Node.js reported an Inspector endpoint outside loopback; RunMind refused the connection.");
        await ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        await _socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        _ = Task.Run(() => ReceiveLoopAsync());
    }

    public async Task InitializeAsync(CancellationToken cancellationToken, bool waitForInitialPause)
    {
        await SendCommandAsync("Debugger.enable", new JsonObject(), cancellationToken).ConfigureAwait(false);
        await SendCommandAsync("Runtime.enable", new JsonObject(), cancellationToken).ConfigureAwait(false);
        await SendCommandAsync("Debugger.setAsyncCallStackDepth", new JsonObject { ["maxDepth"] = 32 }, cancellationToken).ConfigureAwait(false);
        // Launch mode releases Node's --inspect-brk gate so V8 can report the initial paused frame.
        // Attach mode deliberately leaves an externally waiting target untouched until the user resumes it.
        if (waitForInitialPause)
        {
            await SendCommandAsync("Runtime.runIfWaitingForDebugger", new JsonObject(), cancellationToken).ConfigureAwait(false);
            try { await _firstPause.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false); }
            catch (TimeoutException) { throw new TimeoutException("Node Inspector connected, but the initial break-on-start pause was not observed within 10 seconds."); }
        }
    }

    public async Task<JsonElement> SendCommandAsync(string method, JsonObject parameters, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        if (_closed || _socket.State != WebSocketState.Open) throw new InvalidOperationException("The Node.js Inspector connection is closed.");
        var id = Interlocked.Increment(ref _nextCommandId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion)) throw new InvalidOperationException("Could not register the Node Inspector command.");
        try
        {
            var message = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters };
            var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false); }
            finally { _sendLock.Release(); }
            var response = await completion.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            if (response.TryGetProperty("error", out var error))
            {
                var errorMessage = error.TryGetProperty("message", out var messageValue) ? messageValue.GetString() : "Unknown Inspector error.";
                throw new InvalidOperationException($"Node Inspector {method} failed: {errorMessage}");
            }
            return response.TryGetProperty("result", out var result) ? result.Clone() : response.Clone();
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[32 * 1024];
        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    stream.Write(buffer, 0, result.Count);
                    if (stream.Length > MaximumMessageBytes) throw new InvalidDataException("Node Inspector message exceeded the 4 MB safety limit.");
                } while (!result.EndOfMessage);
                using var document = JsonDocument.Parse(stream.ToArray());
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idValue) && idValue.TryGetInt32(out var id) && _pending.TryGetValue(id, out var completion))
                    completion.TrySetResult(root.Clone());
                else if (root.TryGetProperty("method", out var methodValue) && methodValue.ValueKind == JsonValueKind.String)
                    StoreEvent(root.Clone(), methodValue.GetString() ?? string.Empty);
            }
        }
        catch (Exception error)
        {
            foreach (var pending in _pending.Values) pending.TrySetException(error);
        }
        finally { _closed = true; }
    }

    private void StoreEvent(JsonElement value, string method)
    {
        if (method == "HeapProfiler.addHeapSnapshotChunk")
        {
            AppendHeapSnapshotChunk(value);
            return;
        }
        var serialized = JsonSerializer.SerializeToUtf8Bytes(value);
        if (serialized.Length > MaximumEventBytes)
        {
            if (method != "Debugger.paused") return;
            // Preserve only a bounded paused-frame summary when scope previews are unusually large.
            var boundedFrames = new JsonArray();
            if (value.TryGetProperty("params", out var pausedParams) && pausedParams.TryGetProperty("callFrames", out var originalFrames) && originalFrames.ValueKind == JsonValueKind.Array)
            {
                foreach (var frame in originalFrames.EnumerateArray().Take(20))
                {
                    var frameNode = new JsonObject
                    {
                        ["callFrameId"] = frame.TryGetProperty("callFrameId", out var frameId) ? frameId.GetString() : string.Empty,
                        ["functionName"] = frame.TryGetProperty("functionName", out var functionName) ? functionName.GetString() : string.Empty,
                        ["url"] = frame.TryGetProperty("url", out var url) ? url.GetString() : string.Empty,
                        ["location"] = frame.TryGetProperty("location", out var location) ? JsonNode.Parse(location.GetRawText()) : null,
                        ["scopeChain"] = new JsonArray(),
                    };
                    if (frame.TryGetProperty("scopeChain", out var originalScopes) && originalScopes.ValueKind == JsonValueKind.Array)
                    {
                        var scopes = (JsonArray)frameNode["scopeChain"]!;
                        foreach (var scope in originalScopes.EnumerateArray().Take(20))
                            scopes.Add(JsonNode.Parse(scope.GetRawText()));
                    }
                    boundedFrames.Add(frameNode);
                }
            }
            value = JsonSerializer.SerializeToElement(new { method = "Debugger.paused", @params = new { reason = "paused", callFrames = boundedFrames, data = new { truncated = true } } });
        }
        if (method == "Debugger.paused")
        {
            _lastPausedEvent = value.Clone();
            _paused = true;
            _firstPause.TrySetResult(true);
            _topCallFrameId = value.TryGetProperty("params", out var parameters) && parameters.TryGetProperty("callFrames", out var frames) && frames.ValueKind == JsonValueKind.Array && frames.GetArrayLength() > 0 && frames[0].TryGetProperty("callFrameId", out var frameId)
                ? frameId.GetString() ?? string.Empty : string.Empty;
        }
        else if (method == "Debugger.resumed")
        {
            _paused = false;
            _topCallFrameId = string.Empty;
        }
        if (method == "Debugger.scriptParsed")
        {
            _scripts.Enqueue(value.Clone());
            while (Interlocked.Increment(ref _scriptCount) > 200) { if (_scripts.TryDequeue(out _)) Interlocked.Decrement(ref _scriptCount); else break; }
        }
        _events.Enqueue(value.Clone());
        while (Interlocked.Increment(ref _eventCount) > MaximumEventCount) { if (_events.TryDequeue(out _)) Interlocked.Decrement(ref _eventCount); else break; }
    }

    public async Task<object> StartCpuProfileAsync(CancellationToken cancellationToken)
    {
        lock (_profileLock)
        {
            if (_cpuProfileActive || _cpuProfileStarting) throw new InvalidOperationException("A CPU profile is already running or starting for this session.");
            _cpuProfileStarting = true;
        }
        try
        {
            await SendCommandAsync("Profiler.enable", new JsonObject(), cancellationToken).ConfigureAwait(false);
            await SendCommandAsync("Profiler.setSamplingInterval", new JsonObject { ["interval"] = 1000 }, cancellationToken).ConfigureAwait(false);
            await SendCommandAsync("Profiler.start", new JsonObject(), cancellationToken).ConfigureAwait(false);
            lock (_profileLock) { _cpuProfileActive = true; _cpuProfileStarting = false; }
            return new { sessionId = Id, started = true, samplingIntervalMicroseconds = 1000, message = "CPU profiling started. Stop the profile to save the V8 .cpuprofile artifact." };
        }
        catch { lock (_profileLock) _cpuProfileStarting = false; throw; }
    }

    public async Task<JsonElement> StopCpuProfileAsync(CancellationToken cancellationToken)
    {
        lock (_profileLock)
        {
            if (!_cpuProfileActive || _cpuProfileStopping) throw new InvalidOperationException("No CPU profile is running, or the profile is already stopping.");
            _cpuProfileStopping = true;
        }
        try
        {
            var result = await SendCommandAsync("Profiler.stop", new JsonObject(), cancellationToken).ConfigureAwait(false);
            if (!result.TryGetProperty("profile", out var profile) || profile.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Node Inspector did not return a valid CPU profile.");
            return profile.Clone();
        }
        finally { lock (_profileLock) { _cpuProfileActive = false; _cpuProfileStopping = false; } }
    }

    public void BeginHeapSnapshotCapture(string path)
    {
        lock (_snapshotLock)
        {
            if (_heapSnapshotStream is not null) throw new InvalidOperationException("A heap snapshot is already being captured for this session.");
            _heapSnapshotPath = path;
            _heapSnapshotBytes = 0;
            _heapSnapshotTruncated = false;
            _heapSnapshotStream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        }
    }

    private void AppendHeapSnapshotChunk(JsonElement value)
    {
        if (!value.TryGetProperty("params", out var parameters) || !parameters.TryGetProperty("chunk", out var chunkValue) || chunkValue.ValueKind != JsonValueKind.String) return;
        var chunk = chunkValue.GetString() ?? string.Empty;
        var bytes = Encoding.UTF8.GetBytes(chunk);
        lock (_snapshotLock)
        {
            if (_heapSnapshotStream is null || _heapSnapshotTruncated) return;
            if (_heapSnapshotBytes + bytes.Length > 128L * 1024 * 1024)
            {
                _heapSnapshotTruncated = true;
                _heapSnapshotStream.Dispose();
                _heapSnapshotStream = null;
                return;
            }
            _heapSnapshotStream.Write(bytes, 0, bytes.Length);
            _heapSnapshotBytes += bytes.Length;
        }
    }

    public (string Path, long Bytes, bool Truncated) CompleteHeapSnapshotCapture()
    {
        lock (_snapshotLock)
        {
            _heapSnapshotStream?.Flush(true);
            _heapSnapshotStream?.Dispose();
            _heapSnapshotStream = null;
            var path = _heapSnapshotPath;
            var bytes = _heapSnapshotBytes;
            var truncated = _heapSnapshotTruncated;
            _heapSnapshotPath = string.Empty;
            if (truncated) { try { File.Delete(path); } catch { } }
            return (path, bytes, truncated);
        }
    }

    public void AbortHeapSnapshotCapture()
    {
        lock (_snapshotLock)
        {
            try { _heapSnapshotStream?.Dispose(); } catch { }
            _heapSnapshotStream = null;
            if (_heapSnapshotPath.Length > 0) { try { File.Delete(_heapSnapshotPath); } catch { } }
            _heapSnapshotPath = string.Empty;
            _heapSnapshotBytes = 0;
            _heapSnapshotTruncated = false;
        }
    }

    public async Task<object> StartMemoryMonitorAsync(int intervalMilliseconds, int durationSeconds, CancellationToken cancellationToken)
    {
        if (intervalMilliseconds is < 1000 or > 60000) throw new InvalidDataException("Memory monitor interval must be between 1,000 and 60,000 milliseconds.");
        if (durationSeconds is < 5 or > 600) throw new InvalidDataException("Memory monitor duration must be between 5 and 600 seconds.");
        lock (_memorySamples) { if (_memoryMonitorTask is { IsCompleted: false }) throw new InvalidOperationException("A memory monitor is already running for this session."); }
        var monitor = new CancellationTokenSource();
        lock (_memorySamples)
        {
            _memoryMonitorCancellation?.Dispose();
            _memoryMonitorCancellation = monitor;
            while (_memorySamples.TryDequeue(out _)) Interlocked.Decrement(ref _memorySampleCount);
            _memoryMonitorTask = Task.Run(() => MemoryMonitorLoopAsync(intervalMilliseconds, durationSeconds, monitor.Token));
        }
        return new { sessionId = Id, started = true, intervalMilliseconds, durationSeconds, maximumRetainedSamples = 120 };
    }

    private async Task MemoryMonitorLoopAsync(int intervalMilliseconds, int durationSeconds, CancellationToken cancellationToken)
    {
        var end = DateTimeOffset.UtcNow.AddSeconds(durationSeconds);
        try
        {
            while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < end && !IsClosed)
            {
                var heap = await SendCommandAsync("Runtime.getHeapUsage", new JsonObject(), cancellationToken).ConfigureAwait(false);
                var sample = JsonSerializer.SerializeToElement(new { timestamp = DateTimeOffset.UtcNow.ToString("O"), heap });
                _memorySamples.Enqueue(sample);
                while (Interlocked.Increment(ref _memorySampleCount) > 120) { if (_memorySamples.TryDequeue(out _)) Interlocked.Decrement(ref _memorySampleCount); else break; }
                await Task.Delay(intervalMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    public async Task<object> StopMemoryMonitorAsync()
    {
        CancellationTokenSource? cancellation;
        Task? task;
        lock (_memorySamples) { cancellation = _memoryMonitorCancellation; task = _memoryMonitorTask; cancellation?.Cancel(); }
        if (task is not null) { try { await task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch { } }
        var stopped = task is null || task.IsCompleted;
        if (stopped) cancellation?.Dispose();
        lock (_memorySamples) { if (stopped && ReferenceEquals(_memoryMonitorCancellation, cancellation)) _memoryMonitorCancellation = null; }
        return new { sessionId = Id, stopped, sampleCount = _memorySampleCount };
    }

    public object ReadMemoryMonitor() => new { sessionId = Id, running = _memoryMonitorTask is { IsCompleted: false }, sampleCount = _memorySampleCount, samples = _memorySamples.ToArray() };

    public JsonElement[] DrainEvents(int maximum)
    {
        var values = new List<JsonElement>(maximum);
        while (values.Count < maximum && _events.TryDequeue(out var value)) { Interlocked.Decrement(ref _eventCount); values.Add(value); }
        return values.ToArray();
    }

    public object GetPausedCallFrames()
    {
        if (!_paused || _lastPausedEvent.ValueKind != JsonValueKind.Object) return new { paused = false, callFrames = Array.Empty<object>() };
        return new { paused = true, details = _lastPausedEvent.Clone() };
    }

    public string GetTopPausedCallFrameId() => _paused ? _topCallFrameId : string.Empty;

    public object GetScripts()
    {
        return new { scripts = _scripts.ToArray().Select(item => item.Clone()).ToArray(), count = _scriptCount };
    }

    public string ReadOutput(string channel)
    {
        lock (_outputLock) return (channel == "stderr" ? _stderr : _stdout).ToString();
    }

    private async Task DrainOutputAsync(StreamReader reader, StringBuilder target, bool detectInspectorEndpoint)
    {
        var buffer = new char[4096];
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (read <= 0) break;
                var chunk = new string(buffer, 0, read);
                lock (_outputLock)
                {
                    target.Append(chunk);
                    if (target.Length > MaximumOutputCharacters) target.Remove(0, target.Length - MaximumOutputCharacters);
                    if (detectInspectorEndpoint)
                    {
                        _stderrProbe.Append(chunk);
                        if (_stderrProbe.Length > 8192) _stderrProbe.Remove(0, _stderrProbe.Length - 8192);
                        var match = Regex.Match(_stderrProbe.ToString(), @"Debugger listening on (ws://[^\s]+)", RegexOptions.IgnoreCase);
                        if (match.Success) _endpointReady.TrySetResult(match.Groups[1].Value.Trim());
                    }
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
        catch (InvalidOperationException) { }
    }

    public async Task<object> ResumeAsync(CancellationToken cancellationToken)
    {
        // Also releases a target attached while it is waiting at Node's --inspect-brk gate.
        await SendCommandAsync("Runtime.runIfWaitingForDebugger", new JsonObject(), cancellationToken).ConfigureAwait(false);
        if (!_paused && !_firstPause.Task.IsCompleted)
        {
            try { await _firstPause.Task.WaitAsync(TimeSpan.FromMilliseconds(750), cancellationToken).ConfigureAwait(false); }
            catch (TimeoutException) { }
        }
        if (!_paused) return new { resumed = false, alreadyRunning = true, message = "The Node.js target was not paused." };
        return await SendCommandAsync("Debugger.resume", new JsonObject(), cancellationToken).ConfigureAwait(false);
    }

    public object GetSummary()
    {
        return new { sessionId = Id, processId = _process is null ? (int?)null : SafeProcessId(_process), startedByRunMind = StartedByRunMind, state = State, paused = _paused, processExited = ProcessExited, outputAvailable = _process is not null };
    }

    public async Task StopAsync(bool terminateProcess)
    {
        try { await StopMemoryMonitorAsync().ConfigureAwait(false); } catch { }
        AbortHeapSnapshotCapture();
        var stopCpuProfile = false;
        lock (_profileLock) { stopCpuProfile = _cpuProfileActive && !_cpuProfileStopping; _cpuProfileStopping = stopCpuProfile; }
        if (stopCpuProfile) { try { await SendCommandAsync("Profiler.stop", new JsonObject(), CancellationToken.None, TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch { } finally { lock (_profileLock) { _cpuProfileActive = false; _cpuProfileStopping = false; } } }
        if (Interlocked.Exchange(ref _stopStarted, 1) == 0)
        {
            _closed = true;
            try
            {
                if (Interlocked.Exchange(ref _socketDisposeStarted, 1) == 0 && _socket.State == WebSocketState.Open)
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                        await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "RunMind debugger detached", timeout.Token).ConfigureAwait(false);
            }
            catch { }
            try { _socket.Dispose(); } catch { }
            foreach (var pending in _pending.Values) pending.TrySetException(new OperationCanceledException("The debugger session was closed."));
        }
        if (terminateProcess && _process is not null)
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { }
            try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }
        }
        if (_process is null || terminateProcess || SafeHasExited(_process))
        {
            try { _process?.Dispose(); } catch { }
        }
    }

    private static bool SafeHasExited(Process process) { try { return process.HasExited; } catch { return true; } }
    private static int? SafeProcessId(Process process) { try { return process.HasExited ? null : process.Id; } catch { return null; } }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        try { StopAsync(terminateProcess: StartedByRunMind && ProcessExited == false).GetAwaiter().GetResult(); } catch { }
    }
}
