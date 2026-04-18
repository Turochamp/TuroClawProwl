using FluentAssertions;
using Moq;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.Tests.TestSupport;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Tests.UseCases;

public class HandleHealthPollUseCaseTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 4, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IGatewayClient> _client = new(MockBehavior.Strict);
    private readonly Mock<IToastService> _toasts = new(MockBehavior.Loose);
    private readonly Mock<ITrayView> _tray = new(MockBehavior.Loose);
    private readonly FixedClock _clock = new(T0);

    private HandleHealthPollUseCase CreateUseCase() =>
        new(_client.Object, _clock, _toasts.Object, _tray.Object);

    private void QueuePollResults(params GatewayPollResult[] results)
    {
        var sequence = _client.SetupSequence(c => c.GetHealthAsync(It.IsAny<CancellationToken>()));
        foreach (var result in results)
            sequence = sequence.ReturnsAsync(result);
    }

    [Fact]
    public async Task Successful_poll_produces_healthy_state_timestamped_by_the_clock()
    {
        QueuePollResults(new GatewayPollResult.Success(Uptime: TimeSpan.FromMinutes(7)));
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync();

        result.Should().Be(new GatewayHealth.Healthy(T0, TimeSpan.FromMinutes(7)));
    }

    [Fact]
    public async Task Failed_first_poll_produces_unreachable_state_with_no_last_seen_healthy()
    {
        QueuePollResults(new GatewayPollResult.Failure("timeout"));
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync();

        result.Should().Be(new GatewayHealth.Unreachable(LastSeenHealthy: null));
    }

    [Fact]
    public async Task Failure_after_healthy_carries_last_seen_from_the_healthy_state()
    {
        QueuePollResults(
            new GatewayPollResult.Success(null),
            new GatewayPollResult.Failure("connect refused"));
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync();
        _clock.Advance(TimeSpan.FromMinutes(1));
        var result = await useCase.ExecuteAsync();

        result.Should().Be(new GatewayHealth.Unreachable(LastSeenHealthy: T0));
    }

    [Fact]
    public async Task Successive_failures_preserve_the_original_last_seen_anchor()
    {
        QueuePollResults(
            new GatewayPollResult.Success(null),
            new GatewayPollResult.Failure("x"),
            new GatewayPollResult.Failure("y"),
            new GatewayPollResult.Failure("z"));
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync();                  // healthy at T0
        _clock.Advance(TimeSpan.FromSeconds(15));
        await useCase.ExecuteAsync();                  // unreachable anchored at T0
        _clock.Advance(TimeSpan.FromSeconds(15));
        await useCase.ExecuteAsync();
        _clock.Advance(TimeSpan.FromSeconds(15));
        var result = await useCase.ExecuteAsync();

        result.Should().Be(new GatewayHealth.Unreachable(LastSeenHealthy: T0));
    }

    [Fact]
    public async Task Tray_view_receives_gateway_health_on_every_poll()
    {
        QueuePollResults(
            new GatewayPollResult.Success(null),
            new GatewayPollResult.Success(null));
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync();
        await useCase.ExecuteAsync();

        _tray.Verify(
            t => t.SetGatewayHealthAsync(It.IsAny<GatewayHealth>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task First_successful_poll_from_never_reached_emits_no_toast()
    {
        QueuePollResults(new GatewayPollResult.Success(null));
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync();

        _toasts.Verify(
            t => t.NotifyGatewayTransitionAsync(
                It.IsAny<StateTransition<GatewayHealth>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task First_failed_poll_from_never_reached_emits_no_toast()
    {
        QueuePollResults(new GatewayPollResult.Failure("unreachable"));
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync();

        _toasts.Verify(
            t => t.NotifyGatewayTransitionAsync(
                It.IsAny<StateTransition<GatewayHealth>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Healthy_to_unreachable_transition_emits_a_toast()
    {
        QueuePollResults(
            new GatewayPollResult.Success(null),
            new GatewayPollResult.Failure("down"));
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync();
        await useCase.ExecuteAsync();

        _toasts.Verify(
            t => t.NotifyGatewayTransitionAsync(
                It.Is<StateTransition<GatewayHealth>>(s =>
                    s.From is GatewayHealth.Healthy && s.To is GatewayHealth.Unreachable),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Unreachable_to_healthy_transition_emits_a_toast()
    {
        QueuePollResults(
            new GatewayPollResult.Failure("down"),
            new GatewayPollResult.Success(null));
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync();
        await useCase.ExecuteAsync();

        _toasts.Verify(
            t => t.NotifyGatewayTransitionAsync(
                It.Is<StateTransition<GatewayHealth>>(s =>
                    s.From is GatewayHealth.Unreachable && s.To is GatewayHealth.Healthy),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Repeated_healthy_polls_emit_no_toasts()
    {
        QueuePollResults(
            new GatewayPollResult.Success(null),
            new GatewayPollResult.Success(null),
            new GatewayPollResult.Success(null),
            new GatewayPollResult.Success(null));
        var useCase = CreateUseCase();

        for (var i = 0; i < 4; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(15));
            await useCase.ExecuteAsync();
        }

        _toasts.Verify(
            t => t.NotifyGatewayTransitionAsync(
                It.IsAny<StateTransition<GatewayHealth>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Repeated_failures_emit_no_toasts_after_the_first_transition_into_unreachable()
    {
        QueuePollResults(
            new GatewayPollResult.Success(null),
            new GatewayPollResult.Failure("a"),
            new GatewayPollResult.Failure("b"),
            new GatewayPollResult.Failure("c"),
            new GatewayPollResult.Failure("d"));
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync();
        _clock.Advance(TimeSpan.FromSeconds(15));
        await useCase.ExecuteAsync();
        _clock.Advance(TimeSpan.FromSeconds(15));
        await useCase.ExecuteAsync();
        _clock.Advance(TimeSpan.FromSeconds(15));
        await useCase.ExecuteAsync();
        _clock.Advance(TimeSpan.FromSeconds(15));
        await useCase.ExecuteAsync();

        _toasts.Verify(
            t => t.NotifyGatewayTransitionAsync(
                It.IsAny<StateTransition<GatewayHealth>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_to_the_gateway_client()
    {
        QueuePollResults(new GatewayPollResult.Success(null));
        var useCase = CreateUseCase();
        using var cts = new CancellationTokenSource();

        await useCase.ExecuteAsync(cts.Token);

        _client.Verify(c => c.GetHealthAsync(cts.Token), Times.Once);
    }

    [Fact]
    public void Constructor_rejects_null_client()
    {
        Action act = () => new HandleHealthPollUseCase(null!, _clock, _toasts.Object, _tray.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_clock()
    {
        Action act = () => new HandleHealthPollUseCase(_client.Object, null!, _toasts.Object, _tray.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_toast_service()
    {
        Action act = () => new HandleHealthPollUseCase(_client.Object, _clock, null!, _tray.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_tray_view()
    {
        Action act = () => new HandleHealthPollUseCase(_client.Object, _clock, _toasts.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
