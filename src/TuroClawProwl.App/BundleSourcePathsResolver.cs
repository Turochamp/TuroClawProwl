using Microsoft.Extensions.Logging;
using TuroClawProwl.Application;
using TuroClawProwl.Domain;

namespace TuroClawProwl.App;

internal static class BundleSourcePathsResolver
{
    // TodayCcaRoot held the hub path before HubRepoPath existed; falling back to
    // it keeps an already-installed config working without a settings visit.
    public static string ResolveHubRepoPath(TuroClawProwlConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return string.IsNullOrWhiteSpace(config.HubRepoPath)
            ? config.TodayCcaRoot
            : config.HubRepoPath;
    }

    public static string ResolveWorktreePath(TuroClawProwlConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!string.IsNullOrWhiteSpace(config.BundleWorktreePath))
            return config.BundleWorktreePath;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TuroClawProwl",
            "bundle-worktree");
    }

    public static string ResolvePublishBranch(TuroClawProwlConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return string.IsNullOrWhiteSpace(config.BundlePublishBranch) ? "main" : config.BundlePublishBranch;
    }

    public static IReadOnlyList<string> Resolve(
        TuroClawProwlConfig config,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var logger = loggerFactory.CreateLogger("BundleSources");
        var hubRoot = ResolveHubRepoPath(config);

        if (string.IsNullOrWhiteSpace(hubRoot))
        {
            logger.LogInformation("State bundle disabled (hub repo path is empty)");
            return [];
        }

        var resolved = BundleSourcePlan.WatchedRelativePaths
            .Select(relative => Absolute(hubRoot, relative))
            .ToArray();
        logger.LogInformation("State bundle watching {Count} source files", resolved.Length);
        return resolved;
    }

    private static string Absolute(string hubRoot, string relative) =>
        Path.Combine(hubRoot, relative.Replace('/', Path.DirectorySeparatorChar));
}
