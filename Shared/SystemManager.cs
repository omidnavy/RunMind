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
using System.Net;
using System.Net.Sockets;
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
    private static string SystemManagerHealthHistoryPath => Path.Combine(SystemManagerRoot, "health-history.json");
    private static string SystemManagerHealthHistoryMutexName => "Local\\RunMind.SystemManager.HealthHistory." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SystemManagerRoot.ToUpperInvariant())));
    private static readonly object SystemManagerHealthHistorySync = new();
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
            ["systemInformation"] = true, ["environmentInformation"] = false, ["serviceInformation"] = false, ["networkInformation"] = false,
            ["eventLogInformation"] = false, ["crashInvestigation"] = false, ["resourceMonitoring"] = false,
            ["scheduledTaskInformation"] = false, ["scheduledTaskManagement"] = false, ["storageHealth"] = false,
            ["securityPosture"] = false, ["targetedNetworkDiagnostics"] = false, ["healthHistory"] = false
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
            foreach (var capability in new[] { "discoverApplications", "launchApplications", "openTargets", "listProcesses", "inspectProcesses", "terminateProcesses", "systemInformation", "environmentInformation", "serviceInformation", "networkInformation", "eventLogInformation", "crashInvestigation", "resourceMonitoring", "scheduledTaskInformation", "scheduledTaskManagement", "storageHealth", "securityPosture", "targetedNetworkDiagnostics", "healthHistory" })
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
        foreach (var capability in new[] { "discoverApplications", "launchApplications", "openTargets", "listProcesses", "inspectProcesses", "terminateProcesses", "systemInformation", "environmentInformation", "serviceInformation", "networkInformation", "eventLogInformation", "crashInvestigation", "resourceMonitoring", "scheduledTaskInformation", "scheduledTaskManagement", "storageHealth", "securityPosture", "targetedNetworkDiagnostics", "healthHistory" })
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

    private static bool WaitForProcessExit(Process process)
    {
        process.WaitForExit();
        return true;
    }

    private static (int ExitCode, string Output) RunFixedDiagnosticCommand(string executable, IEnumerable<string> arguments, int timeoutMilliseconds = 15000)
    {
        if (!File.Exists(executable)) return (-1, $"Diagnostic utility '{Path.GetFileName(executable)}' was not found.");
        using var readerCancellation = new CancellationTokenSource();
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start diagnostic utility '{Path.GetFileName(executable)}'.");
            // Drain both pipes concurrently and retain at most 512 KB from each;
            // clipping only after collecting the full stream would still allocate without bound.
            var outputTask = ReadBoundedProcessOutputAsync(process.StandardOutput, readerCancellation.Token);
            var errorTask = ReadBoundedProcessOutputAsync(process.StandardError, readerCancellation.Token);
            var hasExited = timeoutMilliseconds > 0
                ? process.WaitForExit(timeoutMilliseconds)
                : WaitForProcessExit(process);
            if (!hasExited)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                try { process.WaitForExit(5000); } catch { }
                readerCancellation.Cancel();
                try { Task.WhenAll(outputTask, errorTask).Wait(TimeSpan.FromSeconds(5)); } catch { }
                return (-2, $"Diagnostic utility '{Path.GetFileName(executable)}' exceeded its {timeoutMilliseconds} ms timeout and was terminated.");
            }

            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            var combined = output.text;
            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error.text))
                combined = string.IsNullOrWhiteSpace(combined) ? "[stderr] " + error.text : combined + Environment.NewLine + "[stderr] " + error.text;
            return (process.ExitCode, combined);
        }
        catch (Exception error)
        {
            readerCancellation.Cancel();
            return (-1, $"Diagnostic utility '{Path.GetFileName(executable)}' failed: {error.Message}");
        }
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

    private static string? ParseServiceControlField(string output, string field)
    {
        var match = System.Text.RegularExpressions.Regex.Match(output, @"(?im)^\s*" + System.Text.RegularExpressions.Regex.Escape(field) + @"\s*:\s*(.*?)\s*$");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static object ParseServiceConfiguration(string output)
    {
        var startType = ParseServiceControlField(output, "START_TYPE");
        var startToken = startType?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        var startupMode = startToken switch
        {
            "BOOT_START" => "Boot",
            "SYSTEM_START" => "System",
            "AUTO_START" => "Automatic",
            "DEMAND_START" => "Manual",
            "DISABLED" => "Disabled",
            _ => "Unavailable"
        };
        return new
        {
            serviceName = ParseServiceControlField(output, "SERVICE_NAME"),
            displayName = ParseServiceControlField(output, "DISPLAY_NAME"),
            serviceType = ParseServiceControlField(output, "TYPE"),
            startupMode,
            startTypeRaw = startType,
            errorControl = ParseServiceControlField(output, "ERROR_CONTROL"),
            binaryPath = ParseServiceControlField(output, "BINARY_PATH_NAME"),
            loadOrderGroup = ParseServiceControlField(output, "LOAD_ORDER_GROUP"),
            dependencies = ParseServiceDependencies(output),
            serviceAccount = ParseServiceControlField(output, "SERVICE_START_NAME")
        };
    }

    private sealed class ServiceDependencyBudget
    {
        public int Remaining { get; private set; }
        public bool Truncated { get; set; }
        public ServiceDependencyBudget(int maximum) => Remaining = maximum;
        public bool Take() { if (Remaining <= 0) { Truncated = true; return false; } Remaining--; return true; }
    }

    private static string[] ParseServiceDependencies(string output)
    {
        var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var values = new List<string>();
        var collecting = false;
        foreach (var line in lines)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*DEPENDENCIES\s*:", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                collecting = true;
                var separator = line.IndexOf(':');
                var first = separator >= 0 ? line[(separator + 1)..].Trim() : string.Empty;
                if (first.Length > 0) values.Add(first);
                continue;
            }
            if (collecting && System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*[A-Z][A-Z _-]{1,30}\s*:")) break;
            if (collecting)
            {
                var candidate = line.Trim();
                if (candidate.Length == 0) continue;
                values.Add(candidate);
            }
        }
        return values.Select(value => value.Trim())
            .Where(value => value.Length > 0 && value.Length <= 128)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToArray();
    }

    private async Task<object> ReadServiceDependencyTreeAsync(string service, int depth, HashSet<string> visited, ServiceDependencyBudget budget, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > 5) { budget.Truncated = true; return new { name = service, truncated = true, dependencies = Array.Empty<object>() }; }
        if (!budget.Take()) return new { name = service, truncated = true, dependencies = Array.Empty<object>() };
        if (!visited.Add(service)) return new { name = service, cycle = true, dependencies = Array.Empty<object>() };
        try
        {
            var config = await RunServiceControlAsync(new[] { "qc", service }, cancellationToken).ConfigureAwait(false);
            if (config.exitCode != 0) return new { name = service, available = false, error = Limit(config.error, 300), dependencies = Array.Empty<object>() };
            var dependencies = new List<object>();
            foreach (var dependency in ParseServiceDependencies(config.output))
            {
                if (dependency.StartsWith('+'))
                {
                    dependencies.Add(new { name = dependency, kind = "load-order-group", dependencies = Array.Empty<object>() });
                    continue;
                }
                if (!System.Text.RegularExpressions.Regex.IsMatch(dependency, "^[A-Za-z0-9_.-]{1,128}$")) continue;
                dependencies.Add(await ReadServiceDependencyTreeAsync(dependency, depth + 1, visited, budget, cancellationToken).ConfigureAwait(false));
                if (budget.Remaining <= 0) break;
            }
            return new { name = service, available = true, dependencies };
        }
        finally { visited.Remove(service); }
    }

    private async Task<object> SystemManagerServiceActionAsync(JsonElement args, CancellationToken cancellationToken)
    {
        RequireSystemCapability("diagnostics", "serviceInformation");
        var action = ReadString(args, "action", 20).ToLowerInvariant();
        var service = ReadString(args, "service", 128);
        if (action is not ("list" or "inspect" or "start" or "stop" or "restart" or "config" or "dependents" or "dependencies"))
            throw new InvalidDataException("Supported service actions are list, inspect, config, dependents, dependencies, start, stop, and restart.");
        if (action != "list" && !System.Text.RegularExpressions.Regex.IsMatch(service, "^[A-Za-z0-9_.-]{1,128}$"))
            throw new InvalidDataException("A valid Windows service name is required.");

        if (action == "list")
        {
            var result = await RunServiceControlAsync(new[] { "query", "state=", "all" }, cancellationToken).ConfigureAwait(false);
            return new { success = result.exitCode == 0, accepted = result.exitCode == 0, action, completed = true, verified = result.exitCode == 0, exitCode = result.exitCode, output = result.output, error = result.error, message = result.exitCode == 0 ? "Service enumeration completed." : "Service enumeration failed." };
        }
        if (action == "dependencies")
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var budget = new ServiceDependencyBudget(50);
            var tree = await ReadServiceDependencyTreeAsync(service, 0, visited, budget, cancellationToken).ConfigureAwait(false);
            return new { success = true, accepted = true, action, target = service, completed = true, verified = true, dependencyTree = tree, maxDepth = 5, maxNodes = 50, truncated = budget.Truncated, note = "Dependency traversal is read-only, cycle-safe, and bounded. Load-order groups are retained as group entries and are not recursively expanded." };
        }
        if (action == "config" || action == "dependents")
        {
            if (action == "dependents")
            {
                var result = await RunServiceControlAsync(new[] { "enumdepend", service }, cancellationToken).ConfigureAwait(false);
                return new { success = result.exitCode == 0, accepted = result.exitCode == 0, action, target = service, completed = true, verified = result.exitCode == 0, exitCode = result.exitCode, output = result.output, error = result.error, message = result.exitCode == 0 ? "Dependent services retrieved." : "The service query failed." };
            }
            var config = await RunServiceControlAsync(new[] { "qc", service }, cancellationToken).ConfigureAwait(false);
            var failure = await RunServiceControlAsync(new[] { "qfailure", service }, cancellationToken).ConfigureAwait(false);
            var failureFlag = await RunServiceControlAsync(new[] { "qfailureflag", service }, cancellationToken).ConfigureAwait(false);
            var triggers = await RunServiceControlAsync(new[] { "qtriggerinfo", service }, cancellationToken).ConfigureAwait(false);
            return new { success = config.exitCode == 0, accepted = config.exitCode == 0, action, target = service, completed = true, verified = config.exitCode == 0, exitCode = config.exitCode, configuration = config.output, configurationDetails = ParseServiceConfiguration(config.output), failureActions = failure.output, failureActionFlag = failureFlag.output, triggerConfiguration = triggers.output, warnings = new[] { failure.error, failureFlag.error, triggers.error }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(), error = config.error, message = config.exitCode == 0 ? "Structured startup mode, declared dependencies, service account, recovery actions and trigger configuration retrieved." : "The service configuration query failed." };
        }
        if (action == "inspect")
        {
            var query = await RunServiceControlAsync(new[] { "queryex", service }, cancellationToken).ConfigureAwait(false);
            var config = await RunServiceControlAsync(new[] { "qc", service }, cancellationToken).ConfigureAwait(false);
            var failure = await RunServiceControlAsync(new[] { "qfailure", service }, cancellationToken).ConfigureAwait(false);
            var failureFlag = await RunServiceControlAsync(new[] { "qfailureflag", service }, cancellationToken).ConfigureAwait(false);
            var triggers = await RunServiceControlAsync(new[] { "qtriggerinfo", service }, cancellationToken).ConfigureAwait(false);
            return new { success = query.exitCode == 0, accepted = query.exitCode == 0, action, target = service, completed = true, verified = query.exitCode == 0, actualState = ParseServiceState(query.output), queryExitCode = query.exitCode, query = query.output, configuration = config.output, configurationDetails = ParseServiceConfiguration(config.output), failureActions = failure.output, failureActionFlag = failureFlag.output, triggerConfiguration = triggers.output, warnings = new[] { config.error, failure.error, failureFlag.error, triggers.error }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray(), error = query.error, message = query.exitCode == 0 ? "Service state, structured startup configuration, dependency declaration and recovery configuration retrieved." : "Service inspection failed." };
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

    private static void RequirePortOwnerCapability()
    {
        var settings = ReadSystemManagerSettings();
        if (!(bool?)settings["enabled"] ?? false)
            throw new UnauthorizedAccessException("Windows System Manager is disabled in the System Manager menu.");
        var processLookup = (bool?)settings["groups"]?["processes"] == true
            && (bool?)settings["capabilities"]?["listProcesses"] == true;
        var networkDiagnostics = (bool?)settings["groups"]?["diagnostics"] == true
            && (bool?)settings["capabilities"]?["networkInformation"] == true;
        if (!processLookup && !networkDiagnostics)
            throw new UnauthorizedAccessException("Enable either Processes → List processes or Diagnostics → Network information to use targeted port-owner lookup.");
    }

    private static object SystemManagerFindPortOwner(JsonElement args)
    {
        RequirePortOwnerCapability();
        if (!args.TryGetProperty("port", out var portValue) || !portValue.TryGetInt32(out var port) || port is < 1 or > 65535)
            throw new InvalidDataException("Provide a TCP/UDP port between 1 and 65535.");
        var protocol = ReadString(args, "protocol", 8).Trim().ToLowerInvariant();
        if (protocol is not ("tcp" or "udp")) throw new InvalidDataException("Protocol must be tcp or udp.");

        var netstatPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netstat.exe");
        var result = RunFixedDiagnosticCommand(netstatPath, new[] { "-ano", "-p", protocol }, 10000);
        if (result.ExitCode != 0)
            return new { success = false, accepted = true, completed = true, verified = false, port, protocol,
                matches = Array.Empty<object>(), message = result.ExitCode == -2 ? "Targeted listener inspection timed out." : "Windows netstat could not be executed; port ownership is unavailable." };

        var includeDetails = (bool?)ReadSystemManagerSettings()["capabilities"]?["inspectProcesses"] == true;
        var matches = new List<object>();
        foreach (var line in result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < (protocol == "tcp" ? 5 : 4) || !parts[0].Equals(protocol, StringComparison.OrdinalIgnoreCase)) continue;
            var localEndpoint = parts[1];
            var separator = localEndpoint.LastIndexOf(':');
            if (separator < 0 || !int.TryParse(localEndpoint[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var localPort) || localPort != port) continue;
            var pidText = parts[^1];
            if (!int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid <= 0) continue;
            var state = protocol == "tcp" ? parts[^2] : "BOUND";
            string? processName = null;
            string? executablePath = null;
            DateTime? startedAtUtc = null;
            try
            {
                using var process = Process.GetProcessById(pid);
                processName = process.ProcessName;
                try { startedAtUtc = process.StartTime.ToUniversalTime(); } catch { }
                if (includeDetails) { try { executablePath = process.MainModule?.FileName; } catch { } }
            }
            catch { /* Listener may exit between netstat and identity lookup. Preserve PID, mark metadata unavailable. */ }
            matches.Add(new { protocol, localEndpoint, state, processId = pid, processName, executablePath, startedAtUtc });
            if (matches.Count >= 100) break;
        }
        return new { success = true, accepted = true, completed = true, verified = true, port, protocol,
            matches, count = matches.Count, detailFieldsEnabled = includeDetails,
            message = matches.Count == 0 ? $"No {protocol.ToUpperInvariant()} endpoint was found using local port {port}." : $"Found {matches.Count} {protocol.ToUpperInvariant()} endpoint(s) using local port {port}." };
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
        public readonly ulong Value => ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out NativeFileTime idleTime, out NativeFileTime kernelTime, out NativeFileTime userTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NativeMemoryStatusEx
    {
        public uint Length; public uint MemoryLoad; public ulong TotalPhysical; public ulong AvailablePhysical;
        public ulong TotalPageFile; public ulong AvailablePageFile; public ulong TotalVirtual; public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref NativeMemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeIoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr processHandle, out NativeIoCounters ioCounters);

    private static object? ReadSystemMemoryStatus()
    {
        var status = new NativeMemoryStatusEx { Length = (uint)Marshal.SizeOf<NativeMemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0) return null;
        return new { totalPhysicalBytes = status.TotalPhysical, availablePhysicalBytes = status.AvailablePhysical, usedPhysicalBytes = status.TotalPhysical - status.AvailablePhysical, memoryLoadPercent = status.MemoryLoad, totalPageFileBytes = status.TotalPageFile, availablePageFileBytes = status.AvailablePageFile };
    }

    private static object ReadDiskIoTelemetry()
    {
        const string script = """
$rows = @()
try {
  $rows = @(Get-CimInstance -ClassName Win32_PerfFormattedData_PerfDisk_PhysicalDisk -ErrorAction Stop | Select-Object -First 32 | ForEach-Object { [pscustomobject]@{ name=$_.Name; percentDiskTime=$_.PercentDiskTime; readBytesPerSecond=$_.DiskReadBytesPersec; writeBytesPerSecond=$_.DiskWriteBytesPersec; averageQueueLength=$_.AvgDiskQueueLength; currentQueueLength=$_.CurrentDiskQueueLength } })
  $result = [pscustomobject]@{ available=($rows.Count -gt 0); disks=$rows; source='Win32_PerfFormattedData_PerfDisk_PhysicalDisk'; warning=$null }
} catch { $result = [pscustomobject]@{ available=$false; disks=@(); source='Win32_PerfFormattedData_PerfDisk_PhysicalDisk'; warning=([string]$_.Exception.Message).Substring(0,[Math]::Min(300,([string]$_.Exception.Message).Length)) } }
[Console]::Out.WriteLine((ConvertTo-Json -InputObject $result -Depth 4 -Compress))
""";
        try { return RunPowerShellJson(script, new { }, 10000); }
        catch (Exception error) { return new { available = false, disks = Array.Empty<object>(), source = "Win32_PerfFormattedData_PerfDisk_PhysicalDisk", warning = Limit(error.Message, 300) }; }
    }

    private sealed record ResourceProcessSample(int ProcessId, long StartedAtTicks, long CpuTicks, long WorkingSetBytes, long PrivateMemoryBytes, ulong ReadTransferBytes, ulong WriteTransferBytes, string Name);
    private sealed record NetworkByteSnapshot(long ReceivedBytes, long SentBytes, int InterfaceCount, string InterfaceFingerprint);
    private sealed class LiveResourceSession
    {
        public required string Id { get; init; }
        public required int DurationSeconds { get; init; }
        public required int IntervalSeconds { get; init; }
        public required DateTimeOffset StartedAtUtc { get; init; }
        public required Stopwatch Clock { get; init; }
        public required long PreviousSampleTimestamp { get; set; }
        public required Dictionary<int, ResourceProcessSample> PreviousProcesses { get; set; }
        public (ulong Idle, ulong Kernel, ulong User)? PreviousSystemCpu { get; set; }
        public NetworkByteSnapshot? PreviousNetwork { get; set; }
        public long Sequence { get; set; }
    }
    private sealed class ResourceProcessAggregate
    {
        public required int ProcessId { get; init; }
        public required long StartedAtTicks { get; init; }
        public required string Name { get; init; }
        public double CpuSeconds { get; set; }
        public long WorkingSetBytes { get; set; }
        public long PrivateMemoryBytes { get; set; }
    }

    private static void RequireSystemManagerCapability(string capability)
        => RequireSystemCapability("diagnostics", capability);

    // All PowerShell bodies below are shipped constants. User-supplied values are
    // serialized as JSON, Base64-encoded, and decoded as data inside the fixed script.
    // No user value is interpolated into executable PowerShell source.
    private static JsonElement RunPowerShellJson(string fixedBody, object request, int timeoutMilliseconds)
    {
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new FileNotFoundException("Windows PowerShell is unavailable.");
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)));
        var source = "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)\n" +
            "$ErrorActionPreference = 'Stop'; $WarningPreference = 'SilentlyContinue'; " +
            "$request = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + payload + "')))\n" + fixedBody;
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(source));
        var result = RunFixedDiagnosticCommand(powershell, new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encodedCommand }, timeoutMilliseconds);
        if (result.ExitCode == -2) throw new TimeoutException("The Windows diagnostic command exceeded its bounded execution time.");
        if (result.ExitCode != 0) throw new InvalidOperationException(Limit(string.IsNullOrWhiteSpace(result.Output) ? "Windows PowerShell diagnostic failed." : result.Output, 1600));
        if (string.IsNullOrWhiteSpace(result.Output)) throw new InvalidDataException("Windows PowerShell returned no diagnostic JSON.");
        using var document = JsonDocument.Parse(result.Output);
        return document.RootElement.Clone();
    }

    private static object SystemManagerReadEventLog(JsonElement args)
    {
        RequireSystemManagerCapability("eventLogInformation");
        var logName = ReadString(args, "log_name", 32);
        if (logName is not ("Application" or "System" or "Setup"))
            throw new InvalidDataException("Event log must be Application, System, or Setup. Security and other sensitive/custom logs are not exposed by this tool.");
        var hours = args.TryGetProperty("hours", out var hoursValue) && hoursValue.TryGetInt32(out var requestedHours) ? requestedHours : 24;
        var maximum = args.TryGetProperty("max_events", out var maximumValue) && maximumValue.TryGetInt32(out var requestedMaximum) ? requestedMaximum : 50;
        var eventId = args.TryGetProperty("event_id", out var eventIdValue) && eventIdValue.TryGetInt32(out var requestedId) ? requestedId : 0;
        if (hours is < 1 or > 720) throw new InvalidDataException("Event log time range must be between 1 and 720 hours.");
        if (maximum is < 1 or > 200) throw new InvalidDataException("Event log results must be between 1 and 200 events.");
        if (eventId is < 0 or > 65535) throw new InvalidDataException("Event ID must be between 0 and 65535, or 0 for all IDs.");
        var query = ReadString(args, "query", 120);
        const string script = """
$filter = @{ LogName = [string]$request.logName; StartTime = (Get-Date).AddHours(-[int]$request.hours) }
if ([int]$request.eventId -gt 0) { $filter.Id = [int]$request.eventId }
$rows = @()
try {
  $rows = @(Get-WinEvent -FilterHashtable $filter -MaxEvents 2000 -ErrorAction Stop | Where-Object {
    [string]::IsNullOrWhiteSpace([string]$request.query) -or ([string]$_.ProviderName).IndexOf([string]$request.query, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or ([string]$_.Message).IndexOf([string]$request.query, [StringComparison]::OrdinalIgnoreCase) -ge 0
  } | Select-Object -First ([int]$request.maxEvents) | ForEach-Object {
    [pscustomobject]@{ logName=$_.LogName; eventId=$_.Id; level=$_.LevelDisplayName; provider=$_.ProviderName; timeCreatedUtc=$_.TimeCreated.ToUniversalTime().ToString('o'); recordId=$_.RecordId; machineName=$_.MachineName; message=([string]$_.Message).Substring(0,[Math]::Min(1200,([string]$_.Message).Length)) }
  })
} catch {
  if ($_.FullyQualifiedErrorId -notmatch 'NoMatchingEventsFound') { throw }
}
$result = [pscustomobject]@{ logName=[string]$request.logName; hours=[int]$request.hours; eventId=[int]$request.eventId; events=@($rows); count=@($rows).Count; truncated=(@($rows).Count -ge [int]$request.maxEvents) }
[Console]::Out.WriteLine((ConvertTo-Json -InputObject $result -Depth 5 -Compress))
""";
        var report = RunPowerShellJson(script, new { logName, hours, eventId, maxEvents = maximum, query }, Math.Min(120000, Math.Max(10000, hours > 168 ? 45000 : 20000)));
        return new { success = true, accepted = true, completed = true, verified = true, investigation = report, note = "Read-only event investigation. Only Application, System and Setup logs are queried; each event message is truncated to 1,200 characters." };
    }

    private static object SystemManagerInvestigateCrashes(JsonElement args)
    {
        RequireSystemManagerCapability("crashInvestigation");
        var days = args.TryGetProperty("days", out var daysValue) && daysValue.TryGetInt32(out var requestedDays) ? requestedDays : 7;
        var maximum = args.TryGetProperty("max_events", out var maximumValue) && maximumValue.TryGetInt32(out var requestedMaximum) ? requestedMaximum : 100;
        if (days is < 1 or > 30) throw new InvalidDataException("Crash investigation range must be between 1 and 30 days.");
        if (maximum is < 1 or > 200) throw new InvalidDataException("Crash investigation results must be between 1 and 200 events.");
        const string script = """
$since = (Get-Date).AddDays(-[int]$request.days)
$rows = @()
$queries = @(@{ LogName='Application'; Id=@(1000,1001,1026); StartTime=$since }, @{ LogName='System'; Id=@(7000,7001,7023,7024,7031,7034,7038,7043); StartTime=$since })
foreach ($filter in $queries) {
  try {
    $rows += @(Get-WinEvent -FilterHashtable $filter -MaxEvents ([int]$request.maxEvents) -ErrorAction Stop | ForEach-Object {
      $message = [string]$_.Message
      $app = ''; $module = ''; $exception = ''; $service = ''
      if ($message -match '(?im)(?:Faulting application name|Application):\s*([^,\r\n]+)') { $app = $Matches[1].Trim() }
      if ($message -match '(?im)(?:Faulting module name|Faulting module):\s*([^,\r\n]+)') { $module = $Matches[1].Trim() }
      if ($message -match '(?im)exception code:\s*(0x[0-9a-f]+)') { $exception = $Matches[1].Trim() }
      if ($_.LogName -eq 'System' -and $_.Id -in @(7000,7001,7023,7024,7031,7034,7038,7043)) {
        if ($message -match '(?im)^The .+? service depends on the (.+?) service which failed to start') { $service = $Matches[1].Trim() }
        elseif ($message -match '(?im)^(?:The )?(.+?) service (?:terminated|failed|did not|was unable|cannot|could not)') { $service = $Matches[1].Trim() }
        elseif ($message -match '(?im)service name:\s*([^\r\n]+)') { $service = $Matches[1].Trim() }
      }
      [pscustomobject]@{ logName=$_.LogName; eventId=$_.Id; provider=$_.ProviderName; timeCreatedUtc=$_.TimeCreated.ToUniversalTime().ToString('o'); recordId=$_.RecordId; application=$app; serviceName=$service; faultingModule=$module; exceptionCode=$exception; message=$message.Substring(0,[Math]::Min(1400,$message.Length)) }
    })
  } catch { if ($_.FullyQualifiedErrorId -notmatch 'NoMatchingEventsFound') { throw } }
}
$ordered = @($rows | Sort-Object timeCreatedUtc -Descending | Select-Object -First ([int]$request.maxEvents))
$patterns = @($ordered | Where-Object { $_.application -or $_.exceptionCode } | Group-Object { ($_.application.ToLowerInvariant() + '|' + $_.exceptionCode.ToLowerInvariant()) } | ForEach-Object { [pscustomobject]@{ key=$_.Name; occurrences=$_.Count; firstSeenUtc=($_.Group | Sort-Object timeCreatedUtc | Select-Object -First 1).timeCreatedUtc; lastSeenUtc=($_.Group | Sort-Object timeCreatedUtc -Descending | Select-Object -First 1).timeCreatedUtc } } | Sort-Object occurrences -Descending)
$serviceFailures = @($ordered | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.serviceName) } | Group-Object { $_.serviceName.ToLowerInvariant() } | ForEach-Object { [pscustomobject]@{ serviceName=$_.Group[0].serviceName; occurrences=$_.Count; eventIds=@($_.Group | Select-Object -ExpandProperty eventId -Unique); firstSeenUtc=($_.Group | Sort-Object timeCreatedUtc | Select-Object -First 1).timeCreatedUtc; lastSeenUtc=($_.Group | Sort-Object timeCreatedUtc -Descending | Select-Object -First 1).timeCreatedUtc } } | Sort-Object occurrences -Descending)
$result = [pscustomobject]@{ days=[int]$request.days; events=$ordered; count=$ordered.Count; recurringPatterns=$patterns; recurringServiceFailures=$serviceFailures; eventSources=@('Application Error 1000','Windows Error Reporting 1001','.NET Runtime 1026','Service Control Manager 7000/7001/7023/7024/7031/7034/7038/7043') }
[Console]::Out.WriteLine((ConvertTo-Json -InputObject $result -Depth 6 -Compress))
""";
        var report = RunPowerShellJson(script, new { days, maxEvents = maximum }, 30000);
        return new { success = true, accepted = true, completed = true, verified = true, investigation = report, note = "Crash correlation is based on Windows event evidence and can miss crashes whose logging is disabled, delayed or outside the selected window. Repeated signatures are indicators, not proof of a common root cause." };
    }

    private static NetworkByteSnapshot? ReadNetworkByteSnapshot()
    {
        try
        {
            long received = 0;
            long sent = 0;
            var count = 0;
            var interfaceIds = new List<string>();
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                try
                {
                    var statistics = networkInterface.GetIPStatistics();
                    if (statistics.BytesReceived < 0 || statistics.BytesSent < 0) continue;
                    received = checked(received + statistics.BytesReceived);
                    sent = checked(sent + statistics.BytesSent);
                    interfaceIds.Add(networkInterface.Id);
                    count++;
                }
                catch { }
            }
            var fingerprint = string.Join("|", interfaceIds.OrderBy(id => id, StringComparer.Ordinal));
            return count > 0 ? new NetworkByteSnapshot(received, sent, count, fingerprint) : null;
        }
        catch { return null; }
    }

    private static Dictionary<int, ResourceProcessSample> ReadResourceProcessSnapshot(out bool truncated)
    {
        var result = new Dictionary<int, ResourceProcessSample>();
        var processList = Process.GetProcesses();
        truncated = processList.Length > 1000;
        foreach (var process in processList)
        {
            if (result.Count >= 1000) { process.Dispose(); continue; }
            try
            {
                long started = -1;
                try { started = process.StartTime.ToUniversalTime().Ticks; } catch { }
                long cpu = -1;
                try { cpu = process.TotalProcessorTime.Ticks; } catch { }
                long memory = 0;
                long privateMemory = 0;
                ulong readTransfer = 0;
                ulong writeTransfer = 0;
                try { memory = process.WorkingSet64; } catch { }
                try { privateMemory = process.PrivateMemorySize64; } catch { }
                try
                {
                    if (GetProcessIoCounters(process.Handle, out var ioCounters))
                    {
                        readTransfer = ioCounters.ReadTransferCount;
                        writeTransfer = ioCounters.WriteTransferCount;
                    }
                }
                catch { }
                result[process.Id] = new ResourceProcessSample(process.Id, started, cpu, memory, privateMemory, readTransfer, writeTransfer, process.ProcessName);
            }
            catch { }
            finally { process.Dispose(); }
        }
        return result;
    }

    private static (ulong Idle, ulong Kernel, ulong User)? ReadSystemCpuTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        return (idle.Value, kernel.Value, user.Value);
    }

    private object SystemManagerLiveResourcesStart(JsonElement args)
    {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(AgentHostBridge));
        RequireSystemManagerCapability("resourceMonitoring");
        var duration = args.TryGetProperty("duration_seconds", out var durationValue) && durationValue.TryGetInt32(out var requestedDuration) ? requestedDuration : 300;
        var interval = args.TryGetProperty("interval_seconds", out var intervalValue) && intervalValue.TryGetInt32(out var requestedInterval) ? requestedInterval : 1;
        if (duration is < 30 or > 900) throw new InvalidDataException("Live resource sessions must be between 30 seconds and 15 minutes.");
        if (interval is not (1 or 2 or 5)) throw new InvalidDataException("Live resource sampling interval must be 1, 2 or 5 seconds.");

        lock (_liveResourceSync)
        {
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(AgentHostBridge));
            if (_liveResourceSession is { } active && active.Clock.Elapsed.TotalSeconds < active.DurationSeconds)
                throw new InvalidOperationException("A live resource monitoring session is already active in this RunMind window.");

            var initialProcesses = ReadResourceProcessSnapshot(out _);
            var initialSystemCpu = ReadSystemCpuTimes();
            var initialNetwork = ReadNetworkByteSnapshot();
            var now = Stopwatch.GetTimestamp();
            var session = new LiveResourceSession
            {
                Id = Guid.NewGuid().ToString("N"),
                DurationSeconds = duration,
                IntervalSeconds = interval,
                StartedAtUtc = DateTimeOffset.UtcNow,
                Clock = Stopwatch.StartNew(),
                PreviousSampleTimestamp = now,
                PreviousProcesses = initialProcesses,
                PreviousSystemCpu = initialSystemCpu,
                PreviousNetwork = initialNetwork
            };
            _liveResourceSession = session;
            return new
            {
                success = true,
                sessionId = session.Id,
                startedAtUtc = session.StartedAtUtc,
                durationSeconds = duration,
                intervalSeconds = interval,
                processorCount = Environment.ProcessorCount,
                storage = "volatile-memory-only",
                note = "Monitoring is user-started, read-only and bounded. Samples remain in this window's memory and are not persisted. Closing the System Manager panel or RunMind window stops the session."
            };
        }
    }

    private object SystemManagerLiveResourcesSample(JsonElement args)
    {
        if (Volatile.Read(ref _disposed) != 0) return new { success = false, accepted = false, running = false, stopped = true, reason = "host-disposed" };
        RequireSystemManagerCapability("resourceMonitoring");
        var requestedId = ReadBoundedExactString(args, "session_id", 64);
        if (requestedId.Length != 32 || !requestedId.All(Uri.IsHexDigit)) throw new InvalidDataException("A valid live resource session ID is required.");

        lock (_liveResourceSync)
        {
            if (Volatile.Read(ref _disposed) != 0) { _liveResourceSession = null; return new { success = false, accepted = false, running = false, stopped = true, reason = "host-disposed" }; }
            var session = _liveResourceSession;
            if (session is null || !string.Equals(session.Id, requestedId, StringComparison.Ordinal))
                return new { success = false, accepted = false, running = false, stopped = true, reason = "session-not-found" };
            if (session.Clock.Elapsed.TotalSeconds >= session.DurationSeconds)
            {
                _liveResourceSession = null;
                return new { success = true, accepted = false, running = false, expired = true, elapsedSeconds = Math.Round(session.Clock.Elapsed.TotalSeconds, 2), reason = "duration-limit-reached" };
            }

            var currentAt = Stopwatch.GetTimestamp();
            var elapsed = Math.Max(0.001, (double)(currentAt - session.PreviousSampleTimestamp) / Stopwatch.Frequency);
            var minimumInterval = Math.Max(0.5, session.IntervalSeconds * 0.7);
            if (elapsed < minimumInterval)
                return new { success = true, accepted = false, running = true, retryAfterMilliseconds = (int)Math.Ceiling((minimumInterval - elapsed) * 1000), sequence = session.Sequence };

            var currentProcesses = ReadResourceProcessSnapshot(out var processListTruncated);
            var currentSystemCpu = ReadSystemCpuTimes();
            double? machineCpuPercent = null;
            if (session.PreviousSystemCpu.HasValue && currentSystemCpu.HasValue)
            {
                var totalDelta = (currentSystemCpu.Value.Kernel - session.PreviousSystemCpu.Value.Kernel) + (currentSystemCpu.Value.User - session.PreviousSystemCpu.Value.User);
                var idleDelta = currentSystemCpu.Value.Idle - session.PreviousSystemCpu.Value.Idle;
                if (totalDelta > 0 && totalDelta >= idleDelta)
                    machineCpuPercent = Math.Round(Math.Clamp((double)(totalDelta - idleDelta) * 100d / totalDelta, 0, 100), 2);
            }

            var topCpu = new List<(double MachineShare, object Data)>();
            var cpuShareByProcess = new Dictionary<int, double>();
            var processorCount = Math.Max(1, Environment.ProcessorCount);
            foreach (var current in currentProcesses.Values)
            {
                if (current.CpuTicks < 0 || current.StartedAtTicks <= 0 || !session.PreviousProcesses.TryGetValue(current.ProcessId, out var previous) || previous.CpuTicks < 0 || previous.StartedAtTicks != current.StartedAtTicks) continue;
                var cpuSeconds = Math.Max(0, current.CpuTicks - previous.CpuTicks) / (double)TimeSpan.TicksPerSecond;
                var machineShare = Math.Clamp(cpuSeconds / elapsed / processorCount * 100d, 0, 100);
                if (machineShare < 0.01) continue;
                cpuShareByProcess[current.ProcessId] = Math.Round(machineShare, 2);
                topCpu.Add((machineShare, new
                {
                    processId = current.ProcessId,
                    name = current.Name,
                    machineCpuSharePercent = Math.Round(machineShare, 2),
                    singleCoreEquivalentPercent = Math.Round(Math.Clamp(cpuSeconds / elapsed * 100d, 0, 100 * processorCount), 2),
                    workingSetBytes = current.WorkingSetBytes,
                    privateMemoryBytes = current.PrivateMemoryBytes,
                    startedAtUtc = new DateTimeOffset(current.StartedAtTicks, TimeSpan.Zero)
                }));
            }
            var orderedCpu = topCpu.OrderByDescending(item => item.MachineShare).Take(8).Select(item => item.Data).ToArray();
            var topMemory = currentProcesses.Values.OrderByDescending(item => item.WorkingSetBytes).Take(8).Select(item => new
            {
                processId = item.ProcessId,
                name = item.Name,
                workingSetBytes = item.WorkingSetBytes,
                privateMemoryBytes = item.PrivateMemoryBytes,
                machineCpuSharePercent = cpuShareByProcess.TryGetValue(item.ProcessId, out var memoryRowCpuShare) ? memoryRowCpuShare : (double?)null,
                startedAtUtc = item.StartedAtTicks > 0 ? new DateTimeOffset(item.StartedAtTicks, TimeSpan.Zero) : (DateTimeOffset?)null
            }).ToArray();

            var currentNetwork = ReadNetworkByteSnapshot();
            double? receiveBytesPerSecond = null;
            double? sendBytesPerSecond = null;
            var networkTopologyStable = currentNetwork is not null && session.PreviousNetwork is not null
                && string.Equals(currentNetwork.InterfaceFingerprint, session.PreviousNetwork.InterfaceFingerprint, StringComparison.Ordinal);
            if (networkTopologyStable && currentNetwork is not null && session.PreviousNetwork is not null)
            {
                var receivedDelta = currentNetwork.ReceivedBytes - session.PreviousNetwork.ReceivedBytes;
                var sentDelta = currentNetwork.SentBytes - session.PreviousNetwork.SentBytes;
                if (receivedDelta >= 0 && sentDelta >= 0)
                {
                    receiveBytesPerSecond = Math.Round(receivedDelta / elapsed, 1);
                    sendBytesPerSecond = Math.Round(sentDelta / elapsed, 1);
                }
            }

            double? processIoReadBytesPerSecond = null;
            double? processIoWriteBytesPerSecond = null;
            ulong totalReadDelta = 0;
            ulong totalWriteDelta = 0;
            var ioObservedProcesses = 0;
            foreach (var current in currentProcesses.Values)
            {
                if (!session.PreviousProcesses.TryGetValue(current.ProcessId, out var previous) || previous.StartedAtTicks <= 0 || previous.StartedAtTicks != current.StartedAtTicks) continue;
                if (current.ReadTransferBytes < previous.ReadTransferBytes || current.WriteTransferBytes < previous.WriteTransferBytes) continue;
                totalReadDelta = totalReadDelta > ulong.MaxValue - (current.ReadTransferBytes - previous.ReadTransferBytes) ? ulong.MaxValue : totalReadDelta + (current.ReadTransferBytes - previous.ReadTransferBytes);
                totalWriteDelta = totalWriteDelta > ulong.MaxValue - (current.WriteTransferBytes - previous.WriteTransferBytes) ? ulong.MaxValue : totalWriteDelta + (current.WriteTransferBytes - previous.WriteTransferBytes);
                ioObservedProcesses++;
            }
            if (ioObservedProcesses > 0)
            {
                processIoReadBytesPerSecond = Math.Round(totalReadDelta / elapsed, 1);
                processIoWriteBytesPerSecond = Math.Round(totalWriteDelta / elapsed, 1);
            }

            var memory = ReadSystemMemoryStatus();
            var totalWorkingSet = currentProcesses.Values.Sum(item => Math.Max(0, item.WorkingSetBytes));
            var totalPrivateMemory = currentProcesses.Values.Sum(item => Math.Max(0, item.PrivateMemoryBytes));
            session.PreviousProcesses = currentProcesses;
            session.PreviousSystemCpu = currentSystemCpu;
            session.PreviousNetwork = currentNetwork;
            session.PreviousSampleTimestamp = currentAt;
            session.Sequence++;

            return new
            {
                success = true,
                accepted = true,
                running = true,
                sessionId = session.Id,
                sequence = session.Sequence,
                sampledAtUtc = DateTimeOffset.UtcNow,
                elapsedSeconds = Math.Round(session.Clock.Elapsed.TotalSeconds, 2),
                sampleIntervalSeconds = Math.Round(elapsed, 2),
                remainingSeconds = Math.Max(0, (int)Math.Ceiling(session.DurationSeconds - session.Clock.Elapsed.TotalSeconds)),
                processorCount,
                machineCpuPercent,
                memory,
                network = new
                {
                    available = receiveBytesPerSecond.HasValue && sendBytesPerSecond.HasValue,
                    receiveBytesPerSecond,
                    sendBytesPerSecond,
                    interfaceCount = currentNetwork?.InterfaceCount ?? 0,
                    topologyStable = networkTopologyStable,
                    note = "Rates sum observed operational non-loopback interfaces and are withheld for a sample when the active interface set changes. VPN, bridge and virtual interfaces may overlap; these are interface counters, not application attribution."
                },
                processIo = new
                {
                    available = processIoReadBytesPerSecond.HasValue && processIoWriteBytesPerSecond.HasValue,
                    readBytesPerSecond = processIoReadBytesPerSecond,
                    writeBytesPerSecond = processIoWriteBytesPerSecond,
                    observedProcessCount = ioObservedProcesses,
                    note = "Aggregated Windows process I/O transfer counters. They include process I/O operations and must not be interpreted as physical disk throughput."
                },
                processes = new
                {
                    count = currentProcesses.Count,
                    truncated = processListTruncated,
                    totalWorkingSetBytes = totalWorkingSet,
                    totalPrivateMemoryBytes = totalPrivateMemory,
                    topCpu = orderedCpu,
                    topMemory
                },
                alerts = new
                {
                    highCpu = machineCpuPercent.HasValue && machineCpuPercent.Value >= 85,
                    highMemory = memory is not null && JsonSerializer.SerializeToElement(memory).TryGetProperty("memoryLoadPercent", out var memoryLoad) && memoryLoad.TryGetDouble(out var memoryPercent) && memoryPercent >= 90
                },
                notes = new[]
                {
                    "CPU and memory are Windows host measurements. A missing measurement remains null/unavailable, never zero.",
                    "Process CPU uses process CPU-time deltas matched by PID and process start time; inaccessible or short-lived processes may be absent.",
                    "Process working-set/private-memory values are sampled for up to the first 1,000 processes returned by Windows.",
                    "Network rates sum operational non-loopback interfaces; a sample is unavailable when the observed interface set changes. VPN, bridge or virtual networking configurations can still overlap.",
                    "Process I/O transfer counters include I/O operations and are not physical-disk-only measurements. Physical disk throughput remains in the finite resource report.",
                    "CPU/memory alert flags are fixed display thresholds (85% CPU, 90% memory), not root-cause or anomaly claims."
                }
            };
        }
    }

    private object SystemManagerLiveResourcesStop(JsonElement args)
    {
        var requestedId = ReadBoundedExactString(args, "session_id", 64);
        lock (_liveResourceSync)
        {
            if (_liveResourceSession is null) return new { success = true, stopped = true, alreadyStopped = true };
            if (requestedId.Length != 32 || !requestedId.All(Uri.IsHexDigit) || !string.Equals(_liveResourceSession.Id, requestedId, StringComparison.Ordinal))
                return new { success = false, stopped = false, reason = "session-id-mismatch" };
            _liveResourceSession = null;
            return new { success = true, stopped = true, stoppedAtUtc = DateTimeOffset.UtcNow };
        }
    }

    private void StopLiveResourceSessionForDispose()
    {
        lock (_liveResourceSync) _liveResourceSession = null;
    }

    private static async Task<object> SystemManagerSampleResourcesAsync(JsonElement args, CancellationToken cancellationToken)
    {
        RequireSystemManagerCapability("resourceMonitoring");
        var duration = args.TryGetProperty("duration_seconds", out var durationValue) && durationValue.TryGetInt32(out var requestedDuration) ? requestedDuration : 10;
        var interval = args.TryGetProperty("interval_seconds", out var intervalValue) && intervalValue.TryGetInt32(out var requestedInterval) ? requestedInterval : 1;
        if (duration is < 1 or > 60) throw new InvalidDataException("Resource sample duration must be between 1 and 60 seconds.");
        if (interval is < 1 or > 10 || interval > duration) throw new InvalidDataException("Resource sample interval must be between 1 and 10 seconds and no longer than the requested duration.");
        var samples = new List<object>();
        var cpuReadings = new List<double>();
        var aggregates = new Dictionary<string, ResourceProcessAggregate>(StringComparer.OrdinalIgnoreCase);
        var previousProcesses = ReadResourceProcessSnapshot(out _);
        var latestProcesses = previousProcesses;
        var previousSystem = ReadSystemCpuTimes();
        var previousAt = Stopwatch.GetTimestamp();
        var timer = Stopwatch.StartNew();
        var sampleCount = 0;
        while (timer.Elapsed.TotalSeconds < duration)
        {
            var remaining = TimeSpan.FromSeconds(duration) - timer.Elapsed;
            var delay = TimeSpan.FromSeconds(Math.Min(interval, Math.Max(0.05, remaining.TotalSeconds)));
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var currentAt = Stopwatch.GetTimestamp();
            var elapsed = Math.Max(0.001, (double)(currentAt - previousAt) / Stopwatch.Frequency);
            var currentProcesses = ReadResourceProcessSnapshot(out var processListTruncated);
            var currentSystem = ReadSystemCpuTimes();
            double? machineCpu = null;
            if (previousSystem.HasValue && currentSystem.HasValue)
            {
                var totalDelta = (currentSystem.Value.Kernel - previousSystem.Value.Kernel) + (currentSystem.Value.User - previousSystem.Value.User);
                var idleDelta = currentSystem.Value.Idle - previousSystem.Value.Idle;
                if (totalDelta > 0 && totalDelta >= idleDelta) machineCpu = Math.Round(Math.Clamp((double)(totalDelta - idleDelta) * 100d / totalDelta, 0, 100), 2);
            }
            double totalProcessCpuSeconds = 0;
            foreach (var current in currentProcesses.Values)
            {
                if (current.CpuTicks < 0 || current.StartedAtTicks <= 0 || !previousProcesses.TryGetValue(current.ProcessId, out var previous) || previous.CpuTicks < 0 || previous.StartedAtTicks <= 0 || previous.StartedAtTicks != current.StartedAtTicks) continue;
                var deltaTicks = Math.Max(0, current.CpuTicks - previous.CpuTicks);
                var cpuSeconds = (double)deltaTicks / TimeSpan.TicksPerSecond;
                totalProcessCpuSeconds += cpuSeconds;
                var key = current.ProcessId.ToString(CultureInfo.InvariantCulture) + ":" + current.StartedAtTicks.ToString(CultureInfo.InvariantCulture);
                if (!aggregates.TryGetValue(key, out var aggregate))
                {
                    aggregate = new ResourceProcessAggregate { ProcessId = current.ProcessId, StartedAtTicks = current.StartedAtTicks, Name = current.Name };
                    aggregates[key] = aggregate;
                }
                aggregate.CpuSeconds += cpuSeconds;
                aggregate.WorkingSetBytes = current.WorkingSetBytes;
                aggregate.PrivateMemoryBytes = current.PrivateMemoryBytes;
            }
            var totalWorkingSet = currentProcesses.Values.Sum(item => Math.Max(0, item.WorkingSetBytes));
            var totalPrivateMemory = currentProcesses.Values.Sum(item => Math.Max(0, item.PrivateMemoryBytes));
            var memoryStatus = ReadSystemMemoryStatus();
            if (machineCpu.HasValue) cpuReadings.Add(machineCpu.Value);
            sampleCount++;
            samples.Add(new { elapsedSeconds = Math.Round(timer.Elapsed.TotalSeconds, 2), sampledAtUtc = DateTimeOffset.UtcNow, cpuPercent = machineCpu, memory = memoryStatus, processCount = currentProcesses.Count, processListTruncated, totalProcessWorkingSetBytes = totalWorkingSet, totalProcessPrivateMemoryBytes = totalPrivateMemory, measuredProcessCpuSeconds = Math.Round(totalProcessCpuSeconds, 3) });
            previousProcesses = currentProcesses;
            latestProcesses = currentProcesses;
            previousSystem = currentSystem;
            previousAt = currentAt;
            if (samples.Count >= 60) break;
        }
        var measuredSeconds = Math.Max(1, timer.Elapsed.TotalSeconds);
        var topProcesses = aggregates.Values.OrderByDescending(item => item.CpuSeconds).Take(20).Select(item => new
        {
            processId = item.ProcessId, name = item.Name, cpuSeconds = Math.Round(item.CpuSeconds, 3), averageMachineCpuPercent = Math.Round(item.CpuSeconds * 100d / (measuredSeconds * Math.Max(1, Environment.ProcessorCount)), 2), workingSetBytes = item.WorkingSetBytes, privateMemoryBytes = item.PrivateMemoryBytes, startedAtUtc = item.StartedAtTicks > 0 ? new DateTimeOffset(item.StartedAtTicks, TimeSpan.Zero) : (DateTimeOffset?)null
        }).ToArray();
        var topWorkingSetProcesses = latestProcesses.Values.OrderByDescending(item => item.WorkingSetBytes).Take(20).Select(item => new { processId = item.ProcessId, name = item.Name, workingSetBytes = item.WorkingSetBytes, privateMemoryBytes = item.PrivateMemoryBytes, startedAtUtc = item.StartedAtTicks > 0 ? new DateTimeOffset(item.StartedAtTicks, TimeSpan.Zero) : (DateTimeOffset?)null }).ToArray();
        var topPrivateMemoryProcesses = latestProcesses.Values.OrderByDescending(item => item.PrivateMemoryBytes).Take(20).Select(item => new { processId = item.ProcessId, name = item.Name, privateMemoryBytes = item.PrivateMemoryBytes, workingSetBytes = item.WorkingSetBytes, startedAtUtc = item.StartedAtTicks > 0 ? new DateTimeOffset(item.StartedAtTicks, TimeSpan.Zero) : (DateTimeOffset?)null }).ToArray();
        var averageCpuPercent = cpuReadings.Count > 0 ? Math.Round(cpuReadings.Average(), 2) : (double?)null;
        var peakCpuPercent = cpuReadings.Count > 0 ? Math.Round(cpuReadings.Max(), 2) : (double?)null;
        var finalMemory = ReadSystemMemoryStatus();
        var diskIo = ReadDiskIoTelemetry();
        return new { success = true, accepted = true, completed = true, verified = true, durationSeconds = Math.Round(timer.Elapsed.TotalSeconds, 2), requestedDurationSeconds = duration, intervalSeconds = interval, sampleCount, processorCount = Environment.ProcessorCount, averageCpuPercent, peakCpuPercent, memory = finalMemory, diskIo, samples, topProcesses, topWorkingSetProcesses, topPrivateMemoryProcesses, notes = new[] { "Sampling is finite and read-only; RunMind does not install a service or continue monitoring after this request.", "CPU and memory figures are Windows host measurements; process working-set/private-memory values are sampled per accessible process.", "Disk I/O telemetry depends on the Windows performance CIM provider and is reported unavailable when the provider is missing or inaccessible.", "Process CPU values are interval deltas and may omit processes that started or exited between samples; records are limited to the first 1,000 processes returned by Windows.", "Processes with inaccessible start times are excluded from CPU aggregation to avoid PID-reuse misattribution.", "Windows system CPU sampling is unavailable when GetSystemTimes cannot be read; such samples report null rather than zero." } };
    }

    private static object SystemManagerScheduledTasks(JsonElement args)
    {
        var action = ReadString(args, "action", 16).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(action)) action = "list";
        if (action == "list")
        {
            RequireSystemManagerCapability("scheduledTaskInformation");
            var query = ReadString(args, "query", 120);
            var maximum = args.TryGetProperty("max_results", out var maxValue) && maxValue.TryGetInt32(out var requestedMaximum) ? requestedMaximum : 100;
            if (maximum is < 1 or > 200) throw new InvalidDataException("Scheduled Task inventory is limited to 1–200 records.");
            const string inventoryScript = """
$tasks = @(Get-ScheduledTask -ErrorAction Stop | Select-Object -First 5000 | Where-Object { [string]::IsNullOrWhiteSpace([string]$request.query) -or $_.TaskName.IndexOf([string]$request.query,[StringComparison]::OrdinalIgnoreCase) -ge 0 -or $_.TaskPath.IndexOf([string]$request.query,[StringComparison]::OrdinalIgnoreCase) -ge 0 -or ([string]$_.Description).IndexOf([string]$request.query,[StringComparison]::OrdinalIgnoreCase) -ge 0 } | Select-Object -First ([int]$request.maxResults))
$rows = @($tasks | ForEach-Object {
  $info = $null; try { $info = Get-ScheduledTaskInfo -InputObject $_ -ErrorAction Stop } catch { }
  [pscustomobject]@{ taskName=$_.TaskName; taskPath=$_.TaskPath; state=[string]$_.State; enabled=[bool]$_.Settings.Enabled; hidden=[bool]$_.Settings.Hidden; description=([string]$_.Description).Substring(0,[Math]::Min(300,([string]$_.Description).Length)); actionExecutables=@($_.Actions | ForEach-Object { $_.Execute } | Select-Object -First 5); lastRunTime=if($info){$info.LastRunTime.ToUniversalTime().ToString('o')}else{$null}; nextRunTime=if($info -and $info.NextRunTime){$info.NextRunTime.ToUniversalTime().ToString('o')}else{$null}; lastTaskResult=if($info){$info.LastTaskResult}else{$null} }
})
$result = [pscustomobject]@{ tasks=$rows; count=$rows.Count; truncated=($rows.Count -ge [int]$request.maxResults); readOnly=$true }
[Console]::Out.WriteLine((ConvertTo-Json -InputObject $result -Depth 6 -Compress))
""";
            var report = RunPowerShellJson(inventoryScript, new { query, maxResults = maximum }, 30000);
            return new { success = true, accepted = true, completed = true, verified = true, inventory = report, note = "Task inventory is read-only. Action arguments are deliberately excluded because scheduled-task arguments may contain credentials or tokens." };
        }
        if (action is not ("enable" or "disable" or "start" or "stop")) throw new InvalidDataException("Scheduled Task action must be list, enable, disable, start, or stop.");
        RequireSystemManagerCapability("scheduledTaskManagement");
        var taskName = ReadBoundedExactString(args, "task_name", 260);
        var taskPath = ReadBoundedExactString(args, "task_path", 260);
        if (string.IsNullOrWhiteSpace(taskName) || taskName.Any(char.IsControl) || taskName.IndexOfAny(new[] { '*', '?', '[', ']', '/', '\\', ':', '<', '>', '|', '"' }) >= 0) throw new InvalidDataException("Provide an exact scheduled task name without wildcards, controls or path separators.");
        if (string.IsNullOrWhiteSpace(taskPath) || taskPath.Any(char.IsControl) || !taskPath.StartsWith('\\') || !taskPath.EndsWith('\\') || taskPath.Contains("..", StringComparison.Ordinal) || taskPath.Contains("\\\\", StringComparison.Ordinal) || taskPath.Contains('/') || taskPath.IndexOfAny(new[] { '*', '?', '[', ']', ':' }) >= 0) throw new InvalidDataException("Provide the exact task path returned by inventory, using leading/trailing single backslashes and no traversal or wildcards.");
        var detail = $"Task: {taskPath}{taskName}\nAction: {action}\n\nRunMind will address this exact Windows Scheduled Task. It will not create, delete, edit task definitions, or change task triggers.";
        if (!ConfirmSystemManagerAction("scheduledTaskManagement", "RunMind · Scheduled Task action", detail, alwaysAsk: true))
            return new { success = false, accepted = false, completed = false, verified = false, cancelled = true, action, taskName, taskPath, message = "The user declined the Scheduled Task action." };
        const string script = """
$taskMatches = @(Get-ScheduledTask -TaskName ([string]$request.taskName) -TaskPath ([string]$request.taskPath) -ErrorAction Stop)
if ($taskMatches.Count -ne 1) { throw 'The exact Scheduled Task identity was not unique.' }
$task = $taskMatches[0]
$before = Get-ScheduledTaskInfo -InputObject $task -ErrorAction SilentlyContinue
$beforeLastRun = if($before){$before.LastRunTime}else{[datetime]::MinValue}
switch ([string]$request.action) {
  'enable' { Enable-ScheduledTask -InputObject $task -ErrorAction Stop | Out-Null }
  'disable' { Disable-ScheduledTask -InputObject $task -ErrorAction Stop | Out-Null }
  'start' { Start-ScheduledTask -InputObject $task -ErrorAction Stop }
  'stop' { Stop-ScheduledTask -InputObject $task -ErrorAction Stop }
}
$verified = $false; $current = $null; $info = $null
for($attempt=0;$attempt -lt 10;$attempt++) {
  Start-Sleep -Milliseconds 500
  $current = Get-ScheduledTask -TaskName ([string]$request.taskName) -TaskPath ([string]$request.taskPath) -ErrorAction Stop
  $info = Get-ScheduledTaskInfo -InputObject $current -ErrorAction SilentlyContinue
  $state = [string]$current.State
  if ($request.action -eq 'enable' -and [bool]$current.Settings.Enabled) { $verified=$true; break }
  if ($request.action -eq 'disable' -and -not [bool]$current.Settings.Enabled) { $verified=$true; break }
  if ($request.action -eq 'start' -and ($state -eq 'Running' -or ($info -and $info.LastRunTime -gt $beforeLastRun))) { $verified=$true; break }
  if ($request.action -eq 'stop' -and $state -ne 'Running') { $verified=$true; break }
}
$result = [pscustomobject]@{ taskName=$current.TaskName; taskPath=$current.TaskPath; action=[string]$request.action; state=[string]$current.State; enabled=[bool]$current.Settings.Enabled; verified=$verified; lastRunTime=if($info){$info.LastRunTime.ToUniversalTime().ToString('o')}else{$null}; lastTaskResult=if($info){$info.LastTaskResult}else{$null} }
[Console]::Out.WriteLine((ConvertTo-Json -InputObject $result -Depth 5 -Compress))
""";
        var outcome = RunPowerShellJson(script, new { taskName, taskPath, action }, 15000);
        var verified = outcome.TryGetProperty("verified", out var verifiedValue) && verifiedValue.ValueKind == JsonValueKind.True;
        return new { success = verified, accepted = true, completed = true, verified, action, task = outcome, message = verified ? "The Scheduled Task action and resulting state were verified." : "Windows accepted the Scheduled Task action, but the requested state could not be verified." };
    }

    private static object SystemManagerStorageHealth()
    {
        RequireSystemManagerCapability("storageHealth");
        const string script = """
$warnings = @(); $physical = @(); $volumes = @(); $fallback = @()
try {
  $physical = @(Get-PhysicalDisk -ErrorAction Stop | ForEach-Object {
    $reliability = $null; try { $reliability = Get-StorageReliabilityCounter -PhysicalDisk $_ -ErrorAction Stop } catch { }
    [pscustomobject]@{ friendlyName=$_.FriendlyName; model=$_.Model; mediaType=[string]$_.MediaType; busType=[string]$_.BusType; sizeBytes=$_.Size; healthStatus=[string]$_.HealthStatus; operationalStatus=@($_.OperationalStatus | ForEach-Object { [string]$_ }); temperatureC=if($reliability){$reliability.Temperature}else{$null}; wearPercent=if($reliability){$reliability.Wear}else{$null}; powerOnHours=if($reliability){$reliability.PowerOnHours}else{$null}; readErrorsTotal=if($reliability){$reliability.ReadErrorsTotal}else{$null}; writeErrorsTotal=if($reliability){$reliability.WriteErrorsTotal}else{$null} }
  })
} catch { $warnings += ('Physical disk/health counters unavailable: ' + $_.Exception.Message) }
try { $volumes = @(Get-Volume -ErrorAction Stop | Where-Object { $_.DriveLetter -or $_.FileSystemLabel } | Select-Object -First 64 | ForEach-Object { [pscustomobject]@{ driveLetter=$_.DriveLetter; fileSystemLabel=$_.FileSystemLabel; fileSystem=[string]$_.FileSystem; sizeBytes=$_.Size; freeBytes=$_.SizeRemaining; healthStatus=[string]$_.HealthStatus; operationalStatus=@($_.OperationalStatus | ForEach-Object { [string]$_ }) } }) } catch { $warnings += ('Volume health unavailable: ' + $_.Exception.Message) }
if ($physical.Count -eq 0) { try { $fallback = @(Get-CimInstance Win32_DiskDrive -ErrorAction Stop | Select-Object -First 32 | ForEach-Object { [pscustomobject]@{ model=$_.Model; status=$_.Status; sizeBytes=$_.Size; interfaceType=$_.InterfaceType; mediaType=$_.MediaType } }) } catch { $warnings += ('Basic disk inventory unavailable: ' + $_.Exception.Message) } }
$result = [pscustomobject]@{ physicalDisks=$physical; volumes=$volumes; basicDiskFallback=$fallback; warnings=$warnings; reliabilityCountersAvailable=($physical.Count -gt 0 -and @($physical | Where-Object { $null -ne $_.temperatureC -or $null -ne $_.wearPercent -or $null -ne $_.powerOnHours }).Count -gt 0) }
[Console]::Out.WriteLine((ConvertTo-Json -InputObject $result -Depth 6 -Compress))
""";
        var report = RunPowerShellJson(script, new { }, 30000);
        return new { success = true, accepted = true, completed = true, verified = true, storage = report, note = "Storage health is best-effort and depends on controller/driver support. Missing SMART/reliability counters are reported as unavailable, never as healthy. This inspection does not run a surface scan or repair filesystem metadata." };
    }

    private static object SystemManagerSecurityPosture()
    {
        RequireSystemManagerCapability("securityPosture");
        const string script = """
$warnings = @(); $defender = $null; $firewall = @(); $bitlocker = $null; $secureBoot = $null; $tpm = $null; $uac = $null
try { $d = Get-MpComputerStatus -ErrorAction Stop; $defender = [pscustomobject]@{ available=$true; antivirusEnabled=$d.AntivirusEnabled; antispywareEnabled=$d.AntispywareEnabled; realTimeProtectionEnabled=$d.RealTimeProtectionEnabled; behaviorMonitorEnabled=$d.BehaviorMonitorEnabled; ioavProtectionEnabled=$d.IoavProtectionEnabled; networkInspectionEnabled=$d.NISEnabled; signatureLastUpdated=if($d.AntivirusSignatureLastUpdated){$d.AntivirusSignatureLastUpdated.ToUniversalTime().ToString('o')}else{$null}; quickScanAge=$d.QuickScanAge; fullScanAge=$d.FullScanAge } } catch { $defender = [pscustomobject]@{ available=$false; warning=$_.Exception.Message }; $warnings += 'Microsoft Defender status unavailable.' }
try { $firewall = @(Get-NetFirewallProfile -ErrorAction Stop | Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction | ForEach-Object { [pscustomobject]@{ name=$_.Name; enabled=$_.Enabled; defaultInboundAction=[string]$_.DefaultInboundAction; defaultOutboundAction=[string]$_.DefaultOutboundAction } }) } catch { $warnings += 'Windows Firewall profile status unavailable.' }
try { $b = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop; $bitlocker = [pscustomobject]@{ available=$true; mountPoint=$b.MountPoint; volumeStatus=[string]$b.VolumeStatus; protectionStatus=[string]$b.ProtectionStatus; encryptionPercentage=$b.EncryptionPercentage; lockStatus=[string]$b.LockStatus } } catch { $bitlocker = [pscustomobject]@{ available=$false; warning=$_.Exception.Message }; $warnings += 'BitLocker status unavailable.' }
try { $value = Confirm-SecureBootUEFI -ErrorAction Stop; $secureBoot = [pscustomobject]@{ available=$true; enabled=[bool]$value } } catch { $secureBoot = [pscustomobject]@{ available=$false; warning=$_.Exception.Message }; $warnings += 'Secure Boot could not be queried on this firmware/platform.' }
try { $t = Get-Tpm -ErrorAction Stop; $tpm = [pscustomobject]@{ available=$true; present=$t.TpmPresent; ready=$t.TpmReady; enabled=$t.TpmEnabled; activated=$t.TpmActivated; manufacturerId=$t.ManufacturerId; specVersion=$t.SpecVersion } } catch { $tpm = [pscustomobject]@{ available=$false; warning=$_.Exception.Message }; $warnings += 'TPM status unavailable.' }
try { $u = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction Stop; $uac = [pscustomobject]@{ available=$true; enabled=([int]$u.EnableLUA -eq 1); adminConsentPromptBehavior=$u.ConsentPromptBehaviorAdmin; secureDesktopPrompt=([int]$u.PromptOnSecureDesktop -eq 1) } } catch { $uac = [pscustomobject]@{ available=$false; warning=$_.Exception.Message }; $warnings += 'UAC configuration unavailable.' }
$result = [pscustomobject]@{ defender=$defender; firewallProfiles=$firewall; bitlockerSystemVolume=$bitlocker; secureBoot=$secureBoot; tpm=$tpm; userAccountControl=$uac; warnings=$warnings }
[Console]::Out.WriteLine((ConvertTo-Json -InputObject $result -Depth 6 -Compress))
""";
        var report = RunPowerShellJson(script, new { }, 30000);
        return new { success = true, accepted = true, completed = true, verified = true, posture = report, note = "Read-only security posture snapshot. It reports availability and configured state; it is not a compliance certification, malware scan or proof that all controls are enforced." };
    }

    private static async Task<object> SystemManagerTargetedNetworkDiagnosticsAsync(JsonElement args, CancellationToken cancellationToken)
    {
        RequireSystemManagerCapability("targetedNetworkDiagnostics");
        var host = ReadBoundedExactString(args, "host", 253).Trim();
        if (host.StartsWith('[') && host.EndsWith(']')) host = host[1..^1];
        if (string.IsNullOrWhiteSpace(host) || host.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) || host.Contains('[') || host.Contains(']') || host.Contains('/') || host.Contains('\\') || host.Contains('@') || Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new InvalidDataException("Provide one hostname or IP address only (no URL, path, credentials or whitespace).");
        var hasPort = args.TryGetProperty("port", out var portValue) && portValue.ValueKind != JsonValueKind.Null;
        var port = 0;
        if (hasPort && (!portValue.TryGetInt32(out port) || port is < 1 or > 65535)) throw new InvalidDataException("TCP probe port must be between 1 and 65535.");
        var timeout = args.TryGetProperty("timeout_seconds", out var timeoutValue) && timeoutValue.TryGetInt32(out var requestedTimeout) ? requestedTimeout : 5;
        if (timeout is < 1 or > 10) throw new InvalidDataException("Network diagnostic timeout must be between 1 and 10 seconds.");
        if (hasPort && !ConfirmSystemManagerAction("targetedNetworkDiagnostics", "RunMind · Targeted network probe", $"Target: {host}:{port}\nTimeout: {timeout} seconds\n\nRunMind will attempt one TCP connection to this endpoint. This sends traffic to the selected host and does not change its configuration.", alwaysAsk: true))
            return new { success = false, accepted = false, completed = false, verified = false, cancelled = true, host, port, message = "The user declined the TCP probe." };
        var dnsTimer = Stopwatch.StartNew();
        IPAddress[] addresses;
        try
        {
            using var dnsTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            dnsTimeout.CancelAfter(TimeSpan.FromSeconds(timeout));
            if (IPAddress.TryParse(host, out var parsedAddress)) addresses = new[] { parsedAddress };
            else addresses = await Dns.GetHostAddressesAsync(host, dnsTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new { success = false, accepted = true, completed = true, verified = false, host, dnsResolved = false, dnsElapsedMilliseconds = Math.Round(dnsTimer.Elapsed.TotalMilliseconds, 1), dnsError = "timeout", tcpProbe = (object?)null, message = "DNS resolution exceeded the configured timeout." };
        }
        catch (Exception error) when (error is SocketException or ArgumentException)
        {
            return new { success = false, accepted = true, completed = true, verified = true, host, dnsResolved = false, dnsElapsedMilliseconds = Math.Round(dnsTimer.Elapsed.TotalMilliseconds, 1), dnsError = Limit(error.Message, 300), tcpProbe = (object?)null, message = "DNS resolution failed; no TCP connection was attempted." };
        }
        var dnsElapsed = Math.Round(dnsTimer.Elapsed.TotalMilliseconds, 1);
        object? route = null;
        string? routeWarning = null;
        try
        {
            const string routeScript = """
$found = @(Find-NetRoute -RemoteIPAddress ([string]$request.remoteAddress) -ErrorAction Stop)
$local = $found | Where-Object { $_.PSObject.Properties['IPAddress'] -and $_.PSObject.Properties['PrefixLength'] } | Select-Object -First 1
$selected = $found | Where-Object { $_.PSObject.Properties['DestinationPrefix'] -and $_.PSObject.Properties['NextHop'] } | Select-Object -First 1
$result = [pscustomobject]@{ available=($null -ne $selected); remoteAddress=[string]$request.remoteAddress; localAddress=if($local){$local.IPAddress}else{$null}; interfaceAlias=if($selected){$selected.InterfaceAlias}else{if($local){$local.InterfaceAlias}else{$null}}; interfaceIndex=if($selected){$selected.InterfaceIndex}else{if($local){$local.InterfaceIndex}else{$null}}; destinationPrefix=if($selected){$selected.DestinationPrefix}else{$null}; nextHop=if($selected){$selected.NextHop}else{$null}; routeMetric=if($selected){$selected.RouteMetric}else{$null}; warning=if($null -eq $selected){'No selected route was returned.'}else{$null} }
[Console]::Out.WriteLine((ConvertTo-Json -InputObject $result -Depth 4 -Compress))
""";
            route = RunPowerShellJson(routeScript, new { remoteAddress = addresses[0].ToString() }, 10000);
        }
        catch (Exception error) { routeWarning = Limit(error.Message, 300); }
        object? tcpProbe = null;
        var tcpConnected = !hasPort;
        if (hasPort)
        {
            var tcpTimer = Stopwatch.StartNew();
            Exception? failure = addresses.Length == 0 ? new InvalidOperationException("DNS returned no usable IP addresses.") : null;
            var attemptedAddresses = new List<string>();
            foreach (var address in addresses.Take(8))
            {
                var remainingSeconds = timeout - tcpTimer.Elapsed.TotalSeconds;
                if (remainingSeconds <= 0) { failure = new TimeoutException("TCP connection probe exceeded the total configured timeout."); tcpConnected = false; break; }
                attemptedAddresses.Add(address.ToString());
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(remainingSeconds));
                try
                {
                    using var client = new TcpClient(address.AddressFamily);
                    await client.ConnectAsync(address, port, connectTimeout.Token).ConfigureAwait(false);
                    tcpConnected = true;
                    failure = null;
                    break;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { failure = new TimeoutException("TCP connection timed out."); tcpConnected = false; }
                catch (Exception error) when (error is SocketException or IOException or ArgumentException) { failure = error; tcpConnected = false; }
                if (tcpConnected) break;
            }
            tcpProbe = new { host, port, connected = tcpConnected, attemptedAddresses, resolvedAddressCount = addresses.Length, addressAttemptLimit = 8, elapsedMilliseconds = Math.Round(tcpTimer.Elapsed.TotalMilliseconds, 1), error = failure is null ? null : Limit(failure.Message, 300), socketError = failure is SocketException socketError ? socketError.SocketErrorCode.ToString() : null };
        }
        return new { success = !hasPort || tcpConnected, accepted = true, completed = true, verified = true, host, dnsResolved = addresses.Length > 0, dnsElapsedMilliseconds = dnsElapsed, resolvedAddresses = addresses.Select(address => address.ToString()).Distinct().Take(16).ToArray(), route, routeWarning, tcpProbe, message = !hasPort ? "DNS resolution completed; route selection is included when Windows exposes it." : tcpConnected ? "DNS resolution and TCP connection succeeded." : "DNS resolution succeeded, but the TCP connection did not." };
    }

    private sealed record NetworkEndpoint(string Local, string? Remote, string State, int ProcessId, string? ProcessName, string? ExecutablePath, DateTime? StartedAtUtc);

    private static bool TryParseLocalEndpoint(string endpoint, out string address, out int port)
    {
        address = string.Empty;
        port = 0;
        if (string.IsNullOrWhiteSpace(endpoint)) return false;
        var value = endpoint.Trim();
        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            var close = value.IndexOf(']');
            if (close <= 1 || close + 2 >= value.Length || value[close + 1] != ':') return false;
            address = value[1..close];
            return int.TryParse(value[(close + 2)..], NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;
        }
        var separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1) return false;
        address = value[..separator];
        return int.TryParse(value[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;
    }

    private static object[] FindPotentialTcpListenerConflicts(IEnumerable<NetworkEndpoint> listeners)
    {
        var parsed = listeners.Select(endpoint =>
        {
            var valid = TryParseLocalEndpoint(endpoint.Local, out var address, out var port);
            return new { endpoint, valid, address, port };
        }).Where(item => item.valid).GroupBy(item => item.port);
        var conflicts = new List<object>();
        foreach (var group in parsed)
        {
            var rows = group.ToArray();
            var overlaps = new List<object>();
            for (var left = 0; left < rows.Length; left++)
            for (var right = left + 1; right < rows.Length; right++)
            {
                var a = rows[left]; var b = rows[right];
                if (a.endpoint.ProcessId == b.endpoint.ProcessId) continue;
                var sameAddress = a.address.Equals(b.address, StringComparison.OrdinalIgnoreCase);
                var aWildcard = a.address is "0.0.0.0" or "::" or "*";
                var bWildcard = b.address is "0.0.0.0" or "::" or "*";
                var sameFamily = a.address.Contains(':') == b.address.Contains(':');
                var dualStackWildcardOverlap = a.address == "::" && b.address == "0.0.0.0" || b.address == "::" && a.address == "0.0.0.0";
                var wildcardOverlap = aWildcard && (a.address == "*" || sameFamily) || bWildcard && (b.address == "*" || sameFamily) || dualStackWildcardOverlap;
                if (!sameAddress && !wildcardOverlap) continue;
                overlaps.Add(new { first = new { local = a.endpoint.Local, processId = a.endpoint.ProcessId, processName = a.endpoint.ProcessName }, second = new { local = b.endpoint.Local, processId = b.endpoint.ProcessId, processName = b.endpoint.ProcessName } });
            }
            if (overlaps.Count > 0)
                conflicts.Add(new { protocol = "tcp", port = group.Key, classification = "possible-listener-conflict", endpointPairs = overlaps.Take(20).ToArray(), note = "Overlapping listener endpoints owned by different processes can indicate a bind conflict. Socket reuse and Windows dual-stack behavior can allow legitimate coexistence; verify the actual bind result and application logs." });
            if (conflicts.Count >= 50) break;
        }
        return conflicts.ToArray();
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
            const string hardwareScript = "$ErrorActionPreference='Stop'; $classes='Win32_Processor','Win32_PhysicalMemory','Win32_VideoController','Win32_DiskDrive'; $items=@(); foreach($class in $classes){ Get-CimInstance -ClassName $class -ErrorAction SilentlyContinue | ForEach-Object { $items += ($_ | Select-Object @{n='class';e={$class}},Name,Manufacturer,Model,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,Capacity,Speed,ConfiguredClockSpeed,SMBIOSMemoryType,AdapterRAM,DriverVersion,Size | ConvertTo-Json -Compress -Depth 2 | ConvertFrom-Json) } }; ConvertTo-Json -InputObject $items -Compress -Depth 3";
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
            var activeConnections = new List<NetworkEndpoint>();
            var listeners = new List<NetworkEndpoint>();
            var includeExecutableIdentity = (bool?)capabilities?["inspectProcesses"] == true;
            var tcpRowsProcessed = 0;
            if (netstat.ExitCode == 0)
            {
                foreach (var line in netstat.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (++tcpRowsProcessed > 500) break;
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5 || !parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
                    var local = parts[1]; var remote = parts[2]; var state = parts[^2];
                    string? processName = null; string? executablePath = null; DateTime? startedAtUtc = null;
                    try { using var owner = Process.GetProcessById(pid); processName = owner.ProcessName; try { startedAtUtc = owner.StartTime.ToUniversalTime(); } catch { } if (includeExecutableIdentity) { try { executablePath = owner.MainModule?.FileName; } catch { } } } catch { }
                    var endpoint = new NetworkEndpoint(local, remote, state, pid, processName, executablePath, startedAtUtc);
                    if (state.Equals("LISTENING", StringComparison.OrdinalIgnoreCase))
                    {
                        if (listeners.Count < 200) listeners.Add(endpoint);
                    }
                    else if (activeConnections.Count < 200) activeConnections.Add(endpoint);
                }
            }
            var udpNetstat = RunFixedDiagnosticCommand(netstatPath, new[] { "-ano", "-p", "udp" });
            var udpEndpoints = new List<NetworkEndpoint>();
            if (udpNetstat.ExitCode == 0)
            {
                foreach (var line in udpNetstat.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4 || !parts[0].Equals("UDP", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)) continue;
                    string? processName = null; string? executablePath = null; DateTime? startedAtUtc = null;
                    try { using var owner = Process.GetProcessById(pid); processName = owner.ProcessName; try { startedAtUtc = owner.StartTime.ToUniversalTime(); } catch { } if (includeExecutableIdentity) { try { executablePath = owner.MainModule?.FileName; } catch { } } } catch { }
                    udpEndpoints.Add(new NetworkEndpoint(parts[1], parts.Length >= 4 ? parts[2] : null, "BOUND", pid, processName, executablePath, startedAtUtc));
                    if (udpEndpoints.Count >= 200) break;
                }
            }
            var tcpPortConflicts = FindPotentialTcpListenerConflicts(listeners);
            var udpListeners = udpEndpoints.Where(item => string.IsNullOrWhiteSpace(item.Remote) || item.Remote == "*:*" || item.Remote == "0.0.0.0:0")
                .Select(item => new { local = item.Local, processId = item.ProcessId, processName = item.ProcessName, executablePath = item.ExecutablePath, startedAtUtc = item.StartedAtUtc }).ToArray();
            data["network"] = new { hostName = properties.HostName, domainName = properties.DomainName, adapters,
                activeTcpConnections = activeConnections, activeTcpConnectionCount = activeConnections.Count, activeTcpConnectionSource = netstat.ExitCode == 0 ? "netstat -ano" : "unavailable",
                tcpListeners = listeners.Select(item => new { local = item.Local, processId = item.ProcessId, processName = item.ProcessName, executablePath = item.ExecutablePath, startedAtUtc = item.StartedAtUtc }).ToArray(), tcpListenerCount = listeners.Count,
                tcpListenerSource = netstat.ExitCode == 0 ? "netstat -ano" : "unavailable", possibleTcpListenerConflicts = tcpPortConflicts,
                udpEndpoints, udpEndpointCount = udpEndpoints.Count, udpEndpointSource = udpNetstat.ExitCode == 0 ? "netstat -ano" : "unavailable",
                udpListeners, udpListenerCount = udpListeners.Length, udpListenerSource = udpNetstat.ExitCode == 0 ? "netstat -ano" : "unavailable", processExecutableIdentityEnabled = includeExecutableIdentity };
        }
        return new { diagnostics = data, note = "Read-only Windows and process diagnostics. Service and network details are included only when their individual capabilities are enabled." };
    }

    private sealed record HealthEvidenceEvent(string Source, string LogName, int EventId, string Provider, string TimeCreatedUtc, string? Subject, string Message, long? RecordId);

    private static bool TryGetJsonString(JsonElement value, string property, out string text)
    {
        text = string.Empty;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out var item) || item.ValueKind != JsonValueKind.String) return false;
        text = item.GetString() ?? string.Empty;
        return true;
    }

    private static int TryGetJsonInt(JsonElement value, string property)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item) && item.TryGetInt32(out var result) ? result : 0;

    private static double? TryGetJsonDouble(JsonElement value, string property)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var result) ? result : null;

    private static JsonElement GetInvestigationRoot(JsonElement result)
        => result.ValueKind == JsonValueKind.Object && result.TryGetProperty("investigation", out var investigation) ? investigation : result;

    private static void AppendHealthEvidence(List<HealthEvidenceEvent> events, HashSet<string> seen, JsonElement result, string source)
    {
        var investigation = GetInvestigationRoot(result);
        if (investigation.ValueKind != JsonValueKind.Object || !investigation.TryGetProperty("events", out var rows) || rows.ValueKind != JsonValueKind.Array) return;
        foreach (var row in rows.EnumerateArray().Take(200))
        {
            var logName = TryGetJsonString(row, "logName", out var log) ? log : string.Empty;
            var eventId = TryGetJsonInt(row, "eventId");
            var provider = TryGetJsonString(row, "provider", out var providerValue) ? providerValue : string.Empty;
            var timestamp = TryGetJsonString(row, "timeCreatedUtc", out var time) ? time : string.Empty;
            var message = TryGetJsonString(row, "message", out var messageValue) ? Limit(messageValue, 1400) : string.Empty;
            var subject = TryGetJsonString(row, "serviceName", out var serviceName) && serviceName.Length > 0 ? serviceName : (TryGetJsonString(row, "application", out var application) && application.Length > 0 ? application : null);
            if (subject is null && provider.Contains("Service Control Manager", StringComparison.OrdinalIgnoreCase))
            {
                var failedDependency = System.Text.RegularExpressions.Regex.Match(message, @"(?im)^The .+? service depends on the (.+?) service which failed to start");
                var serviceMatch = failedDependency.Success ? failedDependency : System.Text.RegularExpressions.Regex.Match(message, @"(?im)^(?:The )?(.+?) service (?:terminated|failed|did not|was unable|cannot|could not)");
                if (serviceMatch.Success) subject = serviceMatch.Groups[1].Value.Trim();
            }
            if (subject is null && logName.Equals("Application", StringComparison.OrdinalIgnoreCase))
            {
                var applicationMatch = System.Text.RegularExpressions.Regex.Match(message, @"(?im)(?:Faulting application name|Application name|Application):\s*([^,\r\n]+)");
                if (applicationMatch.Success) subject = applicationMatch.Groups[1].Value.Trim();
            }
            long? recordId = row.ValueKind == JsonValueKind.Object && row.TryGetProperty("recordId", out var record) && record.TryGetInt64(out var id) ? id : null;
            var identity = logName + "|" + eventId.ToString(CultureInfo.InvariantCulture) + "|" + (recordId?.ToString(CultureInfo.InvariantCulture) ?? timestamp + "|" + provider);
            if (!seen.Add(identity)) continue;
            events.Add(new HealthEvidenceEvent(source, logName, eventId, provider, timestamp, subject, message, recordId));
        }
    }

    private static double? ReadJsonNodeNumber(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        try { return value.GetValue<double>(); } catch { try { return value.GetValue<long>(); } catch { return null; } }
    }

    private static JsonArray ReadHealthHistory(out string? warning)
    {
        warning = null;
        Directory.CreateDirectory(SystemManagerRoot);
        EnsureNoReparseAncestors(SystemManagerRoot);
        if (!File.Exists(SystemManagerHealthHistoryPath)) return new JsonArray();
        if (IsReparsePoint(SystemManagerHealthHistoryPath)) throw new InvalidDataException("Health history cannot be read through a symbolic link or reparse point.");
        var info = new FileInfo(SystemManagerHealthHistoryPath);
        if (info.Length > 512 * 1024) throw new InvalidDataException("Health history exceeded the 512 KB safety limit and was not loaded.");
        var root = JsonNode.Parse(File.ReadAllText(SystemManagerHealthHistoryPath, Encoding.UTF8)) as JsonObject;
        if (root is null || (int?)root["formatVersion"] != 1 || root["snapshots"] is not JsonArray snapshots)
            throw new InvalidDataException("Health history has an unsupported or invalid format; the existing file was preserved.");
        return (JsonArray)snapshots.DeepClone();
    }

    private static void WriteHealthHistory(JsonArray snapshots)
    {
        Directory.CreateDirectory(SystemManagerRoot);
        EnsureNoReparseAncestors(SystemManagerRoot);
        var temporary = SystemManagerHealthHistoryPath + ".tmp";
        if ((File.Exists(SystemManagerHealthHistoryPath) && IsReparsePoint(SystemManagerHealthHistoryPath)) || (File.Exists(temporary) && IsReparsePoint(temporary)))
            throw new InvalidDataException("Health history cannot be written through a symbolic link or reparse point.");
        string content;
        do
        {
            content = new JsonObject { ["formatVersion"] = 1, ["snapshots"] = snapshots.DeepClone() }.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            if (Encoding.UTF8.GetByteCount(content) <= 512 * 1024) break;
            if (snapshots.Count <= 1) throw new InvalidDataException("A single health snapshot exceeds the 512 KB safety limit and was not saved.");
            snapshots.RemoveAt(0);
        } while (true);
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, SystemManagerHealthHistoryPath, true);
    }

    private static JsonObject BuildHealthSnapshot(DateTimeOffset timestamp, string status, double? cpuPercent, double? memoryPercent, double? diskQueue, int serviceFailureCount, int crashCount, int networkFailureCount, int issueCount, Dictionary<string, string> serviceStates, IEnumerable<string> issueKeys)
    {
        var services = new JsonObject();
        foreach (var pair in serviceStates.Take(250)) services[pair.Key] = pair.Value;
        var issues = new JsonArray();
        foreach (var issue in issueKeys.Distinct(StringComparer.Ordinal).Take(50)) issues.Add(issue);
        return new JsonObject
        {
            ["timestampUtc"] = timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["status"] = status,
            ["metrics"] = new JsonObject
            {
                ["cpuPercent"] = cpuPercent is null ? null : JsonValue.Create(cpuPercent.Value),
                ["memoryPercent"] = memoryPercent is null ? null : JsonValue.Create(memoryPercent.Value),
                ["diskQueue"] = diskQueue is null ? null : JsonValue.Create(diskQueue.Value)
            },
            ["counts"] = new JsonObject
            {
                ["serviceFailureEvents"] = serviceFailureCount,
                ["applicationCrashEvents"] = crashCount,
                ["networkFailureEvents"] = networkFailureCount,
                ["issues"] = issueCount
            },
            ["serviceStates"] = services,
            ["issueKeys"] = issues
        };
    }

    private static object BuildHealthComparison(JsonObject? previous, JsonObject current)
    {
        if (previous is null) return new { available = false, reason = "No earlier health snapshot is available yet." };
        var previousMetrics = previous["metrics"] as JsonObject;
        var currentMetrics = current["metrics"] as JsonObject;
        double? Delta(string key)
        {
            var before = ReadJsonNodeNumber(previousMetrics?[key]);
            var after = ReadJsonNodeNumber(currentMetrics?[key]);
            return before is null || after is null ? null : Math.Round(after.Value - before.Value, 2);
        }
        int CountDelta(string key)
        {
            var before = (int?)previous["counts"]?[key] ?? 0;
            var after = (int?)current["counts"]?[key] ?? 0;
            return after - before;
        }
        var changedServices = new List<object>();
        var oldStates = previous["serviceStates"] as JsonObject;
        var newStates = current["serviceStates"] as JsonObject;
        if (oldStates is not null && newStates is not null)
        {
            foreach (var pair in newStates)
            {
                var before = (string?)oldStates[pair.Key];
                var after = (string?)pair.Value;
                if (before is not null && after is not null && !before.Equals(after, StringComparison.OrdinalIgnoreCase))
                    changedServices.Add(new { service = pair.Key, previousState = before, currentState = after });
            }
        }
        var previousTime = (string?)previous["timestampUtc"];
        return new
        {
            available = true,
            previousSnapshotUtc = previousTime,
            currentSnapshotUtc = (string?)current["timestampUtc"],
            metricDelta = new { cpuPercentPoints = Delta("cpuPercent"), memoryPercentPoints = Delta("memoryPercent"), diskQueue = Delta("diskQueue") },
            countDelta = new { serviceFailureEvents = CountDelta("serviceFailureEvents"), applicationCrashEvents = CountDelta("applicationCrashEvents"), networkFailureEvents = CountDelta("networkFailureEvents"), issues = CountDelta("issues") },
            changedServiceStates = changedServices.Take(50).ToArray(),
            note = "Changes compare two on-demand snapshots. They do not establish that a change caused another event or that the entire interval was continuously monitored."
        };
    }

    private static async Task<object> SystemManagerHealthReportAsync(JsonElement args, CancellationToken cancellationToken)
    {
        var settings = ReadSystemManagerSettings();
        if (!(bool?)settings["enabled"] ?? false) throw new UnauthorizedAccessException("Windows System Manager is disabled in the System Manager menu.");
        if (!(bool?)settings["groups"]?["diagnostics"] ?? false) throw new UnauthorizedAccessException("System Diagnostics is disabled.");
        var capabilities = settings["capabilities"];
        bool Enabled(string name) => (bool?)capabilities?[name] == true;
        var canCollect = new[] { "systemInformation", "serviceInformation", "networkInformation", "eventLogInformation", "crashInvestigation", "resourceMonitoring", "storageHealth" }.Any(Enabled);
        if (!canCollect) throw new UnauthorizedAccessException("Enable at least one System Manager diagnostic capability before requesting a health report.");

        var now = DateTimeOffset.UtcNow;
        var warnings = new List<object>();
        JsonElement diagnostics = default;
        JsonElement? crashInvestigation = null;
        JsonElement? applicationEvents = null;
        JsonElement? systemEvents = null;
        JsonElement? resourceSampling = null;
        JsonElement? storageHealth = null;
        var diagnosticCapabilities = new[] { "systemInformation", "serviceInformation", "networkInformation" };
        if (diagnosticCapabilities.Any(Enabled))
        {
            try
            {
                diagnostics = JsonSerializer.SerializeToElement(SystemManagerDiagnostics());
                if (diagnostics.TryGetProperty("diagnostics", out var data)) diagnostics = data.Clone();
            }
            catch (Exception error) { warnings.Add(new { category = "system-inventory", message = Limit(error.Message, 300) }); }
        }

        var days = args.TryGetProperty("days", out var daysValue) && daysValue.TryGetInt32(out var requestedDays) ? requestedDays : 7;
        if (days is < 1 or > 30) throw new InvalidDataException("Health report history window must be between 1 and 30 days.");
        if (Enabled("crashInvestigation"))
        {
            try { crashInvestigation = JsonSerializer.SerializeToElement(SystemManagerInvestigateCrashes(JsonSerializer.SerializeToElement(new { days, max_events = 200 }))); }
            catch (Exception error) { warnings.Add(new { category = "crash-and-service-failures", message = Limit(error.Message, 300) }); }
        }
        if (Enabled("eventLogInformation"))
        {
            foreach (var logName in new[] { "Application", "System" })
            {
                try
                {
                    var logResult = JsonSerializer.SerializeToElement(SystemManagerReadEventLog(JsonSerializer.SerializeToElement(new { log_name = logName, hours = Math.Min(720, days * 24), max_events = 150, event_id = 0, query = string.Empty })));
                    if (logName == "Application") applicationEvents = logResult; else systemEvents = logResult;
                }
                catch (Exception error) { warnings.Add(new { category = logName + "-event-log", message = Limit(error.Message, 300) }); }
            }
        }
        if (Enabled("resourceMonitoring"))
        {
            var duration = args.TryGetProperty("duration_seconds", out var durationValue) && durationValue.TryGetInt32(out var requestedDuration) ? requestedDuration : 3;
            if (duration is < 1 or > 15) throw new InvalidDataException("Health report resource sampling must be between 1 and 15 seconds.");
            try { resourceSampling = JsonSerializer.SerializeToElement(await SystemManagerSampleResourcesAsync(JsonSerializer.SerializeToElement(new { duration_seconds = duration, interval_seconds = 1 }), cancellationToken).ConfigureAwait(false)); }
            catch (Exception error) { warnings.Add(new { category = "resource-sampling", message = Limit(error.Message, 300) }); }
        }
        if (Enabled("storageHealth"))
        {
            try { storageHealth = JsonSerializer.SerializeToElement(SystemManagerStorageHealth()); }
            catch (Exception error) { warnings.Add(new { category = "storage-health", message = Limit(error.Message, 300) }); }
        }

        var allEvents = new List<HealthEvidenceEvent>();
        var seenEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (crashInvestigation.HasValue) AppendHealthEvidence(allEvents, seenEvents, crashInvestigation.Value, "crash-investigation");
        if (applicationEvents.HasValue) AppendHealthEvidence(allEvents, seenEvents, applicationEvents.Value, "application-event-log");
        if (systemEvents.HasValue) AppendHealthEvidence(allEvents, seenEvents, systemEvents.Value, "system-event-log");
        var serviceFailureIds = new HashSet<int> { 7000, 7001, 7023, 7024, 7031, 7034, 7038, 7043 };
        var networkFailureIds = new HashSet<int> { 1014, 4227, 4231 };
        var serviceFailures = allEvents.Where(item => serviceFailureIds.Contains(item.EventId) || (item.Provider.Contains("Service Control Manager", StringComparison.OrdinalIgnoreCase) && item.Subject is not null)).ToArray();
        var crashEvents = allEvents.Where(item => item.LogName.Equals("Application", StringComparison.OrdinalIgnoreCase) && (item.EventId is 1000 or 1001 or 1026)).ToArray();
        var networkFailures = allEvents.Where(item => networkFailureIds.Contains(item.EventId) || item.Provider.Contains("DNS Client", StringComparison.OrdinalIgnoreCase)).ToArray();
        var recurringServiceFailures = serviceFailures.Where(item => !string.IsNullOrWhiteSpace(item.Subject))
            .GroupBy(item => item.Subject!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { serviceName = group.Key, occurrences = group.Count(), eventIds = group.Select(item => item.EventId).Distinct().OrderBy(value => value).ToArray(), firstSeenUtc = group.Select(item => item.TimeCreatedUtc).OrderBy(value => value, StringComparer.Ordinal).FirstOrDefault(), lastSeenUtc = group.Select(item => item.TimeCreatedUtc).OrderByDescending(value => value, StringComparer.Ordinal).FirstOrDefault() })
            .OrderByDescending(item => item.occurrences).Take(50).ToArray();
        string CrashSignature(HealthEvidenceEvent item)
        {
            var exception = System.Text.RegularExpressions.Regex.Match(item.Message, @"(?i)exception code:\s*(0x[0-9a-f]+)").Groups[1].Value;
            var module = System.Text.RegularExpressions.Regex.Match(item.Message, @"(?im)(?:Faulting module name|Faulting module):\s*([^,\r\n]+)").Groups[1].Value.Trim();
            return string.Join("|", item.Subject ?? "unknown-application", exception, module);
        }
        var recurringApplicationCrashes = crashEvents.Select(item => new { item, signature = CrashSignature(item) })
            .Where(item => !item.signature.Equals("unknown-application||", StringComparison.OrdinalIgnoreCase)
                && !item.signature.EndsWith("||", StringComparison.Ordinal))
            .GroupBy(item => item.signature, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { signature = group.Key, application = group.Select(item => item.item.Subject).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)), occurrences = group.Count(), eventIds = group.Select(item => item.item.EventId).Distinct().OrderBy(value => value).ToArray(), firstSeenUtc = group.Select(item => item.item.TimeCreatedUtc).OrderBy(value => value, StringComparer.Ordinal).FirstOrDefault(), lastSeenUtc = group.Select(item => item.item.TimeCreatedUtc).OrderByDescending(value => value, StringComparer.Ordinal).FirstOrDefault() })
            .OrderByDescending(item => item.occurrences).Take(50).ToArray();
        var recurringNetworkFailures = networkFailures.GroupBy(item => item.Provider + "|" + item.EventId.ToString(CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase)
            .Select(group => new { provider = group.First().Provider, eventId = group.First().EventId, occurrences = group.Count(), firstSeenUtc = group.Select(item => item.TimeCreatedUtc).OrderBy(value => value, StringComparer.Ordinal).FirstOrDefault(), lastSeenUtc = group.Select(item => item.TimeCreatedUtc).OrderByDescending(value => value, StringComparer.Ordinal).FirstOrDefault() })
            .OrderByDescending(item => item.occurrences).Take(50).ToArray();

        double? averageCpu = null;
        double? peakCpu = null;
        double? memoryPercent = null;
        double? diskQueue = null;
        if (resourceSampling is JsonElement resource)
        {
            averageCpu = TryGetJsonDouble(resource, "averageCpuPercent");
            peakCpu = TryGetJsonDouble(resource, "peakCpuPercent");
            if (resource.TryGetProperty("memory", out var memory) && memory.ValueKind == JsonValueKind.Object) memoryPercent = TryGetJsonDouble(memory, "memoryLoadPercent");
            if (resource.TryGetProperty("diskIo", out var disk) && disk.ValueKind == JsonValueKind.Object && disk.TryGetProperty("disks", out var diskRows) && diskRows.ValueKind == JsonValueKind.Array)
            {
                var queueValues = diskRows.EnumerateArray().Select(row => TryGetJsonDouble(row, "averageQueueLength")).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
                if (queueValues.Length > 0) diskQueue = queueValues.Max();
            }
        }

        var drives = diagnostics.ValueKind == JsonValueKind.Object && diagnostics.TryGetProperty("drives", out var driveRows) && driveRows.ValueKind == JsonValueKind.Array ? driveRows : default;
        double? minimumFreeDrivePercent = null;
        if (drives.ValueKind == JsonValueKind.Array)
        {
            var freeRatios = drives.EnumerateArray().Select(drive =>
            {
                var total = drive.TryGetProperty("totalBytes", out var totalValue) && totalValue.TryGetDouble(out var totalBytes) ? totalBytes : 0;
                var available = drive.TryGetProperty("availableBytes", out var freeValue) && freeValue.TryGetDouble(out var freeBytes) ? freeBytes : 0;
                return total > 0 ? (double?)(available * 100d / total) : null;
            }).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
            if (freeRatios.Length > 0) minimumFreeDrivePercent = Math.Round(freeRatios.Min(), 2);
        }

        string ProcessHotspotEvidence(string property, string metric, string unit)
        {
            if (!resourceSampling.HasValue || resourceSampling.Value.ValueKind != JsonValueKind.Object ||
                !resourceSampling.Value.TryGetProperty(property, out var rows) || rows.ValueKind != JsonValueKind.Array)
                return "Process attribution was unavailable for this sample.";
            var entries = new List<string>();
            foreach (var row in rows.EnumerateArray().Take(3))
            {
                var name = TryGetJsonString(row, "name", out var processName) ? processName : "unknown process";
                var processId = TryGetJsonInt(row, "processId");
                var amount = TryGetJsonDouble(row, metric);
                if (!amount.HasValue) continue;
                var formatted = unit == "bytes"
                    ? $"{amount.Value / (1024d * 1024d):0.0} MB"
                    : $"{amount.Value:0.0} s";
                entries.Add($"{name} (PID {processId}, {formatted})");
            }
            return entries.Count > 0 ? "Top sampled processes: " + string.Join(", ", entries) + "." : "No process hotspot was recorded in the sample.";
        }
        var cpuProcessEvidence = ProcessHotspotEvidence("topProcesses", "cpuSeconds", "seconds");
        var memoryProcessEvidence = ProcessHotspotEvidence("topWorkingSetProcesses", "workingSetBytes", "bytes");
        var privateMemoryProcessEvidence = ProcessHotspotEvidence("topPrivateMemoryProcesses", "privateMemoryBytes", "bytes");
        var issues = new List<object>();
        var issueKeys = new List<string>();
        void AddIssue(string severity, string code, string title, string evidence)
        {
            issues.Add(new { severity, code, title, evidence });
            issueKeys.Add(code);
        }
        var repeatedServices = recurringServiceFailures.Where(item => item.occurrences >= 3).ToArray();
        var repeatedApplications = recurringApplicationCrashes.Where(item => item.occurrences >= 3).ToArray();
        var repeatedNetworkPatterns = recurringNetworkFailures.Where(item => item.occurrences >= 3).ToArray();
        if (repeatedServices.Length > 0) AddIssue("high", "repeated-service-failures", "Repeated Windows service failures", string.Join("; ", repeatedServices.Take(5).Select(item => $"{item.serviceName}: {item.occurrences} events")));
        else if (serviceFailures.Length > 0) AddIssue("warning", "service-failures", "Windows service failure events", $"{serviceFailures.Length} service failure event(s) were observed; inspect the service name and event details.");
        if (repeatedApplications.Length > 0) AddIssue("high", "repeated-application-crashes", "Repeated application crash signatures", string.Join("; ", repeatedApplications.Take(5).Select(item => $"{item.application ?? item.signature}: {item.occurrences} events")));
        else if (crashEvents.Length > 0) AddIssue("warning", "application-crashes", "Application crash events", $"{crashEvents.Length} application crash/report event(s) were observed.");
        if (repeatedNetworkPatterns.Length > 0) AddIssue("high", "repeated-network-failures", "Repeated DNS/network failure events", string.Join("; ", repeatedNetworkPatterns.Take(5).Select(item => $"{item.provider} {item.eventId}: {item.occurrences} events")));
        else if (networkFailures.Length > 0) AddIssue("warning", "network-failures", "DNS/network failure events", $"{networkFailures.Length} DNS/network event(s) were observed in the System log.");
        if (averageCpu is >= 90) AddIssue("high", "sustained-cpu-pressure", "High average CPU utilization", $"Average CPU utilization during the finite sample was {averageCpu:0.0}% (peak {peakCpu:0.0}%). {cpuProcessEvidence}");
        else if (peakCpu is >= 90) AddIssue("warning", "cpu-spike", "High CPU sample observed", $"Peak CPU utilization during the finite sample was {peakCpu:0.0}%. {cpuProcessEvidence}");
        if (memoryPercent is >= 90) AddIssue("high", "memory-pressure", "High physical memory load", $"Windows reported physical memory load at {memoryPercent:0.0}%. {memoryProcessEvidence} {privateMemoryProcessEvidence}");
        if (minimumFreeDrivePercent is <= 10) AddIssue("warning", "low-free-disk-space", "Low free space on a drive", $"The lowest available drive-space ratio was {minimumFreeDrivePercent:0.0}%.");
        if (diskQueue is >= 2) AddIssue("warning", "disk-queue-pressure", "Elevated physical disk queue", $"The highest sampled average physical-disk queue length was {diskQueue:0.00}.");

        var timeline = allEvents.Select(item =>
        {
            var parsed = DateTimeOffset.TryParse(item.TimeCreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp);
            return new { item, parsed, timestamp };
        }).Where(item => item.parsed).GroupBy(item => item.timestamp.UtcDateTime.Ticks / TimeSpan.FromMinutes(5).Ticks)
            .Where(group => group.Count() >= 2)
            .OrderByDescending(group => group.Key).Take(40)
            .Select(group => new { windowStartUtc = new DateTimeOffset(group.Key * TimeSpan.FromMinutes(5).Ticks, TimeSpan.Zero), evidenceCount = group.Count(), sources = group.Select(item => item.item.Source).Distinct().ToArray(), eventIds = group.Select(item => item.item.EventId).Distinct().Order().ToArray(), subjects = group.Select(item => item.item.Subject).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(), note = "Events share a five-minute time bucket; temporal proximity is not proof of causality." }).ToArray();

        DateTimeOffset? resourceSampleStartUtc = null;
        DateTimeOffset? resourceSampleEndUtc = null;
        if (resourceSampling.HasValue && resourceSampling.Value.ValueKind == JsonValueKind.Object && resourceSampling.Value.TryGetProperty("samples", out var resourceSamples) && resourceSamples.ValueKind == JsonValueKind.Array)
        {
            var sampleTimes = resourceSamples.EnumerateArray()
                .Select(row => TryGetJsonString(row, "sampledAtUtc", out var sampledAt) && DateTimeOffset.TryParse(sampledAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedAt) ? (DateTimeOffset?)parsedAt : null)
                .Where(value => value.HasValue).Select(value => value!.Value).OrderBy(value => value).ToArray();
            if (sampleTimes.Length > 0) { resourceSampleStartUtc = sampleTimes[0]; resourceSampleEndUtc = sampleTimes[^1]; }
        }
        var nearbyResourceEvents = new List<object>();
        if (resourceSampleStartUtc.HasValue && resourceSampleEndUtc.HasValue)
        {
            foreach (var item in allEvents
                .Where(item => DateTimeOffset.TryParse(item.TimeCreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var eventTime) && eventTime >= resourceSampleStartUtc.Value.AddMinutes(-5) && eventTime <= resourceSampleEndUtc.Value.AddMinutes(5))
                .OrderByDescending(item => item.TimeCreatedUtc, StringComparer.Ordinal)
                .Take(50))
            {
                nearbyResourceEvents.Add(new { item.TimeCreatedUtc, item.LogName, item.EventId, item.Provider, item.Subject, source = item.Source });
            }
        }
        var resourceEventCorrelation = new
        {
            available = resourceSampleStartUtc.HasValue && resourceSampleEndUtc.HasValue,
            sampleStartUtc = resourceSampleStartUtc,
            sampleEndUtc = resourceSampleEndUtc,
            nearbyEventCount = nearbyResourceEvents.Count,
            nearbyEvents = nearbyResourceEvents,
            note = "Windows events within five minutes of the finite resource sample are surfaced as nearby evidence only; time overlap does not attribute resource pressure to an event or process."
        };

        var serviceStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (diagnostics.ValueKind == JsonValueKind.Object && diagnostics.TryGetProperty("services", out var serviceInventory) && serviceInventory.ValueKind == JsonValueKind.Object && serviceInventory.TryGetProperty("services", out var serviceRows) && serviceRows.ValueKind == JsonValueKind.Array)
        {
            foreach (var serviceRow in serviceRows.EnumerateArray().Take(250))
            {
                var name = TryGetJsonString(serviceRow, "name", out var serviceName) ? serviceName : string.Empty;
                var state = TryGetJsonString(serviceRow, "state", out var serviceState) ? serviceState : string.Empty;
                if (name.Length > 0 && state.Length > 0) serviceStates[name] = state;
            }
        }

        JsonElement possibleTcpListenerConflicts = default;
        if (diagnostics.ValueKind == JsonValueKind.Object && diagnostics.TryGetProperty("network", out var networkData) && networkData.ValueKind == JsonValueKind.Object && networkData.TryGetProperty("possibleTcpListenerConflicts", out var conflicts))
            possibleTcpListenerConflicts = conflicts.Clone();
        var status = warnings.Count > 0 && issues.Count == 0 ? "Incomplete" : issues.Any(item => item.GetType().GetProperty("severity")?.GetValue(item)?.ToString() == "high") ? "Attention" : issues.Count > 0 ? "Warning" : "NoCriticalFindings";
        var timestampUtc = now.ToString("O", CultureInfo.InvariantCulture);
        var currentSnapshot = BuildHealthSnapshot(now, status, averageCpu, memoryPercent, diskQueue, serviceFailures.Length, crashEvents.Length, networkFailures.Length, issues.Count, serviceStates, issueKeys);
        object comparison = new { available = false, reason = "Local health history is disabled. Enable the opt-in history setting to compare future on-demand reports." };
        var historySaved = false;
        var historyCount = 0;
        string? historyWarning = null;
        if (Enabled("healthHistory"))
        {
            try
            {
                using var mutex = new Mutex(false, SystemManagerHealthHistoryMutexName);
                var mutexAcquired = false;
                try
                {
                    try { mutexAcquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
                    catch (AbandonedMutexException) { mutexAcquired = true; }
                    if (!mutexAcquired) throw new IOException("Another RunMind host is updating health history; this snapshot was not saved. Retry the report once the other update finishes.");
                    lock (SystemManagerHealthHistorySync)
                    {
                        var snapshots = ReadHealthHistory(out historyWarning);
                        var previous = snapshots.LastOrDefault() as JsonObject;
                        comparison = BuildHealthComparison(previous, currentSnapshot);
                        snapshots.Add(currentSnapshot.DeepClone());
                        while (snapshots.Count > 30) snapshots.RemoveAt(0);
                        WriteHealthHistory(snapshots);
                        historySaved = true;
                        historyCount = snapshots.Count;
                    }
                }
                finally { if (mutexAcquired) mutex.ReleaseMutex(); }
            }
            catch (Exception error)
            {
                historyWarning = Limit(error.Message, 300);
                warnings.Add(new { category = "local-health-history", message = historyWarning });
            }
        }

        var coverage = new List<string>();
        if (diagnosticCapabilities.Any(Enabled)) coverage.Add("Windows service/network/hardware inventory, limited to individually enabled categories");
        if (Enabled("eventLogInformation")) coverage.Add("Application and System Event Logs");
        if (Enabled("crashInvestigation")) coverage.Add("Application crash and Windows service failure events");
        if (Enabled("resourceMonitoring")) coverage.Add("Finite CPU, physical memory, process and supported disk-I/O sampling");
        if (Enabled("storageHealth")) coverage.Add("Physical disk reliability and volume health where Windows exposes it");
        var report = new
        {
            success = true, accepted = true, completed = true, verified = true,
            generatedAtUtc = timestampUtc, status,
            summary = new { issueCount = issues.Count, serviceFailureEvents = serviceFailures.Length, applicationCrashEvents = crashEvents.Length, networkFailureEvents = networkFailures.Length, averageCpuPercent = averageCpu, peakCpuPercent = peakCpu, memoryLoadPercent = memoryPercent, minimumFreeDrivePercent, maximumDiskQueueLength = diskQueue },
            issues,
            correlation = new { fiveMinuteEvidenceWindows = timeline, resourceSample = resourceEventCorrelation, note = "RunMind reports evidence that overlaps in time. It does not infer root cause, network causality, or service-to-process causality from temporal proximity alone. CPU and memory pressure findings include the highest sampled process contributors as observational context, not proof of fault." },
            serviceFailures = serviceFailures.Take(100).ToArray(), recurringServiceFailures,
            applicationCrashes = crashEvents.Take(100).ToArray(), recurringApplicationCrashes,
            networkFailures = networkFailures.Take(100).ToArray(), recurringNetworkFailures,
            possibleTcpListenerConflicts = possibleTcpListenerConflicts.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : possibleTcpListenerConflicts,
            resourceHotspots = resourceSampling.HasValue && resourceSampling.Value.ValueKind == JsonValueKind.Object ? new { topCpuProcesses = resourceSampling.Value.TryGetProperty("topProcesses", out var cpuRows) ? cpuRows.Clone() : default, topWorkingSetProcesses = resourceSampling.Value.TryGetProperty("topWorkingSetProcesses", out var workingSetRows) ? workingSetRows.Clone() : default, topPrivateMemoryProcesses = resourceSampling.Value.TryGetProperty("topPrivateMemoryProcesses", out var privateMemoryRows) ? privateMemoryRows.Clone() : default } : null,
            diagnostics = diagnostics.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : diagnostics,
            crashInvestigation, applicationEvents, systemEvents, resourceSampling, storageHealth,
            localHistory = new { enabled = Enabled("healthHistory"), saved = historySaved, retainedSnapshots = historyCount, comparison, warning = historyWarning },
            coverage, warnings,
            networkFollowUp = Enabled("targetedNetworkDiagnostics") ? "For a selected hostname and optional TCP port, use the explicit targeted DNS/route/TCP diagnostic. No unsolicited remote probe or port scan was performed by this report." : "Targeted hostname/DNS/route/TCP probing is not enabled; no remote probe or port scan was performed.",
            notes = new[] { "This is an on-demand evidence report, not a continuous monitor, compliance certification or root-cause determination.", "Only categories enabled in the System Manager settings are queried. Unsupported hardware or performance counters remain unavailable rather than being reported as healthy.", "Local history stores bounded summary metrics, service states and issue identifiers only; it does not persist raw Event Log messages, executable paths, connection endpoints or process command lines.", "Historical comparisons compare on-demand snapshots only; they do not imply continuous observation between reports." }
        };
        return report;
    }

    private static object ReadWindowsServices()
    {
        var scPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe");
        if (!File.Exists(scPath)) throw new FileNotFoundException("Windows Service Control (sc.exe) was not found in the system directory.");
        var timeoutSeconds = (int?)ReadSystemManagerSettings()["commandTimeoutSeconds"] ?? 0;
        var timeoutMilliseconds = timeoutSeconds > 0 ? checked(timeoutSeconds * 1000) : 0;
        var result = RunFixedDiagnosticCommand(scPath, new[] { "query", "type=", "service", "state=", "all" }, timeoutMilliseconds);
        if (result.ExitCode == -2)
            throw new TimeoutException(timeoutSeconds == 0 ? "Windows service enumeration was interrupted." : $"Windows service enumeration exceeded the configured {timeoutSeconds}-second timeout.");
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Windows service enumeration failed. " + result.Output);
        var output = result.Output;
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
        return new { services, count = services.Count, truncated = services.Count >= 250 || output.Contains("[output truncated at 512 KB]", StringComparison.Ordinal), exitCode = result.ExitCode };
    }

}
