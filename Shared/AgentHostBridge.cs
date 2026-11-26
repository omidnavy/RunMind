#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace RunMind.Shared;

/// <summary>
/// Narrow native capability bridge for operations that browser APIs cannot provide:
/// shared profile storage, Git and explicitly approved process execution, global Agent Skills,
/// and resumable downloads. Process execution is not sandboxed and inherits current-user rights.
/// </summary>
public sealed partial class AgentHostBridge : IDisposable
{
    private const int MaximumMessageBytes = 1024 * 1024;
    private const int MaximumOutputCharacters = 512 * 1024;
    private const long MaximumDownloadBytes = 4L * 1024 * 1024 * 1024;
    private static readonly TimeSpan GitTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromHours(2);
    private static readonly HttpClient DownloadClient = CreateDownloadClient();
    private const string SharedStoreMutexName = @"Local\RunMind.ProjectProfileRegistry.v1";
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _requests = new(StringComparer.Ordinal);
    private string? _gitWorkspaceRoot;
    private string? _gitWorkspaceContextId;
    private string? _processWorkspaceRoot;
    private string? _processWorkspaceContextId;
    private int _disposed;

    public bool TryHandleMessage(string? source, string? messageJson, CoreWebView2 core, Dispatcher dispatcher)
    {
        if (Volatile.Read(ref _disposed) != 0 || !IsTrustedSource(source) || string.IsNullOrWhiteSpace(messageJson)) return false;
        if (Encoding.UTF8.GetByteCount(messageJson) > MaximumMessageBytes) return false;

        try
        {
            using var document = JsonDocument.Parse(messageJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String) return false;
            var type = typeElement.GetString();
            if (type == "runmind.agentHostCancel")
            {
                var cancelId = ReadString(root, "requestId", 100);
                if (_requests.TryGetValue(cancelId, out var pending)) { try { pending.Cancel(); } catch (ObjectDisposedException) { } }
                return true;
            }
            if (type != "runmind.agentHostRequest") return false;

            var requestId = ReadString(root, "requestId", 100);
            var operation = ReadString(root, "operation", 80);
            if (requestId.Length is < 1 or > 100 || operation.Length is < 1 or > 80)
            {
                _ = PostAsync(core, dispatcher, new { type = "runmind.agentHostResult", requestId, ok = false, error = "Malformed native Agent request." });
                return true;
            }

            JsonElement args;
            if (root.TryGetProperty("args", out var argsElement) && argsElement.ValueKind == JsonValueKind.Object)
                args = argsElement.Clone();
            else
            {
                using var emptyArguments = JsonDocument.Parse("{}");
                args = emptyArguments.RootElement.Clone();
            }
            var cancellation = new CancellationTokenSource();
            if (!_requests.TryAdd(requestId, cancellation)) { cancellation.Dispose(); return true; }
            _ = ProcessRequestAsync(requestId, operation, args, core, dispatcher, cancellation);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private async Task ProcessRequestAsync(string requestId, string operation, JsonElement args, CoreWebView2 core, Dispatcher dispatcher, CancellationTokenSource cancellation)
    {
        try
        {
            Action<long, long?, string?> progress = (bytes, total, message) =>
            {
                _ = PostAsync(core, dispatcher, new { type = "runmind.agentHostProgress", requestId, bytes, total, message });
            };
            var result = await DispatchAsync(operation, args, cancellation.Token, progress).ConfigureAwait(false);
            await PostAsync(core, dispatcher, new { type = "runmind.agentHostResult", requestId, ok = true, result }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await PostAsync(core, dispatcher, new { type = "runmind.agentHostResult", requestId, ok = false, error = "The native operation was cancelled or reached its time limit." }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await PostAsync(core, dispatcher, new { type = "runmind.agentHostResult", requestId, ok = false, error = Limit(error.Message, 2000) }).ConfigureAwait(false);
        }
        finally
        {
            _requests.TryRemove(requestId, out _);
            cancellation.Dispose();
        }
    }

    private Task<object> DispatchAsync(string operation, JsonElement args, CancellationToken cancellationToken, Action<long, long?, string?> progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return operation switch
        {
            "profiles.list" => Task.FromResult(ListProfiles()),
            "profiles.save" => Task.FromResult(SaveProfile(args)),
            "profiles.delete" => Task.FromResult(DeleteProfile(ReadString(args, "id", 100))),
            "permissions.get" => Task.FromResult(ReadPermissionPolicy()),
            "systemManager.settings.get" => Task.FromResult(SystemManagerSettingsGet()),
            "systemManager.settings.save" => Task.FromResult(SystemManagerSettingsSave(args)),
            "systemManager.apps.list" => Task.FromResult(SystemManagerListApplications(RequireSystemManagerSearchQuery(args))),
            "systemManager.adapters.list" => Task.FromResult(SystemManagerListAdapters()),
            "systemManager.adapters.save" => Task.FromResult(SystemManagerSaveAdapter(args)),
            "systemManager.adapters.remove" => Task.FromResult(SystemManagerRemoveAdapter(args)),
            "systemManager.adapters.launch" => Task.FromResult(SystemManagerLaunchAdapter(args)),
            "systemManager.apps.launch" => Task.FromResult(SystemManagerLaunchApplication(args)),
            "systemManager.apps.behavior.save" => Task.FromResult(SystemManagerSetApplicationBehavior(args)),
            "systemManager.apps.close" => Task.FromResult(SystemManagerCloseApplication(args)),
            "systemManager.open.target" => Task.FromResult(SystemManagerOpenTarget(args)),
            "systemManager.process.list" => Task.FromResult(SystemManagerListProcesses(ReadString(args, "query", 120))),
            "systemManager.process.terminate" => Task.FromResult(SystemManagerTerminateProcess(args)),
            "systemManager.diagnostics" => Task.FromResult(SystemManagerDiagnostics()),
            "themes.list" => Task.FromResult(ListThemes()),
            "skills.list" => Task.FromResult(ListGlobalSkills()),
            "skills.load" => Task.FromResult(LoadGlobalSkill(ReadString(args, "name", 100), ReadString(args, "scope", 20), ReadStringArray(args, "references", 10, 160))),
            "skills.install" => Task.FromResult(InstallGlobalSkill()),
            "skills.previewUrl" => PreviewSkillUrlAsync(ReadString(args, "url", 2048), cancellationToken),
            "skills.installContent" => Task.FromResult(InstallGlobalSkillContent(args.TryGetProperty("content", out var skillContent) && skillContent.ValueKind == JsonValueKind.String ? skillContent.GetString() ?? string.Empty : string.Empty, ReadString(args, "source", 2048))),
            "skills.remove" => Task.FromResult(RemoveGlobalSkill(ReadString(args, "name", 100))),
            "git.select" => Task.FromResult(SelectGitWorkspace(ReadString(args, "contextId", 64))),
            "process.selectWorkspace" => Task.FromResult(SelectProcessWorkspace(ReadString(args, "contextId", 64))),
            "process.powershell" => RunPowerShellAsync(args, cancellationToken),
            "process.run" => RunNativeProcessAsync(args, cancellationToken),
            "git.run" => RunGitAsync(args, cancellationToken),
            "git.clone" => CloneRepositoryAsync(args, cancellationToken, progress),
            "download.file" => DownloadFileAsync(args, cancellationToken, (bytes, total) => progress(bytes, total, null)),
            _ => throw new InvalidOperationException($"Native Agent operation '{operation}' is not supported.")
        };
    }

    private static bool IsTrustedSource(string? source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
            && uri.AbsolutePath.Equals("/ChatWindow.html", StringComparison.OrdinalIgnoreCase);
    }

    private static string RequireSystemManagerSearchQuery(JsonElement args)
    {
        var query = ReadString(args, "query", 120);
        if (query.Length == 0) throw new InvalidDataException("A non-empty application search query is required. Search for the application by name; an empty query does not enumerate the catalog.");
        return query;
    }

    private static string ReadString(JsonElement value, string name, int maximum)
    {
        if (!value.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String) return string.Empty;
        return Limit(element.GetString() ?? string.Empty, maximum).Trim();
    }

    private static List<string> ReadStringArray(JsonElement value, string name, int maximumCount, int maximumLength)
    {
        if (!value.TryGetProperty(name, out var rows) || rows.ValueKind != JsonValueKind.Array) return new List<string>();
        return rows.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.String)
            .Select(row => Limit(row.GetString() ?? string.Empty, maximumLength).Trim())
            .Where(row => row.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(maximumCount).ToList();
    }

    private static string Limit(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];

    private static string ProfilesPath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Local AppData is unavailable.");
            return Path.Combine(root, "RunMind", "ProjectProfiles", "profiles.json");
        }
    }

    private static object ListProfiles() => WithSharedStoreLock(() =>
    {
        EnsureSafeSharedStore();
        var profiles = ReadProfiles();
        return new { profiles, count = profiles.Count, storage = "shared-local-appdata" };
    });

    private static T WithSharedStoreLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, SharedStoreMutexName);
        var acquired = false;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new TimeoutException("The shared RunMind profile store is busy in another host.");
        try { return action(); }
        finally { mutex.ReleaseMutex(); }
    }

