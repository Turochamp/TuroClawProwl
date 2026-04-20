namespace TuroClawProwl.Application.Ports;

public interface ISshTunnel : IAsyncDisposable
{
    int LocalPort { get; }

    bool IsOpen { get; }
}
