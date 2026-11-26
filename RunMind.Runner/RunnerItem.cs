namespace RunMind.Runner;

internal enum RunnerItemKind
{
    ApplicationShortcut,
    Url,
    File,
    Folder,
}

internal sealed record RunnerItem(string Title, string Subtitle, string Target, RunnerItemKind Kind);
