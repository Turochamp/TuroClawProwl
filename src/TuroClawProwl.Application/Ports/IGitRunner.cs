namespace TuroClawProwl.Application.Ports;

public interface IGitRunner
{
    Task<RepoInfo> GetRepoInfoAsync(string repoPath, CancellationToken cancellationToken = default);

    Task<string> GetFilePorcelainAsync(
        string repoPath,
        string relativePath,
        CancellationToken cancellationToken = default);

    Task<GitPushResult> PushAsync(string repoPath, CancellationToken cancellationToken = default);

    Task<GitCommitResult> CommitPathAsync(
        string repoPath,
        string relativePath,
        string message,
        CancellationToken cancellationToken = default);
}
