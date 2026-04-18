using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Ports;

public interface IToastService
{
    Task NotifyGatewayTransitionAsync(
        StateTransition<GatewayHealth> transition,
        CancellationToken cancellationToken = default);
}