    private static List<JsonObject> ReadProfiles()
    {
        EnsureSafeSharedStore();
        var path = ProfilesPath;
        if (!File.Exists(path)) return new List<JsonObject>();
        if (IsReparsePoint(path)) throw new InvalidDataException("The shared Project Profile registry cannot be a symbolic link or reparse point.");
        var info = new FileInfo(path);
        if (info.Length > 8L * 1024 * 1024) throw new InvalidDataException("Project profile registry exceeds 8 MB and was not loaded.");
        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        if (!document.RootElement.TryGetProperty("profiles", out var rows) || rows.ValueKind != JsonValueKind.Array) return new List<JsonObject>();
        return rows.EnumerateArray().Take(100).Where(row => row.ValueKind == JsonValueKind.Object)
            .Select(row => JsonNode.Parse(row.GetRawText()) as JsonObject).Where(row => row is not null).Cast<JsonObject>().ToList();
    }

    private static object SaveProfile(JsonElement args)
    {
        var profile = args.TryGetProperty("profile", out var item) && item.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(item.GetRawText()) as JsonObject : null;
        if (profile is null) throw new InvalidDataException("A project profile object is required.");
        var id = (string?)profile["id"] ?? string.Empty;
        var name = (string?)profile["name"] ?? string.Empty;
        if (!Guid.TryParse(id, out _) || string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new InvalidDataException("Profile ID or name is invalid.");
        if (JsonHasCredentialField(profile)) throw new InvalidDataException("Project profiles cannot contain credentials, tokens, API keys, passwords, or secrets.");
        var allowedFields = new HashSet<string>(new[] { "id", "name", "instructions", "provider", "connectionId", "model", "workspaceName", "agentSettings", "skills", "mcpServerIds", "permissionPolicy" }, StringComparer.Ordinal);
        foreach (var key in profile.Select(pair => pair.Key).Where(key => !allowedFields.Contains(key)).ToArray()) profile.Remove(key);
        profile["name"] = name.Trim();
        profile["instructions"] = Limit((string?)profile["instructions"] ?? string.Empty, 16000);
        profile["provider"] = Limit((string?)profile["provider"] ?? string.Empty, 60);
        profile["connectionId"] = Limit((string?)profile["connectionId"] ?? string.Empty, 100);
        profile["model"] = Limit((string?)profile["model"] ?? string.Empty, 160);
        profile["workspaceName"] = Limit((string?)profile["workspaceName"] ?? string.Empty, 256);
        profile["formatVersion"] = 1;
        profile["updatedAt"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        profile["skills"] = JsonStringArray(profile["skills"], 50, 100);
        profile["mcpServerIds"] = JsonStringArray(profile["mcpServerIds"], 50, 100);
        var policyInput = profile["permissionPolicy"] as JsonObject;
        var safePolicy = new JsonObject();
        foreach (var key in new[] { "gitRead", "gitWrite", "gitRemote", "downloads" })
        {
            var level = (string?)policyInput?[key];
            if (level is not ("allow" or "ask" or "always-ask") || (key == "gitRead" && level == "always-ask")) level = key == "gitRead" ? "ask" : (string?)DefaultPermissionPolicy()[key];
            safePolicy[key] = level;
        }
        safePolicy["gitDestructive"] = "always-ask";
        profile["permissionPolicy"] = safePolicy;
        if (profile["agentSettings"] is JsonObject settings)
        {
            var safeSettings = new JsonObject
            {
                ["searchUrl"] = SafeProfileEndpoint((string?)settings["searchUrl"] ?? string.Empty, allowQuery: true),
                ["readerBaseUrl"] = SafeProfileEndpoint((string?)settings["readerBaseUrl"] ?? string.Empty, allowQuery: false),
                ["customReaderUrl"] = SafeProfileEndpoint((string?)settings["customReaderUrl"] ?? string.Empty, allowQuery: false),
                ["maxSteps"] = Math.Clamp((int?)settings["maxSteps"] ?? 8, 1, 20),
                ["readerFallback"] = (bool?)settings["readerFallback"] ?? true,
            };
            profile["agentSettings"] = safeSettings;
        }
        else profile.Remove("agentSettings");
        if (profile.ToJsonString().Length > 64 * 1024) throw new InvalidDataException("Project Profile data exceeds the 64 KB limit.");
        WithSharedStoreLock(() =>
        {
            var rows = ReadProfiles();
            var existing = rows.FindIndex(row => string.Equals((string?)row["id"], id, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) rows[existing] = profile;
            else { if (rows.Count >= 100) throw new InvalidOperationException("The profile registry is full (100 profiles)."); rows.Add(profile); }
            WriteProfiles(rows);
            return true;
        });
        return new { saved = true, id, name = name.Trim() };
    }

    private static object DeleteProfile(string id)
    {
        if (!Guid.TryParse(id, out _)) throw new InvalidDataException("A valid profile ID is required.");
        return WithSharedStoreLock(() =>
        {
            var rows = ReadProfiles();
            var removed = rows.RemoveAll(row => string.Equals((string?)row["id"], id, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) WriteProfiles(rows);
            return (object)new { deleted = removed > 0, id };
        });
    }

    private static string PermissionPolicyPath => Path.Combine(Path.GetDirectoryName(ProfilesPath)!, "permissions.json");

    private static JsonObject DefaultPermissionPolicy() => new()
    {
        ["gitRead"] = "allow",
        ["gitWrite"] = "ask",
        ["gitRemote"] = "ask",
        ["gitDestructive"] = "always-ask",
        ["downloads"] = "ask",
    };

    private static object ListThemes()
    {
        var themes = ThemeCatalog.Discover()
            .Select(theme => new
            {
                id = theme.Id,
                name = theme.Name,
                mode = theme.Mode,
                fileName = theme.FileName,
                version = theme.Version,
            })
            .ToArray();
        return new { themes, count = themes.Length, source = "native-theme-folder" };
    }

    private static object ReadPermissionPolicy() => WithSharedStoreLock<object>(() =>
    {
        EnsureSafeSharedStore();
        var defaults = DefaultPermissionPolicy();
        var path = PermissionPolicyPath;
        if (!File.Exists(path)) return new { policy = defaults, version = 1 };
        if (IsReparsePoint(path)) return new { policy = defaults, version = 1, warning = "Permission policy file is a reparse point; secure defaults are active." };
        var info = new FileInfo(path);
        if (info.Length > 16 * 1024) return new { policy = defaults, version = 1, warning = "Permission policy file exceeded 16 KB; secure defaults are active." };
        try
        {
            var saved = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject;
            if (saved is not null)
                foreach (var key in new[] { "gitRead", "gitWrite", "gitRemote", "downloads" })
                    if (saved[key] is JsonValue value && value.TryGetValue<string>(out var setting) && setting is "allow" or "ask" or "always-ask")
                        defaults[key] = key == "gitRead" && setting == "always-ask" ? "ask" : setting;
            defaults["gitDestructive"] = "always-ask";
        }
        catch { return new { policy = defaults, version = 1, warning = "Permission policy could not be parsed; secure defaults are active." }; }
        return (object)new { policy = defaults, version = 1 };
    });

    private static void EnsureSafeSharedStore()
    {
        var directory = Path.GetDirectoryName(ProfilesPath)!;
        EnsureNoReparseAncestors(directory);
        if (Directory.Exists(directory) && IsReparsePoint(directory))
            throw new InvalidDataException("The shared RunMind profile directory cannot be a symbolic link or reparse point.");
    }

    private static void WriteProfiles(List<JsonObject> profiles)
    {
        EnsureSafeSharedStore();
        var path = ProfilesPath;
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        EnsureSafeSharedStore();
        var temporary = path + ".tmp";
        if ((File.Exists(path) && IsReparsePoint(path)) || (File.Exists(temporary) && IsReparsePoint(temporary)))
            throw new InvalidDataException("The shared Project Profile registry cannot use a symbolic link or reparse point.");
        var data = JsonSerializer.Serialize(new { formatVersion = 1, profiles }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temporary, data, new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    private static JsonArray JsonStringArray(JsonNode? node, int count, int length)
    {
        var result = new JsonArray();
        if (node is not JsonArray rows) return result;
        foreach (var item in rows.OfType<JsonValue>().Take(count))
        {
            if (item.TryGetValue<string>(out var value) && !string.IsNullOrWhiteSpace(value)) result.Add(Limit(value.Trim(), length));
        }
        return result;
    }

    private static string SafeProfileEndpoint(string value, bool allowQuery)
    {
        value = Limit(value.Trim(), 2048);
        if (value.Length == 0) return string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || (!allowQuery && !string.IsNullOrEmpty(uri.Query)))
            throw new InvalidDataException("Project Profile endpoints must be credential-free HTTP/HTTPS URLs without fragments; Reader endpoints cannot include query strings.");
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = WebUtility.UrlDecode(pair.Split('=', 2)[0]);
            if (System.Text.RegularExpressions.Regex.IsMatch(key, "token|secret|password|api.?key|credential|authorization|signature|access.?key", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new InvalidDataException("Credential-like query parameters cannot be stored in a Project Profile endpoint.");
        }
        return uri.AbsoluteUri;
    }

    private static bool JsonHasCredentialField(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(pair.Key, "token|secret|password|api.?key|credential|authorization", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
                if (JsonHasCredentialField(pair.Value)) return true;
            }
        }
        else if (node is JsonArray array) foreach (var item in array) if (JsonHasCredentialField(item)) return true;
        return false;
    }

    private static string GlobalSkillsRoot
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("Local AppData is unavailable.");
            return Path.Combine(root, "RunMind", "Skills");
        }
    }

    private static object ListGlobalSkills()
    {
        var root = GlobalSkillsRoot;
        EnsureNoReparseAncestors(root);
        Directory.CreateDirectory(root);
        EnsureNoReparseAncestors(root);
        var skills = new List<object>();
        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "Skills");
        if (Directory.Exists(bundledRoot) && !IsReparsePoint(bundledRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(bundledRoot).Take(50))
            {
                if (IsReparsePoint(directory)) continue;
                var skillPath = Path.Combine(directory, "SKILL.md");
                if (!File.Exists(skillPath) || IsReparsePoint(skillPath) || new FileInfo(skillPath).Length > 128 * 1024) continue;
                var metadata = ParseSkillFrontmatter(File.ReadAllText(skillPath, Encoding.UTF8));
                var name = Path.GetFileName(directory);
                if (metadata.Name == name && metadata.Description.Length > 0) skills.Add(new { name, description = metadata.Description, scope = "builtin" });
            }
        }
        foreach (var directory in Directory.EnumerateDirectories(root).Take(200))
        {
            if (IsReparsePoint(directory)) continue;
            var skillPath = Path.Combine(directory, "SKILL.md");
            if (!File.Exists(skillPath) || IsReparsePoint(skillPath)) continue;
            if (new FileInfo(skillPath).Length > 128 * 1024) continue;
            var content = File.ReadAllText(skillPath, Encoding.UTF8);
            var metadata = ParseSkillFrontmatter(content);
            if (metadata.Name.Length == 0 || metadata.Description.Length == 0) continue;
            skills.Add(new { name = metadata.Name, description = metadata.Description, scope = "global" });
        }
        return new { skills, count = skills.Count, root = "%LOCALAPPDATA%\\RunMind\\Skills" };
    }

    private static object InstallGlobalSkill()
    {
        string? selected = null;
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose a local Agent Skill folder containing SKILL.md",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) selected = dialog.SelectedPath;
        });
        if (string.IsNullOrWhiteSpace(selected)) return new { cancelled = true };

