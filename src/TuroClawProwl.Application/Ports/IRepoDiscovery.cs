namespace TuroClawProwl.Application.Ports;

public interface IRepoDiscovery
{
    Task<IReadOnlyList<string>> DiscoverAsync(string rootPath, CancellationToken cancellationToken = default);
}
