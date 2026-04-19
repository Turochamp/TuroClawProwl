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

        // S3 amendment: root itself counts as a repo when it contains .git.
        if (Directory.Exists(Path.Combine(rootPath, ".git")))
            results.Add(rootPath);

        foreach (var child in Directory.EnumerateDirectories(rootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(Path.Combine(child, ".git")))
                results.Add(child);
        }

        return Task.FromResult<IReadOnlyList<string>>(results);
    }
}
