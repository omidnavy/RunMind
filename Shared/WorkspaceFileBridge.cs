#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;

namespace RunMind.Shared;

public sealed partial class AgentHostBridge
{
    private const int WorkspaceTransferChunkBytes = 256 * 1024;
    private const long MaximumWorkspaceWriteBytes = 100L * 1024 * 1024;
    private const int MaximumWorkspaceDirectoryEntries = 10000;
    private static readonly ConcurrentDictionary<string, WorkspaceWriteSession> WorkspaceWriteSessions = new(StringComparer.Ordinal);

    private sealed class WorkspaceWriteSession
    {
        public required string Id { get; init; }
        public required string ContextId { get; init; }
        public required string WorkspaceId { get; init; }
        public required string WorkspaceRoot { get; init; }
        public required string DestinationPath { get; init; }
        public required string TemporaryPath { get; init; }
        public required bool Overwrite { get; init; }
        public required FileStream Stream { get; init; }
        public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
        public long BytesWritten { get; set; }
        public readonly object Sync = new();
    }

    private string RequireWorkspaceRoot(string contextId, string workspaceId)
    {
        lock (_workspaceStateLock)
        {
            EnsureProcessWorkspaceForContext(contextId);
            var currentWorkspaceId = GetProcessWorkspaceId(contextId);
            if (string.IsNullOrWhiteSpace(workspaceId) || !string.Equals(currentWorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This workspace handle is stale or belongs to a different selected folder. Refresh workspace access and retry the operation.");
            var root = Path.GetFullPath(_processWorkspaceRoot ?? throw new InvalidOperationException("Select a workspace first."));
            if (!Directory.Exists(root) || IsReparsePoint(root)) throw new InvalidOperationException("The selected workspace is no longer available or is a reparse point. Select it again.");
            EnsureNoReparseAncestors(root);
            return root;
        }
    }

    private static string[] WorkspaceSegments(string relativePath, bool allowRoot = false)
    {
        var raw = (relativePath ?? string.Empty).Trim().Replace('\\', '/');
        if (raw.Length == 0) return allowRoot ? Array.Empty<string>() : throw new InvalidDataException("A non-empty workspace-relative path is required.");
        if (raw.StartsWith('/') || Path.IsPathRooted(raw) || raw.Contains(':')) throw new InvalidDataException("Workspace paths must be relative and cannot contain drive or alternate-stream syntax.");
        var segments = raw.Split('/');
        if (segments.Length > 64 || segments.Any(part => part.Length == 0 || part is "." or ".." || part.Length > 255 || part.Any(char.IsControl)))
            throw new InvalidDataException("The workspace path contains an empty, unsafe, or overlong segment.");
        foreach (var segment in segments)
        {
            if (!string.Equals(segment, segment.TrimEnd(' ', '.'), StringComparison.Ordinal))
                throw new InvalidDataException("Workspace path segments cannot end in spaces or periods.");
            var stem = segment.Split('.')[0];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                || System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(COM|LPT)[1-9]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new InvalidDataException("Windows device-name path segments are not allowed in a workspace operation.");
        }
        return segments;
    }

    private static string WorkspaceCombine(string root, string relativePath, bool allowRoot = false)
    {
        var segments = WorkspaceSegments(relativePath, allowRoot);
        var candidate = segments.Length == 0 ? root : Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) && !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The requested path escapes the selected workspace.");
        EnsureNoReparseAncestors(candidate);
        return candidate;
    }

    private object WorkspaceFileStat(JsonElement args)
    {
        var root = RequireWorkspaceRoot(ReadString(args, "contextId", 64), ReadString(args, "workspaceId", 64));
        var relative = ReadString(args, "path", 2048);
        var path = WorkspaceCombine(root, relative, allowRoot: true);
        if (Directory.Exists(path))
        {
            var info = new DirectoryInfo(path);
            return new { exists = true, kind = "directory", name = info.Name.Length == 0 ? Path.GetFileName(root) : info.Name, size = 0L, lastModifiedMs = info.LastWriteTimeUtc.Ticks / TimeSpan.TicksPerMillisecond };
        }
        if (File.Exists(path))
        {
            if (IsReparsePoint(path)) throw new InvalidDataException("Workspace file access does not follow symbolic links or reparse points.");
            var info = new FileInfo(path);
            return new { exists = true, kind = "file", name = info.Name, size = info.Length, lastModifiedMs = info.LastWriteTimeUtc.Ticks / TimeSpan.TicksPerMillisecond };
        }
        return new { exists = false, kind = "missing", name = Path.GetFileName(path), size = 0L, lastModifiedMs = 0L };
    }

    private object WorkspaceDirectoryList(JsonElement args)
    {
        var root = RequireWorkspaceRoot(ReadString(args, "contextId", 64), ReadString(args, "workspaceId", 64));
        var relative = ReadString(args, "path", 2048);
        var directory = WorkspaceCombine(root, relative, allowRoot: true);
        if (!Directory.Exists(directory) || IsReparsePoint(directory)) throw new DirectoryNotFoundException("The requested workspace directory does not exist or is not a normal directory.");
        var offset = args.TryGetProperty("offset", out var offsetValue) && offsetValue.TryGetInt32(out var requestedOffset) ? Math.Clamp(requestedOffset, 0, MaximumWorkspaceDirectoryEntries) : 0;
        var limit = args.TryGetProperty("limit", out var limitValue) && limitValue.TryGetInt32(out var requestedLimit) ? Math.Clamp(requestedLimit, 1, 250) : 200;
        var all = new List<(string Name, string Kind, long Size, long Modified)>();
        var skippedReparsePoints = 0;
        var scannedEntries = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (++scannedEntries > MaximumWorkspaceDirectoryEntries) break;
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) { skippedReparsePoints++; continue; }
            var name = Path.GetFileName(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                var info = new DirectoryInfo(entry);
                all.Add((name, "directory", 0, info.LastWriteTimeUtc.Ticks / TimeSpan.TicksPerMillisecond));
            }
            else
            {
                var info = new FileInfo(entry);
                all.Add((name, "file", info.Length, info.LastWriteTimeUtc.Ticks / TimeSpan.TicksPerMillisecond));
            }
        }
        var truncatedBySafety = scannedEntries > MaximumWorkspaceDirectoryEntries;
        all = all.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Name, StringComparer.Ordinal).ToList();
        var page = all.Skip(offset).Take(limit).Select(item => new { name = item.Name, kind = item.Kind, size = item.Size, lastModifiedMs = item.Modified }).ToArray();
        var hasMore = offset + page.Length < all.Count || truncatedBySafety;
        return new { path = relative, entries = page, offset, limit, totalEntries = all.Count, hasMore, truncated = truncatedBySafety, skippedReparsePoints };
    }

    private object WorkspaceDirectoryCreate(JsonElement args)
    {
        var root = RequireWorkspaceRoot(ReadString(args, "contextId", 64), ReadString(args, "workspaceId", 64));
        var relative = ReadString(args, "path", 2048);
        var path = WorkspaceCombine(root, relative);
        var parent = Path.GetDirectoryName(path) ?? throw new InvalidDataException("A workspace directory parent is required.");
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("The parent directory does not exist. Create parent directories explicitly first.");
        EnsureNoReparseAncestors(parent);
        if (File.Exists(path)) throw new IOException("A file with that name already exists.");
        Directory.CreateDirectory(path);
        return new { created = true, path = relative, kind = "directory" };
    }

    private object WorkspaceEntryRemove(JsonElement args)
    {
        var root = RequireWorkspaceRoot(ReadString(args, "contextId", 64), ReadString(args, "workspaceId", 64));
        var relative = ReadString(args, "path", 2048);
        var recursive = args.TryGetProperty("recursive", out var recursiveValue) && recursiveValue.ValueKind == JsonValueKind.True;
        var path = WorkspaceCombine(root, relative);
        if (Directory.Exists(path))
        {
            if (IsReparsePoint(path)) throw new InvalidDataException("Refusing to remove a directory reparse point.");
            if (recursive) DeleteWorkspaceDirectoryWithoutFollowingReparsePoints(path);
            else Directory.Delete(path, false);
            return new { removed = true, kind = "directory", path = relative };
        }
        if (File.Exists(path))
        {
            if (IsReparsePoint(path)) throw new InvalidDataException("Refusing to remove a file reparse point.");
            File.Delete(path);
            return new { removed = true, kind = "file", path = relative };
        }
        throw new FileNotFoundException("The workspace entry does not exist.");
    }

    private static void DeleteWorkspaceDirectoryWithoutFollowingReparsePoints(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(entry, false); else File.Delete(entry);
            }
            else if ((attributes & FileAttributes.Directory) != 0) DeleteWorkspaceDirectoryWithoutFollowingReparsePoints(entry);
            else File.Delete(entry);
        }
        Directory.Delete(path, false);
    }

    private object WorkspaceFileReadChunk(JsonElement args)
    {
        var root = RequireWorkspaceRoot(ReadString(args, "contextId", 64), ReadString(args, "workspaceId", 64));
        var path = WorkspaceCombine(root, ReadString(args, "path", 2048));
        if (!File.Exists(path) || IsReparsePoint(path)) throw new FileNotFoundException("The workspace file does not exist or is not a normal file.");
        var offset = args.TryGetProperty("offset", out var offsetValue) && offsetValue.TryGetInt64(out var requestedOffset) ? Math.Max(0, requestedOffset) : 0;
        var count = args.TryGetProperty("count", out var countValue) && countValue.TryGetInt32(out var requestedCount) ? Math.Clamp(requestedCount, 1, WorkspaceTransferChunkBytes) : WorkspaceTransferChunkBytes;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (offset > stream.Length) throw new InvalidDataException("The file read offset is beyond the end of the file.");
        stream.Position = offset;
        var buffer = new byte[(int)Math.Min(count, stream.Length - offset)];
        stream.ReadExactly(buffer);
        var info = new FileInfo(path);
        var read = buffer.Length;
        return new { data = Convert.ToBase64String(buffer), offset, bytesRead = read, size = stream.Length,
            lastModifiedMs = info.LastWriteTimeUtc.Ticks / TimeSpan.TicksPerMillisecond, eof = offset + read >= stream.Length };
    }

    private object WorkspaceWriteBegin(JsonElement args)
    {
        var contextId = ReadString(args, "contextId", 64);
        var workspaceId = ReadString(args, "workspaceId", 64);
        var root = RequireWorkspaceRoot(contextId, workspaceId);
        var relative = ReadString(args, "path", 2048);
        var destination = WorkspaceCombine(root, relative);
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidDataException("A workspace file parent is required.");
        if (!Directory.Exists(parent) || IsReparsePoint(parent)) throw new DirectoryNotFoundException("The workspace file's parent directory does not exist or is unsafe.");
        var overwrite = args.TryGetProperty("overwrite", out var overwriteValue) && overwriteValue.ValueKind == JsonValueKind.True;
        if (Directory.Exists(destination)) throw new IOException("A directory already exists at the requested file path.");
        if (File.Exists(destination) && !overwrite) throw new IOException("The target file already exists and overwrite was not approved.");
        if (File.Exists(destination) && IsReparsePoint(destination)) throw new InvalidDataException("Refusing to overwrite a reparse-point file.");
        CleanupExpiredWorkspaceWrites();
        var id = Guid.NewGuid().ToString("N");
        var temp = Path.Combine(parent, $".runmind-write-{id}.tmp");
        var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan);
        var session = new WorkspaceWriteSession { Id = id, ContextId = contextId, WorkspaceId = workspaceId, WorkspaceRoot = root, DestinationPath = destination, TemporaryPath = temp, Overwrite = overwrite, Stream = stream };
        if (!WorkspaceWriteSessions.TryAdd(id, session)) { stream.Dispose(); File.Delete(temp); throw new IOException("Could not register the workspace write session."); }
        return new { writeId = id, maximumBytes = MaximumWorkspaceWriteBytes, chunkBytes = WorkspaceTransferChunkBytes };
    }

    private object WorkspaceWriteChunk(JsonElement args)
    {
        var id = ReadString(args, "writeId", 64);
        if (!WorkspaceWriteSessions.TryGetValue(id, out var session)) throw new KeyNotFoundException("Workspace write session was not found or has expired.");
        var contextId = ReadString(args, "contextId", 64);
        if (!string.Equals(contextId, session.ContextId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(ReadString(args, "workspaceId", 64), session.WorkspaceId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Workspace write session belongs to another conversation or selected workspace.");
        var offset = args.TryGetProperty("offset", out var offsetValue) && offsetValue.TryGetInt64(out var requestedOffset) ? requestedOffset : -1;
        var encoded = ReadString(args, "data", WorkspaceTransferChunkBytes * 2);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded); } catch (FormatException) { throw new InvalidDataException("Workspace write chunk is not valid base64."); }
        if (bytes.Length > WorkspaceTransferChunkBytes) throw new InvalidDataException("Workspace write chunk exceeds the 256 KiB limit.");
        lock (session.Sync)
        {
            if (offset != session.BytesWritten) throw new InvalidDataException("Workspace write chunks must be sequential and cannot overlap or skip bytes.");
            if (session.BytesWritten + bytes.Length > MaximumWorkspaceWriteBytes) throw new InvalidDataException("Workspace writes are limited to 100 MiB per file.");
            session.Stream.Write(bytes, 0, bytes.Length);
            session.BytesWritten += bytes.Length;
        }
        return new { writeId = id, bytesWritten = session.BytesWritten };
    }

    private object WorkspaceWriteCommit(JsonElement args)
    {
        var id = ReadString(args, "writeId", 64);
        if (!WorkspaceWriteSessions.TryGetValue(id, out var session)) throw new KeyNotFoundException("Workspace write session was not found or has expired.");
        var contextId = ReadString(args, "contextId", 64);
        if (!string.Equals(contextId, session.ContextId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(ReadString(args, "workspaceId", 64), session.WorkspaceId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Workspace write session belongs to another conversation or selected workspace.");
        try
        {
            lock (session.Sync)
            {
                session.Stream.Flush(true);
                session.Stream.Dispose();
                var root = RequireWorkspaceRoot(contextId, session.WorkspaceId);
                if (!string.Equals(root, session.WorkspaceRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The selected workspace changed while the write was in progress. The temporary file was not committed.");
                EnsureNoReparseAncestors(session.DestinationPath);
                if (Directory.Exists(session.DestinationPath)) throw new IOException("A directory now exists at the target path.");
                if (File.Exists(session.DestinationPath) && !session.Overwrite) throw new IOException("The target file appeared while writing and overwrite was not approved.");
                if (File.Exists(session.DestinationPath) && IsReparsePoint(session.DestinationPath)) throw new InvalidDataException("The target became a reparse point; refusing to replace it.");
                File.Move(session.TemporaryPath, session.DestinationPath, session.Overwrite);
                WorkspaceWriteSessions.TryRemove(id, out _);
                return new { committed = true, bytes = session.BytesWritten };
            }
        }
        catch
        {
            WorkspaceWriteSessions.TryRemove(id, out _);
            lock (session.Sync)
            {
                try { session.Stream.Dispose(); } catch { }
                try { if (File.Exists(session.TemporaryPath) && !IsReparsePoint(session.TemporaryPath)) File.Delete(session.TemporaryPath); } catch { }
            }
            throw;
        }
    }

    private object WorkspaceWriteAbort(JsonElement args)
    {
        var id = ReadString(args, "writeId", 64);
        if (!WorkspaceWriteSessions.TryRemove(id, out var session)) return new { aborted = true, alreadyClosed = true };
        if (!string.Equals(ReadString(args, "contextId", 64), session.ContextId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(ReadString(args, "workspaceId", 64), session.WorkspaceId, StringComparison.OrdinalIgnoreCase))
        {
            WorkspaceWriteSessions.TryAdd(id, session);
            throw new InvalidOperationException("Workspace write session belongs to another conversation or selected workspace.");
        }
        lock (session.Sync) { try { session.Stream.Dispose(); } catch { } try { File.Delete(session.TemporaryPath); } catch { } }
        return new { aborted = true, alreadyClosed = false };
    }

    private static void AbortWorkspaceWritesForContext(string contextId)
    {
        foreach (var pair in WorkspaceWriteSessions.Where(item => string.Equals(item.Value.ContextId, contextId, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            if (!WorkspaceWriteSessions.TryRemove(pair.Key, out var session)) continue;
            lock (session.Sync)
            {
                try { session.Stream.Dispose(); } catch { }
                try { if (File.Exists(session.TemporaryPath) && !IsReparsePoint(session.TemporaryPath)) File.Delete(session.TemporaryPath); } catch { }
            }
        }
    }

    private static void CleanupExpiredWorkspaceWrites()
    {
        foreach (var pair in WorkspaceWriteSessions.Where(item => DateTime.UtcNow - item.Value.CreatedAtUtc > TimeSpan.FromMinutes(15)).ToArray())
        {
            if (!WorkspaceWriteSessions.TryRemove(pair.Key, out var session)) continue;
            lock (session.Sync) { try { session.Stream.Dispose(); } catch { } try { File.Delete(session.TemporaryPath); } catch { } }
        }
    }
}
