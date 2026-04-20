namespace TuroClawProwl.Application.Ports;

public abstract record GatewayPollResult
{
    public sealed record Success(TimeSpan? Uptime) : GatewayPollResult;

    public sealed record Failure(string Reason) : GatewayPollResult;
}
