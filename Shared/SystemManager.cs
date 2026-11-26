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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace RunMind.Shared;

public sealed partial class AgentHostBridge
{
    private static string SystemManagerRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RunMind", "SystemManager");
    private static string SystemManagerSettingsPath => Path.Combine(SystemManagerRoot, "settings.json");
    private static string SystemManagerCatalogPath => Path.Combine(SystemManagerRoot, "applications.json");
    private static string SystemManagerAdaptersRoot => Path.Combine(SystemManagerRoot, "Adapters");
    private static string SystemManagerAdapterOverridesRoot => Path.Combine(SystemManagerAdaptersRoot, "Overrides");
    private sealed record SystemApplicationRecord(string id, string name, string path, string source, string version, string[]? aliases = null, string? appUserModelId = null);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeProcessEntry32
    {
        public uint dwSize; public uint cntUsage; public uint th32ProcessID; public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID; public uint cntThreads; public uint th32ParentProcessID; public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32FirstW(IntPtr snapshot, ref NativeProcessEntry32 entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32NextW(IntPtr snapshot, ref NativeProcessEntry32 entry);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    private static Dictionary<int, int> ReadProcessParentIds()
    {
        var result = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(0x00000002, 0);
        if (snapshot == new IntPtr(-1)) return result;
        try
        {
            var entry = new NativeProcessEntry32 { dwSize = (uint)Marshal.SizeOf<NativeProcessEntry32>(), szExeFile = string.Empty };
            if (Process32FirstW(snapshot, ref entry)) do { result[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID; } while (Process32NextW(snapshot, ref entry));
        }
        catch { }
        finally { CloseHandle(snapshot); }
        return result;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
    private const int SwRestore = 9;

    private static string NormalizeExecutableCandidate(string value)
    {
        var candidate = Environment.ExpandEnvironmentVariables((value ?? string.Empty).Trim());
        if (candidate.Length == 0) return string.Empty;
        if (candidate.StartsWith('"'))
        {
            var closingQuote = candidate.IndexOf('"', 1);
            if (closingQuote > 1) candidate = candidate[1..closingQuote];
        }
        else
        {
            var exeEnd = candidate.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeEnd >= 0) candidate = candidate[..(exeEnd + 4)];
        }
        return candidate.Trim().Trim('"');
    }

    private static string ResolveShortcutTarget(string shortcutPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: false)
                ?? throw new InvalidOperationException("Windows Script Host shortcut resolution is unavailable.");
            shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Could not create the Windows shortcut resolver.");
            shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            if (shortcut is null) throw new InvalidDataException("The Start Menu shortcut could not be opened.");
            var target = shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null) as string;
            var normalized = NormalizeExecutableCandidate(target ?? string.Empty);
            if (normalized.Length == 0 || !Path.IsPathFullyQualified(normalized) || !File.Exists(normalized) || !normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("The Start Menu shortcut target is missing or is not an executable.", normalized);
            return Path.GetFullPath(normalized);
        }
        finally
        {
            if (shortcut is not null && System.Runtime.InteropServices.Marshal.IsComObject(shortcut)) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && System.Runtime.InteropServices.Marshal.IsComObject(shell)) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    private static bool TryResolveApplicationExecutable(string candidate, out string executable)
    {
        executable = string.Empty;
        try
        {
            var normalized = Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"'));
            if (normalized.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) normalized = ResolveShortcutTarget(normalized);
            else normalized = NormalizeExecutableCandidate(normalized);
            if (!Path.IsPathFullyQualified(normalized) || !normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(normalized)) return false;
            EnsureNoReparseAncestors(normalized);
            executable = Path.GetFullPath(normalized);
            return true;
        }
        catch { return false; }
    }

    private static List<int> FindRunningProcessesForExecutable(string executablePath)
    {
        var matches = new List<int>();
        foreach (var candidate in Process.GetProcesses())
        {
            using (candidate)
            {
                try
                {
                    var candidatePath = candidate.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(candidatePath) && string.Equals(Path.GetFullPath(candidatePath), executablePath, StringComparison.OrdinalIgnoreCase))
                        matches.Add(candidate.Id);
                }
                catch { }
            }
        }
        return matches;
    }

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
        ["permissions"] = new JsonObject { ["launchApplications"] = "ask", ["openTargets"] = "ask" },
        ["applicationBehaviors"] = new JsonObject(),
        ["commandTimeoutSeconds"] = 0
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
            var timeoutSeconds = saved["commandTimeoutSeconds"]?.GetValue<int>() ?? 0;
            defaults["commandTimeoutSeconds"] = Math.Clamp(timeoutSeconds, 0, 3600);
            if (saved["applicationBehaviors"] is JsonObject behaviors)
            {
                foreach (var pair in behaviors.Take(100))
                {
                    if (pair.Key.Length != 16 || !pair.Key.All(Uri.IsHexDigit) || pair.Value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var value)) continue;
                    if (value is "ask" or "reuse" or "new-window" or "new-process") defaults["applicationBehaviors"]![pair.Key] = value;
                }
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
        if (input.TryGetProperty("commandTimeoutSeconds", out var timeoutValue) && timeoutValue.TryGetInt32(out var requestedTimeout))
        {
            if (requestedTimeout is < 0 or > 3600) throw new InvalidDataException("Command timeout must be between 0 and 3600 seconds. Use 0 for no automatic timeout.");
            current["commandTimeoutSeconds"] = requestedTimeout;
        }
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
        if (!(bool?)settings["enabled"] ?? false) throw new UnauthorizedAccessException("Windows System Manager is disabled in the System Manager menu.");
        if (!(bool?)settings["groups"]?[group] ?? false) throw new UnauthorizedAccessException($"System Manager group '{group}' is disabled.");
        if (!(bool?)settings["capabilities"]?[capability] ?? false) throw new UnauthorizedAccessException($"System Manager capability '{capability}' is disabled.");
    }

