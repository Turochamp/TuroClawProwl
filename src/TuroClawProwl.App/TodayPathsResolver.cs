using Microsoft.Extensions.Logging;
using TuroClawProwl.Application;
using TuroClawProwl.Domain;

namespace TuroClawProwl.App;

internal static class TodayPathsResolver
{
    public static IReadOnlyList<string> Resolve(
        TuroClawProwlConfig config,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var logger = loggerFactory.CreateLogger("TodayPaths");

        if (string.IsNullOrWhiteSpace(config.TodaySkillPath) ||
            string.IsNullOrWhiteSpace(config.TodayCcaRoot) ||
            string.IsNullOrWhiteSpace(config.TodayCrmIndexPath))
        {
            logger.LogInformation("Today sync disabled (one or more Today settings are empty)");
            return Array.Empty<string>();
        }

        if (!File.Exists(config.TodaySkillPath))
        {
            logger.LogWarning("Today sync disabled: SKILL.md not found at {Path}", config.TodaySkillPath);
            return Array.Empty<string>();
        }

        string skillMarkdown;
        try
        {
            skillMarkdown = File.ReadAllText(config.TodaySkillPath);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Today sync disabled: could not read {Path}", config.TodaySkillPath);
            return Array.Empty<string>();
        }

        return TodaySkillParser.ExtractSyncPaths(
            skillMarkdown,
            config.TodayCcaRoot,
            config.TodayCrmIndexPath);
    }

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
