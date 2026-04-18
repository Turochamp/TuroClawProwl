namespace TuroClawProwl.Domain;

public abstract record GatewayHealth
{
    public sealed record NeverReached : GatewayHealth;

    public sealed record Healthy(DateTimeOffset LastSeen, TimeSpan? Uptime) : GatewayHealth;

    public sealed record Unreachable(DateTimeOffset? LastSeenHealthy) : GatewayHealth;
}
