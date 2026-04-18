namespace TuroClawProwl.Application.Ports;

public interface IConfigStore
{
    Task<TuroClawProwlConfig> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(TuroClawProwlConfig config, CancellationToken cancellationToken = default);
}
