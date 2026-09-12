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

        var localNow = DateTimeOffset.Now;
        var paths = BundleSourcePlan.FixedSources(localNow)
            .Select(i => Absolute(hubRoot, i.SourceRelativePath))
            .ToList();

        var registryPath = Absolute(hubRoot, BundleLayout.RegistryRelativePath);
        if (!File.Exists(registryPath))
        {
            logger.LogWarning(
                "Registry not found at {Path}; watching the fixed sources only", registryPath);
            return Distinct(paths);
        }

        try
        {
            var registry = File.ReadAllText(registryPath);
            foreach (var item in BundleSourcePlan.FromRegistry(registry))
                paths.Add(Absolute(hubRoot, item.SourceRelativePath));
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not read the registry at {Path}", registryPath);
        }

        var resolved = Distinct(paths);
        logger.LogInformation("State bundle watching {Count} source files", resolved.Count);
        return resolved;
    }

    private static string Absolute(string hubRoot, string relative) =>
        Path.Combine(hubRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private static IReadOnlyList<string> Distinct(IEnumerable<string> paths) =>
        paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
