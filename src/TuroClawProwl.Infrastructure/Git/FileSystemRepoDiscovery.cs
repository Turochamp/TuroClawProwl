using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Infrastructure.Git;

public sealed class FileSystemRepoDiscovery : IRepoDiscovery
{
    public Task<IReadOnlyList<string>> DiscoverAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var results = new List<string>();
        if (!Directory.Exists(rootPath))
            return Task.FromResult<IReadOnlyList<string>>(results);

        foreach (var child in Directory.EnumerateDirectories(rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(Path.Combine(child, ".git")))
                results.Add(child);
        }

        return Task.FromResult<IReadOnlyList<string>>(results);
    }
}
