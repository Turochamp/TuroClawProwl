using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Ports;

public interface ITrayView
{
    Task SetGatewayHealthAsync(GatewayHealth health, CancellationToken cancellationToken = default);

    Task SetTodayFileStatusesAsync(
        IReadOnlyCollection<TodayFileStatus> statuses,
        CancellationToken cancellationToken = default);

    Task SetPublishHealthAsync(PublishHealth publish, CancellationToken cancellationToken = default);
}
