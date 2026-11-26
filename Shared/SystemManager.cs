#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using System.Net.NetworkInformation;
using System.Windows;

namespace RunMind.Shared;

public sealed partial class AgentHostBridge
{
    private static string SystemManagerRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RunMind", "SystemManager");
    private static string SystemManagerSettingsPath => Path.Combine(SystemManagerRoot, "settings.json");
    private static string SystemManagerCatalogPath => Path.Combine(SystemManagerRoot, "applications.json");
    private static string SystemManagerAdaptersRoot => Path.Combine(SystemManagerRoot, "Adapters");
    private sealed record SystemApplicationRecord(string id, string name, string path, string source, string version);

    private static JsonObject DefaultSystemManagerSettings() => new()
    {
        ["formatVersion"] = 1,
        ["enabled"] = false,
        ["profile"] = "restricted",
        ["groups"] = new JsonObject { ["applications"] = true, ["processes"] = false, ["diagnostics"] = false },
        ["capabilities"] = new JsonObject
        {
            ["discoverApplications"] = true, ["launchApplications"] = true, ["openTargets"] = true,
            ["listProcesses"] = true, ["inspectProcesses"] = true, ["terminateProcesses"] = false,
            ["systemInformation"] = true, ["environmentInformation"] = false, ["serviceInformation"] = false, ["networkInformation"] = false
        },
        ["permissions"] = new JsonObject { ["launchApplications"] = "ask", ["openTargets"] = "ask" }
    };

    private static JsonObject ReadSystemManagerSettings()
    {
        Directory.CreateDirectory(SystemManagerRoot);
        EnsureNoReparseAncestors(SystemManagerRoot);
        var defaults = DefaultSystemManagerSettings();
        if (!File.Exists(SystemManagerSettingsPath)) return defaults;
        if (IsReparsePoint(SystemManagerSettingsPath)) return defaults;
        var info = new FileInfo(SystemManagerSettingsPath);
        if (info.Length > 32 * 1024) return defaults;
        try
        {
            var saved = JsonNode.Parse(File.ReadAllText(SystemManagerSettingsPath, Encoding.UTF8)) as JsonObject;
            if (saved is null) return defaults;
            defaults["enabled"] = saved["enabled"]?.GetValue<bool>() ?? false;
            var profile = (string?)saved["profile"];
            defaults["profile"] = profile is "restricted" or "balanced" or "custom" ? profile : "restricted";
            foreach (var group in new[] { "applications", "processes", "diagnostics" })
                defaults["groups"]![group] = saved["groups"]?[group]?.GetValue<bool>() ?? (bool)defaults["groups"]![group]!;
            foreach (var capability in new[] { "discoverApplications", "launchApplications", "openTargets", "listProcesses", "inspectProcesses", "terminateProcesses", "systemInformation", "environmentInformation", "serviceInformation", "networkInformation" })
                defaults["capabilities"]![capability] = saved["capabilities"]?[capability]?.GetValue<bool>() ?? (bool)defaults["capabilities"]![capability]!;
            foreach (var permission in new[] { "launchApplications", "openTargets" })
            {
                var value = (string?)saved["permissions"]?[permission];
                defaults["permissions"]![permission] = value is "allow" or "ask" ? value : "ask";
            }
        }
        catch { return DefaultSystemManagerSettings(); }
        return defaults;
    }

