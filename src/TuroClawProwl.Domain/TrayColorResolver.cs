namespace TuroClawProwl.Domain;

public static class TrayColorResolver
{
    public static TrayColor Resolve(GatewayHealth gateway, IReadOnlyCollection<RepoState> repos)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(repos);

        return gateway switch
        {
            GatewayHealth.NeverReached => TrayColor.Grey,
            GatewayHealth.Unreachable => TrayColor.Red,
            GatewayHealth.Healthy => repos.All(r => r.IsClean) ? TrayColor.Green : TrayColor.Yellow,
            _ => throw new ArgumentException(
                $"Unknown gateway health variant: {gateway.GetType().Name}",
                nameof(gateway)),
        };
    }
}
