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
        public required Process Process { get; init; }
        public required DateTime StartedAtUtc { get; init; }
        public DateTime ProcessStartedAtUtc { get; set; }
        public string ExecutablePath { get; set; } = string.Empty;
        public bool Recovered { get; set; }
        public string? ReadinessUrl { get; init; }
        public int? Port { get; init; }
        public readonly object LogLock = new();
        public readonly Queue<string> Logs = new();
        public int LogCharacters;
        public volatile bool Exited;
        public int? ExitCode;
    }

    private sealed class DevelopmentServerMetadata
    {
        public int Version { get; set; } = 1;
        public string Id { get; set; } = string.Empty;
        public string Script { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string ExecutablePath { get; set; } = string.Empty;
        public long ProcessStartedAtUtcTicks { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public string? ReadinessUrl { get; set; }
        public int? Port { get; set; }
        public int? ExitCode { get; set; }
        public string State { get; set; } = "running";
    }

    private sealed record StaleDevelopmentServer(string Id, int ProcessId, string Script, string WorkingDirectory, string Reason, DateTime StartedAtUtc);
    private static readonly object DevelopmentServerReconcileLock = new();
    private static readonly List<StaleDevelopmentServer> StaleDevelopmentServers = new();
    private const int MaximumPersistedDevelopmentServerRecords = 64;
    private const int MaximumDevelopmentServerMetadataBytes = 32 * 1024;
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
        if (action == "discover") return DevelopmentServerDiscover(args);
        if (action == "list" || action == "reconcile") return DevelopmentServerReconcile();
        var id = ReadString(args, "server_id", 80);
        if (action == "logs") return DevelopmentServerLogs(id);
        if (action == "health") return await DevelopmentServerHealthAsync(id, cancellationToken).ConfigureAwait(false);
        if (action == "start") return await DevelopmentServerStartAsync(args, cancellationToken).ConfigureAwait(false);
        if (action == "stop") return await DevelopmentServerStopAsync(id).ConfigureAwait(false);
        throw new InvalidDataException("Development server action must be discover, start, list, reconcile, logs, health, or stop.");
    }

    private async Task<object> DevelopmentServerStartAsync(JsonElement args, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DevelopmentServerDirectory);
        EnsureNoReparseAncestors(DevelopmentServerDirectory);
        var startLockPath = Path.Combine(DevelopmentServerDirectory, "start.lock");
        if (File.Exists(startLockPath) && IsReparsePoint(startLockPath))
            throw new InvalidDataException("The development-server start lock is not a normal file.");
        FileStream startLock;
        try { startLock = new FileStream(startLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new InvalidOperationException("Another RunMind host is currently starting a development server. Reconcile sessions and retry after it finishes."); }
        using (startLock)
        {
            ReconcileDevelopmentServerSessions();
            return await DevelopmentServerStartCoreAsync(args, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<object> DevelopmentServerStartCoreAsync(JsonElement args, CancellationToken cancellationToken)
    {
        ReconcileDevelopmentServerSessions();
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
        var session = new DevelopmentServerSession
        {
            Id = Guid.NewGuid().ToString("N"), Script = script, WorkingDirectory = working, Process = process,
            StartedAtUtc = DateTime.UtcNow, ReadinessUrl = readinessUrl.Length == 0 ? null : readinessUrl, Port = port,
        };
        process.OutputDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) AppendDevelopmentServerLog(session, "stdout", eventArgs.Data); };
        process.ErrorDataReceived += (_, eventArgs) => { if (eventArgs.Data is not null) AppendDevelopmentServerLog(session, "stderr", eventArgs.Data); };
        process.Exited += (_, _) =>
        {
            session.Exited = true;
            try { session.ExitCode = process.ExitCode; } catch { }
            try { PersistDevelopmentServerMetadata(session); } catch { }
        };
        if (!process.Start()) { process.Dispose(); throw new InvalidOperationException("Could not start the development server process."); }
        session.ProcessStartedAtUtc = process.StartTime.ToUniversalTime();
        try { session.ExecutablePath = Path.GetFullPath(process.MainModule?.FileName ?? node); }
        catch { session.ExecutablePath = Path.GetFullPath(node); }
        try { PersistDevelopmentServerMetadata(session); }
        catch
        {
            try { process.Kill(true); } catch { }
            try { process.WaitForExit(3000); } catch { }
            process.Dispose();
            throw new InvalidOperationException("The development server started, but RunMind could not persist its process-ownership record. The process tree was stopped to avoid creating an untrackable server.");
        }
        if (!DevelopmentServers.TryAdd(session.Id, session))
        {
            try { process.Kill(true); } catch { }
            try { File.Delete(DevelopmentServerMetadataPath(session.Id)); } catch { }
            process.Dispose(); throw new InvalidOperationException("Could not register the development server session.");
        }
        TrimDevelopmentServerHistory();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var waitSeconds = 2;
        if (args.TryGetProperty("readiness_timeout_seconds", out var waitValue) && waitValue.TryGetInt32(out var requestedWait)) waitSeconds = Math.Clamp(requestedWait, 2, 90);
        bool? ready = null;
        var deadline = DateTime.UtcNow.AddSeconds(readinessUrl.Length == 0 ? 2 : waitSeconds);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (session.Exited || process.HasExited) break;
            if (readinessUrl.Length == 0) break;
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
        if (readinessUrl.Length > 0 && ready is null) ready = false;
        string logTail;
        lock (session.LogLock) { logTail = string.Join("\n", session.Logs.TakeLast(12)); }
        var conflict = logTail.Contains("EADDRINUSE", StringComparison.OrdinalIgnoreCase) || logTail.Contains("address already in use", StringComparison.OrdinalIgnoreCase) || logTail.Contains("port is already allocated", StringComparison.OrdinalIgnoreCase);
        try { PersistDevelopmentServerMetadata(session); } catch { }
        return new { server_id = session.Id, process_id = SafeProcessId(process), script, working_directory = working, port, readiness_url = session.ReadinessUrl, ready, readiness_status = exited ? "exited" : readinessUrl.Length == 0 ? "not_configured" : ready == true ? "ready" : "not_ready", state = exited ? "exited" : "running", process_running = !exited, startup_failure = exited, port_conflict = conflict, exit_code = session.ExitCode, recovered = false, can_stop = VerifyDevelopmentServerIdentity(session), logs = logTail, note = readinessUrl.Length == 0 ? "The process is running, but no readiness_url was configured. HTTP readiness is unknown; configure a loopback readiness_url to verify the application endpoint." : "RunMind persisted the process identity and verified the loopback readiness URL. Termination remains separately approved and revalidates PID, executable path and process start time." };
    }

    private static void TrimDevelopmentServerHistory()
    {
        foreach (var session in DevelopmentServers.Values.Where(item => SafeHasExited(item.Process)).OrderBy(item => item.StartedAtUtc).Take(Math.Max(0, DevelopmentServers.Count - MaximumDevelopmentServerHistory)))
        {
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
            try
            {
                var logPath = DevelopmentServerLogPath(session.Id);
                Directory.CreateDirectory(DevelopmentServerDirectory);
                EnsureNoReparseAncestors(DevelopmentServerDirectory);
                if (File.Exists(logPath) && IsReparsePoint(logPath)) return;
                File.AppendAllText(logPath, entry + Environment.NewLine, new UTF8Encoding(false));
                if (new FileInfo(logPath).Length > MaximumServerLogCharacters * 2)
                    File.WriteAllLines(logPath, session.Logs, new UTF8Encoding(false));
            }
            catch { /* A log persistence failure must not crash the redirected process stream handlers. */ }
        }
    }

    private static string DevelopmentServerDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RunMind", "DevelopmentServers");
    private static string DevelopmentServerMetadataPath(string id) => Path.Combine(DevelopmentServerDirectory, id + ".json");
    private static string DevelopmentServerLogPath(string id) => Path.Combine(DevelopmentServerDirectory, id + ".log");

    private static void PersistDevelopmentServerMetadata(DevelopmentServerSession session)
    {
        var processId = SafeProcessId(session.Process) ?? throw new InvalidOperationException("The development server process ID is unavailable.");
        var metadata = new DevelopmentServerMetadata
        {
            Id = session.Id, Script = session.Script, WorkingDirectory = session.WorkingDirectory,
            ProcessId = processId, ExecutablePath = session.ExecutablePath,
            ProcessStartedAtUtcTicks = session.ProcessStartedAtUtc.Ticks, StartedAtUtc = session.StartedAtUtc,
            ReadinessUrl = session.ReadinessUrl, Port = session.Port, ExitCode = session.ExitCode,
            State = session.Exited || SafeHasExited(session.Process) ? "exited" : "running"
        };
        Directory.CreateDirectory(DevelopmentServerDirectory);
        EnsureNoReparseAncestors(DevelopmentServerDirectory);
        var target = DevelopmentServerMetadataPath(session.Id);
        if (Directory.Exists(target) || (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0))
            throw new InvalidDataException("The development-server metadata path is not a normal file.");
        var metadataLockPath = target + ".metadata.lock";
        if (File.Exists(metadataLockPath) && IsReparsePoint(metadataLockPath))
            throw new InvalidDataException("The development-server metadata lock is a reparse point; ownership metadata was not changed.");
        FileStream? metadataLock = null;
        for (var attempt = 0; attempt < 40 && metadataLock is null; attempt++)
        {
            try { metadataLock = new FileStream(metadataLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 39) { Thread.Sleep(25); }
        }
        if (metadataLock is null) throw new IOException("Another RunMind host is updating this development-server ownership record.");
        using (metadataLock)
        {
            if (Directory.Exists(target) || (File.Exists(target) && IsReparsePoint(target)))
                throw new InvalidDataException("The development-server metadata path changed to an unsafe entry.");
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(metadata), new UTF8Encoding(false));
                File.Move(temporary, target, true);
            }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }
    }

    private static bool VerifyDevelopmentServerIdentity(DevelopmentServerSession session)
    {
        try
        {
            if (!session.Process.HasExited && session.Process.Id > 0)
            {
                var actualStart = session.Process.StartTime.ToUniversalTime().Ticks;
                if (Math.Abs(actualStart - session.ProcessStartedAtUtc.Ticks) > TimeSpan.TicksPerSecond) return false;
                var actualExecutable = Path.GetFullPath(session.Process.MainModule?.FileName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(actualExecutable) || !string.Equals(actualExecutable, session.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return false;
                return string.Equals(session.Process.ProcessName, "node", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
        catch { return false; }
    }

    private static List<string> ReadPersistedServerLogs(string id)
    {
        try
        {
            var path = DevelopmentServerLogPath(id);
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > MaximumServerLogCharacters * 2) return new List<string>();
            return File.ReadLines(path, Encoding.UTF8).TakeLast(120).Select(line => Limit(line, 4000)).ToList();
        }
        catch { return new List<string>(); }
    }

    private static string? ValidateDevelopmentServerMetadata(string metadataPath, out DevelopmentServerMetadata? metadata)
    {
        metadata = null;
        try
        {
            if ((File.GetAttributes(metadataPath) & FileAttributes.ReparsePoint) != 0 || new FileInfo(metadataPath).Length > MaximumDevelopmentServerMetadataBytes) return "Ownership record is a reparse point or exceeds the size limit.";
            metadata = JsonSerializer.Deserialize<DevelopmentServerMetadata>(File.ReadAllText(metadataPath, Encoding.UTF8));
            if (metadata is null || metadata.Version != 1 || !System.Text.RegularExpressions.Regex.IsMatch(metadata.Id, "^[a-f0-9]{32}$")
                || !string.Equals(Path.GetFileNameWithoutExtension(metadataPath), metadata.Id, StringComparison.OrdinalIgnoreCase)
                || metadata.ProcessId <= 0 || metadata.ProcessStartedAtUtcTicks <= 0 || string.IsNullOrWhiteSpace(metadata.ExecutablePath)
                || string.IsNullOrWhiteSpace(metadata.WorkingDirectory) || metadata.Script.Length > 60
                || (metadata.Port.HasValue && metadata.Port.Value is < 1 or > 65535)
                || (metadata.ReadinessUrl is not null && (!Uri.TryCreate(metadata.ReadinessUrl, UriKind.Absolute, out var readiness)
                    || readiness.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(readiness.UserInfo)
                    || !(readiness.IsLoopback || readiness.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))))
                return "Ownership record fields are invalid.";
            return null;
        }
        catch (Exception error) { return "Ownership record could not be read: " + Limit(error.Message, 300); }
    }

    private static void ReconcileDevelopmentServerSessions()
    {
        lock (DevelopmentServerReconcileLock)
        {
            foreach (var session in DevelopmentServers.Values.ToArray())
            {
                if (!SafeHasExited(session.Process)) continue;
                session.Exited = true;
                try { session.ExitCode ??= session.Process.ExitCode; } catch { }
                try { PersistDevelopmentServerMetadata(session); } catch { }
            }
            try
            {
                Directory.CreateDirectory(DevelopmentServerDirectory);
                EnsureNoReparseAncestors(DevelopmentServerDirectory);
                var reconcileLockPath = Path.Combine(DevelopmentServerDirectory, "reconcile.lock");
                if (File.Exists(reconcileLockPath) && IsReparsePoint(reconcileLockPath)) return;
                using var reconcileFileLock = new FileStream(reconcileLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                foreach (var metadataPath in Directory.EnumerateFiles(DevelopmentServerDirectory, "*.json")
                    .OrderByDescending(path => File.GetLastWriteTimeUtc(path)).Take(MaximumPersistedDevelopmentServerRecords))
                {
                    var recordError = ValidateDevelopmentServerMetadata(metadataPath, out var metadata);
                    if (recordError is not null || metadata is null) { AddStaleServer(metadata?.Id ?? Path.GetFileNameWithoutExtension(metadataPath), 0, metadata?.Script ?? "", metadata?.WorkingDirectory ?? "", recordError ?? "Invalid ownership record.", metadata?.StartedAtUtc ?? File.GetLastWriteTimeUtc(metadataPath)); continue; }
                    if (DevelopmentServers.ContainsKey(metadata.Id)) continue;
                    if (string.Equals(metadata.State, "exited", StringComparison.OrdinalIgnoreCase)) continue;
                    Process? process = null;
                    try
                    {
                        process = Process.GetProcessById(metadata.ProcessId);
                        if (process.HasExited)
                        {
                            process.Dispose();
                            AddStaleServer(metadata.Id, metadata.ProcessId, metadata.Script, metadata.WorkingDirectory, "The recorded Node.js process has exited; no process was adopted or terminated.", metadata.StartedAtUtc);
                            continue;
                        }
                        var startTicks = process.StartTime.ToUniversalTime().Ticks;
                        if (Math.Abs(startTicks - metadata.ProcessStartedAtUtcTicks) > TimeSpan.TicksPerSecond)
                        { process.Dispose(); AddStaleServer(metadata.Id, metadata.ProcessId, metadata.Script, metadata.WorkingDirectory, "PID is now occupied by a different process start instance; not adopted or terminated.", metadata.StartedAtUtc); continue; }
                        var executable = Path.GetFullPath(process.MainModule?.FileName ?? string.Empty);
                        var expectedExecutable = Path.GetFullPath(metadata.ExecutablePath);
                        if (!string.Equals(executable, expectedExecutable, StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(process.ProcessName, "node", StringComparison.OrdinalIgnoreCase))
                        { process.Dispose(); AddStaleServer(metadata.Id, metadata.ProcessId, metadata.Script, metadata.WorkingDirectory, "Live PID executable identity does not match the recorded Node.js process; not adopted or terminated.", metadata.StartedAtUtc); continue; }
                        if (!Directory.Exists(metadata.WorkingDirectory) || (File.GetAttributes(metadata.WorkingDirectory) & FileAttributes.ReparsePoint) != 0)
                        { process.Dispose(); AddStaleServer(metadata.Id, metadata.ProcessId, metadata.Script, metadata.WorkingDirectory, "Recorded project working directory is unavailable or unsafe; process was not adopted or terminated.", metadata.StartedAtUtc); continue; }
                        EnsureNoReparseAncestors(metadata.WorkingDirectory);
                        var session = new DevelopmentServerSession
                        {
                            Id = metadata.Id, Script = metadata.Script, WorkingDirectory = metadata.WorkingDirectory,
                            Process = process, StartedAtUtc = metadata.StartedAtUtc,
                            ProcessStartedAtUtc = new DateTime(metadata.ProcessStartedAtUtcTicks, DateTimeKind.Utc),
                            ExecutablePath = expectedExecutable, ReadinessUrl = metadata.ReadinessUrl,
                            Port = metadata.Port, Recovered = true
                        };
                        foreach (var line in ReadPersistedServerLogs(metadata.Id)) { session.Logs.Enqueue(line); session.LogCharacters += line.Length; }
                        process.EnableRaisingEvents = true;
                        process.Exited += (_, _) => { session.Exited = true; try { session.ExitCode = process.ExitCode; } catch { } try { PersistDevelopmentServerMetadata(session); } catch { } };
                        if (!DevelopmentServers.TryAdd(session.Id, session)) process.Dispose();
                        else PersistDevelopmentServerMetadata(session);
                    }
                    catch (ArgumentException)
                    {
                        process?.Dispose();
                        AddStaleServer(metadata.Id, metadata.ProcessId, metadata.Script, metadata.WorkingDirectory, "Recorded process no longer exists; no action was taken.", metadata.StartedAtUtc);
                    }
                    catch (Exception error)
                    {
                        process?.Dispose();
                        AddStaleServer(metadata.Id, metadata.ProcessId, metadata.Script, metadata.WorkingDirectory, "Process identity could not be verified: " + Limit(error.Message, 300) + ". No action was taken.", metadata.StartedAtUtc);
                    }
                }
            }
            catch { /* Listing must remain available even when the metadata directory is unavailable. */ }
        }
    }

    private static void AddStaleServer(string id, int pid, string script, string working, string reason, DateTime started)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        lock (StaleDevelopmentServers)
        {
            StaleDevelopmentServers.RemoveAll(item => item.Id == id);
            StaleDevelopmentServers.Add(new StaleDevelopmentServer(id, pid, script, working, reason, started));
            if (StaleDevelopmentServers.Count > 64) StaleDevelopmentServers.RemoveRange(0, StaleDevelopmentServers.Count - 64);
        }
    }

    private object DevelopmentServerReconcile()
    {
        ReconcileDevelopmentServerSessions();
        var servers = DevelopmentServers.Values.OrderBy(item => item.StartedAtUtc).Select(item => new
        {
            server_id = item.Id, process_id = SafeProcessId(item.Process), script = item.Script, working_directory = item.WorkingDirectory,
            started_at_utc = item.StartedAtUtc, readiness_url = item.ReadinessUrl, port = item.Port,
            state = item.Exited || SafeHasExited(item.Process) ? "exited" : "running", exit_code = item.ExitCode,
            owned_by_runmind = true, recovered = item.Recovered, can_stop = !SafeHasExited(item.Process) && VerifyDevelopmentServerIdentity(item)
        }).ToArray();
        StaleDevelopmentServer[] stale;
        lock (StaleDevelopmentServers) stale = StaleDevelopmentServers.ToArray();
        return new { servers, stale_sessions = stale, recovered_count = servers.Count(item => item.recovered), stale_count = stale.Length,
            note = "Only sessions whose PID, Node executable path and process start time matched the persisted RunMind ownership record were re-adopted. Stale or unknown processes were never terminated." };
    }

    private static bool SafeHasExited(Process process) { try { return process.HasExited; } catch { return true; } }

    private object DevelopmentServerLogs(string id)
    {
        ReconcileDevelopmentServerSessions();
        if (!DevelopmentServers.TryGetValue(id, out var session)) throw new KeyNotFoundException("Development server session was not found in this RunMind host.");
        string[] logs; lock (session.LogLock) logs = session.Logs.TakeLast(120).ToArray();
        return new { server_id = id, logs, truncated = session.LogCharacters >= MaximumServerLogCharacters, state = session.Exited || SafeHasExited(session.Process) ? "exited" : "running", exit_code = session.ExitCode };
    }

    private async Task<object> DevelopmentServerHealthAsync(string id, CancellationToken cancellationToken)
    {
        ReconcileDevelopmentServerSessions();
        if (!DevelopmentServers.TryGetValue(id, out var session)) throw new KeyNotFoundException("Development server session was not found in this RunMind host.");
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

    private async Task<object> DevelopmentServerStopAsync(string id)
    {
        ReconcileDevelopmentServerSessions();
        if (!DevelopmentServers.TryGetValue(id, out var session)) throw new KeyNotFoundException("Development server session was not found or could not be safely re-adopted. Only identity-verified RunMind-owned sessions can be stopped.");
        if (SafeHasExited(session.Process)) { session.Exited = true; try { PersistDevelopmentServerMetadata(session); } catch { } return new { server_id = id, stopped = false, state = "already_exited", owned_by_runmind = true, recovered = session.Recovered, can_stop = false }; }
        var stopLockPath = DevelopmentServerMetadataPath(session.Id) + ".stop.lock";
        if (File.Exists(stopLockPath) && IsReparsePoint(stopLockPath)) throw new InvalidDataException("The development-server stop lock is not a normal file; termination was refused.");
        FileStream? stopLock = null;
        try { stopLock = new FileStream(stopLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new InvalidOperationException("Another RunMind host is already reconciling or stopping this server session."); }
        using (stopLock)
        {
            if (!VerifyDevelopmentServerIdentity(session)) throw new InvalidOperationException("The recorded process identity no longer matches the live PID, Node executable and process start time. RunMind refused to terminate it.");
            try { session.Process.Kill(true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception error) { throw new InvalidOperationException("RunMind could not stop its owned server process: " + error.Message); }
            try { await session.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch (TimeoutException) { }
            var stopped = SafeHasExited(session.Process);
            if (stopped) { session.Exited = true; try { session.ExitCode = session.Process.ExitCode; } catch { } }
            try { PersistDevelopmentServerMetadata(session); } catch { }
            return new { server_id = id, stopped, process_id = SafeProcessId(session.Process), state = stopped ? "exited" : "terminating", owned_by_runmind = true, recovered = session.Recovered, note = "The process tree was targeted only after verifying the persisted PID, executable path and process start time." };
        }
    }


}
