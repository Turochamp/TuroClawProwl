namespace TuroClawProwl.Domain;

public static class TrayColorResolver
{
    public static TrayColor Resolve(GatewayHealth gateway, PublishHealth publish)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(publish);

        return gateway switch
        {
            GatewayHealth.NeverReached => TrayColor.Grey,
            GatewayHealth.Unreachable => TrayColor.Red,
            GatewayHealth.Healthy => publish is PublishHealth.Failed ? TrayColor.Yellow : TrayColor.Green,
            _ => throw new ArgumentException(
                $"Unknown gateway health variant: {gateway.GetType().Name}",
                nameof(gateway)),
        };
    }
}
