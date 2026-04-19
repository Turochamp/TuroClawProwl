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

    public async Task<GitCommitResult> CommitPathAsync(
        string repoPath,
        string relativePath,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var add = await ProcessRunner.RunAsync(
            _gitExecutable, new[] { "add", "--", relativePath }, repoPath, cancellationToken)
            .ConfigureAwait(false);
        if (add.ExitCode != 0)
            return new GitCommitResult.Failure(FirstNonEmpty(add.StdErr, add.StdOut));

        var diff = await ProcessRunner.RunAsync(
            _gitExecutable, new[] { "diff", "--cached", "--quiet", "--", relativePath },
            repoPath, cancellationToken).ConfigureAwait(false);
        if (diff.ExitCode == 0)
            return new GitCommitResult.Success(HadChangesToCommit: false);

        var commit = await ProcessRunner.RunAsync(
            _gitExecutable, new[] { "commit", "-m", message, "--", relativePath },
            repoPath, cancellationToken).ConfigureAwait(false);
        if (commit.ExitCode != 0)
            return new GitCommitResult.Failure(FirstNonEmpty(commit.StdErr, commit.StdOut));

        return new GitCommitResult.Success(HadChangesToCommit: true);
    }

    private static string FirstNonEmpty(string a, string b) =>
        (string.IsNullOrWhiteSpace(a) ? b : a).Trim();
}
