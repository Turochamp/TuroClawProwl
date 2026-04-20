using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Application.UseCases;

public sealed class OpenControlUiUseCase
{
    private readonly Uri _gatewayBaseUrl;
    private readonly IBrowserLauncher _browser;
    private readonly ITokenStore _tokenStore;
    private readonly ILogger<OpenControlUiUseCase> _logger;

    public OpenControlUiUseCase(
        Uri gatewayBaseUrl,
        IBrowserLauncher browser,
        ITokenStore tokenStore,
        ILogger<OpenControlUiUseCase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(gatewayBaseUrl);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(tokenStore);

        _gatewayBaseUrl = gatewayBaseUrl;
        _browser = browser;
        _tokenStore = tokenStore;
        _logger = logger ?? NullLogger<OpenControlUiUseCase>.Instance;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var token = await _tokenStore.GetTokenAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var url = BuildControlUiUrl(_gatewayBaseUrl, token);

        _logger.LogInformation("Opening Control UI at {Url}", new Uri(_gatewayBaseUrl, "/"));
        await _browser.OpenAsync(url, cancellationToken).ConfigureAwait(false);
    }

    internal static Uri BuildControlUiUrl(Uri baseUrl, string token)
    {
        var root = new Uri(baseUrl, "/");
        return new Uri($"{root.AbsoluteUri.TrimEnd('/')}/#token={Uri.EscapeDataString(token)}");
    }
}
