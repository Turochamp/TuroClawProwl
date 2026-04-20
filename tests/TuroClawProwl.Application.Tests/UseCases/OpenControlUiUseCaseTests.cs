using FluentAssertions;
using Moq;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;

namespace TuroClawProwl.Application.Tests.UseCases;

public class OpenControlUiUseCaseTests
{
    private static readonly Uri GatewayUrl = new("http://192.168.1.43:18789/");

    private readonly Mock<IBrowserLauncher> _browser = new(MockBehavior.Strict);
    private readonly Mock<ITokenStore> _tokenStore = new(MockBehavior.Strict);

    private OpenControlUiUseCase CreateUseCase() =>
        new(GatewayUrl, _browser.Object, _tokenStore.Object);

    [Fact]
    public async Task Opens_browser_with_gateway_url_and_token_in_fragment()
    {
        _tokenStore.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("abc123");
        _browser.Setup(b => b.OpenAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await CreateUseCase().ExecuteAsync();

        _browser.Verify(b => b.OpenAsync(
                It.Is<Uri>(u =>
                    u.Host == "192.168.1.43" &&
                    u.Port == 18789 &&
                    u.Fragment == "#token=abc123"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Missing_token_results_in_empty_token_fragment()
    {
        _tokenStore.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        Uri? capturedUrl = null;
        _browser.Setup(b => b.OpenAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Callback<Uri, CancellationToken>((u, _) => capturedUrl = u)
            .Returns(Task.CompletedTask);

        await CreateUseCase().ExecuteAsync();

        capturedUrl.Should().NotBeNull();
        capturedUrl!.Fragment.Should().Be("#token=");
    }

    [Fact]
    public async Task Token_with_special_characters_is_url_encoded()
    {
        _tokenStore.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("a b&c=d/e");
        Uri? capturedUrl = null;
        _browser.Setup(b => b.OpenAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>()))
            .Callback<Uri, CancellationToken>((u, _) => capturedUrl = u)
            .Returns(Task.CompletedTask);

        await CreateUseCase().ExecuteAsync();

        capturedUrl!.Fragment.Should().Be("#token=a%20b%26c%3Dd%2Fe");
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_to_token_store_and_browser()
    {
        using var cts = new CancellationTokenSource();
        _tokenStore.Setup(s => s.GetTokenAsync(cts.Token)).ReturnsAsync("t");
        _browser.Setup(b => b.OpenAsync(It.IsAny<Uri>(), cts.Token)).Returns(Task.CompletedTask);

        await CreateUseCase().ExecuteAsync(cts.Token);

        _tokenStore.Verify(s => s.GetTokenAsync(cts.Token), Times.Once);
        _browser.Verify(b => b.OpenAsync(It.IsAny<Uri>(), cts.Token), Times.Once);
    }

    [Fact]
    public void Build_control_ui_url_strips_path_and_preserves_scheme_host_port()
    {
        var url = OpenControlUiUseCase.BuildControlUiUrl(
            new Uri("http://192.168.1.43:18789/some/path"), "tok");

        url.Scheme.Should().Be("http");
        url.Host.Should().Be("192.168.1.43");
        url.Port.Should().Be(18789);
        url.AbsolutePath.Should().Be("/");
        url.Fragment.Should().Be("#token=tok");
    }

    [Fact]
    public void Constructor_rejects_null_gateway_url()
    {
        Action act = () => new OpenControlUiUseCase(null!, _browser.Object, _tokenStore.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_browser()
    {
        Action act = () => new OpenControlUiUseCase(GatewayUrl, null!, _tokenStore.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_token_store()
    {
        Action act = () => new OpenControlUiUseCase(GatewayUrl, _browser.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
