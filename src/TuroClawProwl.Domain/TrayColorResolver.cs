namespace TuroClawProwl.Domain;

public static class TrayColorResolver
{
    public static TrayColor Resolve(GatewayHealth gateway, IReadOnlyCollection<TodayFileStatus> todayFiles)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(todayFiles);

        return gateway switch
        {
            GatewayHealth.NeverReached => TrayColor.Grey,
            GatewayHealth.Unreachable => TrayColor.Red,
            GatewayHealth.Healthy => todayFiles.All(f => f.IsSynced) ? TrayColor.Green : TrayColor.Yellow,
            _ => throw new ArgumentException(
                $"Unknown gateway health variant: {gateway.GetType().Name}",
                nameof(gateway)),
        };
    }
}