        var source = Path.GetFullPath(selected);
        EnsureNoReparseAncestors(source);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("The selected Agent Skill folder no longer exists.");
        var manifest = Path.Combine(source, "SKILL.md");
        EnsureNoReparseComponents(source, manifest);
        if (!File.Exists(manifest)) throw new InvalidDataException("The selected folder does not contain SKILL.md.");
        if (new FileInfo(manifest).Length > 128 * 1024) throw new InvalidDataException("SKILL.md exceeds the 128 KB limit.");
        var metadata = ParseSkillFrontmatter(File.ReadAllText(manifest, Encoding.UTF8));
        var folderName = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (metadata.Name.Length == 0 || metadata.Description.Length == 0 || !string.Equals(metadata.Name, folderName, StringComparison.Ordinal))
            throw new InvalidDataException("The Skill must have valid name and description frontmatter, and its name must match the selected folder.");

        var root = GlobalSkillsRoot;
        Directory.CreateDirectory(root);
        EnsureNoReparseAncestors(root);
        var destination = Path.Combine(root, metadata.Name);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"A global Skill named '{metadata.Name}' is already installed. Remove it before installing a replacement.");
        var staging = Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N"));
        var counter = new SkillCopyCounter();
        try
        {
            Directory.CreateDirectory(staging);
            CopySkillDirectoryContents(source, staging, counter, 0);
            var stagedManifest = Path.Combine(staging, "SKILL.md");
            if (!File.Exists(stagedManifest)) throw new InvalidDataException("The selected Skill folder did not contain SKILL.md after validation.");
            Directory.Move(staging, destination);
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
            throw;
        }
        return new { installed = true, name = metadata.Name, description = metadata.Description, scope = "global", files = counter.Files, bytes = counter.Bytes, root = @"%LOCALAPPDATA%\RunMind\Skills" };
    }

    private static readonly HttpClient SkillHttpClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };

    private static async Task<object> PreviewSkillUrlAsync(string rawUrl, CancellationToken cancellationToken)
    {
        var url = NormalizeSkillUrl(rawUrl);
        for (var redirects = 0; redirects <= 4; redirects++)
        {
            await ValidatePublicSkillHostAsync(url, cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("RunMind-SkillInstaller/2.5");
            request.Headers.Accept.ParseAdd("text/markdown, text/plain;q=0.9, */*;q=0.1");
            using var response = await SkillHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                if (redirects == 4 || response.Headers.Location is null) throw new InvalidDataException("The Skill URL redirected too many times or supplied an invalid redirect.");
                url = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(url, response.Headers.Location);
                url = NormalizeSkillUrl(url.AbsoluteUri);
                continue;
            }
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Skill download returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            if (response.Content.Headers.ContentLength is long length && length > 128 * 1024) throw new InvalidDataException("SKILL.md exceeds the 128 KB limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var memory = new MemoryStream(); var buffer = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (memory.Length + read > 128 * 1024) throw new InvalidDataException("SKILL.md exceeds the 128 KB limit.");
                memory.Write(buffer, 0, read);
            }
            var content = Encoding.UTF8.GetString(memory.ToArray());
            var metadata = ParseSkillFrontmatter(content);
            if (metadata.Name.Length == 0 || metadata.Description.Length == 0) throw new InvalidDataException("The downloaded file is not a valid Agent Skill SKILL.md with name and description frontmatter.");
            return new { valid = true, name = metadata.Name, description = metadata.Description, source = url.AbsoluteUri, content, preview = content.Length > 5000 ? content[..5000] : content, bytes = memory.Length, scope = "global", note = "This direct URL installer currently imports SKILL.md only. Additional referenced resource files must be installed from a repository or local folder." };
        }
        throw new InvalidOperationException("Could not retrieve the Skill URL.");
    }

    private static Uri NormalizeSkillUrl(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || !uri.IsDefaultPort)
            throw new InvalidDataException("Skill sources must be credential-free HTTPS URLs on the standard HTTPS port.");
        if (uri.Query.Length > 0) throw new InvalidDataException("Skill URLs with query parameters are not accepted. Use a stable raw SKILL.md URL.");
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2) uri = new Uri($"https://raw.githubusercontent.com/{parts[0]}/{parts[1]}/main/SKILL.md");
            else if (parts.Length >= 4 && (parts[2].Equals("tree", StringComparison.OrdinalIgnoreCase) || parts[2].Equals("blob", StringComparison.OrdinalIgnoreCase)))
            {
                var owner = parts[0]; var repo = parts[1]; var branch = parts[3]; var itemPath = string.Join('/', parts.Skip(4));
                var filePath = parts[2].Equals("blob", StringComparison.OrdinalIgnoreCase) && itemPath.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase)
                    ? itemPath : (itemPath.Length > 0 ? itemPath.TrimEnd('/') + "/" : string.Empty) + "SKILL.md";
                uri = new Uri($"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{filePath}");
            }
        }
        else if (uri.Host.Equals("gitlab.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var marker = Array.FindIndex(parts, item => item == "-");
            if (marker >= 0 && parts.Length > marker + 2 && (parts[marker + 1] is "tree" or "blob"))
            {
                var project = string.Join('/', parts.Take(marker)); var branch = parts[marker + 2]; var itemPath = string.Join('/', parts.Skip(marker + 3));
                var filePath = parts[marker + 1] == "blob" && itemPath.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase)
                    ? itemPath : (itemPath.Length > 0 ? itemPath.TrimEnd('/') + "/" : string.Empty) + "SKILL.md";
                uri = new Uri($"https://gitlab.com/{project}/-/raw/{branch}/{filePath}");
            }
        }
        return uri;
    }

    private static async Task ValidatePublicSkillHostAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Local and private network Skill sources are not allowed.");
        System.Net.IPAddress[] addresses;
        if (System.Net.IPAddress.TryParse(uri.Host, out var literal)) addresses = new[] { literal };
        else addresses = await System.Net.Dns.GetHostAddressesAsync(uri.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(address => System.Net.IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || IsPrivateIpv4(address)))
            throw new InvalidDataException("Skill sources resolving to local or private network addresses are not allowed.");
    }

    private static bool IsPrivateIpv4(System.Net.IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPrivateIpv4(address.MapToIPv4());
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return bytes[0] == 0 || bytes[0] == 10 || bytes[0] == 127 || bytes[0] >= 224 ||
                (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
                (bytes[0] == 169 && bytes[1] == 254) || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                (bytes[0] == 192 && (bytes[1] == 168 || bytes[1] == 0 || (bytes[1] == 0 && bytes[2] == 0))) ||
                (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19));
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return address.Equals(System.Net.IPAddress.IPv6Any) || bytes[0] == 0xff || (bytes[0] & 0xfe) == 0xfc || (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) || address.IsIPv6SiteLocal;
        return true;
    }

    private static object InstallGlobalSkillContent(string content, string source)
    {
        if (string.IsNullOrWhiteSpace(content) || Encoding.UTF8.GetByteCount(content) > 128 * 1024) throw new InvalidDataException("Skill content is empty or exceeds the 128 KB limit.");
        var metadata = ParseSkillFrontmatter(content);
        if (metadata.Name.Length == 0 || metadata.Description.Length == 0) throw new InvalidDataException("Skill content must have valid name and description frontmatter.");
        var root = GlobalSkillsRoot; Directory.CreateDirectory(root); EnsureNoReparseAncestors(root);
        var destination = Path.Combine(root, metadata.Name);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException($"A global Skill named '{metadata.Name}' is already installed. Remove it before installing a replacement.");
        var staging = Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging); EnsureNoReparseAncestors(staging);
            File.WriteAllText(Path.Combine(staging, "SKILL.md"), content, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(staging, "runmind-source.txt"), Limit(source, 2048), new UTF8Encoding(false));
            Directory.Move(staging, destination);
        }
        catch { try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { } throw; }
        return new { installed = true, name = metadata.Name, description = metadata.Description, scope = "global", files = 2, source = Limit(source, 2048), root = @"%LOCALAPPDATA%\RunMind\Skills", note = "Skill instructions are untrusted content. Scripts and commands were not executed." };
    }

    private sealed class SkillCopyCounter
    {
        public int Files { get; set; }
        public long Bytes { get; set; }
    }

    private static void CopySkillDirectoryContents(string source, string destination, SkillCopyCounter counter, int depth)
    {
        if (depth > 10) throw new InvalidDataException("Skill folder nesting exceeds the 10-level limit.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
                throw new InvalidDataException("The Skill folder contains an invalid path entry.");
            if (name.Equals(".git", StringComparison.OrdinalIgnoreCase) || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;
            if (IsReparsePoint(entry)) throw new InvalidDataException("Agent Skills cannot contain symbolic links or reparse points.");
            var target = Path.Combine(destination, name);
            if (Directory.Exists(entry))
            {
                Directory.CreateDirectory(target);
                CopySkillDirectoryContents(entry, target, counter, depth + 1);
                continue;
            }
            if (!File.Exists(entry)) continue;
            var info = new FileInfo(entry);
            if (info.Length > 5L * 1024 * 1024) throw new InvalidDataException($"Skill file '{name}' exceeds the 5 MB per-file limit.");
            counter.Files++;
            counter.Bytes += info.Length;
            if (counter.Files > 500 || counter.Bytes > 20L * 1024 * 1024) throw new InvalidDataException("Skill folder exceeds the safety limits (500 files, 5 MB per file, 20 MB total).");
            File.Copy(entry, target, overwrite: false);
        }
    }

    private static object RemoveGlobalSkill(string name)
    {
        if (!IsSafeSkillName(name)) throw new InvalidDataException("Invalid Agent Skill name.");
        var root = GlobalSkillsRoot;
        EnsureNoReparseAncestors(root);
        var directory = Path.GetFullPath(Path.Combine(root, name));
        EnsureNoReparseComponents(root, directory);
        if (!Directory.Exists(directory)) return new { deleted = false, name };
        EnsureNoReparseTree(directory);
        Directory.Delete(directory, recursive: true);
        return new { deleted = true, name, scope = "global" };
    }

    private static object LoadGlobalSkill(string name, string scope, List<string> references)
    {
        if (!IsSafeSkillName(name)) throw new InvalidDataException("Invalid Agent Skill name.");
        var root = scope == "builtin" ? Path.Combine(AppContext.BaseDirectory, "Skills") : GlobalSkillsRoot;
        if (scope is not ("builtin" or "global")) throw new InvalidDataException("Only built-in and global skills can be loaded through the native bridge.");
        var directory = Path.GetFullPath(Path.Combine(root, name));
        EnsureNoReparseComponents(root, directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The selected global Agent Skill directory was not found.");
        var mainPath = Path.Combine(directory, "SKILL.md");
        EnsureNoReparseComponents(directory, mainPath);
        if (!File.Exists(mainPath)) throw new FileNotFoundException("SKILL.md was not found for this skill.");
        var instruction = File.ReadAllText(mainPath, Encoding.UTF8);
        if (instruction.Length > 128 * 1024) throw new InvalidDataException("SKILL.md exceeds the 128 KB limit.");
        var metadata = ParseSkillFrontmatter(instruction);
        if (metadata.Name != name || metadata.Description.Length == 0) throw new InvalidDataException("SKILL.md frontmatter does not match the selected skill folder.");
        var loadedReferences = new List<object>();
        foreach (var reference in references)
        {
            var relative = ValidateSkillRelativePath(reference);
            var path = Path.GetFullPath(Path.Combine(directory, relative));
            EnsureNoReparseComponents(directory, path);
            if (!File.Exists(path)) throw new FileNotFoundException($"Skill reference '{relative}' was not found.");
            var info = new FileInfo(path);
            if (info.Length > 128 * 1024) throw new InvalidDataException($"Skill reference '{relative}' exceeds the 128 KB limit.");
            loadedReferences.Add(new { path = relative.Replace('\\', '/'), content = File.ReadAllText(path, Encoding.UTF8) });
        }
        return new { name, scope, instructions = instruction, references = loadedReferences };
    }

    private static (string Name, string Description) ParseSkillFrontmatter(string content)
    {
        if (!content.StartsWith("---", StringComparison.Ordinal)) return (string.Empty, string.Empty);
        var end = content.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0 || end > 4096) return (string.Empty, string.Empty);
        var lines = content[3..end].Split('\n');
        var name = string.Empty;
        var description = string.Empty;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                name = value.Trim('"', '\'');
            else if (key.Equals("description", StringComparison.OrdinalIgnoreCase))
            {
                if (value is "|" or ">" or "|-" or ">-" or "|+" or ">+")
                {
                    var block = new List<string>();
                    for (var next = index + 1; next < lines.Length; next++)
                    {
                        var continuation = lines[next].TrimEnd('\r');
                        if (continuation.Length > 0 && !char.IsWhiteSpace(continuation[0])) break;
                        block.Add(continuation.Trim());
                    }
                    description = value.StartsWith('>')
                        ? string.Join(" ", block.Where(part => part.Length > 0))
                        : string.Join("\n", block).Trim();
                }
                else description = value.Trim('"', '\'');
            }
        }
        return (IsSafeSkillName(name) ? name : string.Empty, Limit(description, 1000));
    }

    private static bool IsSafeSkillName(string name) => name.Length is > 0 and <= 64 && System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9]+(?:-[a-z0-9]+)*$");
    private static string ValidateSkillRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || normalized.Split('/').Any(part => part.Length == 0 || part is "." or ".." || part.StartsWith('.')) || Path.IsPathRooted(normalized))
            throw new InvalidDataException("Skill reference paths must be safe relative paths.");
        return normalized;
    }
    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static void EnsureNoReparseAncestors(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? throw new InvalidDataException("The selected path has no filesystem root.");
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if ((Directory.Exists(current) || File.Exists(current)) && IsReparsePoint(current))
                throw new InvalidDataException("Symbolic links and reparse points are not allowed in the selected native path.");
        }
    }
    private static void EnsureInsideRoot(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) && !fullPath.Equals(fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The requested path escapes its authorized directory.");
    }
    private static void EnsureNoReparseTree(string directory)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((directory, 0));
        var entriesSeen = 0;
        while (pending.Count > 0)
        {
            var (current, depth) = pending.Pop();
            if (depth > 10) throw new InvalidDataException("The Skill folder nesting exceeds the 10-level limit.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (++entriesSeen > 2500) throw new InvalidDataException("The Skill folder contains too many filesystem entries to remove safely.");
                if (IsReparsePoint(entry)) throw new InvalidDataException("A Skill containing symbolic links or reparse points cannot be removed recursively.");
                if (Directory.Exists(entry)) pending.Push((entry, depth + 1));
            }
        }
    }

    private static void EnsureNoReparseComponents(string root, string path)
    {
        EnsureNoReparseAncestors(root);
        EnsureInsideRoot(root, path);
        var relative = Path.GetRelativePath(root, path);
        var current = Path.GetFullPath(root);
        if (Directory.Exists(current) && IsReparsePoint(current)) throw new InvalidDataException("Symbolic links and reparse points are not allowed in the global Skills directory.");
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && IsReparsePoint(current))
                throw new InvalidDataException("Symbolic links and reparse points are not allowed inside a Skill folder.");
        }
    }

    private object SelectGitWorkspace(string contextId)
    {
        if (!Guid.TryParse(contextId, out _)) throw new InvalidDataException("A valid conversation workspace context is required before selecting a Git repository.");
        string? selected = null;
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose the Git repository folder that RunMind may access",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) selected = dialog.SelectedPath;
        });
        if (string.IsNullOrWhiteSpace(selected)) return new { cancelled = true };
        var root = Path.GetFullPath(selected);
        EnsureNoReparseAncestors(root);
        var gitDirectory = Path.Combine(root, ".git");
        if (!Directory.Exists(gitDirectory) || IsReparsePoint(gitDirectory)) throw new InvalidDataException("The selected folder is not a normal Git working tree with a local .git directory. Git worktrees and linked git directories are not enabled in this release.");
        _gitWorkspaceRoot = root;
        _gitWorkspaceContextId = contextId;
        return new { selected = true, folder = Path.GetFileName(root), repository = true };
    }


    private object SelectProcessWorkspace(string contextId)
    {
        if (!Guid.TryParse(contextId, out _)) throw new InvalidDataException("A valid conversation workspace context is required before selecting a native execution workspace.");
        string? selected = null;
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose the project folder where RunMind may execute approved commands",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) selected = dialog.SelectedPath;
        });
        if (string.IsNullOrWhiteSpace(selected)) return new { cancelled = true };
        var root = Path.GetFullPath(selected);
        EnsureNoReparseAncestors(root);
        if (!Directory.Exists(root) || IsReparsePoint(root)) throw new InvalidDataException("The selected native execution workspace is not a normal directory.");
        _processWorkspaceRoot = root;
        _processWorkspaceContextId = contextId;
        return new { selected = true, folder = Path.GetFileName(root), note = "Native process execution is scoped to this folder for relative working directories. Commands still run with the current Windows user's permissions." };
    }

    private string GetProcessWorkingDirectory(JsonElement args)
    {
        var contextId = ReadString(args, "contextId", 64);
        var root = _processWorkspaceRoot;
        if (!Guid.TryParse(contextId, out _) || !string.Equals(contextId, _processWorkspaceContextId, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new InvalidOperationException("Select a native execution workspace first with select_native_workspace.");
        root = Path.GetFullPath(root);
        EnsureNoReparseAncestors(root);
        var relative = ReadString(args, "working_directory", 1024).Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("working_directory must be relative to the selected native execution workspace.");
        var working = Path.GetFullPath(Path.Combine(root, relative));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!string.Equals(working, root, StringComparison.OrdinalIgnoreCase) && !working.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("working_directory cannot escape the selected native execution workspace.");
        EnsureNoReparseAncestors(working);
        if (!Directory.Exists(working)) throw new DirectoryNotFoundException("The requested working directory does not exist inside the selected native workspace.");
        return working;
    }

    private async Task<object> RunPowerShellAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var script = ReadString(args, "script", 100_000);
        if (script.Length == 0) throw new InvalidDataException("A non-empty PowerShell script is required.");
        script = "$ErrorActionPreference = 'Stop'\r\n" + script;
        var working = GetProcessWorkingDirectory(args);
        var executable = FindExecutable("pwsh.exe") ?? FindPowerShellExecutable()
            ?? throw new FileNotFoundException("PowerShell was not found. Install PowerShell 7 (pwsh) or enable Windows PowerShell.");
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = working,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("-");
        return await ExecuteBoundedProcessAsync(start, "PowerShell", working, args, cancellationToken, script).ConfigureAwait(false);
    }

    private async Task<object> RunNativeProcessAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var command = ReadString(args, "executable", 260);
        if (command.Length == 0) throw new InvalidDataException("An executable name is required.");
        if (command.Contains('\\') || command.Contains('/') || Path.IsPathRooted(command) || !command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Use an executable name ending in .exe, not a path, script, batch file, or shell command. PowerShell is available through powershell_execute.");
        var executable = FindExecutable(command) ?? throw new FileNotFoundException($"Executable '{command}' was not found on PATH.");
        var working = GetProcessWorkingDirectory(args);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = working,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (args.TryGetProperty("arguments", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            if (values.GetArrayLength() > 100) throw new InvalidDataException("A maximum of 100 command arguments is allowed.");
            var totalArgumentCharacters = 0;
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Every command argument must be a string.");
                var argument = value.GetString() ?? string.Empty;
                if (argument.Length > 8192) throw new InvalidDataException("A command argument exceeds 8 KB.");
                totalArgumentCharacters += argument.Length;
                if (totalArgumentCharacters > 64 * 1024) throw new InvalidDataException("Combined command arguments exceed 64 KB.");
                start.ArgumentList.Add(argument);
            }
        }
        return await ExecuteBoundedProcessAsync(start, command, working, args, cancellationToken).ConfigureAwait(false);
    }

    private static string? FindPowerShellExecutable()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var candidates = new[]
        {
            Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"),
            Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"),
        };
        foreach (var candidate in candidates)
        {
            try { if (File.Exists(candidate) && !IsReparsePoint(candidate)) return Path.GetFullPath(candidate); }
            catch { }
        }
        return FindExecutable("powershell.exe");
    }

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(candidate) && !IsReparsePoint(candidate)) return Path.GetFullPath(candidate);
            }
            catch { }
        }
        return null;
    }

    private static async Task<object> ExecuteBoundedProcessAsync(ProcessStartInfo start, string label, string working, JsonElement args, CancellationToken cancellationToken, string? standardInput = null)
    {
        var timeoutSeconds = 300;
        if (args.TryGetProperty("timeout_seconds", out var timeoutValue) && timeoutValue.TryGetInt32(out var requested)) timeoutSeconds = Math.Clamp(requested, 1, 900);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start()) throw new InvalidOperationException($"Could not start {label}.");
        var stdoutTask = ReadBoundedProcessOutputAsync(process.StandardOutput, timeout.Token);
        var stderrTask = ReadBoundedProcessOutputAsync(process.StandardError, timeout.Token);
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), timeout.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false); } catch { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException($"{label} exceeded its {timeoutSeconds}-second execution limit; the process tree was terminated.");
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false); } catch { }
            throw;
        }
        var output = await stdoutTask.ConfigureAwait(false);
        var error = await stderrTask.ConfigureAwait(false);
        stopwatch.Stop();
        return new { executable = start.FileName, workingDirectory = working, exitCode = process.ExitCode, durationMs = stopwatch.ElapsedMilliseconds, stdout = output.text, stderr = error.text, outputTruncated = output.truncated || error.truncated, note = "Executed as the current Windows user; approval is not a sandbox or privilege boundary." };
    }

    private static async Task<(string text, bool truncated)> ReadBoundedProcessOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        const int maximum = 512 * 1024;
        var buffer = new char[8192];
        var output = new StringBuilder(Math.Min(maximum, 32 * 1024));
        var truncated = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            var remaining = maximum - output.Length;
            if (remaining > 0) output.Append(buffer, 0, Math.Min(remaining, read));
            if (read > remaining) truncated = true;
        }
        if (truncated) output.Append("\n[output truncated at 512 KB]");
        return (output.ToString(), truncated);
    }

    private async Task<object> CloneRepositoryAsync(JsonElement args, CancellationToken cancellationToken, Action<long, long?, string?> progress)
    {
        var contextId = ReadString(args, "contextId", 64);
        if (!Guid.TryParse(contextId, out _)) throw new InvalidDataException("A valid conversation workspace context is required before cloning a Git repository.");
        var rawUrl = ReadString(args, "url", 2048);
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("Git clone requires an absolute HTTP or HTTPS URL without embedded credentials or fragments.");
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = WebUtility.UrlDecode(pair.Split('=', 2)[0]);
            if (System.Text.RegularExpressions.Regex.IsMatch(key, "token|secret|password|api.?key|credential|authorization|signature", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                throw new InvalidDataException("Credential-like query parameters are not allowed in Git clone URLs. Configure credentials with Windows Git Credential Manager instead.");
        }
        var leaf = ReadString(args, "directory", 120);
        if (leaf.Length == 0)
        {
            leaf = uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;
            if (leaf.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) leaf = leaf[..^4];
        }
        if (!IsSafeDirectoryName(leaf)) throw new InvalidDataException("The clone destination name is invalid.");
        string? parent = null;
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose the parent folder where the repository will be cloned",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) parent = dialog.SelectedPath;
        });
        if (string.IsNullOrWhiteSpace(parent)) return new { cancelled = true };
        parent = Path.GetFullPath(parent);
        EnsureNoReparseAncestors(parent);
        var destination = Path.Combine(parent, leaf);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException($"Clone destination already exists: {leaf}. Existing content will not be overwritten.");
        var result = await ExecuteGitProcessAsync(parent, new[] { "clone", "--progress", "--", uri.AbsoluteUri, destination }, cancellationToken, TimeSpan.FromMinutes(18), message => progress(0, null, message)).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new IOException($"Git clone failed with exit code {result.ExitCode}. {Limit(result.Error, 1600)}");
        if (!Directory.Exists(Path.Combine(destination, ".git"))) throw new IOException("Git clone ended without creating a valid repository directory.");
        _gitWorkspaceRoot = destination;
        _gitWorkspaceContextId = contextId;
        return new { cloned = true, folder = leaf, output = result.Output, error = result.Error, exitCode = result.ExitCode };
    }

    private async Task<object> RunGitAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var contextId = ReadString(args, "contextId", 64);
        var root = _gitWorkspaceRoot;
        if (!Guid.TryParse(contextId, out _) || !string.Equals(contextId, _gitWorkspaceContextId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select a Git repository for this conversation first with git_select_repository.");
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) throw new InvalidOperationException("Select a Git repository first with git_select_repository.");
        root = Path.GetFullPath(root);
        EnsureNoReparseAncestors(root);
        var gitDirectory = Path.Combine(root, ".git");
        if (IsReparsePoint(root) || !Directory.Exists(gitDirectory) || IsReparsePoint(gitDirectory)) throw new InvalidDataException("The Git repository changed or is no longer a supported local working tree.");
        var action = ReadString(args, "action", 40);
        var values = new List<string> { "-c", "core.quotepath=false", "-c", "color.ui=false" };
        switch (action)
        {
            case "status": values.AddRange(new[] { "status", "--short", "--branch" }); break;
            case "diff": values.AddRange(new[] { "diff", "--no-ext-diff", "--no-color" }); AddOptionalValidatedPath(values, args, "path"); break;
            case "log": values.AddRange(new[] { "log", "-n", "30", "--oneline", "--decorate" }); break;
            case "show":
                values.AddRange(new[] { "show", "--no-ext-diff", "--no-color", "--format=fuller", "--stat", "-1" });
                var showCommit = ReadString(args, "commit", 80); if (showCommit.Length > 0) values.Add(SafeGitCommit(showCommit));
                break;
            case "branches": values.AddRange(new[] { "branch", "--list" }); break;
            case "remotes": values.AddRange(new[] { "remote", "-v" }); break;
            case "stage": values.Add("add"); values.Add("--"); values.AddRange(ReadGitPaths(args)); break;
            case "unstage": values.AddRange(new[] { "restore", "--staged", "--" }); values.AddRange(ReadGitPaths(args)); break;
            case "commit":
                var message = ReadString(args, "message", 500);
                if (message.Length == 0) throw new InvalidDataException("A non-empty commit message is required.");
                values.AddRange(new[] { "commit", "-m", message }); break;
            case "create_branch": values.AddRange(new[] { "branch", SafeGitRef(ReadString(args, "branch", 160)) }); break;
            case "switch_branch": values.AddRange(new[] { "switch", "--", SafeGitRef(ReadString(args, "branch", 160)) }); break;
            case "merge": values.AddRange(new[] { "merge", "--no-edit", "--", SafeGitRef(ReadString(args, "branch", 160)) }); break;
            case "revert": values.AddRange(new[] { "revert", "--no-edit", SafeGitCommit(ReadString(args, "commit", 80)) }); break;
            case "cherry_pick": values.AddRange(new[] { "cherry-pick", SafeGitCommit(ReadString(args, "commit", 80)) }); break;
            case "stash":
                var stashMessage = ReadString(args, "message", 240);
                values.AddRange(new[] { "stash", "push", "-m", stashMessage.Length == 0 ? "RunMind stash" : stashMessage });
                break;
            case "fetch": values.AddRange(new[] { "fetch", SafeGitRemote(ReadString(args, "remote", 100)), SafeGitRef(ReadString(args, "branch", 160)) }); break;
            case "pull": values.AddRange(new[] { "pull", "--ff-only", SafeGitRemote(ReadString(args, "remote", 100)), SafeGitRef(ReadString(args, "branch", 160)) }); break;
            case "push": values.AddRange(new[] { "push", SafeGitRemote(ReadString(args, "remote", 100)), SafeGitRef(ReadString(args, "branch", 160)) }); break;
            case "reset_hard": values.AddRange(new[] { "reset", "--hard", SafeGitRef(ReadString(args, "target", 160)) }); break;
            case "clean": values.AddRange(new[] { "clean", "-fd" }); break;
            case "restore": values.AddRange(new[] { "restore", "--worktree", "--" }); values.AddRange(ReadGitPaths(args)); break;
            case "delete_branch": values.AddRange(new[] { "branch", "-d", SafeGitRef(ReadString(args, "branch", 160)) }); break;
            default: throw new InvalidOperationException($"Git operation '{action}' is not supported.");
        }
        var result = await ExecuteGitProcessAsync(root, values, cancellationToken).ConfigureAwait(false);
        var output = action == "remotes" ? RedactGitRemoteCredentials(result.Output) : result.Output;
        return new { action, result.ExitCode, output, error = result.Error, success = result.ExitCode == 0, folder = Path.GetFileName(root) };
    }

    private static string RedactGitRemoteCredentials(string value)
    {
        var redacted = System.Text.RegularExpressions.Regex.Replace(value, @"(?<=://)[^/@\s]+@", "[credentials]@");
        return System.Text.RegularExpressions.Regex.Replace(redacted,
            @"(?i)([?&][^=&\s]*(?:token|secret|password|api[_-]?key|credential|authorization|signature|access[_-]?key)[^=&\s]*=)[^&\s]*",
            "$1[redacted]");
    }

    private static void AddOptionalValidatedPath(List<string> args, JsonElement input, string name)
    {
        var path = ReadString(input, name, 500);
        if (path.Length == 0) return;
        args.Add("--"); args.AddRange(ValidateGitPaths(new[] { path }));
    }
    private static List<string> ReadGitPaths(JsonElement args)
    {
        var paths = ReadStringArray(args, "paths", 100, 500);
        if (paths.Count == 0) throw new InvalidDataException("At least one repository-relative path is required.");
        return ValidateGitPaths(paths);
    }
    private static List<string> ValidateGitPaths(IEnumerable<string> paths)
    {
        var output = new List<string>();
        foreach (var path in paths)
        {
            var normalized = path.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || Path.IsPathRooted(normalized) || normalized.Split('/').Any(part => part is ".." or "."))
                throw new InvalidDataException("Git paths must be safe repository-relative paths.");
            output.Add(normalized);
        }
        return output;
    }
    private static string SafeGitRef(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-') || value.Length > 160 || value.Any(char.IsControl) || value.Contains("..", StringComparison.Ordinal) || value.Contains(' '))
            throw new InvalidDataException("The Git branch or ref is invalid.");
        return value;
    }
    private static string SafeGitCommit(string value)
    {
        if (value.Length is < 7 or > 64 || !value.All(Uri.IsHexDigit)) throw new InvalidDataException("The Git commit must be a hexadecimal object ID (7–64 characters).");
        return value;
    }
    private static string SafeGitRemote(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-') || value.Length > 100 || value.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')))
            throw new InvalidDataException("The Git remote name is invalid.");
        return value;
    }
    private static bool IsSafeDirectoryName(string value) => value.Length is > 0 and <= 120 && value is not "." and not ".." && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ' ');

    private static async Task<(int ExitCode, string Output, string Error)> ExecuteGitProcessAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken, TimeSpan? maxDuration = null, Action<string>? stderrProgress = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(maxDuration ?? GitTimeout);
        var start = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GCM_INTERACTIVE"] = "Never";
        start.Environment["GIT_PAGER"] = "cat";
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Git could not be started. Ensure Git for Windows is installed and available on PATH.");
        }
        catch (System.ComponentModel.Win32Exception error) { throw new InvalidOperationException("Git for Windows was not found. Install Git and ensure git.exe is available on PATH.", error); }
        var outputTask = ReadProcessOutputAsync(process.StandardOutput, MaximumOutputCharacters);
        var errorTask = ReadProcessOutputAsync(process.StandardError, MaximumOutputCharacters, stderrProgress);
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            try { await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false); } catch { }
            throw;
        }
        return (process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }

    private static async Task<string> ReadProcessOutputAsync(StreamReader reader, int maxCharacters, Action<string>? onChunk = null)
    {
        var buffer = new char[8192];
        var result = new StringBuilder(Math.Min(maxCharacters, 16384));
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            var remaining = maxCharacters - result.Length;
            if (remaining > 0) result.Append(buffer, 0, Math.Min(read, remaining));
            if (read > remaining) truncated = true;
            onChunk?.Invoke(new string(buffer, 0, read));
        }
        if (truncated) result.Append("\n[output truncated]");
        return result.ToString();
    }

    private async Task<object> DownloadFileAsync(JsonElement args, CancellationToken cancellationToken, Action<long, long?> progress)
    {
        var rawUrl = ReadString(args, "url", 4096);
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var startUri) || startUri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(startUri.UserInfo))
            throw new InvalidDataException("Downloads require an absolute HTTP or HTTPS URL without embedded credentials.");
        var requestedName = ReadString(args, "filename", 240);
        if (requestedName.Length == 0) requestedName = Uri.UnescapeDataString(startUri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty);
        requestedName = SafeDownloadName(requestedName.Length == 0 ? startUri.Host + ".download" : requestedName);
        var expectedHash = ReadString(args, "sha256", 64).ToLowerInvariant();
        if (expectedHash.Length > 0 && (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))) throw new InvalidDataException("Expected SHA-256 must contain exactly 64 hexadecimal characters.");

        string? selectedPath = null;
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            using var dialog = new System.Windows.Forms.SaveFileDialog
            {
                Title = "Save downloaded file",
                FileName = requestedName,
                Filter = "All files (*.*)|*.*",
                AddExtension = false,
                CheckPathExists = true,
                CheckFileExists = false,
                OverwritePrompt = false,
                RestoreDirectory = true,
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) selectedPath = dialog.FileName;
        });
        if (string.IsNullOrWhiteSpace(selectedPath)) return new { cancelled = true };
        selectedPath = Path.GetFullPath(selectedPath);
        var directory = Path.GetDirectoryName(selectedPath)!;
        if (!Directory.Exists(directory)) throw new InvalidDataException("The selected download directory is unavailable.");
        EnsureNoReparseAncestors(directory);
        if (File.Exists(selectedPath)) throw new IOException("The selected destination already exists. RunMind never overwrites downloaded files.");
        var partPath = selectedPath + ".runmind.part";
        var statePath = partPath + ".json";
        EnsureNoReparseComponents(directory, selectedPath);
        EnsureNoReparseComponents(directory, partPath);
        EnsureNoReparseComponents(directory, statePath);
        var urlFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(startUri.AbsoluteUri))).ToLowerInvariant();
        var offset = 0L;
        string? priorEtag = null;
        DateTimeOffset? priorModified = null;
        if (File.Exists(partPath) && File.Exists(statePath))
        {
            try
            {
                using var state = JsonDocument.Parse(File.ReadAllText(statePath, Encoding.UTF8));
                if (state.RootElement.TryGetProperty("urlFingerprint", out var fingerprint) && fingerprint.GetString() == urlFingerprint)
                {
                    priorEtag = state.RootElement.TryGetProperty("etag", out var etag) ? etag.GetString() : null;
                    if (state.RootElement.TryGetProperty("lastModified", out var modified) && DateTimeOffset.TryParse(modified.GetString(), out var parsed)) priorModified = parsed;
                    if (!string.IsNullOrWhiteSpace(priorEtag) || priorModified.HasValue)
                        offset = new FileInfo(partPath).Length;
                }
            }
            catch { offset = 0; }
        }
        else if (File.Exists(partPath)) throw new IOException("An incomplete download exists without matching RunMind resume metadata. Rename or remove the .runmind.part file before retrying.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DownloadTimeout);
        var current = startUri;
        HttpResponseMessage? response = null;
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            await EnsurePublicDestinationAsync(current, timeout.Token).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("RunMind-Downloader/2.1.2");
            request.Headers.Accept.ParseAdd("*/*");
            if (offset > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
                if (!string.IsNullOrWhiteSpace(priorEtag) && EntityTagHeaderValue.TryParse(priorEtag, out var tag)) request.Headers.IfRange = new System.Net.Http.Headers.RangeConditionHeaderValue(tag);
                else if (priorModified.HasValue) request.Headers.IfRange = new System.Net.Http.Headers.RangeConditionHeaderValue(priorModified.Value);
            }
            response = await DownloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is not null)
            {
                if (redirects == 5) throw new HttpRequestException("The download exceeded the five-redirect limit.");
                var next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                response.Dispose(); response = null;
                if (current.Scheme == Uri.UriSchemeHttps && next.Scheme != Uri.UriSchemeHttps) throw new HttpRequestException("The download attempted to downgrade HTTPS to HTTP.");
                current = next; continue;
            }
            break;
        }
        using (response)
        {
            if (response is null || !response.IsSuccessStatusCode) throw new HttpRequestException($"Download failed with HTTP {(int?)response?.StatusCode ?? 0} ({response?.ReasonPhrase ?? "no response"}).");
            var append = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (offset > 0 && response.StatusCode == HttpStatusCode.PartialContent)
            {
                var responseEtag = response.Headers.ETag?.ToString();
                var responseModified = response.Content.Headers.LastModified;
                if ((!string.IsNullOrWhiteSpace(priorEtag) && !string.Equals(priorEtag, responseEtag, StringComparison.Ordinal))
                    || (string.IsNullOrWhiteSpace(priorEtag) && priorModified.HasValue && responseModified != priorModified))
                    throw new IOException("The remote file changed since the partial download. The existing .runmind.part file was retained; remove it before starting a fresh download.");
            }
            if (!append && response.StatusCode == HttpStatusCode.PartialContent)
                throw new IOException("The server returned an unexpected partial response to a fresh download.");
            if (!append) offset = 0;
            if (append && response.Content.Headers.ContentRange?.From != offset)
                throw new IOException("The server returned an invalid HTTP Range offset. The partial download was retained without appending the unexpected response.");
            var expectedLength = response.Content.Headers.ContentLength.HasValue ? response.Content.Headers.ContentLength.Value + offset : -1;
            if (expectedLength > MaximumDownloadBytes) throw new InvalidDataException("The download exceeds the 4 GB RunMind limit.");
            var etagValue = response.Headers.ETag?.ToString();
            var modifiedValue = response.Content.Headers.LastModified?.ToString("O", CultureInfo.InvariantCulture);
            await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new { urlFingerprint, etag = etagValue, lastModified = modifiedValue }), new UTF8Encoding(false), timeout.Token).ConfigureAwait(false);
            await using (var output = new FileStream(partPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            {
                var buffer = new byte[1024 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    if (output.Length + read > MaximumDownloadBytes) throw new InvalidDataException("The download exceeded the 4 GB RunMind limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                    var total = expectedLength >= 0 ? expectedLength : (long?)null;
                    progress(output.Length, total);
                }
                await output.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            var completedBytes = new FileInfo(partPath).Length;
            if (expectedLength >= 0 && completedBytes != expectedLength) throw new IOException($"The download is incomplete ({completedBytes} of {expectedLength} bytes). The .runmind.part file was retained so the next attempt can resume it.");
            await using var hashStream = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, timeout.Token).ConfigureAwait(false)).ToLowerInvariant();
            if (expectedHash.Length > 0 && !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(expectedHash)))
                throw new InvalidDataException($"SHA-256 checksum mismatch. Expected {expectedHash}; received {hash}. The partial file was retained and was not finalized.");
            if (File.Exists(selectedPath)) throw new IOException("The destination file appeared during download; RunMind refused to overwrite it.");
            File.Move(partPath, selectedPath, false);
            try { File.Delete(statePath); } catch { }
            return new { saved = true, path = selectedPath, filename = Path.GetFileName(selectedPath), bytes = completedBytes, sha256 = hash, etag = etagValue, resumed = append, source = current.GetLeftPart(UriPartial.Path) };
        }
    }

    private static string SafeDownloadName(string value)
    {
        var name = Path.GetFileName(value.Replace('\\', '/'));
        name = new string(name.Where(character => !char.IsControl(character) && !"<>:\"/\\|?*".Contains(character)).ToArray()).Trim().TrimEnd('.');
        if (name.Length is 0 or > 240 || name is "." or "..") throw new InvalidDataException("The download filename is invalid.");
        return name;
    }

    private static HttpClient CreateDownloadClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = ConnectPublicAddressAsync,
        };
        return new HttpClient(handler, true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static async ValueTask<Stream> ConnectPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        IPAddress[] addresses = IPAddress.TryParse(context.DnsEndPoint.Host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address))) throw new HttpRequestException("The download destination resolves to a local, private, or reserved network address and was blocked.");
        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false); return new NetworkStream(socket, true); }
            catch (Exception error) { last = error; socket.Dispose(); }
        }
        throw new HttpRequestException("Could not connect to the download host.", last);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal) return false;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6) return IsPublicAddress(address.MapToIPv4());
            var bytes = address.GetAddressBytes();
            if ((bytes[0] & 0xE0) != 0x20) return false; // Only global-unicast 2000::/3.
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8) return false; // Documentation range.
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00) return false; // Teredo transition range.
            return true;
        }
        var b = address.GetAddressBytes();
        return !(b[0] == 10 || b[0] == 127 || b[0] == 0 || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
            || (b[0] == 100 && b[1] is >= 64 and <= 127) || (b[0] == 192 && b[1] == 0)
            || (b[0] == 192 && b[1] == 2 && b[2] == 0) || (b[0] == 192 && b[1] == 88 && b[2] == 99)
            || (b[0] == 198 && b[1] is 18 or 19) || (b[0] == 198 && b[1] == 51 && b[2] == 100)
            || (b[0] == 203 && b[1] == 0 && b[2] == 113) || (b[0] >= 224));
    }

    private static async Task EnsurePublicDestinationAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) throw new HttpRequestException("Only credential-free HTTP/HTTPS destinations are permitted.");
        var addresses = IPAddress.TryParse(uri.Host, out var literal) ? new[] { literal } : await Dns.GetHostAddressesAsync(uri.Host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address))) throw new HttpRequestException("The download destination resolves to a local, private, or reserved network address and was blocked.");
    }

    private static Task PostAsync(CoreWebView2 core, Dispatcher dispatcher, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return dispatcher.InvokeAsync(() =>
        {
            try { core.PostWebMessageAsJson(json); } catch (InvalidOperationException) { }
        }).Task;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var request in _requests.Values) { try { request.Cancel(); } catch (ObjectDisposedException) { } }
        _requests.Clear();
    }
}
