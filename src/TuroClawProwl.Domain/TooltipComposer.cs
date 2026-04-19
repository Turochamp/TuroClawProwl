using System.Globalization;

namespace TuroClawProwl.Domain;

public static class TooltipComposer
{
    public const string LineSeparator = "\n";
    private const int MaxPendingListed = 3;

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
            ? "Today repo files: not configured"
            : synced == total
                ? $"Today repo files: {synced}/{total} synced"
                : ComposePendingSummary(todayFiles, synced, total);

        return gatewayLine + LineSeparator + todayLine;
    }

    private static string ComposePendingSummary(
        IReadOnlyCollection<TodayFileStatus> files,
        int synced,
        int total)
    {
        var pending = files.Where(f => f.NeedsAttention).ToArray();
        var pendingNames = pending.Take(MaxPendingListed).Select(DescribePending);
        var suffix = pending.Length > MaxPendingListed ? $", +{pending.Length - MaxPendingListed} more" : "";
        return $"Today repo files: {synced}/{total} synced \u2014 pending: {string.Join(", ", pendingNames)}{suffix}";
    }

    private static string DescribePending(TodayFileStatus file)
    {
        var relative = Path.GetFileName(file.Path);
        var reasons = new List<string>();
        if (file.HasUncommitted) reasons.Add("uncommitted");
        if (file.IsUnpushed) reasons.Add("unpushed");
        return reasons.Count == 0 ? relative : $"{relative} ({string.Join("+", reasons)})";
    }
}