    private static bool ConfirmSystemManagerAction(string capability, string title, string detail, bool alwaysAsk = false)
    {
        var settings = ReadSystemManagerSettings();
        var profile = (string?)settings["profile"] ?? "restricted";
        var permission = (string?)settings["permissions"]?[capability] ?? "ask";
        if (!alwaysAsk && profile != "restricted" && permission == "allow") return true;
        var application = System.Windows.Application.Current;
        if (application?.Dispatcher is null) throw new InvalidOperationException("Native confirmation UI is unavailable; the System Manager action was blocked.");
        var approved = false;
        application.Dispatcher.Invoke(() =>
        {
            var result = System.Windows.MessageBox.Show(application.MainWindow, detail + "\n\nRunMind will perform this action using your current Windows account permissions.", title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            approved = result == MessageBoxResult.Yes;
        });
        return approved;
    }

    private static object SystemManagerSettingsGet() => new { settings = ReadSystemManagerSettings(), path = @"%LOCALAPPDATA%\RunMind\SystemManager\settings.json" };

    private static object SystemManagerSettingsSave(JsonElement args)
    {
        if (!args.TryGetProperty("settings", out var input) || input.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("A System Manager settings object is required.");
        var current = ReadSystemManagerSettings();
        if (input.TryGetProperty("enabled", out var enabled) && (enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)) current["enabled"] = enabled.GetBoolean();
        if (input.TryGetProperty("profile", out var profile) && profile.ValueKind == JsonValueKind.String)
        {
            var value = profile.GetString();
            if (value is not ("restricted" or "balanced" or "custom")) throw new InvalidDataException("Invalid System Manager permission profile.");
            current["profile"] = value;
        }
        foreach (var group in new[] { "applications", "processes", "diagnostics" })
            if (input.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Object && groups.TryGetProperty(group, out var groupValue) && (groupValue.ValueKind is JsonValueKind.True or JsonValueKind.False)) current["groups"]![group] = groupValue.GetBoolean();
        foreach (var capability in new[] { "discoverApplications", "launchApplications", "openTargets", "listProcesses", "inspectProcesses", "terminateProcesses", "systemInformation", "environmentInformation", "serviceInformation", "networkInformation" })
            if (input.TryGetProperty("capabilities", out var capabilities) && capabilities.ValueKind == JsonValueKind.Object && capabilities.TryGetProperty(capability, out var capabilityValue) && (capabilityValue.ValueKind is JsonValueKind.True or JsonValueKind.False)) current["capabilities"]![capability] = capabilityValue.GetBoolean();
        foreach (var permission in new[] { "launchApplications", "openTargets" })
            if (input.TryGetProperty("permissions", out var permissions) && permissions.ValueKind == JsonValueKind.Object && permissions.TryGetProperty(permission, out var permissionValue) && permissionValue.ValueKind == JsonValueKind.String && (permissionValue.GetString() is "allow" or "ask")) current["permissions"]![permission] = permissionValue.GetString();
        Directory.CreateDirectory(SystemManagerRoot);
        EnsureNoReparseAncestors(SystemManagerRoot);
        var temp = SystemManagerSettingsPath + ".tmp";
        if ((File.Exists(SystemManagerSettingsPath) && IsReparsePoint(SystemManagerSettingsPath)) || (File.Exists(temp) && IsReparsePoint(temp))) throw new InvalidDataException("System Manager settings cannot be stored through a symbolic link or reparse point.");
        File.WriteAllText(temp, current.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temp, SystemManagerSettingsPath, true);
        return new { saved = true, settings = current };
    }

    private static void RequireSystemCapability(string group, string capability)
    {
        var settings = ReadSystemManagerSettings();
        if (!(bool?)settings["enabled"] ?? false) throw new UnauthorizedAccessException("Windows System Manager is disabled in RunMind Settings.");
        if (!(bool?)settings["groups"]?[group] ?? false) throw new UnauthorizedAccessException($"System Manager group '{group}' is disabled.");
        if (!(bool?)settings["capabilities"]?[capability] ?? false) throw new UnauthorizedAccessException($"System Manager capability '{capability}' is disabled.");
    }

    private static object SystemManagerListApplications(string query, bool forceRefresh = false)
    {
        RequireSystemCapability("applications", "discoverApplications");
        Directory.CreateDirectory(SystemManagerRoot);
        EnsureNoReparseAncestors(SystemManagerRoot);
        List<SystemApplicationRecord>? cached = null;
        try
        {
            if (!forceRefresh && File.Exists(SystemManagerCatalogPath) && !IsReparsePoint(SystemManagerCatalogPath) && new FileInfo(SystemManagerCatalogPath).Length <= 2 * 1024 * 1024 && DateTime.UtcNow - File.GetLastWriteTimeUtc(SystemManagerCatalogPath) < TimeSpan.FromMinutes(5))
            {
                var loaded = JsonSerializer.Deserialize<List<SystemApplicationRecord>>(File.ReadAllText(SystemManagerCatalogPath, Encoding.UTF8));
                cached = loaded?.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.id) && !string.IsNullOrWhiteSpace(item.name) && !string.IsNullOrWhiteSpace(item.path))
                    .GroupBy(item => item.id, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).Take(1000).ToList();
            }
        }
        catch { cached = null; }
        var rows = cached is { Count: > 0 }
            ? cached.ToDictionary(item => item.id, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, SystemApplicationRecord>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, string path, string source, string version = "")
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path) || rows.Count >= 1000) return;
            var normalized = Environment.ExpandEnvironmentVariables(path.Trim()).Trim('"');
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToUpperInvariant())))[..16].ToLowerInvariant();
            if (!rows.ContainsKey(id)) rows[id] = new SystemApplicationRecord(id, name.Trim(), normalized, source, version);
        }
        if (cached is null || cached.Count == 0)
        {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var apps = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths");
                if (apps is null) continue;
                foreach (var appName in apps.GetSubKeyNames().Take(500))
                {
                    try { using var key = apps.OpenSubKey(appName); var path = key?.GetValue(null) as string; if (!string.IsNullOrWhiteSpace(path)) Add(Path.GetFileNameWithoutExtension(appName), Environment.ExpandEnvironmentVariables(path), "App Paths"); } catch { }
                }
            }
            catch { }
        }
        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(100);
        foreach (var directory in pathDirectories)
        {
            try
            {
                var normalizedDirectory = directory.Trim('"');
                if (!Directory.Exists(normalizedDirectory) || IsReparsePoint(normalizedDirectory)) continue;
                foreach (var executable in Directory.EnumerateFiles(normalizedDirectory, "*.exe", SearchOption.TopDirectoryOnly).Take(25))
                    if (!IsReparsePoint(executable)) Add(Path.GetFileNameWithoutExtension(executable), Path.GetFullPath(executable), "PATH");
            }
            catch { }
            if (rows.Count >= 1000) break;
        }
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var pending = new Stack<(string path, int depth)>(); pending.Push((root, 0)); var seen = 0;
            while (pending.Count > 0 && seen < 2500)
            {
                var (folder, depth) = pending.Pop(); if (depth > 4) continue;
                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
                    {
                        if (++seen > 2500) break;
                        if (IsReparsePoint(entry)) continue;
                        if (Directory.Exists(entry)) pending.Push((entry, depth + 1));
                        else if (entry.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) Add(Path.GetFileNameWithoutExtension(entry), entry, "Start Menu shortcut");
                    }
                }
                catch { }
            }
        }
        try
        {
            if (File.Exists(SystemManagerCatalogPath) && IsReparsePoint(SystemManagerCatalogPath)) throw new InvalidDataException("The application catalog cannot be a symbolic link or reparse point.");
            File.WriteAllText(SystemManagerCatalogPath, JsonSerializer.Serialize(rows.Values.Take(1000).ToList()), new UTF8Encoding(false));
        }
        catch { }
        }
        var result = rows.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query)) result = result.Where(value => value.name.Contains(query, StringComparison.OrdinalIgnoreCase) || value.path.Contains(query, StringComparison.OrdinalIgnoreCase));
        var matches = result.Take(250).ToList();
        if (matches.Count == 0 && cached is { Count: > 0 } && !string.IsNullOrWhiteSpace(query))
        {
            return SystemManagerListApplications(query, forceRefresh: true);
        }
        return new { applications = matches, count = matches.Count, catalog = "persistent local catalog with five-minute freshness and on-demand refresh", note = "Start Menu shortcuts are launched through their .lnk files; executable targets are not resolved or executed during discovery." };
    }

    private sealed record SystemApplicationAdapter(string id, string displayName, string match, Dictionary<string, string[]> operations, bool builtIn);

    private static List<SystemApplicationAdapter> ReadSystemApplicationAdapters()
    {
        var rows = new List<SystemApplicationAdapter>
        {
            new("vscode", "Visual Studio Code", "code", new Dictionary<string,string[]> { ["open-file"] = new[] { "--goto", "{target}:{line}" }, ["open-folder"] = new[] { "--new-window", "{target}" } }, true),
            new("windows-terminal", "Windows Terminal", "wt", new Dictionary<string,string[]> { ["open-folder"] = new[] { "-d", "{target}" } }, true),
            new("firefox", "Firefox", "firefox", new Dictionary<string,string[]> { ["open-url"] = new[] { "-new-tab", "{target}" } }, true),
            new("notepad-plus-plus", "Notepad++", "notepad++", new Dictionary<string,string[]> { ["open-file"] = new[] { "-n{line}", "{target}" } }, true),
            new("blender", "Blender", "blender", new Dictionary<string,string[]> { ["open-file"] = new[] { "{target}" } }, true),
        };
        Directory.CreateDirectory(SystemManagerAdaptersRoot);
        EnsureNoReparseAncestors(SystemManagerAdaptersRoot);
        foreach (var file in Directory.EnumerateFiles(SystemManagerAdaptersRoot, "*.json").Take(100))
        {
            try
            {
                if (IsReparsePoint(file) || new FileInfo(file).Length > 32 * 1024) continue;
                using var document = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
                var root = document.RootElement;
                var id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? "" : "";
                var name = root.TryGetProperty("displayName", out var nameValue) ? nameValue.GetString() ?? "" : "";
                var match = root.TryGetProperty("match", out var matchValue) ? matchValue.GetString() ?? "" : "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9]+(?:-[a-z0-9]+)*$") || name.Length is < 1 or > 80 || match.Length is < 1 or > 120 || rows.Any(item => item.id == id)) continue;
                if (!root.TryGetProperty("operations", out var operationsValue) || operationsValue.ValueKind != JsonValueKind.Object) continue;
                var operations = new Dictionary<string,string[]>(StringComparer.Ordinal);
                foreach (var operation in operationsValue.EnumerateObject().Take(8))
                {
                    if (operation.Name is not ("open-file" or "open-folder" or "open-url")) continue;
                    if (operation.Value.ValueKind != JsonValueKind.Array || operation.Value.GetArrayLength() > 20) continue;
                    var values = operation.Value.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString() ?? "").ToArray();
                    if (values.Any(value => value.Length > 1024 || System.Text.RegularExpressions.Regex.IsMatch(value, "\\{(?!target\\}|line\\})"))) continue;
                    operations[operation.Name] = values;
                }
                if (operations.Count > 0) rows.Add(new SystemApplicationAdapter(id, name, match, operations, false));
            }
            catch { }
        }
        return rows;
    }

    private static object SystemManagerSaveAdapter(JsonElement args)
    {
        RequireSystemCapability("applications", "launchApplications");
        if (!args.TryGetProperty("adapter", out var input) || input.ValueKind != JsonValueKind.Object) throw new InvalidDataException("A declarative adapter object is required.");
        var id = input.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? "" : "";
        var name = input.TryGetProperty("displayName", out var nameValue) ? nameValue.GetString() ?? "" : "";
        var match = input.TryGetProperty("match", out var matchValue) ? matchValue.GetString() ?? "" : "";
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9]+(?:-[a-z0-9]+)*$") || id.Length > 64) throw new InvalidDataException("Adapter ID must use lowercase letters, numbers and hyphens.");
        if (name.Length is < 1 or > 80 || match.Length is < 1 or > 120) throw new InvalidDataException("Adapter displayName or application match is invalid.");
        if (!input.TryGetProperty("operations", out var operationsInput) || operationsInput.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Adapter operations must be an object.");
        var operations = new JsonObject();
        foreach (var operation in operationsInput.EnumerateObject().Take(8))
        {
            if (operation.Name is not ("open-file" or "open-folder" or "open-url")) throw new InvalidDataException($"Unsupported adapter operation '{operation.Name}'.");
            if (operation.Value.ValueKind != JsonValueKind.Array || operation.Value.GetArrayLength() is < 1 or > 20) throw new InvalidDataException("Each adapter operation must contain 1–20 argument templates.");
            var values = new JsonArray();
            foreach (var argument in operation.Value.EnumerateArray())
            {
                if (argument.ValueKind != JsonValueKind.String) throw new InvalidDataException("Adapter argument templates must be strings.");
                var value = argument.GetString() ?? "";
                if (value.Length > 1024 || System.Text.RegularExpressions.Regex.IsMatch(value, "\\{(?!target\\}|line\\})")) throw new InvalidDataException("Adapter templates may use only {target} and {line}; scripts and shell syntax are not supported.");
                values.Add(value);
            }
            operations[operation.Name] = values;
        }
        if (operations.Count == 0) throw new InvalidDataException("At least one supported adapter operation is required.");
        var root = SystemManagerAdaptersRoot; Directory.CreateDirectory(root); EnsureNoReparseAncestors(root);
        if (Directory.EnumerateFiles(root, "*.json").Take(101).Count() >= 100) throw new InvalidOperationException("The custom application adapter limit (100) has been reached.");
        var destination = Path.Combine(root, id + ".json");
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException($"Adapter '{id}' already exists. Custom adapter updates are not supported by this operation.");
        if (ReadSystemApplicationAdapters().Any(item => item.id.Equals(id, StringComparison.OrdinalIgnoreCase))) throw new IOException($"Adapter ID '{id}' is already registered.");
        var manifest = new JsonObject { ["id"] = id, ["displayName"] = name, ["match"] = match, ["operations"] = operations };
        if (!ConfirmSystemManagerAction("launchApplications", "RunMind · Install application adapter", $"Adapter: {name} ({id})\nApplication match: {match}\nOperations: {string.Join(", ", operations.Select(pair => pair.Key))}\n\nOnly argument templates are installed. No scripts or shell commands are supported.", alwaysAsk: true)) return new { installed = false, cancelled = true, id };
        var temp = Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temp, destination, false);
        }
        catch { try { if (File.Exists(temp)) File.Delete(temp); } catch { } throw; }
        return new { installed = true, id, displayName = name, operations = operations.Select(pair => pair.Key).ToArray(), path = destination, note = "Declarative adapter installed. It contains argument templates only and cannot execute scripts or shell commands." };
    }

    private static object SystemManagerListAdapters()
    {
        RequireSystemCapability("applications", "launchApplications");
        var adapters = ReadSystemApplicationAdapters().Select(item => new { item.id, item.displayName, item.match, operations = item.operations.Keys.ToArray(), item.builtIn }).ToArray();
        return new { adapters, count = adapters.Length, customAdapterDirectory = SystemManagerAdaptersRoot, format = "JSON manifests; argument arrays only; no scripts or shell commands" };
    }

    private static object SystemManagerLaunchAdapter(JsonElement args)
    {
        RequireSystemCapability("applications", "launchApplications");
        var id = ReadString(args, "adapterId", 80);
        var operation = ReadString(args, "operation", 40);
        var target = ReadString(args, "target", 4096);
        var line = args.TryGetProperty("line", out var lineValue) && lineValue.TryGetInt32(out var requestedLine) ? Math.Clamp(requestedLine, 1, 1000000) : 1;
        var adapter = ReadSystemApplicationAdapters().FirstOrDefault(item => item.id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The requested application adapter was not found.");
        if (!adapter.operations.TryGetValue(operation, out var templates)) throw new InvalidDataException($"Adapter '{id}' does not support operation '{operation}'.");
        if (operation == "open-url")
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("The browser adapter accepts HTTPS URLs only.");
        }
        else
        {
            if (!Path.IsPathFullyQualified(target) || (!File.Exists(target) && !Directory.Exists(target))) throw new InvalidDataException("The adapter target must be an existing fully-qualified file or folder path.");
            EnsureNoReparseAncestors(target);
        }
        _ = SystemManagerListApplications(adapter.match);
        var catalog = File.Exists(SystemManagerCatalogPath) && !IsReparsePoint(SystemManagerCatalogPath) && new FileInfo(SystemManagerCatalogPath).Length <= 2 * 1024 * 1024
            ? JsonSerializer.Deserialize<List<SystemApplicationRecord>>(File.ReadAllText(SystemManagerCatalogPath, Encoding.UTF8))?.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.name) && !string.IsNullOrWhiteSpace(item.path)).ToList() ?? new List<SystemApplicationRecord>()
            : new List<SystemApplicationRecord>();
        var matches = catalog.Where(item => item.name.Contains(adapter.match, StringComparison.OrdinalIgnoreCase) || item.path.Contains(adapter.match, StringComparison.OrdinalIgnoreCase))
            .Where(item => item.path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(item.path)).Take(10).ToList();
        if (matches.Count == 0) return new { launched = false, reason = "application-not-found", adapter = id, matches = Array.Empty<object>() };
        if (matches.Count > 1) return new { launched = false, reason = "ambiguous-application", adapter = id, matches = matches.Select(item => new { item.name, item.path, item.source }).ToArray() };
        var launchArguments = templates.Select(value => value.Replace("{target}", target, StringComparison.Ordinal).Replace("{line}", line.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)).ToArray();
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { path = matches[0].path, arguments = launchArguments }));
        return SystemManagerLaunchApplication(document.RootElement);
    }

    private static object SystemManagerLaunchApplication(JsonElement args)
    {
        RequireSystemCapability("applications", "launchApplications");
        var path = ReadString(args, "path", 2048);
        if (path.Length == 0 || !Path.IsPathFullyQualified(path)) throw new InvalidDataException("A fully qualified application path from the discovered catalog is required.");
        EnsureNoReparseAncestors(path);
        if (!File.Exists(path) || !(path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))) throw new FileNotFoundException("The selected application is no longer available.");
        _ = SystemManagerListApplications(string.Empty);
        var catalog = File.Exists(SystemManagerCatalogPath) && !IsReparsePoint(SystemManagerCatalogPath) && new FileInfo(SystemManagerCatalogPath).Length <= 2 * 1024 * 1024
            ? JsonSerializer.Deserialize<List<SystemApplicationRecord>>(File.ReadAllText(SystemManagerCatalogPath, Encoding.UTF8))?.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.path)).ToList() ?? new List<SystemApplicationRecord>()
            : new List<SystemApplicationRecord>();
        var catalogued = false;
        foreach (var item in catalog)
        {
            try { if (string.Equals(Path.GetFullPath(item.path), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) { catalogued = true; break; } }
            catch { }
        }
        if (!catalogued)
        {
            _ = SystemManagerListApplications(string.Empty, forceRefresh: true);
            catalog = File.Exists(SystemManagerCatalogPath) && !IsReparsePoint(SystemManagerCatalogPath) && new FileInfo(SystemManagerCatalogPath).Length <= 2 * 1024 * 1024
                ? JsonSerializer.Deserialize<List<SystemApplicationRecord>>(File.ReadAllText(SystemManagerCatalogPath, Encoding.UTF8))?.Where(item => item is not null && !string.IsNullOrWhiteSpace(item.path)).ToList() ?? new List<SystemApplicationRecord>()
                : new List<SystemApplicationRecord>();
            foreach (var item in catalog)
            {
                try { if (string.Equals(Path.GetFullPath(item.path), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) { catalogued = true; break; } }
                catch { }
            }
        }
        if (!catalogued) throw new UnauthorizedAccessException("The requested executable is not present in RunMind's discovered application catalog. Search the catalog first.");
        var start = new ProcessStartInfo(path) { UseShellExecute = true };
        if (args.TryGetProperty("arguments", out var values) && values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0)
        {
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Arguments cannot be added to a Start Menu shortcut. Select its executable installation instead.");
            start.UseShellExecute = false;
            foreach (var value in values.EnumerateArray().Take(40)) { if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Application arguments must be strings."); start.ArgumentList.Add(value.GetString() ?? string.Empty); }
        }
        var argumentPreview = args.TryGetProperty("arguments", out var confirmArguments) && confirmArguments.ValueKind == JsonValueKind.Array
            ? string.Join(Environment.NewLine, confirmArguments.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()).Take(40))
            : "(none)";
        if (!ConfirmSystemManagerAction("launchApplications", "RunMind · Launch application", $"Application: {Path.GetFileNameWithoutExtension(path)}\nPath: {path}\nArguments:\n{argumentPreview}")) return new { launched = false, cancelled = true, reason = "user-declined" };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows did not return a process for the application launch.");
        return new { launched = true, processId = process.Id, application = Path.GetFileNameWithoutExtension(path), path };
    }

    private static object SystemManagerOpenTarget(JsonElement args)
    {
        RequireSystemCapability("applications", "openTargets");
        var target = ReadString(args, "target", 4096);
        if (target.Length == 0) throw new InvalidDataException("A file, folder or HTTPS URL is required.");
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo))
        {
            if (!ConfirmSystemManagerAction("openTargets", "RunMind · Open URL", uri.AbsoluteUri)) return new { opened = false, cancelled = true, reason = "user-declined" };
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return new { opened = true, type = "url", target = uri.AbsoluteUri };
        }
        if (!Path.IsPathFullyQualified(target)) throw new InvalidDataException("File and folder targets must be fully qualified paths.");
        EnsureNoReparseAncestors(target);
        if (!File.Exists(target) && !Directory.Exists(target)) throw new FileNotFoundException("The requested file or folder does not exist.");
        if (!ConfirmSystemManagerAction("openTargets", "RunMind · Open file or folder", target)) return new { opened = false, cancelled = true, reason = "user-declined" };
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        return new { opened = true, type = Directory.Exists(target) ? "folder" : "file", target };
    }

    private static object SystemManagerListProcesses(string query)
    {
        RequireSystemCapability("processes", "listProcesses");
        var rows = new List<object>();
        var includeDetails = (bool?)ReadSystemManagerSettings()["capabilities"]?["inspectProcesses"] == true;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                if (!string.IsNullOrWhiteSpace(query) && !name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                string? path = null; DateTime? started = null;
                if (includeDetails) { try { path = process.MainModule?.FileName; } catch { } }
                try { started = process.StartTime.ToUniversalTime(); } catch { }
                long memory = 0; try { memory = process.WorkingSet64; } catch { }
                rows.Add(new { name, processId = process.Id, memoryBytes = memory, executablePath = path, startedAtUtc = started });
            }
            catch { }
            finally { process.Dispose(); }
            if (rows.Count >= 250) break;
        }
        return new { processes = rows, count = rows.Count, truncated = rows.Count >= 250 };
    }

    private static object SystemManagerTerminateProcess(JsonElement args)
    {
        RequireSystemCapability("processes", "terminateProcesses");
        if (!args.TryGetProperty("processId", out var pidElement) || !pidElement.TryGetInt32(out var pid) || pid <= 0) throw new InvalidDataException("A valid process ID is required.");
        var expectedName = ReadString(args, "expectedName", 260);
        var expectedPath = ReadString(args, "expectedPath", 2048);
        var expectedStartedAt = ReadString(args, "expectedStartedAtUtc", 80);
        if (!DateTimeOffset.TryParse(expectedStartedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var expectedStart))
            throw new InvalidDataException("A process start timestamp from the process listing is required to prevent PID-reuse termination.");
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { return new { terminated = false, reason = "already-exited", processId = pid }; }
        using (process)
        {
        if (process.HasExited) return new { terminated = false, reason = "already-exited", processId = pid };
        if (string.IsNullOrWhiteSpace(expectedName) || !string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The process identity changed since confirmation; termination was cancelled.");
        DateTimeOffset actualStart;
        try { actualStart = new DateTimeOffset(process.StartTime.ToUniversalTime()); }
        catch { throw new UnauthorizedAccessException("RunMind cannot verify this process start time; termination was blocked."); }
        if (Math.Abs((actualStart - expectedStart).TotalSeconds) > 1) throw new InvalidOperationException("The PID now belongs to a different process instance; termination was cancelled.");
        string? actualPath = null; try { actualPath = process.MainModule?.FileName; } catch { }
        if (!string.IsNullOrWhiteSpace(expectedPath) && !string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The executable path changed since confirmation; termination was cancelled.");
        var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "System", "Idle", "Registry", "csrss", "wininit", "winlogon", "services", "lsass", "smss", "svchost", "dwm", "fontdrvhost" };
        if (protectedNames.Contains(process.ProcessName)) throw new UnauthorizedAccessException("RunMind will not terminate a protected Windows process.");
        if (!ConfirmSystemManagerAction("terminateProcesses", "RunMind · Terminate process", $"Process: {process.ProcessName}\nPID: {pid}\nStarted: {actualStart:O}\nExecutable: {actualPath ?? "Unavailable"}\n\nThis action terminates the process immediately and may cause unsaved work to be lost.", alwaysAsk: true))
            return new { terminated = false, cancelled = true, reason = "user-declined", processId = pid };
        process.Kill(entireProcessTree: false);
        var exited = process.WaitForExit(5000);
        return new { terminated = exited, processId = pid, processName = expectedName, executablePath = actualPath, reason = exited ? "terminated" : "termination-requested-process-still-exiting" };
        }
    }

    private static object SystemManagerDiagnostics()
    {
        var settings = ReadSystemManagerSettings();
        if (!(bool?)settings["enabled"] ?? false) throw new UnauthorizedAccessException("Windows System Manager is disabled in RunMind Settings.");
        if (!(bool?)settings["groups"]?["diagnostics"] ?? false) throw new UnauthorizedAccessException("System Diagnostics is disabled.");
        var capabilities = settings["capabilities"];
        if (!new[] { "systemInformation", "environmentInformation", "serviceInformation", "networkInformation" }.Any(key => (bool?)capabilities?[key] == true)) throw new UnauthorizedAccessException("No System Diagnostics capability is enabled.");
        var data = new Dictionary<string, object?>();
        if ((bool?)capabilities?["systemInformation"] == true)
        {
            data["machineName"] = Environment.MachineName;
            data["osVersion"] = Environment.OSVersion.VersionString;
            data["processorCount"] = Environment.ProcessorCount;
            data["processArchitecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
            data["osArchitecture"] = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();
            data["framework"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            data["workingSetBytes"] = Environment.WorkingSet;
        }
        if ((bool?)capabilities?["environmentInformation"] == true)
        {
            data["pathEntries"] = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator).Take(100).ToArray();
        }
        if ((bool?)capabilities?["serviceInformation"] == true)
        {
            data["services"] = ReadWindowsServices();
        }
        if ((bool?)capabilities?["networkInformation"] == true)
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces().Take(50).Select(adapter => new
            {
                name = adapter.Name, description = adapter.Description, type = adapter.NetworkInterfaceType.ToString(),
                status = adapter.OperationalStatus.ToString(), speedBitsPerSecond = adapter.Speed,
                addresses = adapter.GetIPProperties().UnicastAddresses.Take(20).Select(item => item.Address.ToString()).ToArray()
            }).ToArray();
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            data["network"] = new { hostName = properties.HostName, domainName = properties.DomainName, adapters, activeTcpConnections = properties.GetActiveTcpConnections().Take(200).Select(item => new { local = item.LocalEndPoint.ToString(), remote = item.RemoteEndPoint.ToString(), state = item.State.ToString() }).ToArray(), tcpListeners = properties.GetActiveTcpListeners().Take(100).Select(item => item.ToString()).ToArray() };
        }
        return new { diagnostics = data, note = "Read-only Windows and process diagnostics. Service and network details are included only when their individual capabilities are enabled." };
    }

    private static object ReadWindowsServices()
    {
        var scPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
        if (!File.Exists(scPath)) throw new FileNotFoundException("Windows Service Control (sc.exe) was not found in the system directory.");
        var start = new ProcessStartInfo(scPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("query"); start.ArgumentList.Add("type="); start.ArgumentList.Add("service"); start.ArgumentList.Add("state="); start.ArgumentList.Add("all");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Windows Service Control utility.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10000)) { try { process.Kill(true); } catch { } throw new TimeoutException("Windows service enumeration exceeded 10 seconds."); }
        var output = outputTask.GetAwaiter().GetResult();
        _ = errorTask.GetAwaiter().GetResult();
        var services = new List<object>();
        string? name = null; string? display = null; string? state = null;
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase)) name = trimmed[13..].Trim();
            else if (trimmed.StartsWith("DISPLAY_NAME:", StringComparison.OrdinalIgnoreCase)) display = trimmed[13..].Trim();
            else if (trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                state = parts.Length >= 4 ? parts[3] : trimmed;
                if (name is not null) { services.Add(new { name, displayName = display, state }); name = null; display = null; state = null; }
            }
            if (services.Count >= 250) break;
        }
        return new { services, count = services.Count, truncated = services.Count >= 250, exitCode = process.ExitCode };
    }

}
