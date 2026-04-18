namespace TuroClawProwl.Application.Ports;

public interface IGitRunner
{
    Task<RepoInfo> GetRepoInfoAsync(string repoPath, CancellationToken cancellationToken = default);

    Task<GitPushResult> PushAsync(string repoPath, CancellationToken cancellationToken = default);
}
