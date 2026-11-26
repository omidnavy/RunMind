#nullable enable

using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using RunMind.Shared;

namespace RunMind.Runner;

internal static class StandaloneLocator
{
    private const string ExecutableName = "RunMind.exe";

    public static string? FindExecutable()
    {
        // Prefer the binary built/deployed with this Runner over a potentially older
        // process image. This matters during upgrades: an older Standalone instance may
        // not yet understand the Runner handoff protocol.
        foreach (var development in FindDevelopmentPaths())
            if (IsRunMindExecutable(development)) return development;

        var cached = StandalonePathStore.ReadCachedExecutable();
        if (!string.IsNullOrWhiteSpace(cached) && IsRunMindExecutable(cached)) return cached;

        foreach (var registered in FindRegisteredExecutables())
            if (IsRunMindExecutable(registered)) return registered;

        foreach (var known in FindKnownPaths())
            if (IsRunMindExecutable(known)) return known;

        foreach (var running in FindRunningExecutable())
            if (IsRunMindExecutable(running)) return running;

        return null;
    }

    private static IEnumerable<string> FindRunningExecutable()
    {
        foreach (var process in Process.GetProcessesByName("RunMind"))
        {
            using (process)
            {
                string? path = null;
                try { path = process.MainModule?.FileName; }
                catch { }
                if (!string.IsNullOrWhiteSpace(path)) yield return path;
            }
        }
    }

    private static IEnumerable<string> FindRegisteredExecutables()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            RegistryKey? baseKey = null;
            try { baseKey = RegistryKey.OpenBaseKey(hive, view); }
            catch { }
            if (baseKey is null) continue;
            using (baseKey)
            {
                using (var appPaths = baseKey.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{ExecutableName}"))
                {
                    var value = appPaths?.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(value)) yield return value;
                }

                using var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                string[] subKeyNames;
                try { subKeyNames = uninstall.GetSubKeyNames(); }
                catch { continue; }
                foreach (var name in subKeyNames)
                {
                    using var entry = uninstall.OpenSubKey(name);
                    var displayName = entry?.GetValue("DisplayName") as string;
                    if (!IsStandaloneDisplayName(displayName)) continue;

                    var installLocation = entry?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(installLocation))
                        yield return Path.Combine(installLocation, ExecutableName);

                    var displayIcon = entry?.GetValue("DisplayIcon") as string;
                    var iconPath = ParseExecutablePath(displayIcon);
                    if (iconPath is not null) yield return iconPath;
                }
            }
        }
    }

    private static IEnumerable<string> FindKnownPaths()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (var root in new[] { local, programFiles, programFilesX86 })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            yield return Path.Combine(root, "Programs", "RunMind", ExecutableName);
            yield return Path.Combine(root, "Programs", "LocalLLM", ExecutableName);
            yield return Path.Combine(root, "RunMind", ExecutableName);
            yield return Path.Combine(root, "LocalLLM", ExecutableName);
        }
    }

    private static IEnumerable<string> FindDevelopmentPaths()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; current is not null && depth < 12; depth++, current = current.Parent)
        {
            var solution = Path.Combine(current.FullName, "RunMind.sln");
            if (!File.Exists(solution)) continue;

            foreach (var configuration in new[] { "Release", "Debug" })
            foreach (var platform in new[] { "x64", "arm64" })
            {
                yield return Path.Combine(current.FullName, "RunMind.Standalone", "bin", platform,
                    configuration, "net10.0-windows10.0.22621.0", ExecutableName);
                yield return Path.Combine(current.FullName, "RunMind.Standalone", "bin",
                    configuration, "net10.0-windows10.0.22621.0", ExecutableName);
            }

            yield return Path.Combine(current.FullName, "Release", "Standalone", "win-x64", "publish", ExecutableName);
            yield return Path.Combine(current.FullName, "Release", "Standalone", "win-x64", ExecutableName);
        }
    }

    private static bool IsStandaloneDisplayName(string? value)
        => string.Equals(value?.Trim(), "RunMind", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "RunMind Standalone", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value?.Trim(), "RunMind Desktop", StringComparison.OrdinalIgnoreCase);

    private static string? ParseExecutablePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.StartsWith('"'))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote > 1) return trimmed[1..closingQuote];
        }
        var comma = trimmed.LastIndexOf(',');
        if (comma > 0 && int.TryParse(trimmed[(comma + 1)..], out _)) trimmed = trimmed[..comma];
        return trimmed.Trim('"', ' ');
    }

    private static bool IsRunMindExecutable(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            return string.Equals(Path.GetFileName(path), ExecutableName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
