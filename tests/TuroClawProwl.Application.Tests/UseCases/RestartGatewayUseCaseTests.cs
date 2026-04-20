using FluentAssertions;
using Moq;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;

namespace TuroClawProwl.Application.Tests.UseCases;

public class RestartGatewayUseCaseTests
{
    private static readonly SshTarget Target = new(Host: "macmini.lan", User: "turo");

    private readonly Mock<ISshRunner> _ssh = new(MockBehavior.Strict);
    private readonly Mock<IToastService> _toasts = new(MockBehavior.Strict);

    private RestartGatewayUseCase CreateUseCase() => new(Target, _ssh.Object, _toasts.Object);

    [Fact]
    public async Task Runs_openclaw_gateway_restart_via_ssh_with_configured_target()
    {
        _ssh.Setup(s => s.RunCommandAsync(
                Target, "openclaw",
                It.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "gateway", "restart" })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SshCommandResult.Success(StdOut: "ok"));
        _toasts.Setup(t => t.NotifyGatewayRestartResultAsync(It.IsAny<SshCommandResult>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync();

        _ssh.VerifyAll();
    }

    [Fact]
    public async Task Successful_restart_is_reported_via_toast()
    {
        var success = new SshCommandResult.Success("started");
        _ssh.Setup(s => s.RunCommandAsync(Target, It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(success);
        _toasts.Setup(t => t.NotifyGatewayRestartResultAsync(success, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync();

        result.Should().Be(success);
        _toasts.Verify(
            t => t.NotifyGatewayRestartResultAsync(success, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Failed_restart_is_reported_via_toast_with_exit_code_and_stderr()
    {
        var failure = new SshCommandResult.Failure(ExitCode: 255, StdErr: "permission denied");
        _ssh.Setup(s => s.RunCommandAsync(Target, It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failure);
        _toasts.Setup(t => t.NotifyGatewayRestartResultAsync(failure, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync();

        result.Should().Be(failure);
        _toasts.Verify(
            t => t.NotifyGatewayRestartResultAsync(failure, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_to_ssh_runner_and_toast_service()
    {
        using var cts = new CancellationTokenSource();
        _ssh.Setup(s => s.RunCommandAsync(Target, It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), cts.Token))
            .ReturnsAsync(new SshCommandResult.Success("ok"));
        _toasts.Setup(t => t.NotifyGatewayRestartResultAsync(It.IsAny<SshCommandResult>(), cts.Token))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(cts.Token);

        _ssh.Verify(s => s.RunCommandAsync(Target, It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), cts.Token), Times.Once);
        _toasts.Verify(t => t.NotifyGatewayRestartResultAsync(It.IsAny<SshCommandResult>(), cts.Token), Times.Once);
    }

    [Fact]
    public void Constructor_rejects_null_target()
    {
        Action act = () => new RestartGatewayUseCase(null!, _ssh.Object, _toasts.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_ssh_runner()
    {
        Action act = () => new RestartGatewayUseCase(Target, null!, _toasts.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_toast_service()
    {
        Action act = () => new RestartGatewayUseCase(Target, _ssh.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
