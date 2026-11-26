#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace RunMind.Shared;

/// <summary>
/// Native, path-contained filesystem for the selected Agent workspace. The native host
/// owns the absolute root; WebView code receives only relative paths and bounded chunks.
/// </summary>
public sealed partial class AgentHostBridge
{
    private const int WorkspaceListLimit = 1000;
    private const int WorkspaceReadChunkBytes = 256 * 1024;
    private const int WorkspaceWriteChunkBytes = 256 * 1024;
    private const long WorkspaceFileLimitBytes = 128L * 1024 * 1024;
    private const string WorkspaceRegistryMutexName = @"Local\RunMind.WorkspaceRegistry.v1";

    private sealed class WorkspaceWriteSession
    {
        public required string Id { get; init; }
        public required string ContextId { get; init; }
        public required string SelectionId { get; init; }
        public required string WorkspaceRoot { get; init; }
        public required string TargetPath { get; init; }
        public required string TemporaryPath { get; init; }
        public required FileStream Stream { get; init; }
    }

    private readonly ConcurrentDictionary<string, WorkspaceWriteSession> _workspaceWriteSessions = new(StringComparer.Ordinal);

    private static string WorkspaceRegistryDirectory
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) throw new InvalidOperationException("Local AppData is unavailable; the workspace selection cannot be persisted.");
            return Path.Combine(local, "RunMind", "Workspaces");
        }
    }

    private static string WorkspaceRegistryPath(string contextId)
    {
        if (!Guid.TryParse(contextId, out var id)) throw new InvalidDataException("A valid conversation workspace context is required.");
        return Path.Combine(WorkspaceRegistryDirectory, id.ToString("N") + ".json");
    }

    private static T WithWorkspaceRegistryMutex<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, WorkspaceRegistryMutexName);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new TimeoutException("The workspace registry is busy in another RunMind host. Try again shortly.");
            return action();
        }
        finally { if (acquired) { try { mutex.ReleaseMutex(); } catch (ApplicationException) { } } }
    }

    private sealed class PersistedWorkspaceEntry
    {
        public int Version { get; set; } = 2;
        public string ContextId { get; set; } = string.Empty;
        public string Root { get; set; } = string.Empty;
        public string SelectionId { get; set; } = string.Empty;
        public DateTime SelectedAtUtc { get; set; }
    }

    private static string PersistWorkspaceRoot(string contextId, string root)
    {
        var path = WorkspaceRegistryPath(contextId);
        var normalized = Path.GetFullPath(root);
        if (!Directory.Exists(normalized) || IsReparsePoint(normalized)) throw new InvalidDataException("The selected workspace is not a normal directory.");
        EnsureNoReparseAncestors(normalized);
        var selectionId = Guid.NewGuid().ToString("N");
        WithWorkspaceRegistryMutex(() =>
        {
            Directory.CreateDirectory(WorkspaceRegistryDirectory);
            EnsureNoReparseAncestors(WorkspaceRegistryDirectory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var entry = new PersistedWorkspaceEntry { Version = 2, ContextId = contextId, Root = normalized, SelectionId = selectionId, SelectedAtUtc = DateTime.UtcNow };
                File.WriteAllText(temporary, JsonSerializer.Serialize(entry), new UTF8Encoding(false));
                if (File.Exists(path)) File.Move(temporary, path, true); else File.Move(temporary, path);
            }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
            return true;
        });
        return selectionId;
    }

    private static PersistedWorkspaceEntry? ReadPersistedWorkspaceEntry(string contextId)
    {
        var path = WorkspaceRegistryPath(contextId);
        return WithWorkspaceRegistryMutex(() =>
        {
            if (!File.Exists(path) || IsReparsePoint(path)) return null;
            if (new FileInfo(path).Length > 16 * 1024) return null;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
                var root = document.RootElement;
                if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number) return null;
                var versionNumber = version.GetInt32();
                if (versionNumber is not (1 or 2)) return null;
                if (!root.TryGetProperty("contextId", out var context) || context.ValueKind != JsonValueKind.String || !string.Equals(context.GetString(), contextId, StringComparison.OrdinalIgnoreCase)) return null;
                if (!root.TryGetProperty("root", out var value) || value.ValueKind != JsonValueKind.String) return null;
                var selectedRoot = Path.GetFullPath(value.GetString() ?? string.Empty);
                if (!Directory.Exists(selectedRoot) || IsReparsePoint(selectedRoot)) return null;
                EnsureNoReparseAncestors(selectedRoot);
                var selectionId = root.TryGetProperty("selectionId", out var selection) && selection.ValueKind == JsonValueKind.String
                    ? selection.GetString() ?? string.Empty : string.Empty;
                if (!Guid.TryParse(selectionId, out _))
                {
                    selectionId = Guid.NewGuid().ToString("N");
                    // Migrate legacy version-1 workspaces without changing their selected path.
                    var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        var migrated = new PersistedWorkspaceEntry { Version = 2, ContextId = contextId, Root = selectedRoot, SelectionId = selectionId, SelectedAtUtc = DateTime.UtcNow };
                        File.WriteAllText(temporary, JsonSerializer.Serialize(migrated), new UTF8Encoding(false));
                        File.Move(temporary, path, true);
                    }
                    finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
                }
                return new PersistedWorkspaceEntry { Version = 2, ContextId = contextId, Root = selectedRoot, SelectionId = selectionId, SelectedAtUtc = DateTime.UtcNow };
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or FormatException or OverflowException) { return null; }
        });
    }

    private static string? ReadPersistedWorkspaceRoot(string contextId) => ReadPersistedWorkspaceEntry(contextId)?.Root;

    private object PersistedWorkspaceStatus(string contextId)
    {
        var entry = ReadPersistedWorkspaceEntry(contextId);
        if (entry is null) return new { selected = false, folder = (string?)null, workspaceId = contextId, selectionId = (string?)null };
        var git = Path.Combine(entry.Root, ".git");
        var isGit = Directory.Exists(git) && !IsReparsePoint(git);
        return new { selected = true, folder = Path.GetFileName(entry.Root), workspaceId = contextId, selectionId = entry.SelectionId, gitRepository = isGit,
            note = "The native host owns the selected workspace. File access, Git, and approved project execution share this root." };
    }

    private object ClearPersistedWorkspace(string contextId)
    {
        var path = WorkspaceRegistryPath(contextId);
        WithWorkspaceRegistryMutex(() => { try { if (File.Exists(path) && !IsReparsePoint(path)) File.Delete(path); } catch (FileNotFoundException) { } return true; });
        foreach (var item in _workspaceWriteSessions.Values.Where(value => value.ContextId == contextId).ToArray()) AbortWorkspaceWrite(item.Id, contextId, item.SelectionId);
        return new { cleared = true, workspaceId = contextId };
    }

    private string RequireWorkspaceRoot(string contextId)
    {
        var entry = ReadPersistedWorkspaceEntry(contextId);
        if (entry is null) throw new InvalidOperationException("Select the project workspace once. File tools and native project tools now use the same workspace selection.");
        var root = Path.GetFullPath(entry.Root);
        EnsureNoReparseAncestors(root);
        if (!Directory.Exists(root) || IsReparsePoint(root)) throw new InvalidDataException("The selected workspace root no longer exists or became a reparse point; access was refused.");
        return root;
    }

    private string RequireWorkspaceSelectionId(string contextId)
    {
        var entry = ReadPersistedWorkspaceEntry(contextId);
        if (entry is null || !Guid.TryParse(entry.SelectionId, out _)) throw new InvalidOperationException("The selected workspace has no valid selection identity. Re-select the workspace.");
        return entry.SelectionId;
    }

    private static string ResolveWorkspaceFileSystemPath(string root, string relativePath, bool allowRoot = false)
    {
        root = Path.GetFullPath(root);
        var raw = (relativePath ?? string.Empty).Replace('\\', '/');
        if (raw.Length == 0) { if (allowRoot) return root; throw new InvalidDataException("A relative workspace path is required."); }
        if (raw.StartsWith('/') || Path.IsPathRooted(raw) || raw.Contains(':')) throw new InvalidDataException("Workspace paths must be relative and cannot contain a drive, root, or alternate data stream.");
        var segments = raw.Split('/');
        if (segments.Any(part => part.Length == 0 || part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("Workspace paths cannot contain empty, dot, parent, or invalid path segments.");
        var combined = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The requested path escapes the selected workspace.");
        var parent = Path.GetDirectoryName(combined);
        if (parent is not null) EnsureWorkspaceNoReparse(root, parent);
        if ((File.Exists(combined) || Directory.Exists(combined)) && IsReparsePoint(combined)) throw new InvalidDataException("Workspace access through symbolic links or reparse points is not allowed.");
        return combined;
    }

    private static void EnsureWorkspaceNoReparse(string root, string directory)
    {
        var relative = Path.GetRelativePath(root, directory);
        var current = root;
        if (IsReparsePoint(current)) throw new InvalidDataException("The selected workspace root is a reparse point.");
        if (relative == ".") return;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Length == 0 || part == ".") continue;
            current = Path.Combine(current, part);
            if (Directory.Exists(current) && IsReparsePoint(current)) throw new InvalidDataException("Workspace access through symbolic links or reparse points is not allowed.");
        }
    }

    private object WorkspaceFileSystem(JsonElement args)
    {
        var contextId = ReadString(args, "contextId", 64);
        var selectionId = ReadString(args, "selectionId", 80);
        var entry = ReadPersistedWorkspaceEntry(contextId);
        if (entry is null || !string.Equals(selectionId, entry.SelectionId, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var stale in _workspaceWriteSessions.Values.Where(value => value.ContextId == contextId && (entry is null || !string.Equals(value.SelectionId, entry.SelectionId, StringComparison.OrdinalIgnoreCase))).ToArray())
                AbortWorkspaceWrite(stale.Id, contextId, stale.SelectionId);
            throw new InvalidOperationException("The selected workspace changed. Refresh the Agent workspace handle before accessing project files.");
        }
        var root = Path.GetFullPath(entry.Root);
        EnsureNoReparseAncestors(root);
        if (!Directory.Exists(root) || IsReparsePoint(root)) throw new InvalidDataException("The selected workspace root no longer exists or became a reparse point.");
        var action = ReadString(args, "action", 24);
        var relative = ReadString(args, "path", 4096);
        var path = ResolveWorkspaceFileSystemPath(root, relative, allowRoot: action is "stat" or "list");
        switch (action)
        {
            case "stat":
            {
                if (Directory.Exists(path))
                {
                    var directoryName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (directoryName.Length == 0) directoryName = Path.GetFileName(root);
                    return new { exists = true, kind = "directory", name = directoryName, size = 0L, lastWriteTicks = Directory.GetLastWriteTimeUtc(path).Ticks };
                }
                if (File.Exists(path)) { var info = new FileInfo(path); return new { exists = true, kind = "file", name = info.Name, size = info.Length, lastWriteTicks = info.LastWriteTimeUtc.Ticks, contentType = WorkspaceContentType(info.Extension) }; }
                return new { exists = false, kind = (string?)null, name = Path.GetFileName(path), size = 0L, lastWriteTicks = 0L, contentType = "application/octet-stream" };
            }
            case "list":
                if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"Workspace directory '{relative}' does not exist.");
                var entries = new List<object>();
                foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (entries.Count >= WorkspaceListLimit) break;
                    var isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
                    entries.Add(new { name = entry.Name, kind = isDirectory ? "directory" : "file", size = isDirectory ? 0L : ((FileInfo)entry).Length, lastWriteTicks = entry.LastWriteTimeUtc.Ticks });
                }
                return new { entries, truncated = new DirectoryInfo(path).EnumerateFileSystemInfos().Take(WorkspaceListLimit + 1).Count() > WorkspaceListLimit };
            case "ensureDirectory":
                if (File.Exists(path)) throw new IOException("A file already occupies the requested directory path.");
                Directory.CreateDirectory(path);
                EnsureWorkspaceNoReparse(root, path);
                return new { created = true, kind = "directory", name = Path.GetFileName(path) };
            case "ensureFile":
                if (Directory.Exists(path)) throw new IOException("A directory already occupies the requested file path.");
                if (!File.Exists(path)) using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                if (IsReparsePoint(path)) throw new InvalidDataException("File creation through a reparse point is not allowed.");
                var createdInfo = new FileInfo(path);
                return new { created = true, kind = "file", name = createdInfo.Name, size = createdInfo.Length, lastWriteTicks = createdInfo.LastWriteTimeUtc.Ticks };
            case "delete":
                if (File.Exists(path)) { File.Delete(path); return new { deleted = true, kind = "file" }; }
                if (Directory.Exists(path)) { Directory.Delete(path, ReadBool(args, "recursive")); return new { deleted = true, kind = "directory" }; }
                throw new FileNotFoundException($"Workspace entry '{relative}' does not exist.");
            case "readChunk":
                return WorkspaceReadChunk(args, path);
            case "writeBegin":
                return WorkspaceWriteBegin(args, contextId, selectionId, root, path);
            case "writeChunk":
                return WorkspaceWriteChunk(args, contextId, selectionId);
            case "writeTruncate":
                return WorkspaceWriteTruncate(args, contextId, selectionId);
            case "writeCommit":
                return WorkspaceWriteCommit(args, contextId, selectionId);
            case "writeAbort":
                return AbortWorkspaceWrite(ReadString(args, "sessionId", 80), contextId, selectionId);
            default: throw new InvalidDataException("Unsupported workspace filesystem action.");
        }
    }

    private static string WorkspaceContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html", ".css" => "text/css", ".js" or ".mjs" or ".cjs" => "text/javascript", ".json" => "application/json", ".md" => "text/markdown", ".txt" => "text/plain", ".svg" => "image/svg+xml", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".pdf" => "application/pdf", _ => "application/octet-stream"
    };

    private object WorkspaceReadChunk(JsonElement args, string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The workspace file no longer exists.");
        if (IsReparsePoint(path)) throw new InvalidDataException("Reading through a reparse point is not allowed.");
        var expectedLength = ReadLong(args, "expectedLength", 0, WorkspaceFileLimitBytes);
        var expectedTicks = ReadLong(args, "expectedLastWriteTicks", 0, long.MaxValue);
        var offset = ReadLong(args, "offset", 0, WorkspaceFileLimitBytes);
        var requested = (int)ReadLong(args, "length", 1, WorkspaceReadChunkBytes);
        var info = new FileInfo(path);
        if (info.Length != expectedLength || info.LastWriteTimeUtc.Ticks != expectedTicks) throw new IOException("The file changed while RunMind was reading it. Restart the read to obtain a consistent snapshot.");
        if (offset > expectedLength) throw new InvalidDataException("The file read offset is outside the file.");
        var remaining = (int)Math.Min(requested, expectedLength - offset);
        var bytes = new byte[Math.Max(0, remaining)];
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            stream.Seek(offset, SeekOrigin.Begin);
            var read = 0;
            while (read < bytes.Length) { var count = stream.Read(bytes, read, bytes.Length - read); if (count == 0) break; read += count; }
            if (read != bytes.Length) throw new IOException("The file changed or ended during the workspace read.");
        }
        return new { offset, bytesRead = bytes.Length, totalLength = expectedLength, lastWriteTicks = expectedTicks, base64 = Convert.ToBase64String(bytes), isLast = offset + bytes.Length >= expectedLength };
    }

    private object WorkspaceWriteBegin(JsonElement args, string contextId, string selectionId, string root, string target)
    {
        root = Path.GetFullPath(root);
        var parent = Path.GetDirectoryName(target) ?? throw new InvalidDataException("The file parent directory is invalid.");
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("The destination directory does not exist.");
        EnsureWorkspaceNoReparse(root, parent);
        if (Directory.Exists(target)) throw new IOException("A directory already occupies the destination file path.");
        if (File.Exists(target) && IsReparsePoint(target)) throw new InvalidDataException("Replacing a reparse point is not allowed.");
        var keepExisting = ReadBool(args, "keepExistingData");
        var temporary = Path.Combine(parent, ".runmind-write-" + Guid.NewGuid().ToString("N") + ".tmp");
        var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.SequentialScan);
        try
        {
            if (keepExisting && File.Exists(target))
            {
                var info = new FileInfo(target);
                if (info.Length > WorkspaceFileLimitBytes) throw new InvalidDataException("The existing file exceeds the 128 MB workspace write limit.");
                using var input = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
                input.CopyTo(stream);
            }
            var id = Guid.NewGuid().ToString("N");
            var session = new WorkspaceWriteSession { Id = id, ContextId = contextId, SelectionId = selectionId, WorkspaceRoot = root, TargetPath = target, TemporaryPath = temporary, Stream = stream };
            if (!_workspaceWriteSessions.TryAdd(id, session)) throw new InvalidOperationException("Could not register the workspace write session.");
            return new { sessionId = id, currentLength = stream.Length, maximumBytes = WorkspaceFileLimitBytes };
        }
        catch { stream.Dispose(); try { File.Delete(temporary); } catch { } throw; }
    }

    private object WorkspaceWriteChunk(JsonElement args, string contextId, string selectionId)
    {
        var id = ReadString(args, "sessionId", 80);
        if (!_workspaceWriteSessions.TryGetValue(id, out var session) || !string.Equals(session.ContextId, contextId, StringComparison.OrdinalIgnoreCase) || !string.Equals(session.SelectionId, selectionId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The workspace write session is missing or belongs to another workspace selection.");
        var encoded = ReadString(args, "base64", WorkspaceWriteChunkBytes * 2);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded); } catch (FormatException) { throw new InvalidDataException("The workspace write chunk is not valid base64."); }
        if (bytes.Length > WorkspaceWriteChunkBytes) throw new InvalidDataException("The workspace write chunk exceeds 256 KB.");
        var position = ReadLong(args, "position", 0, WorkspaceFileLimitBytes);
        if (position + bytes.Length > WorkspaceFileLimitBytes) throw new InvalidDataException("The destination file would exceed the 128 MB workspace write limit.");
        session.Stream.Position = position;
        session.Stream.Write(bytes, 0, bytes.Length);
        return new { written = bytes.Length, totalWritten = session.Stream.Length, position = session.Stream.Position };
    }

    private object WorkspaceWriteTruncate(JsonElement args, string contextId, string selectionId)
    {
        var id = ReadString(args, "sessionId", 80);
        if (!_workspaceWriteSessions.TryGetValue(id, out var session) || !string.Equals(session.ContextId, contextId, StringComparison.OrdinalIgnoreCase) || !string.Equals(session.SelectionId, selectionId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The workspace write session is missing or belongs to another workspace selection.");
        var size = ReadLong(args, "size", 0, WorkspaceFileLimitBytes);
        session.Stream.SetLength(size);
        if (session.Stream.Position > size) session.Stream.Position = size;
        return new { truncated = true, size = session.Stream.Length };
    }

    private object WorkspaceWriteCommit(JsonElement args, string contextId, string selectionId)
    {
        var id = ReadString(args, "sessionId", 80);
        if (!_workspaceWriteSessions.TryGetValue(id, out var session) || !string.Equals(session.ContextId, contextId, StringComparison.OrdinalIgnoreCase) || !string.Equals(session.SelectionId, selectionId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The workspace write session is missing or belongs to another workspace selection.");
        try
        {
            var current = ReadPersistedWorkspaceEntry(contextId);
            if (current is null || !string.Equals(current.SelectionId, session.SelectionId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFullPath(current.Root), session.WorkspaceRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The workspace changed while this file write was in progress. The uncommitted write was discarded.");
            EnsureWorkspaceNoReparse(session.WorkspaceRoot, Path.GetDirectoryName(session.TargetPath)!);
            if (Directory.Exists(session.TargetPath) || (File.Exists(session.TargetPath) && IsReparsePoint(session.TargetPath)))
                throw new InvalidDataException("The destination changed to an unsafe filesystem entry while the write was pending.");
            session.Stream.Flush(true);
            session.Stream.Dispose();
            File.Move(session.TemporaryPath, session.TargetPath, true);
            var info = new FileInfo(session.TargetPath);
            return new { written = true, path = Path.GetRelativePath(session.WorkspaceRoot, session.TargetPath).Replace('\\', '/'), bytes = info.Length, lastWriteTicks = info.LastWriteTimeUtc.Ticks };
        }
        finally
        {
            _workspaceWriteSessions.TryRemove(id, out _);
            try { session.Stream.Dispose(); } catch { }
            try { if (File.Exists(session.TemporaryPath)) File.Delete(session.TemporaryPath); } catch { }
        }
    }

    private object AbortWorkspaceWrite(string id, string contextId, string selectionId = "")
    {
        if (_workspaceWriteSessions.TryGetValue(id, out var candidate) &&
            string.Equals(candidate.ContextId, contextId, StringComparison.OrdinalIgnoreCase) &&
            (selectionId.Length == 0 || string.Equals(candidate.SelectionId, selectionId, StringComparison.OrdinalIgnoreCase)) &&
            _workspaceWriteSessions.TryRemove(id, out var session))
        {
            try { session.Stream.Dispose(); } catch { }
            try { if (File.Exists(session.TemporaryPath)) File.Delete(session.TemporaryPath); } catch { }
        }
        return new { aborted = true };
    }

    private void CleanupWorkspaceFileSessions()
    {
        foreach (var session in _workspaceWriteSessions.Values.ToArray()) AbortWorkspaceWrite(session.Id, session.ContextId);
    }

    private static long ReadLong(JsonElement args, string name, long minimum, long maximum)
    {
        if (!args.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var parsed) || parsed < minimum || parsed > maximum)
            throw new InvalidDataException($"'{name}' must be an integer between {minimum.ToString(CultureInfo.InvariantCulture)} and {maximum.ToString(CultureInfo.InvariantCulture)}.");
        return parsed;
    }

    private static bool ReadBool(JsonElement args, string name) => args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
