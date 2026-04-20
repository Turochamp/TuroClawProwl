using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Application.UseCases;

public sealed class OpenControlUiUseCase : IAsyncDisposable
{
    public const int LocalPort = 18790;
    public const int RemotePort = 18789;
    public const string RemoteBindHost = "127.0.0.1";

    private readonly SshTarget _target;
    private readonly ISshTunnelLauncher _tunnelLauncher;
    private readonly IBrowserLauncher _browser;
    private readonly ITokenStore _tokenStore;
    private readonly ILogger<OpenControlUiUseCase> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private ISshTunnel? _tunnel;

    public OpenControlUiUseCase(
        SshTarget target,
        ISshTunnelLauncher tunnelLauncher,
        IBrowserLauncher browser,
        ITokenStore tokenStore,
        ILogger<OpenControlUiUseCase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(tunnelLauncher);
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(tokenStore);

        _target = target;
        _tunnelLauncher = tunnelLauncher;
        _browser = browser;
        _tokenStore = tokenStore;
        _logger = logger ?? NullLogger<OpenControlUiUseCase>.Instance;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var tunnel = await EnsureTunnelAsync(cancellationToken).ConfigureAwait(false);

        var token = await _tokenStore.GetTokenAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var url = BuildControlUiUrl(tunnel.LocalPort, token);

        _logger.LogInformation("Opening Control UI via local port {Port}", tunnel.LocalPort);
        await _browser.OpenAsync(url, cancellationToken).ConfigureAwait(false);
    }

    internal static Uri BuildControlUiUrl(int localPort, string token) =>
        new($"http://localhost:{localPort}/#token={Uri.EscapeDataString(token)}");

    private async Task<ISshTunnel> EnsureTunnelAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_tunnel is { IsOpen: true })
                return _tunnel;

            if (_tunnel is not null)
                await _tunnel.DisposeAsync().ConfigureAwait(false);

            _tunnel = await _tunnelLauncher
                .OpenAsync(_target, LocalPort, RemoteBindHost, RemotePort, cancellationToken)
                .ConfigureAwait(false);
            return _tunnel;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_tunnel is not null)
        {
            await _tunnel.DisposeAsync().ConfigureAwait(false);
            _tunnel = null;
        }
        _gate.Dispose();
    }
}
