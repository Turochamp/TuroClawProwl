using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.UseCases;

public sealed class HandleHealthPollUseCase
{
    private readonly IGatewayClient _client;
    private readonly IClock _clock;
    private readonly IToastService _toasts;
    private readonly ITrayView _tray;
    private readonly ILogger<HandleHealthPollUseCase> _logger;
    private readonly TransitionDetector<GatewayHealth> _detector =
        new(GatewayHealthKindComparer.Instance);

    private GatewayHealth _current = new GatewayHealth.NeverReached();
    private bool _hasEverBeenHealthy;

    public HandleHealthPollUseCase(
        IGatewayClient client,
        IClock clock,
        IToastService toasts,
        ITrayView tray,
        ILogger<HandleHealthPollUseCase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(tray);

        _client = client;
        _clock = clock;
        _toasts = toasts;
        _tray = tray;
        _logger = logger ?? NullLogger<HandleHealthPollUseCase>.Instance;
    }

    public async Task<GatewayHealth> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var pollResult = await _client.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;

        var next = pollResult switch
        {
            GatewayPollResult.Success success =>
                (GatewayHealth)new GatewayHealth.Healthy(now, success.Uptime),
            GatewayPollResult.Failure failure =>
                NoteFailure(failure, new GatewayHealth.Unreachable(ResolveLastSeenHealthy(_current))),
            _ => throw new ArgumentException(
                $"Unknown gateway poll result variant: {pollResult.GetType().Name}",
                nameof(pollResult)),
        };

        if (next is GatewayHealth.Healthy) _hasEverBeenHealthy = true;

        var transition = _detector.Observe(next);
        _current = next;

        if (transition is not null && transition.From is not GatewayHealth.NeverReached)
        {
            _logger.LogInformation(
                "Gateway transition {FromKind} -> {ToKind}",
                transition.From.GetType().Name,
                transition.To.GetType().Name);
            await _toasts.NotifyGatewayTransitionAsync(transition, cancellationToken).ConfigureAwait(false);
        }

        await _tray.SetGatewayHealthAsync(next, cancellationToken).ConfigureAwait(false);
        return next;
    }

    private GatewayHealth NoteFailure(GatewayPollResult.Failure failure, GatewayHealth next)
    {
        if (!_hasEverBeenHealthy)
            _logger.LogWarning("Gateway poll failed during startup: {Reason}", failure.Reason);
        return next;
    }

    private static DateTimeOffset? ResolveLastSeenHealthy(GatewayHealth previous) => previous switch
    {
        GatewayHealth.Healthy h => h.LastSeen,
        GatewayHealth.Unreachable u => u.LastSeenHealthy,
        _ => null,
    };
}
