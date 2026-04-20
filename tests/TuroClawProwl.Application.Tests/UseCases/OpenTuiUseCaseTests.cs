using FluentAssertions;
using Moq;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;

namespace TuroClawProwl.Application.Tests.UseCases;

public class OpenTuiUseCaseTests
{
    private static readonly SshTarget Target = new(Host: "macmini.lan", User: "turo");

    private readonly Mock<ITerminalLauncher> _launcher = new(MockBehavior.Strict);

    private OpenTuiUseCase CreateUseCase() => new(Target, _launcher.Object);

    [Fact]
    public async Task Launches_ssh_with_configured_target_and_openclaw_tui_command()
    {
        _launcher.Setup(l => l.LaunchSshInteractiveAsync(Target, "openclaw tui", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync();

        _launcher.Verify(
            l => l.LaunchSshInteractiveAsync(Target, OpenTuiUseCase.RemoteCommand, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_to_the_launcher()
    {
        using var cts = new CancellationTokenSource();
        _launcher.Setup(l => l.LaunchSshInteractiveAsync(Target, OpenTuiUseCase.RemoteCommand, cts.Token))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(cts.Token);

        _launcher.Verify(
            l => l.LaunchSshInteractiveAsync(Target, OpenTuiUseCase.RemoteCommand, cts.Token),
            Times.Once);
    }

    [Fact]
    public void Constructor_rejects_null_target()
    {
        Action act = () => new OpenTuiUseCase(null!, _launcher.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_launcher()
    {
        Action act = () => new OpenTuiUseCase(Target, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
