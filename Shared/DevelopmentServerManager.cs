#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace RunMind.Shared;

public sealed partial class AgentHostBridge
{
    private sealed class DevelopmentServerSession
    {
        public required string Id { get; init; }
        public required string Script { get; init; }
        public required string WorkingDirectory { get; init; }
        public required string ContextId { get; init; }
        public required string WorkspaceSelectionId { get; init; }
        public required string ExecutablePath { get; init; }
        public required long ProcessStartTimeUtcTicks { get; set; }
        public required Process Process { get; init; }
        public required DateTime StartedAtUtc { get; init; }
        public string? ReadinessUrl { get; init; }
        public int? Port { get; init; }
        public bool Recovered { get; init; }
        public bool LogsAvailable { get; init; } = true;
        public volatile bool RegistryPersisted;
        public readonly object LogLock = new();
        public readonly Queue<string> Logs = new();
        public int LogCharacters;
        public volatile bool Exited;
        public int? ExitCode;
    }

    private const int MaximumDevelopmentServers = 8;
    private const int MaximumDevelopmentServerHistory = 32;
    private const int MaximumServerLogCharacters = 48 * 1024;
    private static readonly ConcurrentDictionary<string, DevelopmentServerSession> DevelopmentServers = new(StringComparer.Ordinal);
    private static readonly HttpClient DevelopmentHealthClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(3) };
    private static readonly Regex ServerSecretAssignment = new(@"(?i)\b(authorization|proxy-authorization|password|passwd|secret|token|api[_-]?key)\b(\s*[:=]\s*)[^\s,;]+", RegexOptions.Compiled);
    private static readonly Regex ServerBearerToken = new(@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.Compiled);

    private object DevelopmentServerDiscover(JsonElement args)
    {
        var working = GetProcessWorkingDirectory(args);
        var relativeWorkingDirectory = ReadString(args, "working_directory", 1024).Trim();
        var packagePath = Path.Combine(working, "package.json");
        if (!File.Exists(packagePath)) return new { detected = false, working_directory = working, working_directory_relative = relativeWorkingDirectory, message = "No package.json was found in this directory. This increment currently discovers Node.js package scripts." };
        if (IsReparsePoint(packagePath)) throw new InvalidDataException("package.json cannot be a symbolic link or reparse point.");
        if (new FileInfo(packagePath).Length > 1024 * 1024) throw new InvalidDataException("package.json exceeds the 1 MB discovery limit.");
        using var package = JsonDocument.Parse(File.ReadAllText(packagePath));
        var root = package.RootElement;
        var scripts = new List<object>();
        if (root.TryGetProperty("scripts", out var scriptObject) && scriptObject.ValueKind == JsonValueKind.Object)
        {
            foreach (var item in scriptObject.EnumerateObject().Take(80))
                if (item.Value.ValueKind == JsonValueKind.String)
                    scripts.Add(new { name = item.Name, command = Limit(item.Value.GetString() ?? string.Empty, 2048), recommended = item.Name is "dev" or "start" or "serve" });
        }
        var suggested = new[] { "dev", "start", "serve" }.FirstOrDefault(name => root.TryGetProperty("scripts", out var list) && list.ValueKind == JsonValueKind.Object && list.TryGetProperty(name, out _));
        var packageManager = root.TryGetProperty("packageManager", out var manager) && manager.ValueKind == JsonValueKind.String ? manager.GetString() : null;
        return new { detected = scripts.Count > 0, working_directory = working, working_directory_relative = relativeWorkingDirectory, project = root.TryGetProperty("name", out var projectName) ? projectName.ToString() : Path.GetFileName(working), package_manager = packageManager ?? DetectPackageManager(working), suggested_script = suggested, scripts };
    }

    private static string DetectPackageManager(string directory)
    {
        if (File.Exists(Path.Combine(directory, "pnpm-lock.yaml"))) return "pnpm";
        if (File.Exists(Path.Combine(directory, "yarn.lock"))) return "yarn";
        if (File.Exists(Path.Combine(directory, "bun.lockb")) || File.Exists(Path.Combine(directory, "bun.lock"))) return "bun";
        return File.Exists(Path.Combine(directory, "package-lock.json")) ? "npm" : "npm-compatible";
    }

    private async Task<object> DevelopmentServerManageAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var action = ReadString(args, "action", 20).ToLowerInvariant();
        if (action is not ("discover" or "start" or "list" or "logs" or "health" or "stop"))
            throw new InvalidDataException("Development server action must be discover, start, list, logs, health, or stop.");
        var contextId = ReadString(args, "contextId", 64);
        var workspaceRoot = RequireWorkspaceRoot(contextId);
        var selectionId = RequireWorkspaceSelectionId(contextId);
        if (action == "discover") return DevelopmentServerDiscover(args);
        var recovery = ReconcileDevelopmentServerSessions(contextId, workspaceRoot, selectionId);
        if (action == "list") return DevelopmentServerList(contextId, selectionId, recovery.Recovered, recovery.Warnings);
        var id = ReadString(args, "server_id", 80);
        if (action == "logs") return DevelopmentServerLogs(id, contextId, selectionId);
        if (action == "health") return await DevelopmentServerHealthAsync(id, contextId, selectionId, cancellationToken).ConfigureAwait(false);
        if (action == "start") return await DevelopmentServerStartAsync(args, contextId, selectionId, cancellationToken).ConfigureAwait(false);
        if (action == "stop") return await DevelopmentServerStopAsync(id, contextId, selectionId).ConfigureAwait(false);
        throw new InvalidDataException("Unsupported development server action.");
    }

    private async Task<object> DevelopmentServerStartAsync(JsonElement args, string contextId, string selectionId, CancellationToken cancellationToken)
    {
        if (DevelopmentServers.Values.Count(item => !SafeHasExited(item.Process)) >= MaximumDevelopmentServers) throw new InvalidOperationException($"RunMind manages at most {MaximumDevelopmentServers} development servers per host.");
        var working = GetProcessWorkingDirectory(args);
        var packagePath = Path.Combine(working, "package.json");
        if (!File.Exists(packagePath)) throw new FileNotFoundException("No package.json was found. Only project-defined Node.js package scripts are supported in this increment.");
        if (IsReparsePoint(packagePath)) throw new InvalidDataException("package.json cannot be a symbolic link or reparse point.");
        if (new FileInfo(packagePath).Length > 1024 * 1024) throw new InvalidDataException("package.json exceeds the 1 MB startup limit.");
        var script = ReadString(args, "script", 60);
        if (script.Length == 0 || !char.IsLetterOrDigit(script[0]) || script.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_' or ':' or '.'))) throw new InvalidDataException("Provide a valid package script name (must start with a letter or number; letters, numbers, dash, underscore, colon, and dot are allowed).");
        using (var package = JsonDocument.Parse(File.ReadAllText(packagePath)))
        {
            if (!package.RootElement.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Object || !scripts.TryGetProperty(script, out var command) || command.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"The project does not define package script '{script}'. Run discover to inspect available scripts.");
            var actualCommand = command.GetString() ?? string.Empty;
            if (actualCommand.Length > 2048) throw new InvalidDataException("The selected package script exceeds the 2 KB approval-preview limit.");
            var approvedCommand = ReadString(args, "expected_command", 2048);
            if (!string.Equals(actualCommand, approvedCommand, StringComparison.Ordinal)) throw new InvalidOperationException("The package script changed after its approval preview. Discover it again and approve the current command.");
            foreach (var hookName in new[] { "pre" + script, "post" + script })
            {
                var actualHook = scripts.TryGetProperty(hookName, out var hook) && hook.ValueKind == JsonValueKind.String ? hook.GetString() ?? string.Empty : string.Empty;
                if (actualHook.Length > 2048) throw new InvalidDataException($"Package lifecycle hook '{hookName}' exceeds the 2 KB approval-preview limit.");
                var expectedHookProperty = hookName == "pre" + script ? "expected_pre_command" : "expected_post_command";
                var approvedHook = ReadString(args, expectedHookProperty, 2048);
                if (!string.Equals(actualHook, approvedHook, StringComparison.Ordinal)) throw new InvalidOperationException($"Package lifecycle hook '{hookName}' changed after its approval preview. Discover it again and approve the current command.");
            }
        }

        int? port = null;
        if (args.TryGetProperty("port", out var portValue) && portValue.ValueKind == JsonValueKind.Number && portValue.TryGetInt32(out var parsedPort))
        {
            if (parsedPort is < 1 or > 65535) throw new InvalidDataException("port must be between 1 and 65535.");
            port = parsedPort;
            using (var listener = new TcpListener(IPAddress.Loopback, parsedPort))
            {
                try { listener.Start(); } catch (SocketException) { throw new InvalidOperationException($"Port {parsedPort} is already in use. RunMind did not stop or modify the process using it."); }
            }
        }

        var readinessUrl = ReadString(args, "readiness_url", 2048);
        if (readinessUrl.Length == 0 && port.HasValue) readinessUrl = $"http://127.0.0.1:{port.Value}/";
        if (readinessUrl.Length > 0)
        {
            if (!Uri.TryCreate(readinessUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)
                || !(uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("readiness_url must be an HTTP(S) loopback URL (localhost or 127.0.0.1).");
        }

        var node = FindExecutable("node.exe") ?? throw new FileNotFoundException("Node.js was not found on PATH. Install Node.js and reopen RunMind.");
        var npm = FindExecutable("npm.cmd") ?? FindExecutable("npm.exe") ?? throw new FileNotFoundException("npm was not found on PATH. Install npm or add its directory to PATH.");
        var npmDirectory = Path.GetDirectoryName(npm) ?? string.Empty;
        var npmCli = Path.Combine(npmDirectory, "node_modules", "npm", "bin", "npm-cli.js");
        if (!File.Exists(npmCli) || IsReparsePoint(npmCli)) throw new FileNotFoundException("Could not locate a normal npm-cli.js beside npm. Reinstall Node.js/npm or ensure the standard Node.js installation directory is on PATH.");

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
        start.ArgumentList.Add(npmCli);
        start.ArgumentList.Add("run");
        start.ArgumentList.Add(script);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var sessionId = Guid.NewGuid().ToString("N");
        var executablePath = Path.GetFullPath(node);
        var processStartTicks = 0L;
        var session = new DevelopmentServerSession
        {
            Id = sessionId, Script = script, WorkingDirectory = working, ContextId = contextId,
            WorkspaceSelectionId = selectionId, ExecutablePath = executablePath, ProcessStartTimeUtcTicks = 0,
            Process = process, StartedAtUtc = DateTime.UtcNow,
            ReadinessUrl = readinessUrl.Length == 0 ? null : readinessUrl, Port = port,
        };
        process.OutputDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) AppendDevelopmentServerLog(session, "stdout", eventArgs.Data); };
        process.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) AppendDevelopmentServerLog(session, "stderr", eventArgs.Data); };
        process.Exited += (_, _) =>
        {
            session.Exited = true;
            try { session.ExitCode = process.ExitCode; } catch { }
            if (session.RegistryPersisted) RemoveDevelopmentServerRecord(session.ContextId, session.Id);
        };
        if (!process.Start()) { process.Dispose(); throw new InvalidOperationException("Could not start the development server process."); }
        try
        {
            try { processStartTicks = process.StartTime.ToUniversalTime().Ticks; }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { throw new InvalidOperationException("RunMind could not verify the newly launched server process start time; the process was not retained as an owned server.", error); }
            session.ProcessStartTimeUtcTicks = processStartTicks;
            if (!DevelopmentServers.TryAdd(session.Id, session)) throw new InvalidOperationException("Could not register the development server session.");
            PersistDevelopmentServerRecord(session);
            session.RegistryPersisted = true;
            if (session.Exited || SafeHasExited(process)) RemoveDevelopmentServerRecord(session.ContextId, session.Id);
        }
        catch
        {
            DevelopmentServers.TryRemove(session.Id, out _);
            try { if (!SafeHasExited(process)) process.Kill(true); } catch { }
            try { process.Dispose(); } catch { }
            throw;
        }
        TrimDevelopmentServerHistory();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var waitSeconds = 2;
        if (args.TryGetProperty("readiness_timeout_seconds", out var waitValue) && waitValue.TryGetInt32(out var requestedWait)) waitSeconds = Math.Clamp(requestedWait, 2, 90);
        var ready = false;
        var deadline = DateTime.UtcNow.AddSeconds(readinessUrl.Length == 0 ? 2 : waitSeconds);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (session.Exited || process.HasExited) break;
            if (readinessUrl.Length == 0) { ready = true; break; }
            try
            {
                using var response = await DevelopmentHealthClient.GetAsync(readinessUrl, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 200 and < 500) { ready = true; break; }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { }
            catch (HttpRequestException) { }
            await Task.Delay(500).ConfigureAwait(false);
        }
        var exited = session.Exited || process.HasExited;
        string logTail;
        lock (session.LogLock) { logTail = string.Join("\n", session.Logs.TakeLast(12)); }
        var conflict = logTail.Contains("EADDRINUSE", StringComparison.OrdinalIgnoreCase) || logTail.Contains("address already in use", StringComparison.OrdinalIgnoreCase) || logTail.Contains("port is already allocated", StringComparison.OrdinalIgnoreCase);
        return new { server_id = session.Id, process_id = SafeProcessId(process), script, working_directory = working, port, readiness_url = session.ReadinessUrl, ready, state = exited ? "exited" : ready ? "running" : "starting", startup_failure = exited, port_conflict = conflict, exit_code = session.ExitCode, logs = logTail, recovered = false, logs_available = true, note = "RunMind recorded the exact process identity and workspace selection. It may stop this server only after separate approval and identity revalidation." };
    }

    private static void TrimDevelopmentServerHistory()
    {
        foreach (var session in DevelopmentServers.Values.Where(item => SafeHasExited(item.Process)).OrderBy(item => item.StartedAtUtc).Take(Math.Max(0, DevelopmentServers.Count - MaximumDevelopmentServerHistory)))
        {
            RemoveDevelopmentServerRecord(session.ContextId, session.Id);
            if (DevelopmentServers.TryRemove(session.Id, out var removed)) { try { removed.Process.Dispose(); } catch { } }
        }
    }

    private static int? SafeProcessId(Process process) { try { return process.Id; } catch { return null; } }

    private static void AppendDevelopmentServerLog(DevelopmentServerSession session, string stream, string line)
    {
        var safeLine = ServerSecretAssignment.Replace(line, "$1$2[REDACTED]");
        safeLine = ServerBearerToken.Replace(safeLine, "Bearer [REDACTED]");
        var entry = $"{DateTime.UtcNow:O} [{stream}] {Limit(safeLine, 4000)}";
        lock (session.LogLock)
        {
            session.Logs.Enqueue(entry); session.LogCharacters += entry.Length;
            while (session.LogCharacters > MaximumServerLogCharacters && session.Logs.Count > 1) session.LogCharacters -= session.Logs.Dequeue().Length;
        }
    }

    private object DevelopmentServerList(string contextId, string selectionId, int recoveredCount, string[] recoveryWarnings) => new
    {
        servers = DevelopmentServers.Values.Where(item => item.ContextId == contextId && item.WorkspaceSelectionId == selectionId).OrderBy(item => item.StartedAtUtc).Select(item =>
        {
            var exited = item.Exited || SafeHasExited(item.Process);
            if (exited) { RemoveDevelopmentServerRecord(item.ContextId, item.Id); }
            return new
            {
                server_id = item.Id, process_id = SafeProcessId(item.Process), script = item.Script, working_directory = item.WorkingDirectory,
                started_at_utc = item.StartedAtUtc, readiness_url = item.ReadinessUrl, port = item.Port,
                state = exited ? "exited" : "running", exit_code = item.ExitCode, owned_by_runmind = true,
                recovered = item.Recovered, logs_available = item.LogsAvailable
            };
        }).ToArray(),
        recovery = new { recovered_sessions_added = recoveredCount, warnings = recoveryWarnings },
        ownership = "Only sessions with a persisted RunMind record and matching PID, process start time, executable path, conversation and workspace selection are recovered. Unknown port owners are never adopted or terminated."
    };

    private static bool SafeHasExited(Process process) { try { return process.HasExited; } catch { return true; } }

    private object DevelopmentServerLogs(string id, string contextId, string selectionId)
    {
        if (!DevelopmentServers.TryGetValue(id, out var session) || session.ContextId != contextId || session.WorkspaceSelectionId != selectionId) throw new KeyNotFoundException("Development server session was not found in the selected workspace.");
        string[] logs; lock (session.LogLock) logs = session.Logs.TakeLast(120).ToArray();
        return new { server_id = id, logs, truncated = session.LogCharacters >= MaximumServerLogCharacters, state = session.Exited || SafeHasExited(session.Process) ? "exited" : "running", exit_code = session.ExitCode, recovered = session.Recovered, logs_available = session.LogsAvailable, note = session.LogsAvailable ? null : "This server was recovered after a RunMind host restart. Historical stdout/stderr could not be recovered because live streams are process-bound." };
    }

    private async Task<object> DevelopmentServerHealthAsync(string id, string contextId, string selectionId, CancellationToken cancellationToken)
    {
        if (!DevelopmentServers.TryGetValue(id, out var session) || session.ContextId != contextId || session.WorkspaceSelectionId != selectionId) throw new KeyNotFoundException("Development server session was not found in the selected workspace.");
        if (string.IsNullOrWhiteSpace(session.ReadinessUrl)) return new { server_id = id, state = session.Exited || SafeHasExited(session.Process) ? "exited" : "running", health_check = "not_configured", message = "Start the server with readiness_url to enable HTTP health checks." };
        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await DevelopmentHealthClient.GetAsync(session.ReadinessUrl, cancellationToken).ConfigureAwait(false);
            return new { server_id = id, state = session.Exited || SafeHasExited(session.Process) ? "exited" : "running", healthy = response.IsSuccessStatusCode, status_code = (int)response.StatusCode, elapsed_ms = watch.ElapsedMilliseconds, url = session.ReadinessUrl };
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            return new { server_id = id, state = session.Exited || SafeHasExited(session.Process) ? "exited" : "running", healthy = false, elapsed_ms = watch.ElapsedMilliseconds, url = session.ReadinessUrl, error = Limit(error.Message, 500) };
        }
    }

    private async Task<object> DevelopmentServerStopAsync(string id, string contextId, string selectionId)
    {
        if (!DevelopmentServers.TryGetValue(id, out var session) || session.ContextId != contextId || session.WorkspaceSelectionId != selectionId) throw new KeyNotFoundException("Development server session was not found in the selected workspace. Unknown or stale processes cannot be stopped.");
        if (SafeHasExited(session.Process)) { RemoveDevelopmentServerRecord(session.ContextId, session.Id); return new { server_id = id, stopped = false, state = "already_exited", owned_by_runmind = true, recovered = session.Recovered }; }
        if (!TryClaimDevelopmentServerStop(session, out var verifiedProcess, out var reason))
            return new { server_id = id, stopped = false, state = "not_stopped", owned_by_runmind = false, reason };
        try { verifiedProcess.Kill(true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception error) { ClearDevelopmentServerStopClaim(session); throw new InvalidOperationException("RunMind could not stop the verified owned server process: " + error.Message); }
        try { await verifiedProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch (TimeoutException) { }
        var stopped = SafeHasExited(verifiedProcess);
        if (stopped) RemoveDevelopmentServerRecord(session.ContextId, session.Id); else ClearDevelopmentServerStopClaim(session);
        return new { server_id = id, stopped, process_id = SafeProcessId(verifiedProcess), state = stopped ? "exited" : "terminating", owned_by_runmind = stopped, recovered = session.Recovered, note = "Termination was attempted only after the persisted RunMind process identity and workspace selection were revalidated." };
    }


}
