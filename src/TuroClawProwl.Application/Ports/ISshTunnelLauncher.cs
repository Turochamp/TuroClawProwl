namespace TuroClawProwl.Application.Ports;

public interface ISshTunnelLauncher
{
    Task<ISshTunnel> OpenAsync(
        SshTarget target,
        int localPort,
        string remoteBindHost,
        int remotePort,
        CancellationToken cancellationToken = default);
}
