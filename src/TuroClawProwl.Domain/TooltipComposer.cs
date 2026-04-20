using System.Globalization;

namespace TuroClawProwl.Domain;

public static class TooltipComposer
{
    public const string LineSeparator = "\n";
    private const int MaxReposListedPerBucket = 4;

    public static string Compose(GatewayHealth gateway, IReadOnlyCollection<TodayFileStatus> todayFiles)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(todayFiles);

        var gatewayLine = gateway switch
        {
            GatewayHealth.NeverReached => "Gateway: never reached",
            GatewayHealth.Healthy => "Gateway: healthy",
            GatewayHealth.Unreachable { LastSeenHealthy: { } lastSeen } =>
                $"Gateway: unreachable (last seen {lastSeen.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC)",
            GatewayHealth.Unreachable => "Gateway: unreachable",
            _ => throw new ArgumentException(
                $"Unknown gateway health variant: {gateway.GetType().Name}",
                nameof(gateway)),
        };

        var total = todayFiles.Count;
        var synced = todayFiles.Count(f => f.IsSynced);
        var todayLine = total == 0
            ? "Today: not configured"
            : synced == total
                ? $"Today: {synced}/{total} synced"
                : ComposePendingSummary(todayFiles, synced, total);

        return gatewayLine + LineSeparator + todayLine;
    }

    private static string ComposePendingSummary(
        IReadOnlyCollection<TodayFileStatus> files,
        int synced,
        int total)
    {
        // Repos with any dirty file go into the uncommitted bucket; a file that's
        // both uncommitted and unpushed counts as uncommitted only (since the
        // commit is the primary blocker -- Option X will commit then push).
        var uncommittedRepos = files
            .Where(f => f.HasUncommitted)
            .Select(f => Path.GetFileName(f.RepoPath))
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var unpushedOnlyRepos = files
            .Where(f => f.IsUnpushed)
            .Select(f => Path.GetFileName(f.RepoPath))
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Except(uncommittedRepos, StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var parts = new List<string> { $"Today: {synced}/{total} synced" };
        if (uncommittedRepos.Length > 0)
            parts.Add("uncommitted: " + FormatRepoList(uncommittedRepos));
        if (unpushedOnlyRepos.Length > 0)
            parts.Add("unpushed: " + FormatRepoList(unpushedOnlyRepos));

        return string.Join(" \u2014 ", parts);
    }

    private static string FormatRepoList(string[] repos)
    {
        if (repos.Length <= MaxReposListedPerBucket)
            return string.Join(", ", repos);
        var listed = string.Join(", ", repos.Take(MaxReposListedPerBucket));
        return $"{listed} +{repos.Length - MaxReposListedPerBucket} more";
    }
}
