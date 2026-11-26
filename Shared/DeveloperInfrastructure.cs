#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RunMind.Shared;

public sealed partial class AgentHostBridge
{
    private static readonly Regex ContainerSecretAssignment = new(@"(?i)((?:[A-Za-z0-9-]*[_-])?(?:password|passwd|secret|token|access[_-]?key|private[_-]?key|client[_-]?secret|api[_-]?key|authorization)\b\s*[:=]\s*)([^\s,;]+)", RegexOptions.Compiled);
    private static readonly Regex ContainerBearer = new(@"(?i)Bearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.Compiled);
    private static readonly Regex ContainerUriCredentials = new(@"(?i)\b([a-z][a-z0-9+.-]*://)[^/\s:@]+:[^/\s@]+@", RegexOptions.Compiled);

    private async Task<object> ContainerWorkbenchAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var working = GetProcessWorkingDirectory(args);
        var runtime = ReadString(args, "runtime", 16).ToLowerInvariant();
        if (runtime.Length == 0) runtime = "auto";
        if (runtime is not ("auto" or "podman" or "docker")) throw new InvalidDataException("runtime must be auto, podman, or docker.");
        var action = ReadString(args, "action", 32).ToLowerInvariant();
        var supported = new[] { "runtime_info", "ps", "images", "volumes", "networks", "container_inspect", "container_logs", "container_stats", "container_start", "container_stop", "container_restart", "compose_config", "compose_ps", "compose_up", "compose_down", "compose_build", "compose_logs" };
        if (!supported.Contains(action, StringComparer.Ordinal)) throw new InvalidDataException("Unsupported container workbench action.");
        var executable = (runtime == "auto" ? new[] { "podman.exe", "docker.exe", "podman", "docker" } : new[] { runtime + ".exe", runtime })
            .Select(FindExecutable).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        if (executable is null) return new { success = false, available = false, runtime, action, message = "Neither Docker nor Podman was found on PATH. Install or expose the runtime, then reopen RunMind." };

        var compose = action.StartsWith("compose_", StringComparison.Ordinal);
        var containerAction = action.StartsWith("container_", StringComparison.Ordinal);
        var containerId = ReadBoundedExactString(args, "container_id", 128);
        if (containerAction && (containerId.Length == 0 || !Regex.IsMatch(containerId, "^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$"))) throw new InvalidDataException("container_id must be one exact container ID or name without path or option characters.");
        var service = ReadString(args, "service", 128);
        var composeFile = string.Empty;
        if (compose)
        {
            composeFile = ReadBoundedExactString(args, "compose_file", 1024);
            if (composeFile.Length == 0 || Path.IsPathRooted(composeFile) || composeFile.Split(new[] { '/', '\\' }, StringSplitOptions.None).Any(part => part.Length == 0 || part is "." or ".."))
                throw new InvalidDataException("compose_file must be a normalized workspace-relative compose.yaml or compose.yml path.");
            var fullCompose = Path.GetFullPath(Path.Combine(working, composeFile));
            EnsureInsideRoot(working, fullCompose);
            EnsureNoReparseAncestors(fullCompose);
            if (!File.Exists(fullCompose) || new FileInfo(fullCompose).Length > 1024 * 1024 || !Regex.IsMatch(Path.GetFileName(fullCompose), @"^(?:compose|docker-compose)\.(?:ya?ml)$", RegexOptions.IgnoreCase))
                throw new InvalidDataException("compose_file must identify an existing compose.yaml or compose.yml file (maximum 1 MB).");
            if (service.Length > 0 && !Regex.IsMatch(service, "^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$")) throw new InvalidDataException("service must be a valid Compose service identifier.");
        }

        var tail = args.TryGetProperty("tail_lines", out var tailValue) && tailValue.TryGetInt32(out var tailParsed) ? Math.Clamp(tailParsed, 1, 500) : 100;
        var command = new System.Collections.Generic.List<string>();
        if (compose)
        {
            command.Add("compose"); command.Add("-f"); command.Add(Path.GetFullPath(Path.Combine(working, composeFile)));
            switch (action)
            {
                case "compose_config": command.Add("config"); command.Add("--no-interpolate"); break;
                case "compose_ps": command.Add("ps"); break;
                case "compose_up": command.AddRange(new[] { "up", "-d" }); if (service.Length > 0) command.Add(service); break;
                case "compose_down": command.Add("down"); break;
                case "compose_build": command.Add("build"); if (service.Length > 0) command.Add(service); break;
                case "compose_logs": command.AddRange(new[] { "logs", "--no-color", "--tail", tail.ToString(System.Globalization.CultureInfo.InvariantCulture) }); if (service.Length > 0) command.Add(service); break;
            }
        }
        else if (containerAction)
        {
            switch (action)
            {
                case "container_inspect": command.AddRange(new[] { "inspect", containerId }); break;
                case "container_logs": command.AddRange(new[] { "logs", "--timestamps", "--tail", tail.ToString(System.Globalization.CultureInfo.InvariantCulture), containerId }); break;
                case "container_stats": command.AddRange(new[] { "stats", "--no-stream", containerId }); break;
                case "container_start": command.AddRange(new[] { "start", containerId }); break;
                case "container_stop": command.AddRange(new[] { "stop", "--time", "10", containerId }); break;
                case "container_restart": command.AddRange(new[] { "restart", "--time", "10", containerId }); break;
            }
        }
        else
        {
            switch (action)
            {
                case "runtime_info": command.Add("version"); break;
                case "ps": command.AddRange(new[] { "ps", "-a" }); break;
                case "images": command.Add("images"); break;
                case "volumes": command.AddRange(new[] { "volume", "ls" }); break;
                case "networks": command.AddRange(new[] { "network", "ls" }); break;
            }
        }

        var start = new ProcessStartInfo(executable!) { WorkingDirectory = working, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in command) start.ArgumentList.Add(argument);
        var timeoutSeconds = args.TryGetProperty("timeout_seconds", out var timeoutValue) && timeoutValue.TryGetInt32(out var parsedTimeout) ? Math.Clamp(parsedTimeout, 5, 180) : 60;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start()) throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        var stdout = ReadBoundedProcessOutputAsync(process.StandardOutput, timeout.Token);
        var stderr = ReadBoundedProcessOutputAsync(process.StandardError, timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); } catch { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException($"Container workbench action '{action}' exceeded {timeoutSeconds} seconds; the runtime process was terminated.");
        }
        var output = await stdout.ConfigureAwait(false); var error = await stderr.ConfigureAwait(false); stopwatch.Stop();
        var safeOutput = RedactContainerOutput(output.text); var safeError = RedactContainerOutput(error.text);
        var stateChanged = action is "compose_up" or "compose_down" or "compose_build" or "container_start" or "container_stop" or "container_restart";
        return new
        {
            success = process.ExitCode == 0, available = true, accepted = true, completed = true,
            runtime = Path.GetFileNameWithoutExtension(executable), action,
            command = string.Join(" ", command.Select(value => value.Contains(' ') ? $"\"{value}\"" : value)),
            workingDirectory = working, exitCode = process.ExitCode, durationMs = stopwatch.ElapsedMilliseconds,
            output = Limit(safeOutput, 256 * 1024), error = Limit(safeError, 16 * 1024),
            outputTruncated = output.truncated, errorTruncated = error.truncated,
            stateChanged,
            verified = process.ExitCode == 0 && !stateChanged,
            verification = stateChanged ? "not_performed" : process.ExitCode == 0 ? "command_result" : "failed",
            message = process.ExitCode != 0 ? "Container runtime command failed; inspect the bounded error output." : stateChanged ? "Container runtime command completed. Resulting container state and health were not independently verified." : "Container runtime query completed successfully."
        };
    }

    private static string RedactContainerOutput(string value)
    {
        var redacted = ContainerUriCredentials.Replace(value, "$1[REDACTED]@");
        redacted = ContainerSecretAssignment.Replace(redacted, "$1[REDACTED]");
        return ContainerBearer.Replace(redacted, "Bearer [REDACTED]");
    }

    private Task<object> SqliteWorkbenchAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var working = GetProcessWorkingDirectory(args);
        var databasePath = ReadBoundedExactString(args, "database_path", 1024);
        if (databasePath.Length == 0 || !Regex.IsMatch(databasePath, @"\.(?:db|sqlite|sqlite3)$", RegexOptions.IgnoreCase) || Path.IsPathRooted(databasePath) || databasePath.Split(new[] { '/', '\\' }, StringSplitOptions.None).Any(part => part.Length == 0 || part is "." or ".."))
            throw new InvalidDataException("database_path must be a normalized workspace-relative .db, .sqlite or .sqlite3 path to an existing SQLite database.");
        var full = Path.GetFullPath(Path.Combine(working, databasePath));
        EnsureInsideRoot(working, full); EnsureNoReparseAncestors(full);
        if (!File.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0 || new FileInfo(full).Length > 512L * 1024 * 1024)
            throw new InvalidDataException("The SQLite database must be an existing regular file inside the selected workspace, no larger than 512 MB.");
        var action = ReadString(args, "action", 20).ToLowerInvariant();
        if (action is not ("inspect" or "tables" or "schema" or "query" or "execute")) throw new InvalidDataException("SQLite action must be inspect, tables, schema, query or execute.");
        var node = FindExecutable("node.exe") ?? FindExecutable("node") ?? throw new FileNotFoundException("Node.js 24 or later is required for the SQLite workbench.");
        var runner = Path.Combine(AppContext.BaseDirectory, "SqliteRunner.cjs");
        if (!File.Exists(runner) || IsReparsePoint(runner)) throw new FileNotFoundException("The bundled SQLite runner is missing or unsafe.");
        return RunDeveloperJsonRunnerAsync(node, runner, args, working, cancellationToken, action == "execute" ? 45 : 30, "SQLite workbench");
    }

    private Task<object> OpenApiContractTestAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var working = GetProcessWorkingDirectory(args);
        var specPath = ReadBoundedExactString(args, "spec_path", 1024);
        if (specPath.Length == 0 || Path.IsPathRooted(specPath) || specPath.Split(new[] { '/', '\\' }, StringSplitOptions.None).Any(part => part.Length == 0 || part is "." or ".."))
            throw new InvalidDataException("spec_path must be a normalized workspace-relative OpenAPI file.");
        var full = Path.GetFullPath(Path.Combine(working, specPath));
        EnsureInsideRoot(working, full); EnsureNoReparseAncestors(full);
        if (!File.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0 || new FileInfo(full).Length > 2 * 1024 * 1024)
            throw new InvalidDataException("OpenAPI specification must be an existing regular file inside the workspace and no larger than 2 MB.");
        var node = FindExecutable("node.exe") ?? FindExecutable("node") ?? throw new FileNotFoundException("Node.js 24 or later is required for OpenAPI contract testing.");
        var runner = Path.Combine(AppContext.BaseDirectory, "OpenApiTestRunner.cjs");
        if (!File.Exists(runner) || IsReparsePoint(runner)) throw new FileNotFoundException("The bundled OpenAPI test runner is missing or unsafe.");
        return RunDeveloperJsonRunnerAsync(node, runner, args, working, cancellationToken, 180, "OpenAPI contract test");
    }

    private async Task<object> RunDeveloperJsonRunnerAsync(string node, string runner, JsonElement args, string working, CancellationToken cancellationToken, int defaultTimeoutSeconds, string label)
    {
        var timeoutSeconds = args.TryGetProperty("timeout_seconds", out var timeoutValue) && timeoutValue.TryGetInt32(out var requested) ? Math.Clamp(requested, 5, 180) : defaultTimeoutSeconds;
        var start = new ProcessStartInfo(node) { WorkingDirectory = working, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = Utf8NoBom, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add(runner);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start()) throw new InvalidOperationException($"Could not start {label}.");
        var stdout = ReadBoundedProcessOutputAsync(process.StandardOutput, timeout.Token); var stderr = ReadBoundedProcessOutputAsync(process.StandardError, timeout.Token);
        try { await process.StandardInput.WriteAsync(args.GetRawText().AsMemory(), timeout.Token).ConfigureAwait(false); process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); } catch { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException($"{label} exceeded {timeoutSeconds} seconds; its Node.js process tree was terminated.");
        }
        catch { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } throw; }
        var output = await stdout.ConfigureAwait(false); var error = await stderr.ConfigureAwait(false); stopwatch.Stop();
        if (output.truncated || string.IsNullOrWhiteSpace(output.text)) throw new InvalidDataException($"{label} returned no bounded result or exceeded the output limit. {Limit(error.text, 1000)}");
        try
        {
            using var document = JsonDocument.Parse(output.text);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{label} returned an invalid result shape.");
            return new { result = document.RootElement.Clone(), exitCode = process.ExitCode, durationMs = stopwatch.ElapsedMilliseconds, stderr = Limit(error.text, 3000), workingDirectory = working };
        }
        catch (JsonException jsonError) { throw new InvalidDataException($"{label} returned invalid JSON: {Limit(output.text, 1200)} {Limit(error.text, 1000)}", jsonError); }
    }
}
