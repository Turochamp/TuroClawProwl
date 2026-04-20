namespace TuroClawProwl.Application.Ports;

public interface IBrowserLauncher
{
    Task OpenAsync(Uri url, CancellationToken cancellationToken = default);
}
