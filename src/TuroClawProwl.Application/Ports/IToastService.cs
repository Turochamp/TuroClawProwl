using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Ports;

public interface IToastService
{
    Task NotifyGatewayTransitionAsync(
        StateTransition<GatewayHealth> transition,
        CancellationToken cancellationToken = default);

    Task NotifyPushSummaryAsync(
        PushSummary summary,
        CancellationToken cancellationToken = default);

    Task NotifyGatewayRestartResultAsync(
        SshCommandResult result,
        CancellationToken cancellationToken = default);
}
