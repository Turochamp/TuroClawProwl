using FluentAssertions;
using Moq;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;

namespace TuroClawProwl.Application.Tests.UseCases;

public class OpenControlUiUseCaseTests
{
    private static readonly SshTarget Target = new(Host: "fox.local", User: "michael");

    private readonly Mock<ISshTunnelLauncher> _tunnelLauncher = new(MockBehavior.Strict);
    private readonly Mock<IBrowserLauncher> _browser = new(MockBehavior.Strict);
    private readonly Mock<ITokenStore> _tokenStore = new(MockBehavior.Strict);

    private OpenControlUiUseCase CreateUseCase() =>
        new(Target, _tunnelLauncher.Object, _browser.Object, _tokenStore.Object);

    private Mock<ISshTunnel> SetupTunnel(bool isOpen = true, int localPort = OpenControlUiUseCase.LocalPort)
    {
        var tunnel = new Mock<ISshTunnel>(MockBehavior.Strict);
        tunnel.SetupGet(t => t.IsOpen).Returns(isOpen);
        tunnel.SetupGet(t => t.LocalPort).Returns(localPort);
        tunnel.Setup(t => t.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return tunnel;
    }

    [Fact]
    public async Task Opens_tunnel_with_constants_and_configured_target_then_opens_browser_with_token()
    {
        var tunnel = SetupTunnel();
        _tunnelLauncher.Setup(l => l.OpenAsync(
                Target,
                OpenControlUiUseCase.LocalPort,
                OpenControlUiUseCase.RemoteBindHost,
                OpenControlUiUseCase.RemotePort,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tunnel.Object);
        _tokenStore.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("abc123");
        _browser.Setup(b => b.OpenAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var useCase = CreateUseCase();
        await useCase.ExecuteAsync();

        _tunnelLauncher.Verify(l => l.OpenAsync(
            Target,
            OpenControlUiUseCase.LocalPort,
            OpenControlUiUseCase.RemoteBindHost,
            OpenControlUiUseCase.RemotePort,
            It.IsAny<CancellationToken>()), Times.Once);
        _browser.Verify(b => b.OpenAsync(
                It.Is<Uri>(u =>
                    u.Host == "localhost" &&
                    u.Port == OpenControlUiUseCase.LocalPort &&
                    u.Fragment == "#token=abc123"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Subsequent_calls_reuse_an_open_tunnel()
    {
        var tunnel = SetupTunnel();
        _tunnelLauncher.Setup(l => l.OpenAsync(
                It.IsAny<SshTarget>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tunnel.Object);
        _tokenStore.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _browser.Setup(b => b.OpenAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var useCase = CreateUseCase();
        await useCase.ExecuteAsync();
        await useCase.ExecuteAsync();

        _tunnelLauncher.Verify(l => l.OpenAsync(
            It.IsAny<SshTarget>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _browser.Verify(b => b.OpenAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Closed_tunnel_is_disposed_and_a_new_one_is_opened()
    {
        var stale = SetupTunnel(isOpen: false);
        var fresh = SetupTunnel(isOpen: true);
        _tunnelLauncher.SetupSequence(l => l.OpenAsync(
                It.IsAny<SshTarget>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stale.Object)
            .ReturnsAsync(fresh.Object);
        _tokenStore.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("t");
        _browser.Setup(b => b.OpenAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var useCase = CreateUseCase();
        await useCase.ExecuteAsync();
        await useCase.ExecuteAsync();

        stale.Verify(t => t.DisposeAsync(), Times.AtLeastOnce);
        _tunnelLauncher.Verify(l => l.OpenAsync(
            It.IsAny<SshTarget>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Missing_token_results_in_empty_token_fragment()
    {
        var tunnel = SetupTunnel();
        _tunnelLauncher.Setup(l => l.OpenAsync(
                It.IsAny<SshTarget>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tunnel.Object);
        _tokenStore.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        Uri? capturedUrl = null;
        _browser.Setup(b => b.OpenAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Callback<Uri, CancellationToken>((u, _) => capturedUrl = u)
            .Returns(Task.CompletedTask);

        await using var useCase = CreateUseCase();
        await useCase.ExecuteAsync();

        capturedUrl.Should().NotBeNull();
        capturedUrl!.Fragment.Should().Be("#token=");
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_to_tunnel_launcher_token_store_and_browser()
    {
        using var cts = new CancellationTokenSource();
        var tunnel = SetupTunnel();
        _tunnelLauncher.Setup(l => l.OpenAsync(
                Target,
                OpenControlUiUseCase.LocalPort,
                OpenControlUiUseCase.RemoteBindHost,
                OpenControlUiUseCase.RemotePort,
                cts.Token))
            .ReturnsAsync(tunnel.Object);
        _tokenStore.Setup(s => s.GetTokenAsync(cts.Token)).ReturnsAsync("t");
        _browser.Setup(b => b.OpenAsync(It.IsAny<Uri>(), cts.Token)).Returns(Task.CompletedTask);

        await using var useCase = CreateUseCase();
        await useCase.ExecuteAsync(cts.Token);

        _tunnelLauncher.Verify(l => l.OpenAsync(Target,
            OpenControlUiUseCase.LocalPort,
            OpenControlUiUseCase.RemoteBindHost,
            OpenControlUiUseCase.RemotePort,
            cts.Token), Times.Once);
        _tokenStore.Verify(s => s.GetTokenAsync(cts.Token), Times.Once);
        _browser.Verify(b => b.OpenAsync(It.IsAny<Uri>(), cts.Token), Times.Once);
    }

    [Fact]
    public async Task Disposing_use_case_disposes_the_active_tunnel()
    {
        var tunnel = SetupTunnel();
        _tunnelLauncher.Setup(l => l.OpenAsync(
                It.IsAny<SshTarget>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(tunnel.Object);
        _tokenStore.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("t");
        _browser.Setup(b => b.OpenAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var useCase = CreateUseCase();
        await useCase.ExecuteAsync();
        await useCase.DisposeAsync();

        tunnel.Verify(t => t.DisposeAsync(), Times.Once);
    }

    [Fact]
    public void Constructor_rejects_null_target()
    {
        Action act = () => new OpenControlUiUseCase(null!, _tunnelLauncher.Object, _browser.Object, _tokenStore.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_tunnel_launcher()
    {
        Action act = () => new OpenControlUiUseCase(Target, null!, _browser.Object, _tokenStore.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_browser()
    {
        Action act = () => new OpenControlUiUseCase(Target, _tunnelLauncher.Object, null!, _tokenStore.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_token_store()
    {
        Action act = () => new OpenControlUiUseCase(Target, _tunnelLauncher.Object, _browser.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
