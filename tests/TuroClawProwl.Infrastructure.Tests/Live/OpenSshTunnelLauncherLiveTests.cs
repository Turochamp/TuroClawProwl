using System.Net.Http.Headers;
using System.Net.Sockets;
using FluentAssertions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.Ssh;

namespace TuroClawProwl.Infrastructure.Tests.Live;

[Trait("Category", "Live")]
public class OpenSshTunnelLauncherLiveTests
{
    [SkippableFact]
    public async Task Tunnel_forwards_local_port_to_remote_gateway_and_returns_http_response()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(LiveEnv.SshHost) || string.IsNullOrWhiteSpace(LiveEnv.SshUser),
            "TURO_LIVE_SSH_HOST or TURO_LIVE_SSH_USER not set");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var launcher = new OpenSshTunnelLauncher(readyTimeout: TimeSpan.FromSeconds(20));
        var target = new SshTarget(LiveEnv.SshHost!, LiveEnv.SshUser!);
        var localPort = LiveEnv.TunnelLocalPort;
        var remotePort = LiveEnv.TunnelRemotePort;

        await using var tunnel = await launcher.OpenAsync(
            target, localPort, "127.0.0.1", remotePort, cts.Token);

        tunnel.IsOpen.Should().BeTrue();
        tunnel.LocalPort.Should().Be(localPort);

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync("127.0.0.1", localPort, cts.Token);
            tcp.Connected.Should().BeTrue("tunnel should accept TCP connections on localhost");
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri($"http://localhost:{localPort}/health"));
        if (!string.IsNullOrWhiteSpace(LiveEnv.GatewayToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", LiveEnv.GatewayToken);
        }

        using var response = await http.SendAsync(request, cts.Token);

        // Either 2xx (authorized) or 401/403 (unauth) proves the remote HTTP server answered
        // through the tunnel. A network-level failure would throw instead.
        ((int)response.StatusCode).Should().BeInRange(200, 499,
            "gateway must respond with an HTTP status over the forwarded port");
    }

    [SkippableFact]
    public async Task Tunnel_dispose_stops_listening_on_local_port()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(LiveEnv.SshHost) || string.IsNullOrWhiteSpace(LiveEnv.SshUser),
            "TURO_LIVE_SSH_HOST or TURO_LIVE_SSH_USER not set");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var launcher = new OpenSshTunnelLauncher(readyTimeout: TimeSpan.FromSeconds(20));
        var target = new SshTarget(LiveEnv.SshHost!, LiveEnv.SshUser!);
        var localPort = LiveEnv.TunnelLocalPort;

        var tunnel = await launcher.OpenAsync(
            target, localPort, "127.0.0.1", LiveEnv.TunnelRemotePort, cts.Token);

        await tunnel.DisposeAsync();

        // Give ssh.exe a moment to fully release the port
        await Task.Delay(500, cts.Token);

        using var tcp = new TcpClient();
        Func<Task> connect = () => tcp.ConnectAsync("127.0.0.1", localPort, cts.Token).AsTask();
        await connect.Should().ThrowAsync<SocketException>(
            "the forwarded port should no longer accept connections after the tunnel is disposed");
    }
}
