using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.UseCases;

public sealed class HandleHealthPollUseCase
{
    private readonly IGatewayClient _client;
    private readonly IClock _clock;
    private readonly IToastService _toasts;
    private readonly ITrayView _tray;
    private readonly TransitionDetector<GatewayHealth> _detector =
        new(GatewayHealthKindComparer.Instance);

    private GatewayHealth _current = new GatewayHealth.NeverReached();

    public HandleHealthPollUseCase(
        IGatewayClient client,
        IClock clock,
        IToastService toasts,
        ITrayView tray)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(tray);

        _client = client;
        _clock = clock;
        _toasts = toasts;
        _tray = tray;
    }

    public async Task<GatewayHealth> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var pollResult = await _client.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;

        var next = pollResult switch
        {
            GatewayPollResult.Success success =>
                (GatewayHealth)new GatewayHealth.Healthy(now, success.Uptime),
            GatewayPollResult.Failure =>
                new GatewayHealth.Unreachable(ResolveLastSeenHealthy(_current)),
            _ => throw new ArgumentException(
                $"Unknown gateway poll result variant: {pollResult.GetType().Name}",
                nameof(pollResult)),
        };

        var transition = _detector.Observe(next);
        _current = next;

        if (transition is not null && transition.From is not GatewayHealth.NeverReached)
        {
            await _toasts.NotifyGatewayTransitionAsync(transition, cancellationToken).ConfigureAwait(false);
        }

        await _tray.SetGatewayHealthAsync(next, cancellationToken).ConfigureAwait(false);
        return next;
    }

    private static DateTimeOffset? ResolveLastSeenHealthy(GatewayHealth previous) => previous switch
    {
        GatewayHealth.Healthy h => h.LastSeen,
        GatewayHealth.Unreachable u => u.LastSeenHealthy,
        _ => null,
    };
}
