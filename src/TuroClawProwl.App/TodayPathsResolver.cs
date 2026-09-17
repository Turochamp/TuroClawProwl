namespace TuroClawProwl.App;

internal static class TodayPathsResolver
{
    public static IReadOnlyList<(string AbsolutePath, string RepoPath)> ToOrchestratorPairs(
        IReadOnlyList<string> trackedPaths)
    {
        ArgumentNullException.ThrowIfNull(trackedPaths);

        return trackedPaths
            .Select(p => (AbsolutePath: p, RepoPath: FindContainingRepo(p) ?? string.Empty))
            .Where(t => t.RepoPath.Length > 0)
            .ToArray();
    }

    public static string? FindContainingRepo(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