    private static bool IsValidAppUserModelId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 300 || value.Any(char.IsWhiteSpace)) return false;
        var separator = value.IndexOf('!');
        return separator > 0 && separator < value.Length - 1 && value.IndexOf('!', separator + 1) < 0
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or '!');
    }

    private static string NormalizeApplicationSearchText(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousSpace = true;
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character)) { builder.Append(character); previousSpace = false; }
            else if (!previousSpace) { builder.Append(' '); previousSpace = true; }
        }
        return builder.ToString().Trim();
    }

    private static string NormalizeApplicationQuery(string query)
    {
        var words = NormalizeApplicationSearchText(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var leadingVerbs = new HashSet<string>(new[] { "open", "launch", "start", "run", "find", "search", "please" }, StringComparer.Ordinal);
        var genericWords = new HashSet<string>(new[] { "the", "my", "a", "an", "application", "app", "program", "browser", "called" }, StringComparer.Ordinal);
        while (words.Count > 1 && leadingVerbs.Contains(words[0])) words.RemoveAt(0);
        words.RemoveAll(genericWords.Contains);
        return string.Join(' ', words);
    }

    private static string[] BuildApplicationAliases(string name, string path, IEnumerable<string>? supplied = null)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value) { var normalized = NormalizeApplicationSearchText(value ?? string.Empty); if (normalized.Length > 0) aliases.Add(normalized); }
        Add(name);
        Add(Path.GetFileNameWithoutExtension(path));
        if (supplied is not null) foreach (var alias in supplied) Add(alias);
        // Search aliases are derived only from the discovered application's own
        // metadata (display name, executable name, and source-provided names).
        // Do not add application-specific aliases or rules for individual products.
        return aliases.Take(24).ToArray();
    }

    private static int EditDistanceAtMost(string left, string right, int maximum)
    {
        if (Math.Abs(left.Length - right.Length) > maximum) return maximum + 1;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            var rowMinimum = current[0];
            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), previous[column - 1] + cost);
                rowMinimum = Math.Min(rowMinimum, current[column]);
            }
            if (rowMinimum > maximum) return maximum + 1;
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    private static int ScoreApplicationMatch(SystemApplicationRecord item, string query, out string reason)
    {
        reason = "no-match";
        if (query.Length == 0) return 0;
        var terms = (item.aliases ?? BuildApplicationAliases(item.name, item.path))
            .Append(NormalizeApplicationSearchText(item.name))
            .Append(NormalizeApplicationSearchText(item.path))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var best = 0;
        foreach (var term in terms)
        {
            var score = 0;
            if (term.Equals(query, StringComparison.OrdinalIgnoreCase)) score = 1000;
            else if (term.StartsWith(query, StringComparison.OrdinalIgnoreCase)) score = 900;
            else if (term.Contains(query, StringComparison.OrdinalIgnoreCase)) score = 820;
            else if (tokens.All(token => term.Split(' ').Any(part => part.StartsWith(token, StringComparison.OrdinalIgnoreCase)))) score = 720;
            else
            {
                var hits = tokens.Count(token => term.Contains(token, StringComparison.OrdinalIgnoreCase));
                if (hits > 0) score = 500 + (int)(300.0 * hits / tokens.Length);
                else
                {
                    if (tokens.Length <= 3 && tokens.All(token => token.Length >= 4))
                    {
                        var termTokens = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var fuzzyHits = tokens.Count(token => termTokens.Any(part => part.Length >= 4 && part[0] == token[0]
                            && Math.Abs(part.Length - token.Length) <= (token.Length >= 8 ? 2 : 1)
                            && EditDistanceAtMost(token, part, token.Length >= 8 ? 2 : 1) <= (token.Length >= 8 ? 2 : 1)));
                        if (fuzzyHits == tokens.Length) score = 640;
                    }
                }
            }
            if (score <= best) continue;
            best = score;
            reason = score >= 900 ? "exact-or-prefix" : score >= 800 ? "contains" : score >= 700 ? "all-query-terms" : "partial-token-match";
        }
        return best;
    }

    private static List<SystemApplicationRecord> ReadSystemApplicationCatalog()
    {
        try
        {
            if (!File.Exists(SystemManagerCatalogPath) || IsReparsePoint(SystemManagerCatalogPath) || new FileInfo(SystemManagerCatalogPath).Length > 4 * 1024 * 1024) return new List<SystemApplicationRecord>();
            return JsonSerializer.Deserialize<List<SystemApplicationRecord>>(File.ReadAllText(SystemManagerCatalogPath, Encoding.UTF8))?
                .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.id) && !string.IsNullOrWhiteSpace(item.name)
                    && ((TryResolveApplicationExecutable(item.path ?? string.Empty, out _)) || IsValidAppUserModelId(item.appUserModelId)))
                .GroupBy(item => item.id, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).Take(1500).ToList() ?? new List<SystemApplicationRecord>();
        }
        catch { return new List<SystemApplicationRecord>(); }
    }

    private static void EnumeratePackagedApplications(Action<string, string> add)
    {
        List<(string name, string appUserModelId)> results = new();
        var worker = new System.Threading.Thread(() => results = EnumeratePackagedApplicationsOnSta())
        {
            IsBackground = true,
            Name = "RunMind packaged application discovery"
        };
        try
        {
            worker.SetApartmentState(System.Threading.ApartmentState.STA);
            worker.Start();
            if (!worker.Join(TimeSpan.FromSeconds(8))) return;
        }
        catch { return; }
        foreach (var item in results) add(item.name, item.appUserModelId);
    }

    private static List<(string name, string appUserModelId)> EnumeratePackagedApplicationsOnSta()
    {
        var results = new List<(string name, string appUserModelId)>();
        object? shell = null;
        object? folder = null;
        object? items = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: false);
            if (shellType is null) return results;
            shell = Activator.CreateInstance(shellType);
            if (shell is null) return results;
            folder = shellType.InvokeMember("NameSpace", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { "shell:AppsFolder" });
            if (folder is null) return results;
            items = folder.GetType().InvokeMember("Items", System.Reflection.BindingFlags.InvokeMethod, null, folder, null);
            if (items is null) return results;
            var countValue = items.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, items, null);
            var count = countValue is int number ? Math.Clamp(number, 0, 3000) : 0;
            for (var index = 0; index < count; index++)
            {
                object? item = null;
                try
                {
                    item = items.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, items, new object[] { index });
                    if (item is null) continue;
                    var itemType = item.GetType();
                    var name = itemType.InvokeMember("Name", System.Reflection.BindingFlags.GetProperty, null, item, null) as string ?? string.Empty;
                    var appId = itemType.InvokeMember("ExtendedProperty", System.Reflection.BindingFlags.InvokeMethod, null, item, new object[] { "System.AppUserModel.ID" }) as string;
                    if (!IsValidAppUserModelId(appId)) appId = itemType.InvokeMember("Path", System.Reflection.BindingFlags.GetProperty, null, item, null) as string;
                    if (name.Length > 0 && IsValidAppUserModelId(appId)) results.Add((name, appId!));
                }
                catch { }
                finally { if (item is not null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item); }
            }
        }
        catch { }
        finally
        {
            if (items is not null && Marshal.IsComObject(items)) Marshal.FinalReleaseComObject(items);
            if (folder is not null && Marshal.IsComObject(folder)) Marshal.FinalReleaseComObject(folder);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
        return results;
    }

    private static object SystemManagerListStartupApplications(string query)
    {
        RequireSystemCapability("applications", "discoverApplications");
        var results = new List<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, string source, string command, string? shortcutPath = null, string? entryPath = null, string state = "enabled", string? recoveryPath = null)
        {
            if (results.Count >= 250 || string.IsNullOrWhiteSpace(name)) return;
            if (!string.IsNullOrWhiteSpace(query) && !name.Contains(query, StringComparison.OrdinalIgnoreCase)) return;
            var executable = NormalizeExecutableCandidate(command);
            if (string.IsNullOrWhiteSpace(executable) && !string.IsNullOrWhiteSpace(shortcutPath))
            {
                try { executable = ResolveShortcutTarget(shortcutPath); } catch { }
            }
            var key = $"{source}|{name}|{entryPath}|{state}|{executable}";
            if (!seen.Add(key)) return;
            results.Add(new { name = name[..Math.Min(name.Length, 180)], source, executablePath = executable, shortcutPath, entryPath, state, recoveryPath });
        }

        var runLocations = new[]
        {
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run"),
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU RunOnce"),
            (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM Run"),
            (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM RunOnce")
        };
        foreach (var (hive, subKey, source) in runLocations)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(subKey, writable: false);
                    if (key is null) continue;
                    foreach (var valueName in key.GetValueNames().Take(100))
                    {
                        try
                        {
                            var command = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                            if (!string.IsNullOrWhiteSpace(command)) Add(valueName, $"{source} ({view})", command);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        foreach (var (folder, source) in new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.Startup), "User Startup Folder"),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Common Startup Folder")
        })
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) || IsReparsePoint(folder)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder).Take(100))
                {
                    try
                    {
                        if (IsReparsePoint(file)) continue;
                        var extension = Path.GetExtension(file);
                        if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        var target = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? ResolveShortcutTarget(file) : file;
                        Add(Path.GetFileNameWithoutExtension(file), source, target, extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? file : null, file);
                    }
                    catch { }
                }
            }
            catch { }
        }
        var recoveryRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RunMind", "DisabledStartupEntries");
        if (Directory.Exists(recoveryRoot) && !IsReparsePoint(recoveryRoot))
        {
            try
            {
                foreach (var directory in Directory.EnumerateDirectories(recoveryRoot).Take(100))
                {
                    try
                    {
                        if (IsReparsePoint(directory)) continue;
                        var metadataPath = Path.Combine(directory, "metadata.json");
                        if (!File.Exists(metadataPath) || IsReparsePoint(metadataPath) || new FileInfo(metadataPath).Length > 16 * 1024) continue;
                        using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                        var root = metadata.RootElement;
                        var itemName = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var itemSource = root.TryGetProperty("source", out var so) ? so.GetString() ?? "" : "";
                        var originalPath = root.TryGetProperty("entryPath", out var ep) ? ep.GetString() : null;
                        var target = root.TryGetProperty("target", out var t) ? t.GetString() ?? "" : "";
                        var backupItem = Directory.EnumerateFiles(directory).FirstOrDefault(file => !string.Equals(Path.GetFileName(file), "metadata.json", StringComparison.OrdinalIgnoreCase));
                        if (string.IsNullOrWhiteSpace(backupItem) || IsReparsePoint(backupItem)) continue;
                        Add(itemName, itemSource, target, Path.GetExtension(backupItem).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? backupItem : null, originalPath, "disabled", backupItem);
                    }
                    catch { }
                }
            }
            catch { }
        }
        try
        {
            using var recoveryKey = Registry.CurrentUser.OpenSubKey(@"Software\RunMind\DisabledStartupEntries", writable: false);
            if (recoveryKey is not null)
            {
                foreach (var metadataName in recoveryKey.GetValueNames().Where(value => value.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).Take(100))
                {
                    try
                    {
                        var metadataText = recoveryKey.GetValue(metadataName) as string;
                        if (string.IsNullOrWhiteSpace(metadataText) || metadataText.Length > 16 * 1024) continue;
                        using var metadata = JsonDocument.Parse(metadataText);
                        var root = metadata.RootElement;
                        var itemName = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var itemSource = root.TryGetProperty("source", out var so) ? so.GetString() ?? "" : "";
                        var originalPath = root.TryGetProperty("entryPath", out var ep) ? ep.GetString() : null;
                        var backupName = metadataName[..^5];
                        var command = recoveryKey.GetValue(backupName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
                        if (command.Length > 0) Add(itemName, itemSource, command, null, originalPath, "disabled", backupName);
                    }
                    catch { }
                }
            }
        }
        catch { }
        return new { success = true, action = "list-startup-applications", completed = true, verified = true, startupApplications = results, count = results.Count, truncated = results.Count >= 250, message = $"Retrieved {results.Count} startup entries, including RunMind-disabled items available for restoration." };
    }

    private static byte[] HashStartupItem(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidOperationException("Startup item exceeds the 16 MB recovery limit.");
        return SHA256.HashData(stream);
    }

    private static object SystemManagerSetStartupApplication(JsonElement args)
    {
        RequireSystemCapability("applications", "launchApplications");
        var action = ReadString(args, "action", 16).ToLowerInvariant();
        var name = ReadString(args, "name", 180);
        var source = ReadString(args, "source", 80);
        var entryPath = ReadString(args, "entryPath", 1024);
        if (action is not ("enable" or "disable")) throw new InvalidDataException("Startup action must be enable or disable.");
        if (name.Length == 0 || name.Any(char.IsControl)) throw new InvalidDataException("A valid exact startup entry name is required.");
        var backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RunMind", "DisabledStartupEntries");
        var backupName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source + "\n" + name + "\n" + entryPath)));

        if (source is "User Startup Folder" or "Common Startup Folder")
        {
            var folder = source == "User Startup Folder"
                ? Environment.GetFolderPath(Environment.SpecialFolder.Startup)
                : Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) || IsReparsePoint(folder)) throw new InvalidOperationException("The selected Startup folder is unavailable or is a reparse point.");
            if (string.IsNullOrWhiteSpace(entryPath) || Path.IsPathRooted(entryPath) is false) throw new InvalidDataException("The exact absolute entryPath from the current startup inventory is required.");
            var exactPath = Path.GetFullPath(entryPath);
            var folderRoot = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!exactPath.StartsWith(folderRoot, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetFileNameWithoutExtension(exactPath), name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The startup entry path must be the exact inventoried item inside the selected Startup folder.");
            if (!File.Exists(exactPath) || IsReparsePoint(exactPath)) throw new InvalidOperationException("The selected startup item no longer exists or is a reparse point. Refresh the inventory.");
            var extension = Path.GetExtension(exactPath);
            if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Only inventoried .lnk and .exe Startup-folder entries can be changed.");
            var backupFolder = Path.Combine(backupRoot, backupName);
            var backupPath = Path.Combine(backupFolder, Path.GetFileName(exactPath));
            if (action == "disable")
            {
                if (!ConfirmSystemManagerAction("launchApplications", "RunMind · Disable Startup-folder item", $"Entry: {name}\nSource: {source}\nPath: {exactPath}\n\nRunMind will copy the exact item to its recovery folder, verify its SHA-256, then remove the original. This does not uninstall the application.", alwaysAsk: true))
                    return new { success = false, accepted = false, completed = false, verified = false, cancelled = true, action = "disable-startup", target = name, requestedState = "disabled", actualState = "enabled", message = "The user declined the Startup-folder change." };
                EnsureNoReparseAncestors(backupRoot);
                Directory.CreateDirectory(backupFolder);
                if (IsReparsePoint(backupFolder)) throw new InvalidOperationException("The RunMind recovery folder cannot be a reparse point.");
                var metadataPath = Path.Combine(backupFolder, "metadata.json");
                if (Directory.EnumerateFileSystemEntries(backupFolder).Any() || File.Exists(backupPath) || File.Exists(metadataPath)) throw new InvalidOperationException("A recovery item already exists. Restore or inspect it before retrying.");
                var originalHash = HashStartupItem(exactPath);
                byte[] copiedHash;
                try
                {
                    File.Copy(exactPath, backupPath, overwrite: false);
                    copiedHash = HashStartupItem(backupPath);
                }
                catch { try { if (File.Exists(backupPath)) File.Delete(backupPath); } catch { } throw; }
                if (!CryptographicOperations.FixedTimeEquals(originalHash, copiedHash)) { File.Delete(backupPath); throw new IOException("The recovery copy did not match the original; the startup item was not changed."); }
                var target = exactPath;
                if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) { try { target = ResolveShortcutTarget(exactPath); } catch { target = string.Empty; } }
                try { File.WriteAllText(metadataPath, JsonSerializer.Serialize(new { name, source, entryPath = exactPath, target = NormalizeExecutableCandidate(target) })); }
                catch { try { File.Delete(backupPath); } catch { } throw; }
                try { File.Delete(exactPath); }
                catch { try { File.Delete(backupPath); File.Delete(metadataPath); } catch { } throw; }
                var verified = !File.Exists(exactPath) && File.Exists(backupPath) && CryptographicOperations.FixedTimeEquals(originalHash, HashStartupItem(backupPath));
                return new { success = verified, accepted = true, completed = true, verified, action = "disable-startup", target = name, requestedState = "disabled", actualState = !File.Exists(exactPath) ? "disabled" : "enabled", source, recoveryPath = backupPath, message = verified ? "Startup-folder item disabled and its original file preserved for re-enabling." : "The change was attempted but could not be fully verified; inspect the recovery path." };
            }
            else
            {
                if (!File.Exists(backupPath) || IsReparsePoint(backupPath)) throw new InvalidOperationException("No safe RunMind recovery copy exists for this Startup-folder item.");
                if (File.Exists(exactPath)) throw new InvalidOperationException("A Startup-folder item already exists at the original path. RunMind will not overwrite it.");
                if (!ConfirmSystemManagerAction("launchApplications", "RunMind · Restore Startup-folder item", $"Entry: {name}\nSource: {source}\nRestore path: {exactPath}\nRecovery copy: {backupPath}", alwaysAsk: true))
                    return new { success = false, accepted = false, completed = false, verified = false, cancelled = true, action = "enable-startup", target = name, requestedState = "enabled", actualState = "disabled", message = "The user declined the Startup-folder restore." };
                Directory.CreateDirectory(folder);
                var savedHash = HashStartupItem(backupPath);
                File.Copy(backupPath, exactPath, overwrite: false);
                var restoredHash = HashStartupItem(exactPath);
                if (!CryptographicOperations.FixedTimeEquals(savedHash, restoredHash)) { File.Delete(exactPath); throw new IOException("The restored Startup item failed SHA-256 verification; the recovery copy was retained."); }
                File.Delete(backupPath);
                var metadataPath = Path.Combine(backupFolder, "metadata.json");
                if (File.Exists(metadataPath) && !IsReparsePoint(metadataPath)) File.Delete(metadataPath);
                try { Directory.Delete(backupFolder, recursive: false); } catch { }
                var verified = File.Exists(exactPath) && !File.Exists(backupPath) && CryptographicOperations.FixedTimeEquals(savedHash, HashStartupItem(exactPath));
                return new { success = verified, accepted = true, completed = true, verified, action = "enable-startup", target = name, requestedState = "enabled", actualState = verified ? "enabled" : "disabled", source, message = verified ? "Startup-folder item restored and verified." : "The restore was attempted but could not be fully verified." };
            }
        }

        var location = source switch
        {
            "HKCU Run (Registry64)" => (RegistryHive.CurrentUser, RegistryView.Registry64, false),
            "HKCU Run (Registry32)" => (RegistryHive.CurrentUser, RegistryView.Registry32, false),
            "HKLM Run (Registry64)" => (RegistryHive.LocalMachine, RegistryView.Registry64, false),
            "HKLM Run (Registry32)" => (RegistryHive.LocalMachine, RegistryView.Registry32, false),
            "HKCU RunOnce (Registry64)" => (RegistryHive.CurrentUser, RegistryView.Registry64, true),
            "HKCU RunOnce (Registry32)" => (RegistryHive.CurrentUser, RegistryView.Registry32, true),
            "HKLM RunOnce (Registry64)" => (RegistryHive.LocalMachine, RegistryView.Registry64, true),
            "HKLM RunOnce (Registry32)" => (RegistryHive.LocalMachine, RegistryView.Registry32, true),
            _ => throw new InvalidDataException("Select an exact Run/RunOnce registry entry or a Startup-folder item from the current inventory.")
        };
        var keyPath = location.Item3 ? @"Software\Microsoft\Windows\CurrentVersion\RunOnce" : @"Software\Microsoft\Windows\CurrentVersion\Run";
        using var baseKey = RegistryKey.OpenBaseKey(location.Item1, location.Item2);
        using var startupKey = baseKey.OpenSubKey(keyPath, writable: true) ?? throw new InvalidOperationException("The selected startup registry key does not exist or is not writable.");
        using var backupKey = Registry.CurrentUser.CreateSubKey(@"Software\RunMind\DisabledStartupEntries", writable: true) ?? throw new InvalidOperationException("RunMind could not create its startup recovery registry key.");
        var kindName = backupName + ".kind";
        var metadataName = backupName + ".meta";
        if (action == "disable")
        {
            var command = startupKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (string.IsNullOrWhiteSpace(command)) throw new InvalidOperationException("The startup entry no longer exists or is not a string value. Refresh the startup inventory.");
            if (backupKey.GetValue(backupName) is not null) throw new InvalidOperationException("A RunMind disabled-entry backup already exists. Enable the saved entry before retrying.");
            var valueKind = startupKey.GetValueKind(name);
            if (valueKind is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) throw new InvalidOperationException("Only REG_SZ and REG_EXPAND_SZ startup values can be changed safely.");
            if (!ConfirmSystemManagerAction("launchApplications", "RunMind · Disable startup registry entry", $"Entry: {name}\nSource: {source}\nCommand: {command}\n\nRunMind will remove this exact registry value and preserve its command and value type for recovery.", alwaysAsk: true))
                return new { success = false, accepted = false, completed = false, verified = false, cancelled = true, action = "disable-startup", target = name, requestedState = "disabled", actualState = "enabled", message = "The user declined the startup change." };
            try
            {
                backupKey.SetValue(backupName, command, RegistryValueKind.String);
                backupKey.SetValue(kindName, valueKind.ToString(), RegistryValueKind.String);
                backupKey.SetValue(metadataName, JsonSerializer.Serialize(new { name, source, entryPath = "" }), RegistryValueKind.String);
                startupKey.DeleteValue(name, throwOnMissingValue: false);
            }
            catch
            {
                backupKey.DeleteValue(backupName, throwOnMissingValue: false);
                backupKey.DeleteValue(kindName, throwOnMissingValue: false);
                backupKey.DeleteValue(metadataName, throwOnMissingValue: false);
                throw;
            }
            var verified = startupKey.GetValue(name) is null && string.Equals(backupKey.GetValue(backupName) as string, command, StringComparison.Ordinal);
            return new { success = verified, accepted = true, completed = true, verified, action = "disable-startup", target = name, requestedState = "disabled", actualState = startupKey.GetValue(name) is null ? "disabled" : "enabled", source, recoveryStored = backupKey.GetValue(backupName) is string, message = verified ? "Startup registry entry disabled; its original command and registry value type are preserved." : "The registry change was attempted but could not be fully verified." };
        }
        else
        {
            var command = backupKey.GetValue(backupName) as string;
            if (string.IsNullOrWhiteSpace(command)) throw new InvalidOperationException("No RunMind recovery entry exists for this startup item. Refresh the inventory and do not reconstruct its command manually.");
            if (startupKey.GetValue(name) is not null) throw new InvalidOperationException("A startup entry with this name already exists. RunMind will not overwrite it.");
            var hasSavedKind = Enum.TryParse<RegistryValueKind>(backupKey.GetValue(kindName) as string, out var parsedKind);
            var kind = hasSavedKind && (parsedKind == RegistryValueKind.String || parsedKind == RegistryValueKind.ExpandString) ? parsedKind : RegistryValueKind.String;
            if (!ConfirmSystemManagerAction("launchApplications", "RunMind · Enable startup registry entry", $"Entry: {name}\nSource: {source}\nCommand: {command}\nRegistry type: {kind}\n\nRunMind will restore the previously saved startup registry value.", alwaysAsk: true))
                return new { success = false, accepted = false, completed = false, verified = false, cancelled = true, action = "enable-startup", target = name, requestedState = "enabled", actualState = "disabled", message = "The user declined the startup change." };
            startupKey.SetValue(name, command, kind);
            var verified = string.Equals(startupKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string, command, StringComparison.Ordinal) && startupKey.GetValueKind(name) == kind;
            if (verified) { backupKey.DeleteValue(backupName, throwOnMissingValue: false); backupKey.DeleteValue(kindName, throwOnMissingValue: false); backupKey.DeleteValue(metadataName, throwOnMissingValue: false); }
            return new { success = verified, accepted = true, completed = true, verified, action = "enable-startup", target = name, requestedState = "enabled", actualState = verified ? "enabled" : "disabled", source, recoveryStored = !verified, message = verified ? "Startup registry entry restored and verified." : "The registry write was attempted but the restored value could not be verified; recovery data was retained." };
        }
    }

    private static (int ExitCode, string Output) RunFixedDiagnosticCommand(string executable, IEnumerable<string> arguments, int timeoutMilliseconds = 15000)
    {
        if (!File.Exists(executable)) return (-1, string.Empty);
        try
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start diagnostic utility '{Path.GetFileName(executable)}'.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMilliseconds)) { try { process.Kill(entireProcessTree: true); } catch { } return (-2, string.Empty); }
            var output = outputTask.GetAwaiter().GetResult();
            _ = errorTask.GetAwaiter().GetResult();
            return (process.ExitCode, output.Length > 1024 * 1024 ? output[..(1024 * 1024)] : output);
        }
        catch { return (-1, string.Empty); }
    }

    private static string[] ParseCsvDiagnosticRow(string line)
    {
        var values = new List<string>(); var current = new StringBuilder(); var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"') { if (quoted && index + 1 < line.Length && line[index + 1] == '"') { current.Append('"'); index++; } else quoted = !quoted; }
            else if (character == ',' && !quoted) { values.Add(current.ToString()); current.Clear(); }
            else current.Append(character);
        }
        values.Add(current.ToString());
        return values.ToArray();
    }

    private static object SystemManagerListApplications(string query, bool forceRefresh = false)
    {
        RequireSystemCapability("applications", "discoverApplications");
        Directory.CreateDirectory(SystemManagerRoot);
        EnsureNoReparseAncestors(SystemManagerRoot);
        var normalizedQuery = NormalizeApplicationQuery(query);
        var cached = forceRefresh ? new List<SystemApplicationRecord>() : ReadSystemApplicationCatalog();
        var cacheFresh = !forceRefresh && File.Exists(SystemManagerCatalogPath) && !IsReparsePoint(SystemManagerCatalogPath)
            && DateTime.UtcNow - File.GetLastWriteTimeUtc(SystemManagerCatalogPath) < TimeSpan.FromMinutes(5);
        var shouldDiscover = forceRefresh || cached.Count == 0 || !cacheFresh;
        var rows = shouldDiscover
            ? new Dictionary<string, SystemApplicationRecord>(StringComparer.OrdinalIgnoreCase)
            : cached.ToDictionary(item => item.id, StringComparer.OrdinalIgnoreCase);
        var discoveredSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void MergeRecord(SystemApplicationRecord record)
        {
            if (!rows.TryGetValue(record.id, out var existing))
            {
                if (rows.Count < 1500) rows[record.id] = record;
                return;
            }
            var aliases = (existing.aliases ?? Array.Empty<string>()).Concat(record.aliases ?? Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToArray();
            var sources = existing.source.Split("; ", StringSplitOptions.RemoveEmptyEntries)
                .Concat(record.source.Split("; ", StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.OrdinalIgnoreCase);
            rows[record.id] = existing with
            {
                name = record.name.Length > existing.name.Length ? record.name : existing.name,
                source = string.Join("; ", sources),
                version = string.IsNullOrWhiteSpace(existing.version) ? record.version : existing.version,
                aliases = aliases
            };
        }
        void Add(string name, string path, string source, string version = "", IEnumerable<string>? aliases = null)
        {
            if (rows.Count >= 1500 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) return;
            if (!TryResolveApplicationExecutable(path, out var normalized)) return;
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToUpperInvariant())))[..16].ToLowerInvariant();
            MergeRecord(new SystemApplicationRecord(id, name.Trim(), normalized, source, version, BuildApplicationAliases(name, normalized, aliases)));
            discoveredSources.Add(source);
        }
        void AddPackaged(string name, string appUserModelId)
        {
            if (rows.Count >= 1500 || !IsValidAppUserModelId(appUserModelId)) return;
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("AUMID:" + appUserModelId.ToUpperInvariant())))[..16].ToLowerInvariant();
            MergeRecord(new SystemApplicationRecord(id, name.Trim(), string.Empty, "Windows Apps folder", string.Empty, BuildApplicationAliases(name, string.Empty), appUserModelId));
            discoveredSources.Add("Windows Apps folder");
        }

        // Keep a valid fresh index for fast lookup. A miss or stale index triggers a bounded live rebuild.
        if (shouldDiscover)
        {
            EnumeratePackagedApplications(AddPackaged);
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
                        try { using var key = apps.OpenSubKey(appName); var path = key?.GetValue(null) as string; if (!string.IsNullOrWhiteSpace(path)) Add(Path.GetFileNameWithoutExtension(appName), path, "App Paths"); } catch { }
                    }
                }
                catch { }
            }

            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null) continue;
                    foreach (var entryName in uninstall.GetSubKeyNames().Take(1800))
                    {
                        try
                        {
                            using var entry = uninstall.OpenSubKey(entryName);
                            var displayName = entry?.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName)) continue;
                            var displayVersion = entry?.GetValue("DisplayVersion") as string ?? string.Empty;
                            var displayIcon = entry?.GetValue("DisplayIcon") as string;
                            if (!string.IsNullOrWhiteSpace(displayIcon) && TryResolveApplicationExecutable(displayIcon, out _)) Add(displayName, displayIcon, "Installed application registry", displayVersion);
                            var installLocation = Environment.ExpandEnvironmentVariables((entry?.GetValue("InstallLocation") as string ?? string.Empty).Trim().Trim('"'));
                            if (string.IsNullOrWhiteSpace(displayIcon) || !TryResolveApplicationExecutable(displayIcon, out _))
                            {
                                if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation) && !IsReparsePoint(installLocation))
                                {
                                    try
                                    {
                                        foreach (var executable in Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly).Take(12))
                                            if (!IsReparsePoint(executable)) Add(displayName, executable, "Installed application registry", displayVersion, new[] { displayName });
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }
                        if (rows.Count >= 1500) break;
                    }
                }
                catch { }
                if (rows.Count >= 1500) break;
            }

            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var pending = new Stack<(string path, int depth)>(); pending.Push((root, 0)); var seen = 0;
                while (pending.Count > 0 && seen < 3000)
                {
                    var (folder, depth) = pending.Pop(); if (depth > 5) continue;
                    try
                    {
                        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
                        {
                            if (++seen > 3000) break;
                            if (IsReparsePoint(entry)) continue;
                            if (Directory.Exists(entry)) pending.Push((entry, depth + 1));
                            else if (entry.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) Add(Path.GetFileNameWithoutExtension(entry), entry, "Start Menu shortcut");
                        }
                    }
                    catch { }
                }
            }

            var installRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
            }.Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var root in installRoots)
            {
                try
                {
                    if (IsReparsePoint(root)) continue;
                    foreach (var executable in Directory.EnumerateFiles(root, "*.exe", SearchOption.TopDirectoryOnly).Take(25))
                        if (!IsReparsePoint(executable)) Add(Path.GetFileNameWithoutExtension(executable), executable, "Common install locations");
                    foreach (var folder in Directory.EnumerateDirectories(root).Take(100))
                    {
                        if (IsReparsePoint(folder)) continue;
                        try
                        {
                            foreach (var executable in Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly).Take(8))
                                if (!IsReparsePoint(executable)) Add(Path.GetFileNameWithoutExtension(executable), executable, "Common install locations");
                        }
                        catch { }
                        if (rows.Count >= 1500) break;
                    }
                }
                catch { }
                if (rows.Count >= 1500) break;
            }

            var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(100);
            foreach (var directory in pathDirectories)
            {
                try
                {
                    var normalizedDirectory = directory.Trim('"');
                    if (!Directory.Exists(normalizedDirectory) || IsReparsePoint(normalizedDirectory)) continue;
                    foreach (var executable in Directory.EnumerateFiles(normalizedDirectory, "*.exe", SearchOption.TopDirectoryOnly).Take(25))
                        if (!IsReparsePoint(executable)) Add(Path.GetFileNameWithoutExtension(executable), executable, "PATH");
                }
                catch { }
                if (rows.Count >= 1500) break;
            }

            // A running Standalone host is itself a discoverable RunMind application.
            try
            {
                var currentExecutable = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(currentExecutable)
                    && Path.GetFileName(currentExecutable).Equals("RunMind.exe", StringComparison.OrdinalIgnoreCase))
                    Add("RunMind", currentExecutable, "Current RunMind host");
            }
            catch { }

            // Preserve still-valid cached entries only as a fallback after fresh candidates have priority.
            foreach (var item in cached)
            {
                if (rows.Count >= 1500) break;
                MergeRecord(item);
            }
            try
            {
                if (File.Exists(SystemManagerCatalogPath) && IsReparsePoint(SystemManagerCatalogPath)) throw new InvalidDataException("The application catalog cannot be a symbolic link or reparse point.");
                var catalogTemp = SystemManagerCatalogPath + ".tmp";
                if (File.Exists(catalogTemp) && IsReparsePoint(catalogTemp)) throw new InvalidDataException("The application catalog temporary file cannot be a symbolic link or reparse point.");
                File.WriteAllText(catalogTemp, JsonSerializer.Serialize(rows.Values.Take(1500).ToList()), new UTF8Encoding(false));
                File.Move(catalogTemp, SystemManagerCatalogPath, true);
            }
            catch { }
        }

        var savedBehaviors = ReadSystemManagerSettings()["applicationBehaviors"] as JsonObject ?? new JsonObject();
        var matches = rows.Values.Select(item =>
        {
            var score = ScoreApplicationMatch(item, normalizedQuery, out var reason);
            return new { item, score, reason };
        })
            .Where(result => result.score > 0).OrderByDescending(result => result.score).ThenBy(result => result.item.name, StringComparer.OrdinalIgnoreCase)
            .Take(40).Select(result => new
            {
                applicationId = result.item.id, result.item.name, result.item.path, result.item.source, result.item.version,
                aliases = result.item.aliases ?? Array.Empty<string>(), appUserModelId = result.item.appUserModelId,
                kind = string.IsNullOrWhiteSpace(result.item.appUserModelId) ? "executable" : "packaged",
                defaultBehavior = (string?)savedBehaviors[result.item.id] ?? "reuse",
                matchScore = result.score, matchReason = result.reason
            }).ToList();

        if (matches.Count == 0 && !forceRefresh && !shouldDiscover && normalizedQuery.Length > 0)
            return SystemManagerListApplications(query, forceRefresh: true);

        return new
        {
            query = query.Trim(), normalizedQuery, applications = matches, count = matches.Count,
            catalog = "bounded local application index with live refresh on stale index or search miss",
            cache = new { hit = cached.Count > 0, fresh = cacheFresh, refreshed = shouldDiscover },
            sources = new[] { "App Paths", "Installed application registry", "Start Menu shortcuts", "Common install locations", "PATH", "Windows Apps folder" },
            discoveredSources = discoveredSources.ToArray(),
            note = matches.Count == 0
                ? "No matching application was found in the supported Windows discovery sources. The catalog was refreshed before returning this result."
                : "Candidates are ranked by exact name, prefix, alias, substring and token matches. If multiple candidates remain plausible, ask the user to choose; do not launch an unrelated candidate. Packaged applications use Windows Apps folder activation rather than an executable path."
        };
    }

    private sealed record SystemApplicationAdapter(string id, string displayName, string match, Dictionary<string, string[]> operations, bool builtIn);

    private static List<SystemApplicationAdapter> ReadSystemApplicationAdapters()
    {
        var rows = new List<SystemApplicationAdapter>
        {
            new("vscode", "Visual Studio Code", "code", new Dictionary<string,string[]> { ["open-file"] = new[] { "--goto", "{target}:{line}" }, ["open-folder"] = new[] { "--new-window", "{target}" } }, true),
            new("windows-terminal", "Windows Terminal", "wt", new Dictionary<string,string[]> { ["open-folder"] = new[] { "-d", "{target}" } }, true),
            new("firefox", "Firefox", "firefox", new Dictionary<string,string[]> { ["open-url"] = new[] { "-new-tab", "{target}" } }, true),
            new("notepad", "Windows Notepad", "notepad", new Dictionary<string,string[]> { ["open-file"] = new[] { "{target}" } }, true),
            new("file-explorer", "File Explorer", "explorer", new Dictionary<string,string[]> { ["open-folder"] = new[] { "{target}" }, ["select-file"] = new[] { "/select,{target}" } }, true),
            new("blender", "Blender", "blender", new Dictionary<string,string[]> { ["open-file"] = new[] { "{target}" } }, true),
            new("runmind", "RunMind", "RunMind", new Dictionary<string,string[]> { ["send-prompt"] = new[] { "--prompt", "{target}" }, ["open-session"] = new[] { "--session-file", "{target}" }, ["new-conversation"] = new[] { "--new-conversation" } }, true),
        };
        Directory.CreateDirectory(SystemManagerAdaptersRoot);
        EnsureNoReparseAncestors(SystemManagerAdaptersRoot);
        if (Directory.Exists(SystemManagerAdapterOverridesRoot) && !IsReparsePoint(SystemManagerAdapterOverridesRoot))
        {
            foreach (var file in Directory.EnumerateFiles(SystemManagerAdapterOverridesRoot, "*.json").Take(50))
            {
                try
                {
                    if (IsReparsePoint(file) || new FileInfo(file).Length > 32 * 1024) continue;
                    using var document = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
                    var root = document.RootElement;
                    var id = Path.GetFileNameWithoutExtension(file);
                    var index = rows.FindIndex(item => item.id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (index < 0 || !root.TryGetProperty("operations", out var ops) || ops.ValueKind != JsonValueKind.Object) continue;
                    var operations = new Dictionary<string,string[]>(StringComparer.Ordinal);
                    foreach (var op in ops.EnumerateObject().Take(8))
                    {
                        if (op.Name is not ("open-file" or "open-folder" or "open-url" or "select-file" or "send-prompt" or "open-session" or "new-conversation")) continue;
                        if (op.Value.ValueKind != JsonValueKind.Array || op.Value.GetArrayLength() is < 1 or > 20) continue;
                        var values = op.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString() ?? "").ToArray();
                        if (values.Any(v => v.Length > 1024 || System.Text.RegularExpressions.Regex.IsMatch(v, "\\{(?!target\\}|line\\})"))) continue;
                        operations[op.Name] = values;
                    }
                    if (operations.Count > 0) rows[index] = rows[index] with { operations = operations };
                }
                catch { }
            }
        }
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
        var builtIn = ReadSystemApplicationAdapters().FirstOrDefault(item => item.id.Equals(id, StringComparison.OrdinalIgnoreCase) && item.builtIn);
        if (!input.TryGetProperty("operations", out var operationsInput) || operationsInput.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Adapter operations must be an object.");
        var operations = new JsonObject();
        foreach (var operation in operationsInput.EnumerateObject().Take(8))
        {
            if (operation.Name is not ("open-file" or "open-folder" or "open-url") && !(builtIn is not null && operation.Name is "select-file" or "send-prompt" or "open-session" or "new-conversation")) throw new InvalidDataException($"Unsupported adapter operation '{operation.Name}'.");
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
        var isOverride = builtIn is not null;
        if (!isOverride && ReadSystemApplicationAdapters().Any(item => item.id.Equals(id, StringComparison.OrdinalIgnoreCase))) throw new IOException($"Adapter ID '{id}' is already registered.");
        if (!isOverride && Directory.EnumerateFiles(root, "*.json").Take(101).Count() >= 100) throw new InvalidOperationException("The custom application adapter limit (100) has been reached.");
        if (isOverride && (!name.Equals(builtIn!.displayName, StringComparison.Ordinal) || !match.Equals(builtIn.match, StringComparison.Ordinal))) throw new InvalidDataException("Built-in adapter identity and application matching are fixed; only supported operation templates can be edited.");
        var manifest = new JsonObject { ["id"] = id, ["displayName"] = name, ["match"] = match, ["operations"] = operations };
        var destinationRoot = isOverride ? SystemManagerAdapterOverridesRoot : root;
        Directory.CreateDirectory(destinationRoot); EnsureNoReparseAncestors(destinationRoot);
        var destination = Path.Combine(destinationRoot, id + ".json");
        if (!isOverride && (File.Exists(destination) || Directory.Exists(destination))) throw new IOException($"Adapter '{id}' already exists.");
        if (!ConfirmSystemManagerAction("launchApplications", isOverride ? "RunMind · Edit built-in adapter" : "RunMind · Install application adapter", $"Adapter: {name} ({id})\nApplication match: {match}\nOperations: {string.Join(", ", operations.Select(pair => pair.Key))}\n\nOnly argument templates are stored. No scripts or shell commands are supported.", alwaysAsk: true)) return new { installed = false, cancelled = true, id };
        var temp = Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            if (isOverride && File.Exists(destination)) File.Replace(temp, destination, null); else File.Move(temp, destination, false);
        }
        catch { try { if (File.Exists(temp)) File.Delete(temp); } catch { } throw; }
        return new { installed = true, overridden = isOverride, id, displayName = name, operations = operations.Select(pair => pair.Key).ToArray(), path = destination, note = "Declarative adapter installed. It contains argument templates only and cannot execute scripts or shell commands." };
    }

    private static object SystemManagerResetAdapter(JsonElement args)
    {
        RequireSystemCapability("applications", "launchApplications");
        var id = ReadString(args, "adapterId", 80);
        var builtIn = ReadSystemApplicationAdapters().FirstOrDefault(item => item.id.Equals(id, StringComparison.OrdinalIgnoreCase) && item.builtIn);
        if (builtIn is null) throw new InvalidDataException("Only a built-in adapter override can be reset.");
        var path = Path.Combine(SystemManagerAdapterOverridesRoot, id + ".json");
        if (!File.Exists(path)) return new { reset = false, reason = "no-override", adapterId = id };
        if (!ConfirmSystemManagerAction("launchApplications", "RunMind · Reset built-in adapter", $"Adapter: {builtIn.displayName}\nThis removes the user override and restores shipped defaults.", alwaysAsk: true)) return new { reset = false, cancelled = true, adapterId = id };
        if (IsReparsePoint(path)) throw new InvalidDataException("The adapter override file is unsafe.");
        File.Delete(path);
        return new { reset = true, adapterId = id };
    }

    private static object SystemManagerRemoveAdapter(JsonElement args)
    {
        RequireSystemCapability("applications", "launchApplications");
        var id = ReadString(args, "adapterId", 80);
        if (id.Length == 0) throw new InvalidDataException("A custom adapter ID is required.");
        var adapter = ReadSystemApplicationAdapters().FirstOrDefault(item => item.id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (adapter is null) return new { removed = false, reason = "not-found", adapterId = id };
        if (adapter.builtIn) throw new InvalidOperationException("Built-in adapters cannot be removed.");
        var path = Path.Combine(SystemManagerAdaptersRoot, id + ".json");
        if (!File.Exists(path) || IsReparsePoint(path)) throw new FileNotFoundException("The custom adapter manifest is missing or unsafe.", path);
        if (!ConfirmSystemManagerAction("launchApplications", "RunMind · Remove application adapter", $"Adapter: {adapter.displayName} ({adapter.id})\nThis removes only its declarative manifest. It does not uninstall the application.", alwaysAsk: true))
            return new { removed = false, cancelled = true, adapterId = id };
        File.Delete(path);
        return new { removed = true, adapterId = id, displayName = adapter.displayName };
    }

    private static object SystemManagerListAdapters()
    {
        RequireSystemCapability("applications", "launchApplications");
        var adapters = ReadSystemApplicationAdapters().Select(item => new
        {
            item.id, item.displayName, item.match, item.builtIn, hasOverride = item.builtIn && File.Exists(Path.Combine(SystemManagerAdapterOverridesRoot, item.id + ".json")),
            operations = item.operations.Keys.Select(operation => new
            {
                name = operation,
                required = operation == "new-conversation" ? Array.Empty<string>() : new[] { "target" },
                optional = operation == "open-file" ? new[] { "line" } : Array.Empty<string>(),
                access = operation == "open-url" ? "external-navigation" : "local-file-system",
                stateChanging = true,
                confirmation = "native System Manager permission policy",
                resultFields = new[] { "accepted", "launched", "verified", "outcome", "message" },
                templates = item.operations[operation]
            }).ToArray()
        }).ToArray();
        return new { adapters, count = adapters.Length, customAdapterDirectory = SystemManagerAdaptersRoot, format = "JSON manifests; argument arrays only; no scripts or shell commands", operationMetadata = "Required/optional parameters, access classification, confirmation policy, and result fields are descriptive; native validation and permissions remain authoritative." };
    }

    private static object SystemManagerLaunchAdapter(JsonElement args)
    {
        RequireSystemCapability("applications", "launchApplications");
        var id = ReadString(args, "adapterId", 80);
        var operation = ReadString(args, "operation", 40);
        var target = ReadString(args, "target", 12000);
        var line = args.TryGetProperty("line", out var lineValue) && lineValue.TryGetInt32(out var requestedLine) ? Math.Clamp(requestedLine, 1, 1000000) : 1;
        var adapter = ReadSystemApplicationAdapters().FirstOrDefault(item => item.id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The requested application adapter was not found.");
        if (!adapter.operations.TryGetValue(operation, out var templates)) throw new InvalidDataException($"Adapter '{id}' does not support operation '{operation}'.");
        if (operation == "open-url")
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("The browser adapter accepts HTTPS URLs only.");
        }
        else if (operation == "send-prompt")
        {
            if (target.Length is < 1 or > 12000 || target.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t'))) throw new InvalidDataException("A RunMind prompt must contain 1–12,000 printable characters.");
        }
        else if (operation == "open-session")
        {
            if (!Path.IsPathFullyQualified(target) || !File.Exists(target) || !Path.GetExtension(target).Equals(".runmind", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Open-session requires an existing fully-qualified .runmind file.");
            EnsureNoReparseAncestors(target);
            var info = new FileInfo(target);
            if (info.Length is < 2 or > 16 * 1024 * 1024) throw new InvalidDataException("The .runmind session file must be between 2 bytes and 16 MB.");
            using var session = JsonDocument.Parse(File.ReadAllText(target, Encoding.UTF8));
            if (session.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The .runmind session must contain a JSON object.");
        }
        else if (operation == "new-conversation")
        {
            if (target.Length != 0) throw new InvalidDataException("New-conversation does not accept a target.");
        }
        else
        {
            if (!Path.IsPathFullyQualified(target) || (!File.Exists(target) && !Directory.Exists(target))) throw new InvalidDataException("The adapter target must be an existing fully-qualified file or folder path.");
            EnsureNoReparseAncestors(target);
        }
        _ = SystemManagerListApplications(adapter.match);
        var catalog = ReadSystemApplicationCatalog();
        var normalizedAdapterMatch = NormalizeApplicationSearchText(adapter.match);
        var matches = catalog.Where(item => !string.IsNullOrWhiteSpace(item.path)
                && ((item.aliases ?? BuildApplicationAliases(item.name, item.path)).Any(alias => NormalizeApplicationSearchText(alias).Contains(normalizedAdapterMatch, StringComparison.OrdinalIgnoreCase))
                    || NormalizeApplicationSearchText(item.name).Contains(normalizedAdapterMatch, StringComparison.OrdinalIgnoreCase)
                    || NormalizeApplicationSearchText(item.path).Contains(normalizedAdapterMatch, StringComparison.OrdinalIgnoreCase)))
            .Where(item => item.path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(item.path)).Take(10).ToList();
        if (matches.Count == 0) return new { launched = false, reason = "application-not-found", adapter = id, matches = Array.Empty<object>() };
        if (matches.Count > 1) return new { launched = false, reason = "ambiguous-application", adapter = id, matches = matches.Select(item => new { item.name, item.path, item.source }).ToArray() };
        var launchArguments = templates.Select(value => value.Replace("{target}", target, StringComparison.Ordinal).Replace("{line}", line.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)).ToArray();
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { applicationId = matches[0].id, arguments = launchArguments }));
        return SystemManagerLaunchApplication(document.RootElement);
    }

    private async Task<object> SystemManagerContainerActionAsync(JsonElement args, CancellationToken cancellationToken)
    {
        RequireSystemCapability("applications", "launchApplications");
        var action = ReadString(args, "action", 20).ToLowerInvariant();
        var container = ReadString(args, "container", 128);
        var runtime = ReadString(args, "runtime", 16).ToLowerInvariant();
        if (action is not ("list" or "inspect" or "start" or "stop" or "restart" or "logs" or "stats" or "images" or "volumes" or "networks" or "health")) throw new InvalidDataException("Supported container actions are list, inspect, start, stop, restart, logs, stats, images, volumes, networks, and health.");
        if (runtime is not ("auto" or "docker" or "podman")) throw new InvalidDataException("Runtime must be auto, docker, or podman.");
        if (action is not ("list" or "stats" or "images" or "volumes" or "networks") && !System.Text.RegularExpressions.Regex.IsMatch(container, "^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}$")) throw new InvalidDataException("A container name or ID using letters, numbers, dot, underscore, or hyphen is required.");
        var engines = runtime == "auto" ? new[] { "podman", "docker" } : new[] { runtime };
        var executable = engines.Select(name => FindExecutable(name + ".exe") ?? FindExecutable(name)).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        if (executable is null) return new { available = false, success = false, accepted = false, completed = false, verified = false, runtime, action, message = "Neither Docker nor Podman was found on PATH. Install or expose a supported runtime, then retry." };
        var commandArguments = action switch
        {
            "list" => new[] { "ps", "-a", "--format", "{{.ID}}\t{{.Names}}\t{{.Status}}\t{{.Image}}" },
            "stats" => new[] { "stats", "--no-stream" },
            "images" => new[] { "images", "--format", "{{.ID}}\t{{.Repository}}:{{.Tag}}\t{{.Size}}" },
            "volumes" => new[] { "volume", "ls" },
            "networks" => new[] { "network", "ls" },
            "health" => new[] { "inspect", "--format", "{{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{else}}no-healthcheck{{end}}", container },
            "inspect" => new[] { "inspect", container },
            "start" => new[] { "start", container },
            "stop" => new[] { "stop", "--time", "10", container },
            "restart" => new[] { "restart", "--time", "10", container },
            _ => new[] { "logs", "--tail", "200", container }
        };
        var stateChanging = action is "start" or "stop" or "restart";
        if (stateChanging && !ConfirmSystemManagerAction("launchApplications", $"RunMind · {runtime} container {action}", $"Runtime: {Path.GetFileNameWithoutExtension(executable)}\nContainer: {container}\nAction: {action}", alwaysAsk: true)) return new { success = false, accepted = false, completed = false, verified = false, cancelled = true, runtime, action, container, message = "The user declined the container state change." };
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var value in commandArguments) start.ArgumentList.Add(value);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeoutSeconds = (int?)ReadSystemManagerSettings()["commandTimeoutSeconds"] ?? 0;
        if (timeoutSeconds > 0) timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var stdoutTask = ReadBoundedProcessOutputAsync(process.StandardOutput, timeout.Token);
        var stderrTask = ReadBoundedProcessOutputAsync(process.StandardError, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false); } catch { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException($"Container action '{action}' exceeded the configured {timeoutSeconds}-second timeout and was terminated.");
        }
        var output = await stdoutTask.ConfigureAwait(false);
        var error = await stderrTask.ConfigureAwait(false);
        var actualState = (string?)null;
        var verified = process.ExitCode == 0 && !stateChanging;
        if (process.ExitCode == 0 && stateChanging)
        {
            var verifyStart = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            verifyStart.ArgumentList.Add("inspect"); verifyStart.ArgumentList.Add("--format"); verifyStart.ArgumentList.Add("{{.State.Status}}"); verifyStart.ArgumentList.Add(container);
            using var verifyProcess = new Process { StartInfo = verifyStart };
            if (verifyProcess.Start())
            {
                using var verifyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (timeoutSeconds > 0) verifyTimeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                var verifyOutputTask = ReadBoundedProcessOutputAsync(verifyProcess.StandardOutput, verifyTimeout.Token);
                var verifyErrorTask = ReadBoundedProcessOutputAsync(verifyProcess.StandardError, verifyTimeout.Token);
                try { await verifyProcess.WaitForExitAsync(verifyTimeout.Token).ConfigureAwait(false); actualState = (await verifyOutputTask.ConfigureAwait(false)).text.Trim(); _ = await verifyErrorTask.ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    try { if (!verifyProcess.HasExited) verifyProcess.Kill(entireProcessTree: true); } catch { }
                    try { await verifyProcess.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    try { await Task.WhenAll(verifyOutputTask, verifyErrorTask).ConfigureAwait(false); } catch { }
                    if (cancellationToken.IsCancellationRequested) throw;
                }
                var desired = action == "stop" ? "exited" : "running";
                verified = verifyProcess.ExitCode == 0 && string.Equals(actualState, desired, StringComparison.OrdinalIgnoreCase);
            }
        }
        var success = process.ExitCode == 0 && (!stateChanging || verified);
        return new { available = true, success, accepted = process.ExitCode == 0, completed = process.ExitCode == 0, verified, runtime = Path.GetFileNameWithoutExtension(executable), action, container, requestedState = stateChanging ? (action == "stop" ? "exited" : "running") : null, actualState, exitCode = process.ExitCode, output = output.text, outputTruncated = output.truncated, error = error.text, errorTruncated = error.truncated, message = success ? (stateChanging ? $"Container state verified: {actualState}." : "Runtime query completed.") : stateChanging && process.ExitCode == 0 ? $"Runtime accepted the action, but the requested state could not be verified. Actual state: {actualState ?? "unknown"}." : "Container runtime returned a non-zero exit code." };
    }

    private async Task<(int exitCode, string output, string error)> RunServiceControlAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Could not start the Windows Service Control utility.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeoutSeconds = (int?)ReadSystemManagerSettings()["commandTimeoutSeconds"] ?? 0;
        if (timeoutSeconds > 0) timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var stdout = ReadBoundedProcessOutputAsync(process.StandardOutput, timeout.Token);
        var stderr = ReadBoundedProcessOutputAsync(process.StandardError, timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); } catch { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException(timeoutSeconds == 0 ? "The Windows service operation was cancelled." : $"The Windows service operation exceeded {timeoutSeconds} seconds.");
        }
        var output = await stdout.ConfigureAwait(false);
        var error = await stderr.ConfigureAwait(false);
        return (process.ExitCode, output.text, error.text);
    }

    private static string? ParseServiceState(string output)
    {
        var match = System.Text.RegularExpressions.Regex.Match(output, @"(?im)^\s*STATE\s*:\s*\d+\s+([A-Z_]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<object> SystemManagerServiceActionAsync(JsonElement args, CancellationToken cancellationToken)
    {
        RequireSystemCapability("diagnostics", "serviceInformation");
        var action = ReadString(args, "action", 20).ToLowerInvariant();
        var service = ReadString(args, "service", 128);
        if (action is not ("list" or "inspect" or "start" or "stop" or "restart" or "config" or "dependents"))
            throw new InvalidDataException("Supported service actions are list, inspect, config, dependents, start, stop, and restart.");
        if (action != "list" && !System.Text.RegularExpressions.Regex.IsMatch(service, "^[A-Za-z0-9_.-]{1,128}$"))
            throw new InvalidDataException("A valid Windows service name is required.");

        if (action == "list")
        {
            var result = await RunServiceControlAsync(new[] { "query", "state=", "all" }, cancellationToken).ConfigureAwait(false);
            return new { success = result.exitCode == 0, accepted = result.exitCode == 0, action, completed = true, verified = result.exitCode == 0, exitCode = result.exitCode, output = result.output, error = result.error, message = result.exitCode == 0 ? "Service enumeration completed." : "Service enumeration failed." };
        }
        if (action == "config" || action == "dependents")
        {
            var command = action == "config" ? new[] { "qc", service } : new[] { "enumdepend", service };
            var result = await RunServiceControlAsync(command, cancellationToken).ConfigureAwait(false);
            return new { success = result.exitCode == 0, accepted = result.exitCode == 0, action, target = service, completed = true, verified = result.exitCode == 0, exitCode = result.exitCode, output = result.output, error = result.error, message = result.exitCode == 0 ? (action == "config" ? "Service configuration retrieved." : "Dependent services retrieved.") : "The service query failed." };
        }
        if (action == "inspect")
        {
            var query = await RunServiceControlAsync(new[] { "queryex", service }, cancellationToken).ConfigureAwait(false);
            var config = await RunServiceControlAsync(new[] { "qc", service }, cancellationToken).ConfigureAwait(false);
            return new { success = query.exitCode == 0, accepted = query.exitCode == 0, action, target = service, completed = true, verified = query.exitCode == 0, actualState = ParseServiceState(query.output), queryExitCode = query.exitCode, query = query.output, configuration = config.output, error = string.Join("\n", new[] { query.error, config.error }.Where(value => !string.IsNullOrWhiteSpace(value))), message = query.exitCode == 0 ? "Service state and configuration retrieved." : "Service inspection failed." };
        }

        if (!ConfirmSystemManagerAction("launchApplications", $"RunMind · {action} Windows service", $"Service: {service}\nAction: {action}\nThis changes Windows service state and may affect applications or the operating system.", alwaysAsk: true))
            return new { success = false, accepted = false, completed = false, verified = false, cancelled = true, action, target = service, message = "The user declined the service operation." };

        var commandOutput = new List<string>();
        var commandErrors = new List<string>();
        var accepted = true;
        var restartCycleCompleted = action != "restart";
        if (action == "restart")
        {
            var before = await RunServiceControlAsync(new[] { "query", service }, cancellationToken).ConfigureAwait(false);
            var beforeState = ParseServiceState(before.output);
            if (beforeState == "RUNNING")
            {
                var stop = await RunServiceControlAsync(new[] { "stop", service }, cancellationToken).ConfigureAwait(false);
                commandOutput.Add(stop.output); commandErrors.Add(stop.error); accepted &= stop.exitCode == 0;
                if (stop.exitCode == 0)
                {
                    var stopped = false;
                    for (var attempt = 0; attempt < 20 && !stopped; attempt++)
                    {
                        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                        var state = await RunServiceControlAsync(new[] { "query", service }, cancellationToken).ConfigureAwait(false);
                        stopped = ParseServiceState(state.output) == "STOPPED";
                    }
                    if (!stopped) accepted = false;
                }
            }
            if (accepted)
            {
                var start = await RunServiceControlAsync(new[] { "start", service }, cancellationToken).ConfigureAwait(false);
                commandOutput.Add(start.output); commandErrors.Add(start.error); accepted &= start.exitCode == 0;
                restartCycleCompleted = start.exitCode == 0;
            }
        }
        else
        {
            var result = await RunServiceControlAsync(new[] { action, service }, cancellationToken).ConfigureAwait(false);
            commandOutput.Add(result.output); commandErrors.Add(result.error); accepted = result.exitCode == 0;
        }

        string? actualState = null;
        var desiredState = action == "stop" ? "STOPPED" : "RUNNING";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await RunServiceControlAsync(new[] { "query", service }, cancellationToken).ConfigureAwait(false);
            actualState = ParseServiceState(state.output);
            if (actualState == desiredState) break;
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }
        var verified = actualState == desiredState && restartCycleCompleted;
        return new { success = verified, accepted, completed = true, verified, action, target = service, requestedState = desiredState, actualState, output = string.Join("\n", commandOutput), error = string.Join("\n", commandErrors.Where(value => !string.IsNullOrWhiteSpace(value))), message = verified ? $"Service {service} is verified {desiredState.ToLowerInvariant()}." : $"Service command completed, but the requested state could not be verified. Actual state: {actualState ?? "unknown"}." };
    }

    private static bool SupportsNewWindowBehavior(SystemApplicationRecord application)
    {
        if (!string.IsNullOrWhiteSpace(application.appUserModelId)) return false;
        if (!TryResolveApplicationExecutable(application.path, out var executable)) return false;

        // Keep this allowlist deliberately narrow: these applications have a known
        // --new-window command-line switch. Unknown applications must use reuse/new-process.
        return Path.GetFileNameWithoutExtension(executable).ToLowerInvariant() is
            "chrome" or "msedge" or "firefox" or "brave" or "vivaldi" or "opera" or "code";
    }

    private static object SystemManagerSetApplicationBehavior(JsonElement args)
    {
        RequireSystemCapability("applications", "launchApplications");
        var applicationId = ReadString(args, "applicationId", 80);
        var behavior = ReadString(args, "behavior", 40);
        if (applicationId.Length == 0 || behavior is not ("ask" or "reuse" or "new-window" or "new-process")) throw new InvalidDataException("A discovered application ID and a supported behavior are required.");
        _ = SystemManagerListApplications("", forceRefresh: false);
        var application = ReadSystemApplicationCatalog().FirstOrDefault(item => item.id.Equals(applicationId, StringComparison.OrdinalIgnoreCase));
        if (application is null) throw new InvalidDataException("The application is no longer present in the discovered catalog. Search again before changing its behavior.");
        if (behavior == "new-window" && !SupportsNewWindowBehavior(application)) throw new InvalidDataException("This application does not have a known, supported new-window command. Keep the reuse behavior or use new-process where supported.");
        if (!string.IsNullOrWhiteSpace(application.appUserModelId) && behavior != "reuse") throw new InvalidDataException("Packaged Windows applications currently support reuse/activation only; an ask-each-time preference is not useful for this app.");
        if (!ConfirmSystemManagerAction("launchApplications", "RunMind · Change application behavior", $"Application: {application.name}\nBehavior: {behavior}\nThis preference applies to future launch requests for this discovered application.", alwaysAsk: true))
            return new { saved = false, cancelled = true, applicationId, behavior };
        var settings = ReadSystemManagerSettings();
        var behaviors = settings["applicationBehaviors"] as JsonObject ?? new JsonObject();
        if (behaviors.Count >= 100 && !behaviors.ContainsKey(applicationId)) throw new InvalidOperationException("The application behavior preference limit (100) has been reached.");
        behaviors[applicationId] = behavior;
        settings["applicationBehaviors"] = behaviors;
        Directory.CreateDirectory(SystemManagerRoot);
        EnsureNoReparseAncestors(SystemManagerRoot);
        var temporary = SystemManagerSettingsPath + ".tmp";
        if ((File.Exists(SystemManagerSettingsPath) && IsReparsePoint(SystemManagerSettingsPath)) || (File.Exists(temporary) && IsReparsePoint(temporary))) throw new InvalidDataException("System Manager settings cannot be stored through a symbolic link or reparse point.");
        File.WriteAllText(temporary, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temporary, SystemManagerSettingsPath, true);
        return new { saved = true, applicationId, application = application.name, behavior };
    }

    private static bool TryActivateApplicationWindow(string executablePath, out int processId, out bool foregroundConfirmed)
    {
        processId = 0;
        foregroundConfirmed = false;
        var activationRequested = false;
        foreach (var candidate in Process.GetProcesses())
        {
            using (candidate)
            {
                try
                {
                    var candidatePath = candidate.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(candidatePath) || !string.Equals(Path.GetFullPath(candidatePath), executablePath, StringComparison.OrdinalIgnoreCase)) continue;
                    var handle = candidate.MainWindowHandle;
                    if (handle == IntPtr.Zero) continue;
                    processId = candidate.Id;
                    if (IsIconic(handle)) ShowWindowAsync(handle, SwRestore);
                    activationRequested |= SetForegroundWindow(handle);
                    System.Threading.Thread.Sleep(120);
                    if (GetForegroundWindow() != handle) continue;
                    foregroundConfirmed = true;
                    return true;
                }
                catch { }
            }
        }
        return activationRequested;
    }

    private static object SystemManagerLaunchApplication(JsonElement args)
    {
        RequireSystemCapability("applications", "launchApplications");
        var applicationId = ReadString(args, "applicationId", 80);
        if (applicationId.Length == 0) throw new InvalidDataException("A discovered application ID is required. Search for the application before launching it.");
        var catalog = ReadSystemApplicationCatalog();
        var application = catalog.FirstOrDefault(item => item.id.Equals(applicationId, StringComparison.OrdinalIgnoreCase));
        if (application is null)
        {
            _ = SystemManagerListApplications("", forceRefresh: true);
            application = ReadSystemApplicationCatalog().FirstOrDefault(item => item.id.Equals(applicationId, StringComparison.OrdinalIgnoreCase));
        }
        if (application is null) throw new UnauthorizedAccessException("The selected application is not in the refreshed local catalog. Search again and select a current result.");
        var requestedBehavior = ReadString(args, "behavior", 40);
        if (requestedBehavior.Length > 0 && requestedBehavior is not ("reuse" or "new-window" or "new-process"))
            throw new InvalidDataException("Unsupported per-request application behavior.");
        var savedBehavior = (string?)ReadSystemManagerSettings()["applicationBehaviors"]?[applicationId];
        if (requestedBehavior.Length == 0 && savedBehavior == "ask")
        {
            var choices = SupportsNewWindowBehavior(application) ? new[] { "reuse", "new-window", "new-process" } : new[] { "reuse", "new-process" };
            return new { launched = false, verified = false, needsUserChoice = true, application = application.name, applicationId, choices, message = "This application's saved preference is Ask each time. Ask the user whether to reuse/activate, open a new window when supported, or request a new process. Do not launch until the user chooses." };
        }
        var behavior = requestedBehavior is "reuse" or "new-window" or "new-process" ? requestedBehavior : savedBehavior is "reuse" or "new-window" or "new-process" ? savedBehavior : "reuse";
        var arguments = ReadStringArray(args, "arguments", 40, 2048);
        var argumentPreview = string.Join(Environment.NewLine, arguments);

        if (!string.IsNullOrWhiteSpace(application.appUserModelId))
        {
            if (!IsValidAppUserModelId(application.appUserModelId)) throw new InvalidDataException("The packaged application activation identifier is invalid.");
            if (behavior != "reuse") return new { launched = false, verified = false, reason = "behavior-not-supported", application = application.name, behavior, supportedBehavior = "reuse" };
            if (arguments.Count > 0) return new { launched = false, verified = false, reason = "packaged-arguments-not-supported", application = application.name, applicationId, message = "The generic Windows Apps folder activation path cannot safely pass command-line arguments to this packaged application. The URL or requested document was not forwarded." };
            if (!ConfirmSystemManagerAction("launchApplications", "RunMind · Activate application", $"Application: {application.name}\nPackage activation ID: {application.appUserModelId}\nArguments: {argumentPreview}")) return new { launched = false, cancelled = true, reason = "user-declined" };
            using var activationProcess = Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, Arguments = "shell:AppsFolder\\" + application.appUserModelId });
            return new { outcome = "packaged-activation-requested", launched = false, verified = false, processStartedVerified = false, activationVerified = false, windowVerified = false, navigationVerified = false, activationRequested = true, requestAccepted = true, alreadyRunning = (bool?)null, application = application.name, applicationId, appUserModelId = application.appUserModelId, behavior, note = "Windows accepted the packaged-app activation request. RunMind cannot verify a visible window, focus, or completion of the application's action." };
        }

        if (!TryResolveApplicationExecutable(application.path ?? string.Empty, out var path))
            throw new FileNotFoundException("The selected application's executable no longer exists. Search again to refresh the catalog.", application.path);
        if (behavior == "new-window")
        {
            if (!SupportsNewWindowBehavior(application)) return new { launched = false, verified = false, reason = "new-window-not-supported", application = application.name, path, behavior };
            if (!arguments.Contains("--new-window", StringComparer.OrdinalIgnoreCase)) arguments.Insert(0, "--new-window");
        }

        var existingProcesses = FindRunningProcessesForExecutable(path).ToHashSet();
        if (behavior == "reuse" && existingProcesses.Count > 0 && arguments.Count == 0)
        {
            if (!ConfirmSystemManagerAction("launchApplications", "RunMind · Activate application", $"Application: {application.name}\nPath: {path}\nRunMind will try to restore and activate an existing window."))
                return new { launched = false, cancelled = true, reason = "user-declined", application = application.name, applicationId };
            var activationAttempted = TryActivateApplicationWindow(path, out var existingProcessId, out var foregroundConfirmed);
            if (activationAttempted && foregroundConfirmed)
                return new { outcome = "existing-window-activated", launched = false, verified = true, navigationVerified = false, activated = true, activationVerified = true, processStartedVerified = false, processRunning = true, alreadyRunning = true, processId = existingProcessId, application = application.name, applicationId, path, behavior, note = "An existing application window was restored/activated and Windows reported it as the foreground window." };
            return new { outcome = "already-running-not-activated", launched = false, verified = false, navigationVerified = false, activated = false, activationVerified = false, processStartedVerified = false, processRunning = true, alreadyRunning = true, processId = existingProcessId > 0 ? existingProcessId : existingProcesses.First(), application = application.name, applicationId, path, behavior, reason = "already-running-not-activated", note = "The application is already running, but RunMind could not verify that its window received foreground focus. No duplicate process was started. Ask the user whether to try a new process." };
        }

        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (!ConfirmSystemManagerAction("launchApplications", "RunMind · Launch application", $"Application: {application.name}\nPath: {path}\nBehavior: {behavior}\nArguments:\n{argumentPreview}")) return new { launched = false, cancelled = true, reason = "user-declined" };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows did not return a process for the application launch.");
        var processId = process.Id;
        try { process.WaitForExit(1800); } catch { }
        process.Refresh();
        if (!process.HasExited)
            return new { outcome = behavior == "new-window" ? "new-window-requested-process-started" : "new-process-started", launched = true, verified = true, processStartedVerified = true, windowVerified = false, activationVerified = false, navigationVerified = false, processRunning = true, handoffAccepted = false, processId, alreadyRunning = false, application = application.name, applicationId, path, behavior,
                note = "A newly started process for the selected executable is still running. This verifies process startup, not that a visible window received focus." };

        var exitCode = process.ExitCode;
        var matchingProcesses = FindRunningProcessesForExecutable(path);
        var survivingPriorInstance = matchingProcesses.FirstOrDefault(id => existingProcesses.Contains(id));
        if (survivingPriorInstance != 0 && exitCode == 0)
        {
            var activationAttempted = TryActivateApplicationWindow(path, out var activatedProcessId, out var foregroundConfirmed);
            return new { outcome = "action-handed-off-unverified", launched = false, verified = false, navigationVerified = false, processStartedVerified = false, activationVerified = activationAttempted && foregroundConfirmed, processRunning = true, handoffAccepted = true, activated = activationAttempted && foregroundConfirmed, processId = activatedProcessId > 0 ? activatedProcessId : survivingPriorInstance, alreadyRunning = true, application = application.name, applicationId, path, behavior,
                reason = "existing-instance-handoff-unverified", exitCode,
                message = "The launcher exited successfully and a pre-existing instance remains. RunMind attempted to activate its window, but cannot verify that the requested URL or action reached the existing application. The requested navigation/action remains unverified." };
        }

        return new { outcome = "process-exited", launched = false, verified = false, navigationVerified = false, processStartedVerified = false, activationVerified = false, processRunning = matchingProcesses.Count > 0, handoffAccepted = false, processId, application = application.name, applicationId, path, behavior,
            reason = "process-exited", exitCode,
            message = "The launch process exited before a new running process or a confirmed handoff could be verified." };
    }

    private static object SystemManagerCloseApplication(JsonElement args)
    {
        RequireSystemCapability("applications", "launchApplications");
        var applicationId = ReadString(args, "applicationId", 80);
        if (applicationId.Length == 0) throw new InvalidDataException("A discovered application ID is required.");
        var application = ReadSystemApplicationCatalog().FirstOrDefault(item => item.id.Equals(applicationId, StringComparison.OrdinalIgnoreCase));
        if (application is null) throw new InvalidDataException("The application is not in the current catalog. Search again before closing it.");
        if (!string.IsNullOrWhiteSpace(application.appUserModelId)) return new { closed = false, reason = "packaged-app-close-not-supported", application = application.name, message = "RunMind does not force-close packaged Windows applications. Close the app from its own window or Windows task manager." };
        if (!TryResolveApplicationExecutable(application.path ?? string.Empty, out var executablePath)) throw new FileNotFoundException("The application's executable is no longer available.", application.path);
        var expectedProcessName = Path.GetFileNameWithoutExtension(executablePath);
        var running = new List<(int processId, DateTime startedAtUtc)>();
        var targets = new List<(int processId, DateTime startedAtUtc)>();
        var unverified = new List<(int processId, DateTime startedAtUtc)>();
        foreach (var candidate in Process.GetProcesses())
        {
            using (candidate)
            {
                try
                {
                    var candidatePath = candidate.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(candidatePath))
                    {
                        if (candidate.ProcessName.Equals(expectedProcessName, StringComparison.OrdinalIgnoreCase))
                            unverified.Add((candidate.Id, candidate.StartTime.ToUniversalTime()));
                        continue;
                    }
                    if (!string.Equals(Path.GetFullPath(candidatePath), executablePath, StringComparison.OrdinalIgnoreCase)) continue;
                    var identity = (candidate.Id, candidate.StartTime.ToUniversalTime());
                    running.Add(identity);
                    if (candidate.MainWindowHandle != IntPtr.Zero) targets.Add(identity);
                }
                catch
                {
                    try
                    {
                        if (candidate.ProcessName.Equals(expectedProcessName, StringComparison.OrdinalIgnoreCase))
                            unverified.Add((candidate.Id, candidate.StartTime.ToUniversalTime()));
                    }
                    catch { }
                }
            }
        }
        if (running.Count == 0 && unverified.Count == 0) return new { closed = true, alreadyClosed = true, application = application.name, processRunning = false };
        if (targets.Count == 0) return new { closed = false, alreadyClosed = false, processRunning = running.Count > 0, application = application.name, processIds = running.Select(item => item.processId).ToArray(), unverifiedProcessIds = unverified.Select(item => item.processId).ToArray(), reason = running.Count == 0 ? "process-identity-unavailable" : "no-visible-main-window", note = "RunMind could not identify an accessible main window for every matching application process. It did not force-terminate any process." };
        if (!ConfirmSystemManagerAction("launchApplications", "RunMind · Close application", $"Application: {application.name}\nExecutable: {executablePath}\nWindows to close: {targets.Count}\nRunMind will send a normal window-close request. It will not force-terminate the process.", alwaysAsk: true))
            return new { closed = false, cancelled = true, application = application.name, processIds = targets.Select(item => item.processId).ToArray() };
        var requested = new List<int>();
        var failed = new List<int>();
        foreach (var target in targets)
        {
            try
            {
                using var process = Process.GetProcessById(target.processId);
                var currentPath = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(currentPath) || !string.Equals(Path.GetFullPath(currentPath), executablePath, StringComparison.OrdinalIgnoreCase)
                    || process.StartTime.ToUniversalTime() != target.startedAtUtc) { failed.Add(target.processId); continue; }
                if (process.CloseMainWindow()) requested.Add(target.processId); else failed.Add(target.processId);
            }
            catch { failed.Add(target.processId); }
        }
        foreach (var processId in requested)
        {
            try { using var process = Process.GetProcessById(processId); process.WaitForExit(2500); } catch { }
        }
        var remaining = new List<int>();
        foreach (var original in running)
        {
            try
            {
                using var process = Process.GetProcessById(original.processId);
                if (process.StartTime.ToUniversalTime() == original.startedAtUtc && !process.HasExited) remaining.Add(original.processId);
            }
            catch { }
        }
        var unverifiedRemaining = new List<int>();
        foreach (var original in unverified)
        {
            try
            {
                using var process = Process.GetProcessById(original.processId);
                if (process.StartTime.ToUniversalTime() == original.startedAtUtc && process.ProcessName.Equals(expectedProcessName, StringComparison.OrdinalIgnoreCase)) unverifiedRemaining.Add(original.processId);
            }
            catch { }
        }
        return new { closed = remaining.Count == 0 && unverifiedRemaining.Count == 0 && failed.Count == 0, closeRequested = requested, remaining, unverifiedRemaining, failed, application = application.name,
            note = remaining.Count == 0 && unverifiedRemaining.Count == 0 ? "All identified application processes have exited." : "Some processes did not close or could not be fully verified after the normal window-close request. RunMind did not force-terminate them." };
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
            return new { requestAccepted = true, opened = false, verified = false, type = "url", target = uri.AbsoluteUri,
                note = "Windows accepted the default-handler request. The target application, visible window, focus and navigation are not verified. Use the application-specific launch tool when the user named an application." };
        }
        if (!Path.IsPathFullyQualified(target)) throw new InvalidDataException("File and folder targets must be fully qualified paths.");
        EnsureNoReparseAncestors(target);
        if (!File.Exists(target) && !Directory.Exists(target)) throw new FileNotFoundException("The requested file or folder does not exist.");
        if (!ConfirmSystemManagerAction("openTargets", "RunMind · Open file or folder", target)) return new { opened = false, cancelled = true, reason = "user-declined" };
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        return new { requestAccepted = true, opened = false, verified = false, type = Directory.Exists(target) ? "folder" : "file", target,
            note = "Windows accepted the default-handler request. The target application's visible window and focus are not verified." };
    }

    private static object SystemManagerListProcesses(string query)
    {
        RequireSystemCapability("processes", "listProcesses");
        var rows = new List<object>();
        var parentIds = ReadProcessParentIds();
        var includeDetails = (bool?)ReadSystemManagerSettings()["capabilities"]?["inspectProcesses"] == true;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                if (!string.IsNullOrWhiteSpace(query) && !name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                string? path = null; DateTime? started = null; double? cpuSeconds = null; int? threadCount = null; int? handleCount = null; string? priority = null; bool? responding = null;
                if (includeDetails)
                {
                    try { path = process.MainModule?.FileName; } catch { }
                    try { cpuSeconds = Math.Round(process.TotalProcessorTime.TotalSeconds, 3); } catch { }
                    try { threadCount = process.Threads.Count; } catch { }
                    try { handleCount = process.HandleCount; } catch { }
                    try { priority = process.PriorityClass.ToString(); } catch { }
                    try { if (process.MainWindowHandle != IntPtr.Zero) responding = process.Responding; } catch { }
                }
                try { started = process.StartTime.ToUniversalTime(); } catch { }
                long memory = 0; try { memory = process.WorkingSet64; } catch { }
                long privateMemory = 0; try { privateMemory = process.PrivateMemorySize64; } catch { }
                rows.Add(new { name, processId = process.Id, parentProcessId = parentIds.TryGetValue(process.Id, out var parentId) ? parentId : (int?)null, memoryBytes = memory, privateMemoryBytes = privateMemory, cpuSeconds, threadCount, handleCount, priority, responding, executablePath = path, startedAtUtc = started });
            }
            catch { }
            finally { process.Dispose(); }
            if (rows.Count >= 500) break;
        }
        return new { success = true, accepted = true, action = "list-processes", completed = true, verified = true, processes = rows, count = rows.Count, truncated = rows.Count >= 500, detailFieldsEnabled = includeDetails, message = $"Retrieved {rows.Count} process records." };
    }

    private static object SystemManagerFindPortOwner(JsonElement args)
    {
        RequireSystemCapability("processes", "listProcesses");
        if (!args.TryGetProperty("port", out var portElement) || !portElement.TryGetInt32(out var port) || port is < 1 or > 65535)
            throw new InvalidDataException("Provide a TCP/UDP port between 1 and 65535.");
        var protocol = ReadString(args, "protocol", 8).Trim().ToLowerInvariant();
        if (protocol is not ("tcp" or "udp")) throw new InvalidDataException("Protocol must be tcp or udp.");
        var netstat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netstat.exe");
        var output = RunFixedDiagnosticCommand(netstat, new[] { "-ano", "-p", protocol }, 10000);
        if (output.ExitCode != 0)
            return new { success = false, accepted = true, completed = true, verified = false, protocol, port, matches = Array.Empty<object>(),
                message = output.ExitCode == -2 ? "The targeted listener query timed out." : "Windows netstat could not be executed; port ownership is unavailable." };

        var includeDetails = (bool?)ReadSystemManagerSettings()["capabilities"]?["inspectProcesses"] == true;
        var matches = new List<object>();
        foreach (var line in output.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < (protocol == "tcp" ? 5 : 4) || !columns[0].Equals(protocol, StringComparison.OrdinalIgnoreCase)) continue;
            var local = columns[1];
            var separator = local.LastIndexOf(':');
            if (separator < 0 || !int.TryParse(local[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var localPort) || localPort != port) continue;
            if (!int.TryParse(columns[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid < 0) continue;
            var state = protocol == "tcp" ? columns[^2] : "BOUND";
            var foreign = protocol == "tcp" && columns.Length >= 5 ? columns[2] : null;
            string? processName = null, executablePath = null;
            DateTime? startedAtUtc = null;
            try
            {
                using var owner = Process.GetProcessById(pid);
                processName = owner.ProcessName;
                try { startedAtUtc = owner.StartTime.ToUniversalTime(); } catch { }
                if (includeDetails) { try { executablePath = owner.MainModule?.FileName; } catch { } }
            }
            catch { }
            matches.Add(new { protocol, localEndpoint = local, foreignEndpoint = foreign, state, processId = pid,
                processName, executablePath, startedAtUtc });
            if (matches.Count >= 100) break;
        }
        return new { success = true, accepted = true, completed = true, verified = true, protocol, port, matches, count = matches.Count,
            detailFieldsEnabled = includeDetails, message = matches.Count == 0 ? $"No {protocol.ToUpperInvariant()} endpoint uses local port {port}." : $"Found {matches.Count} {protocol.ToUpperInvariant()} endpoint(s) using local port {port}." };
    }

    private static object SystemManagerProcessTree(JsonElement args)
    {
        RequireSystemCapability("processes", "listProcesses");
        if (!args.TryGetProperty("processId", out var pidElement) || !pidElement.TryGetInt32(out var rootPid) || rootPid <= 0)
            throw new InvalidDataException("A valid process ID is required.");
        var parentIds = ReadProcessParentIds();
        var includeDetails = (bool?)ReadSystemManagerSettings()["capabilities"]?["inspectProcesses"] == true;
        var processes = new Dictionary<int, (string Name, string? Path, DateTime? Started, long Memory, long PrivateMemory)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    string? path = null; DateTime? started = null; long memory = 0, privateMemory = 0;
                    try { started = process.StartTime.ToUniversalTime(); } catch { }
                    try { memory = process.WorkingSet64; } catch { }
                    try { privateMemory = process.PrivateMemorySize64; } catch { }
                    if (includeDetails) { try { path = process.MainModule?.FileName; } catch { } }
                    processes[process.Id] = (process.ProcessName, path, started, memory, privateMemory);
                }
                catch { }
            }
        }
        if (!processes.TryGetValue(rootPid, out _))
            return new { success = true, accepted = true, completed = true, verified = true, found = false, processId = rootPid, processes = Array.Empty<object>(), count = 0,
                message = "The requested process is no longer running or is not accessible." };

        var selected = new Dictionary<int, (string Relation, int Depth)> { [rootPid] = ("target", 0) };
        var cursor = rootPid;
        for (var depth = 1; depth <= 32 && parentIds.TryGetValue(cursor, out var parentId) && parentId > 0 && processes.ContainsKey(parentId) && !selected.ContainsKey(parentId); depth++)
        {
            selected[parentId] = ("ancestor", -depth);
            cursor = parentId;
        }
        var queue = new Queue<(int ProcessId, int Depth)>();
        queue.Enqueue((rootPid, 0));
        while (queue.Count > 0 && selected.Count < 200)
        {
            var current = queue.Dequeue();
            foreach (var child in parentIds.Where(pair => pair.Value == current.ProcessId).Select(pair => pair.Key).OrderBy(value => value))
            {
                if (!processes.TryGetValue(child, out _) || selected.ContainsKey(child)) continue;
                var depth = current.Depth + 1;
                selected[child] = ("descendant", depth);
                queue.Enqueue((child, depth));
                if (selected.Count >= 200) break;
            }
        }
        var rows = selected.OrderBy(pair => pair.Value.Depth).ThenBy(pair => pair.Key).Select(pair =>
        {
            var info = processes[pair.Key];
            return (object)new { processId = pair.Key, parentProcessId = parentIds.TryGetValue(pair.Key, out var parent) ? parent : (int?)null,
                name = info.Name, executablePath = info.Path, startedAtUtc = info.Started, memoryBytes = info.Memory,
                privateMemoryBytes = info.PrivateMemory, relationship = pair.Value.Relation, depth = pair.Value.Depth };
        }).ToArray();
        var ancestors = selected.Values.Count(value => value.Relation == "ancestor");
        return new { success = true, accepted = true, completed = true, verified = true, found = true, processId = rootPid,
            processes = rows, count = rows.Length, truncated = selected.Count >= 200, detailFieldsEnabled = includeDetails,
            message = $"Retrieved the target process, {ancestors} ancestor(s), and its bounded descendants." };
    }

    private static object SystemManagerCloseProcess(JsonElement args)
    {
        RequireSystemCapability("processes", "terminateProcesses");
        if (!args.TryGetProperty("processId", out var pidElement) || !pidElement.TryGetInt32(out var pid) || pid <= 0)
            throw new InvalidDataException("A valid process ID is required.");
        var expectedName = ReadString(args, "expectedName", 260);
        var expectedPath = ReadString(args, "expectedPath", 2048);
        var expectedStartedAt = ReadString(args, "expectedStartedAtUtc", 80);
        if (!DateTimeOffset.TryParse(expectedStartedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var expectedStart))
            throw new InvalidDataException("A process start timestamp from the process listing is required to prevent PID-reuse actions.");
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException) { return new { success = true, accepted = false, action = "close-process", target = pid.ToString(CultureInfo.InvariantCulture), completed = true, verified = true, requestedState = "closed", actualState = "exited", message = "The process has already exited." }; }
        using (process)
        {
            if (process.HasExited) return new { success = true, accepted = false, action = "close-process", target = pid.ToString(CultureInfo.InvariantCulture), completed = true, verified = true, requestedState = "closed", actualState = "exited", message = "The process has already exited." };
            if (string.IsNullOrWhiteSpace(expectedName) || !string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The process identity changed since discovery; close was cancelled.");
            DateTimeOffset actualStart;
            try { actualStart = new DateTimeOffset(process.StartTime.ToUniversalTime()); } catch { throw new UnauthorizedAccessException("RunMind cannot verify this process start time; close was blocked."); }
            if (Math.Abs((actualStart - expectedStart).TotalSeconds) > 1) throw new InvalidOperationException("The PID now belongs to a different process instance; close was cancelled.");
            string? actualPath = null; try { actualPath = process.MainModule?.FileName; } catch { }
            if (!string.IsNullOrWhiteSpace(expectedPath) && !string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The executable path changed since discovery; close was cancelled.");
            var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "System", "Idle", "Registry", "csrss", "wininit", "winlogon", "services", "lsass", "smss", "svchost", "dwm", "fontdrvhost" };
            if (protectedNames.Contains(process.ProcessName)) throw new UnauthorizedAccessException("RunMind will not send a close request to a protected Windows process.");
            if (process.MainWindowHandle == IntPtr.Zero) return new { success = false, accepted = false, action = "close-process", target = pid.ToString(CultureInfo.InvariantCulture), completed = false, verified = false, requestedState = "closed", actualState = "running-no-main-window", message = "This process has no accessible main window. RunMind did not terminate it." };
            if (!ConfirmSystemManagerAction("terminateProcesses", "RunMind · Close process gracefully", $"Process: {process.ProcessName}\nPID: {pid}\nExecutable: {actualPath ?? "Unavailable"}\n\nRunMind will send a normal window-close request. It will not force-terminate the process.", alwaysAsk: true))
                return new { success = false, accepted = false, cancelled = true, action = "close-process", target = pid.ToString(CultureInfo.InvariantCulture), completed = false, verified = false, requestedState = "closed", actualState = "running", message = "The user declined the graceful close request." };
            var requested = process.CloseMainWindow();
            var exited = requested && process.WaitForExit(5000);
            return new { success = exited, accepted = requested, action = "close-process", target = pid.ToString(CultureInfo.InvariantCulture), completed = requested, verified = exited, requestedState = "closed", actualState = exited ? "exited" : "running", message = exited ? "The process closed gracefully." : requested ? "A close request was sent, but the process is still running." : "Windows did not accept a main-window close request. The process was not force-terminated." };
        }
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
        catch (ArgumentException) { return new { success = true, accepted = false, completed = true, verified = true, action = "terminate-process", target = pid.ToString(CultureInfo.InvariantCulture), requestedState = "exited", actualState = "exited", message = "The process has already exited." }; }
        using (process)
        {
            if (process.HasExited) return new { success = true, accepted = false, completed = true, verified = true, action = "terminate-process", target = pid.ToString(CultureInfo.InvariantCulture), requestedState = "exited", actualState = "exited", message = "The process has already exited." };
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
                return new { success = false, accepted = false, completed = false, verified = false, cancelled = true, action = "terminate-process", target = pid.ToString(CultureInfo.InvariantCulture), requestedState = "exited", actualState = "running", message = "The user declined process termination." };
            process.Kill(entireProcessTree: false);
            var exited = process.WaitForExit(5000);
            return new { success = exited, accepted = true, completed = exited, verified = exited, action = "terminate-process", target = pid.ToString(CultureInfo.InvariantCulture), requestedState = "exited", actualState = exited ? "exited" : "running", processName = expectedName, executablePath = actualPath, message = exited ? "The process terminated and exit was verified." : "Termination was requested, but the process is still running." };
        }
    }

    private static object SystemManagerDiagnostics()
    {
        var settings = ReadSystemManagerSettings();
        if (!(bool?)settings["enabled"] ?? false) throw new UnauthorizedAccessException("Windows System Manager is disabled in the System Manager menu.");
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
            data["uptimeMilliseconds"] = Environment.TickCount64;
            data["drives"] = DriveInfo.GetDrives().Take(32).Select(drive =>
            {
                try
                {
                    if (!drive.IsReady) return null;
                    var total = drive.TotalSize; var available = drive.AvailableFreeSpace;
                    return new { name = drive.Name, format = drive.DriveFormat, type = drive.DriveType.ToString(), totalBytes = total, availableBytes = available, usedBytes = total - available };
                }
                catch { return null; }
            }).Where(drive => drive is not null).ToArray();
            var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var driverQuery = RunFixedDiagnosticCommand(Path.Combine(systemDirectory, "driverquery.exe"), new[] { "/fo", "csv", "/nh" });
            if (driverQuery.ExitCode == 0)
                data["drivers"] = driverQuery.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Take(100).Select(line => ParseCsvDiagnosticRow(line)).Where(row => row.Length >= 4).Select(row => new { module = row[0], displayName = row[1], driverType = row[2], linkDate = row[3], path = row.Length > 4 ? row[4] : string.Empty }).ToArray();
            else
            {
                data["drivers"] = Array.Empty<object>();
                data["driverDiagnosticWarning"] = "Driver inventory is unavailable or exceeded its diagnostic timeout.";
            }
            var powerShell = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            const string hardwareScript = "$ErrorActionPreference='Stop'; $classes='Win32_Processor','Win32_PhysicalMemory','Win32_VideoController','Win32_DiskDrive'; $items=@(); foreach($class in $classes){ Get-CimInstance -ClassName $class -ErrorAction SilentlyContinue | ForEach-Object { $items += ($_ | Select-Object @{n='class';e={$class}},Name,Manufacturer,Model,NumberOfCores,NumberOfLogicalProcessors,Capacity,AdapterRAM,DriverVersion,Size | ConvertTo-Json -Compress -Depth 2 | ConvertFrom-Json) } }; ConvertTo-Json -InputObject $items -Compress -Depth 3";
            var hardware = RunFixedDiagnosticCommand(powerShell, new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", hardwareScript }, 20000);
            if (hardware.ExitCode == 0 && hardware.Output.Length > 0)
            {
                try { using var hardwareDocument = JsonDocument.Parse(hardware.Output); data["hardware"] = hardwareDocument.RootElement.Clone(); }
                catch { data["hardwareDiagnosticWarning"] = "Hardware inventory returned data that could not be parsed as JSON."; }
            }
            else data["hardwareDiagnosticWarning"] = "Hardware inventory was unavailable; Windows PowerShell CIM support may be disabled.";
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
            var adapters = new List<object>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Take(50))
            {
                try
                {
                    var adapterProperties = adapter.GetIPProperties();
                    adapters.Add(new
                    {
                        name = adapter.Name, description = adapter.Description, type = adapter.NetworkInterfaceType.ToString(),
                        status = adapter.OperationalStatus.ToString(), speedBitsPerSecond = adapter.Speed,
                        addresses = adapterProperties.UnicastAddresses.Take(20).Select(item => item.Address.ToString()).ToArray(),
                        gateways = adapterProperties.GatewayAddresses.Take(10).Select(item => item.Address.ToString()).ToArray(),
                        dnsServers = adapterProperties.DnsAddresses.Take(10).Select(item => item.ToString()).ToArray()
                    });
                }
                catch { }
            }
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var netstatPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netstat.exe");
            var netstat = RunFixedDiagnosticCommand(netstatPath, new[] { "-ano", "-p", "tcp" });
            var listeners = new List<object>();
            if (netstat.ExitCode == 0)
            {
                foreach (var line in netstat.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5 || !parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase) || !parts[^2].Equals("LISTENING", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[^1], out var pid)) continue;
                    string? processName = null; try { using var owner = Process.GetProcessById(pid); processName = owner.ProcessName; } catch { }
                    listeners.Add(new { local = parts[1], processId = pid, processName });
                    if (listeners.Count >= 200) break;
                }
            }
            var udpNetstat = RunFixedDiagnosticCommand(netstatPath, new[] { "-ano", "-p", "udp" });
            var udpListeners = new List<object>();
            if (udpNetstat.ExitCode == 0)
            {
                foreach (var line in udpNetstat.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4 || !parts[0].Equals("UDP", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[^1], out var pid)) continue;
                    string? processName = null; try { using var owner = Process.GetProcessById(pid); processName = owner.ProcessName; } catch { }
                    udpListeners.Add(new { local = parts[1], processId = pid, processName });
                    if (udpListeners.Count >= 200) break;
                }
            }
            data["network"] = new { hostName = properties.HostName, domainName = properties.DomainName, adapters, activeTcpConnections = properties.GetActiveTcpConnections().Take(200).Select(item => new { local = item.LocalEndPoint.ToString(), remote = item.RemoteEndPoint.ToString(), state = item.State.ToString() }).ToArray(), tcpListeners = listeners, tcpListenerCount = listeners.Count, tcpListenerSource = netstat.ExitCode == 0 ? "netstat -ano" : "unavailable", udpListeners, udpListenerCount = udpListeners.Count, udpListenerSource = udpNetstat.ExitCode == 0 ? "netstat -ano" : "unavailable" };
        }
        return new { diagnostics = data, note = "Read-only Windows and process diagnostics. Service and network details are included only when their individual capabilities are enabled." };
    }

    private static bool WaitForProcessExit(Process process)
    {
        process.WaitForExit();
        return true;
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
        var timeoutSeconds = (int?)ReadSystemManagerSettings()["commandTimeoutSeconds"] ?? 0;
        var exited = timeoutSeconds > 0 ? process.WaitForExit(checked(timeoutSeconds * 1000)) : WaitForProcessExit(process);
        if (!exited) { try { process.Kill(true); } catch { } throw new TimeoutException($"Windows service enumeration exceeded the configured {timeoutSeconds}-second timeout."); }
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
