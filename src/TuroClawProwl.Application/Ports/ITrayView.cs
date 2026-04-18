using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Ports;

public interface ITrayView
{
    Task SetGatewayHealthAsync(GatewayHealth health, CancellationToken cancellationToken = default);

    Task SetRepoStatesAsync(
        IReadOnlyDictionary<string, RepoState> states,
        CancellationToken cancellationToken = default);
}
