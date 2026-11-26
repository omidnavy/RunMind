using System.Diagnostics;

namespace RunMind.Runner;

internal static class RunnerCommandService
{
    private static readonly HashSet<string> BlockedDirectFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".hta", ".msi", ".msp", ".scr", ".reg", ".lnk", ".url", ".cpl", ".msc",
        ".appref-ms", ".application", ".gadget", ".settingcontent-ms",
    };

    public static RunnerItem? ResolveDirectInput(string input)
    {
        var value = input.Trim().Trim('"');
        if (value.Length is 0 or > 32_000)
            return null;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.IsNullOrEmpty(uri.UserInfo))
                return null;
            return new RunnerItem(uri.Host, "Website · Open in your default browser", uri.AbsoluteUri, RunnerItemKind.Url);
        }

        value = value.Trim('"');
        if (!Path.IsPathRooted(value) && !value.StartsWith('%'))
            return null;

        try
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(value);
            if (!Path.IsPathRooted(expandedPath))
                return null;
            var path = Path.GetFullPath(expandedPath);
            if (Directory.Exists(path))
            {
                var folderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(folderName))
                    folderName = path;
                return new RunnerItem(folderName, "Folder · Open in File Explorer", path, RunnerItemKind.Folder);
            }

            if (!File.Exists(path) || BlockedDirectFileExtensions.Contains(Path.GetExtension(path)))
                return null;

            return new RunnerItem(Path.GetFileName(path), "File · Open with its default application", path, RunnerItemKind.File);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public static void Execute(RunnerItem item)
    {
        switch (item.Kind)
        {
            case RunnerItemKind.ApplicationShortcut:
                if (!File.Exists(item.Target) || !string.Equals(Path.GetExtension(item.Target), ".lnk", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The application shortcut is no longer available.");
                Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true });
                break;

            case RunnerItemKind.Url:
                if (!Uri.TryCreate(item.Target, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw new InvalidOperationException("Only HTTP and HTTPS links can be opened by Runner.");
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                break;

            case RunnerItemKind.Folder:
                if (!Directory.Exists(item.Target))
                    throw new DirectoryNotFoundException("The folder no longer exists.");
                var explorer = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                explorer.ArgumentList.Add(item.Target);
                Process.Start(explorer);
                break;

            case RunnerItemKind.File:
                if (!File.Exists(item.Target) || BlockedDirectFileExtensions.Contains(Path.GetExtension(item.Target)))
                    throw new InvalidOperationException("The file is no longer available or is not allowed as a direct file target.");
                Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true });
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(item), "Unknown Runner item type.");
        }
    }
}
