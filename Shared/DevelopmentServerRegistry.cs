#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace RunMind.Shared;

/// <summary>
/// Durable ownership records for RunMind-launched development servers. Recovery is
/// identity-verified and observational: unknown or mismatched processes are never killed.
/// </summary>
public sealed partial class AgentHostBridge
{
    private const string DevelopmentServerRegistryMutexName = @"Local\RunMind.DevelopmentServerRegistry.v1";
    private const int MaximumPersistedDevelopmentServersPerWorkspace = 64;
    private static readonly JsonSerializerOptions DevelopmentServerJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class DevelopmentServerRecord
    {
        public int Version { get; set; } = 1;
        public string ContextId { get; set; } = string.Empty;
        public string WorkspaceSelectionId { get; set; } = string.Empty;
        public string ServerId { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public long ProcessStartTimeUtcTicks { get; set; }
        public string ExecutablePath { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        public string Script { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
        public string? ReadinessUrl { get; set; }
        public int? Port { get; set; }
        public DateTime? StopRequestedAtUtc { get; set; }
    }

    private sealed class DevelopmentServerIdentityResult
    {
        public bool Verified { get; init; }
        public bool Missing { get; init; }
        public string Reason { get; init; } = string.Empty;
    }

    private static string DevelopmentServerRegistryRoot
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) throw new InvalidOperationException("Local AppData is unavailable; development-server ownership cannot be persisted.");
            return Path.Combine(local, "RunMind", "DevelopmentServers");
        }
    }

    private static string DevelopmentServerContextDirectory(string contextId)
    {
        if (!Guid.TryParse(contextId, out var parsed)) throw new InvalidDataException("A valid conversation context is required for development-server ownership.");
        return Path.Combine(DevelopmentServerRegistryRoot, parsed.ToString("N"));
    }

    private static string DevelopmentServerRecordPath(string contextId, string serverId)
    {
        if (!Guid.TryParse(serverId, out var parsed)) throw new InvalidDataException("A valid development-server session ID is required.");
        return Path.Combine(DevelopmentServerContextDirectory(contextId), parsed.ToString("N") + ".json");
    }

    private static T WithDevelopmentServerRegistryLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, DevelopmentServerRegistryMutexName);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new TimeoutException("The development-server ownership registry is busy in another RunMind host.");
            return action();
        }
        finally
        {
            if (acquired) { try { mutex.ReleaseMutex(); } catch (ApplicationException) { } }
        }
    }

    private static void EnsureDevelopmentServerRegistryDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        EnsureNoReparseAncestors(directory);
        if (IsReparsePoint(directory)) throw new InvalidDataException("The development-server registry cannot be a symbolic link or reparse point.");
    }

    private static void WriteDevelopmentServerRecordFile(string path, DevelopmentServerRecord record)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidDataException("The development-server record directory is invalid.");
        EnsureDevelopmentServerRegistryDirectory(directory);
        if (File.Exists(path) && IsReparsePoint(path)) throw new InvalidDataException("The development-server ownership record cannot be a reparse point.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(record), new UTF8Encoding(false));
            EnsureNoReparseAncestors(directory);
            if (File.Exists(path)) File.Move(temporary, path, true); else File.Move(temporary, path);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
    }

    private static void PersistDevelopmentServerRecord(DevelopmentServerSession session)
    {
        if (session.ProcessStartTimeUtcTicks <= 0 || (SafeProcessId(session.Process) ?? 0) <= 0)
            throw new InvalidOperationException("The development-server process identity was incomplete; ownership was not persisted.");
        var record = new DevelopmentServerRecord
        {
            ContextId = session.ContextId,
            WorkspaceSelectionId = session.WorkspaceSelectionId,
            ServerId = session.Id,
            ProcessId = SafeProcessId(session.Process) ?? 0,
            ProcessStartTimeUtcTicks = session.ProcessStartTimeUtcTicks,
            ExecutablePath = Path.GetFullPath(session.ExecutablePath),
            WorkingDirectory = Path.GetFullPath(session.WorkingDirectory),
            Script = session.Script,
            StartedAtUtc = session.StartedAtUtc,
            ReadinessUrl = session.ReadinessUrl,
            Port = session.Port
        };
        var path = DevelopmentServerRecordPath(session.ContextId, session.Id);
        WithDevelopmentServerRegistryLock(() => { WriteDevelopmentServerRecordFile(path, record); return true; });
    }

    private static DevelopmentServerRecord? ReadDevelopmentServerRecord(string path)
    {
        if (!File.Exists(path) || IsReparsePoint(path)) return null;
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > 16 * 1024) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return JsonSerializer.Deserialize<DevelopmentServerRecord>(stream, DevelopmentServerJsonOptions);
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static DevelopmentServerRecord? ReadDevelopmentServerRecord(string contextId, string serverId)
    {
        var path = DevelopmentServerRecordPath(contextId, serverId);
        return WithDevelopmentServerRegistryLock(() => ReadDevelopmentServerRecord(path));
    }

    private static void RemoveDevelopmentServerRecord(string contextId, string serverId)
    {
        try
        {
            var path = DevelopmentServerRecordPath(contextId, serverId);
            WithDevelopmentServerRegistryLock(() =>
            {
                if (File.Exists(path) && !IsReparsePoint(path)) File.Delete(path);
                return true;
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or TimeoutException) { }
    }

    private static DevelopmentServerIdentityResult VerifyDevelopmentServerIdentity(Process process, DevelopmentServerRecord record)
    {
        try
        {
            if (process.HasExited) return new DevelopmentServerIdentityResult { Missing = true, Reason = "The recorded process has exited." };
            var processId = process.Id;
            if (processId != record.ProcessId) return new DevelopmentServerIdentityResult { Reason = "The process ID no longer matches the ownership record." };
            var executable = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executable)) return new DevelopmentServerIdentityResult { Reason = "Windows did not expose the process executable path; ownership could not be verified." };
            var normalizedExecutable = Path.GetFullPath(executable);
            var expectedExecutable = Path.GetFullPath(record.ExecutablePath);
            if (!string.Equals(normalizedExecutable, expectedExecutable, StringComparison.OrdinalIgnoreCase))
                return new DevelopmentServerIdentityResult { Reason = "The PID now belongs to a different executable; the process was not adopted." };
            var startTicks = process.StartTime.ToUniversalTime().Ticks;
            if (Math.Abs(startTicks - record.ProcessStartTimeUtcTicks) > TimeSpan.TicksPerSecond * 2)
                return new DevelopmentServerIdentityResult { Reason = "The process start time does not match; the process may have reused the recorded PID and was not adopted." };
            return new DevelopmentServerIdentityResult { Verified = true, Reason = "PID, process start time and executable path match the persisted RunMind launch record." };
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or ArgumentException)
        {
            return new DevelopmentServerIdentityResult { Reason = "Windows could not verify the recorded process identity: " + error.Message };
        }
    }

    private static bool IsDevelopmentServerRecordForWorkspace(DevelopmentServerRecord record, string contextId, string root, string selectionId)
    {
        try
        {
            if (record.Version != 1 || !string.Equals(record.ContextId, contextId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(record.WorkspaceSelectionId, selectionId, StringComparison.OrdinalIgnoreCase)) return false;
            if (!Guid.TryParse(record.ServerId, out _) || record.ProcessId <= 0 || record.ProcessStartTimeUtcTicks <= 0
                || !Guid.TryParse(record.WorkspaceSelectionId, out _) || string.IsNullOrWhiteSpace(record.WorkingDirectory) || string.IsNullOrWhiteSpace(record.ExecutablePath)) return false;
            var recordedRoot = Path.GetFullPath(record.WorkingDirectory);
            var normalizedRoot = Path.GetFullPath(root);
            var prefix = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!string.Equals(recordedRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase)
                && !recordedRoot.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (!Directory.Exists(recordedRoot) || IsReparsePoint(recordedRoot)) return false;
            EnsureNoReparseAncestors(recordedRoot);
            var executable = Path.GetFullPath(record.ExecutablePath);
            if (!File.Exists(executable) || IsReparsePoint(executable)) return false;
            return true;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            return false;
        }
    }

    private (int Recovered, string[] Warnings) ReconcileDevelopmentServerSessions(string contextId, string workspaceRoot, string selectionId)
    {
        var warnings = new List<string>();
        var directory = DevelopmentServerContextDirectory(contextId);
        if (!Directory.Exists(directory)) return (0, Array.Empty<string>());
        EnsureNoReparseAncestors(directory);
        if (IsReparsePoint(directory)) throw new InvalidDataException("The persisted development-server registry is a reparse point.");
        var paths = Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).Take(MaximumPersistedDevelopmentServersPerWorkspace).ToArray();
        var recovered = 0;
        foreach (var path in paths)
        {
            if (IsReparsePoint(path)) continue;
            var record = WithDevelopmentServerRegistryLock(() => ReadDevelopmentServerRecord(path));
            if (record is null) continue;
            if (!IsDevelopmentServerRecordForWorkspace(record, contextId, workspaceRoot, selectionId)) continue;
            if (DevelopmentServers.ContainsKey(record.ServerId)) continue;
            Process? process = null;
            try { process = Process.GetProcessById(record.ProcessId); }
            catch (ArgumentException) { RemoveDevelopmentServerRecord(contextId, record.ServerId); continue; }
            catch (InvalidOperationException) { RemoveDevelopmentServerRecord(contextId, record.ServerId); continue; }
            catch (System.ComponentModel.Win32Exception error) { warnings.Add($"Session {record.ServerId}: Windows could not inspect the recorded PID ({error.Message}). The record was retained and no process was adopted."); continue; }
            catch (UnauthorizedAccessException error) { warnings.Add($"Session {record.ServerId}: access to the recorded PID was denied ({error.Message}). The record was retained and no process was adopted."); continue; }
            var identity = VerifyDevelopmentServerIdentity(process, record);
            if (identity.Missing)
            {
                process.Dispose();
                RemoveDevelopmentServerRecord(contextId, record.ServerId);
                continue;
            }
            if (!identity.Verified)
            {
                warnings.Add($"Session {record.ServerId}: {identity.Reason}");
                process.Dispose();
                continue;
            }
            var session = new DevelopmentServerSession
            {
                Id = record.ServerId,
                Script = record.Script,
                WorkingDirectory = record.WorkingDirectory,
                ContextId = record.ContextId,
                WorkspaceSelectionId = record.WorkspaceSelectionId,
                ExecutablePath = record.ExecutablePath,
                ProcessStartTimeUtcTicks = record.ProcessStartTimeUtcTicks,
                Process = process,
                StartedAtUtc = record.StartedAtUtc,
                ReadinessUrl = record.ReadinessUrl,
                Port = record.Port,
                Recovered = true,
                LogsAvailable = false,
                RegistryPersisted = true
            };
            process.Exited += (_, _) => RemoveDevelopmentServerRecord(session.ContextId, session.Id);
            process.EnableRaisingEvents = true;
            if (SafeHasExited(process))
            {
                process.Dispose();
                RemoveDevelopmentServerRecord(contextId, record.ServerId);
                continue;
            }
            if (DevelopmentServers.TryAdd(session.Id, session)) recovered++;
            else process.Dispose();
        }
        return (recovered, warnings.Take(20).ToArray());
    }

    private bool TryClaimDevelopmentServerStop(DevelopmentServerSession session, out Process verifiedProcess, out string reason)
    {
        verifiedProcess = session.Process;
        reason = string.Empty;
        try
        {
            return WithDevelopmentServerRegistryLock(() =>
            {
                var path = DevelopmentServerRecordPath(session.ContextId, session.Id);
                var record = ReadDevelopmentServerRecord(path);
                if (record is null) { reason = "The persisted RunMind ownership record is missing or invalid; no process was stopped."; return false; }
                var currentRoot = RequireWorkspaceRoot(session.ContextId);
                var currentSelection = RequireWorkspaceSelectionId(session.ContextId);
                if (!string.Equals(currentSelection, session.WorkspaceSelectionId, StringComparison.OrdinalIgnoreCase)
                    || !IsDevelopmentServerRecordForWorkspace(record, session.ContextId, currentRoot, session.WorkspaceSelectionId))
                { reason = "The persisted ownership record no longer matches the current workspace selection; no process was stopped."; return false; }
                if (record.ProcessId != session.Process.Id || record.ProcessStartTimeUtcTicks != session.ProcessStartTimeUtcTicks
                    || !string.Equals(Path.GetFullPath(record.ExecutablePath), Path.GetFullPath(session.ExecutablePath), StringComparison.OrdinalIgnoreCase))
                { reason = "The session identity differs from the persisted ownership record; no process was stopped."; return false; }
                if (record.StopRequestedAtUtc.HasValue && DateTime.UtcNow - record.StopRequestedAtUtc.Value < TimeSpan.FromSeconds(30))
                { reason = "Another RunMind host has already requested termination for this server session."; return false; }
                var identity = VerifyDevelopmentServerIdentity(session.Process, record);
                if (!identity.Verified) { reason = identity.Reason + " No process was stopped."; return false; }
                record.StopRequestedAtUtc = DateTime.UtcNow;
                WriteDevelopmentServerRecordFile(path, record);
                reason = "Process identity verified and stop request claimed.";
                return true;
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or TimeoutException or InvalidOperationException)
        {
            reason = "Could not safely verify/claim the RunMind process record: " + error.Message;
            return false;
        }
    }

    private static void ClearDevelopmentServerStopClaim(DevelopmentServerSession session)
    {
        try
        {
            WithDevelopmentServerRegistryLock(() =>
            {
                var path = DevelopmentServerRecordPath(session.ContextId, session.Id);
                var record = ReadDevelopmentServerRecord(path);
                if (record is not null && record.ProcessId == session.Process.Id && record.ProcessStartTimeUtcTicks == session.ProcessStartTimeUtcTicks)
                {
                    record.StopRequestedAtUtc = null;
                    WriteDevelopmentServerRecordFile(path, record);
                }
                return true;
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or TimeoutException) { }
    }
}
