using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Ports;

public interface IToastService
{
    Task NotifyGatewayTransitionAsync(
        StateTransition<GatewayHealth> transition,
        CancellationToken cancellationToken = default);

    Task NotifyGatewayRestartResultAsync(
        SshCommandResult result,
        CancellationToken cancellationToken = default);

    Task NotifyTodaySyncFailureAsync(
        string title,
        string detail,
        CancellationToken cancellationToken = default);

    Task NotifyBundlePublishFailureAsync(
        PublishHealth.Failed failure,
        CancellationToken cancellationToken = default);
}
