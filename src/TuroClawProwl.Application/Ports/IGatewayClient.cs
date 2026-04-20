namespace TuroClawProwl.Application.Ports;

public interface IGatewayClient
{
    Task<GatewayPollResult> GetHealthAsync(CancellationToken cancellationToken = default);
}
