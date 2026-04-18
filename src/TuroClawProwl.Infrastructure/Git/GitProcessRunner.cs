using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.Process;

namespace TuroClawProwl.Infrastructure.Git;

public sealed class GitProcessRunner : IGitRunner
{
    private readonly string _gitExecutable;

    public GitProcessRunner(string gitExecutable = "git")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutable);
        _gitExecutable = gitExecutable;
    }

    public async Task<RepoInfo> GetRepoInfoAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);

        var status = await ProcessRunner.RunAsync(
            _gitExecutable, new[] { "status", "--porcelain" }, repoPath, cancellationToken)
            .ConfigureAwait(false);

        var upstream = await ProcessRunner.RunAsync(
            _gitExecutable, new[] { "rev-parse", "--abbrev-ref", "@{u}" }, repoPath, cancellationToken)
            .ConfigureAwait(false);

        var hasUpstream = upstream.ExitCode == 0;
        var unpushedCount = 0;

        if (hasUpstream)
        {
            var count = await ProcessRunner.RunAsync(
                _gitExecutable, new[] { "rev-list", "--count", "@{u}..HEAD" }, repoPath, cancellationToken)
                .ConfigureAwait(false);

            if (count.ExitCode == 0 && int.TryParse(count.StdOut.Trim(), out var parsed))
                unpushedCount = parsed;
        }

        return new RepoInfo(status.StdOut, unpushedCount, hasUpstream);
    }

    public async Task<GitPushResult> PushAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);

        var result = await ProcessRunner.RunAsync(
            _gitExecutable, new[] { "push" }, repoPath, cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode == 0)
            return new GitPushResult.Success();

        var error = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return new GitPushResult.Failure(error.Trim());
    }
}
