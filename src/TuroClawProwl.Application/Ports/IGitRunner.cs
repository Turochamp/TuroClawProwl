namespace TuroClawProwl.Application.Ports;

public interface IGitRunner
{
    Task<RepoInfo> GetRepoInfoAsync(string repoPath, CancellationToken cancellationToken = default);

    Task<string> GetFilePorcelainAsync(
        string repoPath,
        string relativePath,
        CancellationToken cancellationToken = default);
}
