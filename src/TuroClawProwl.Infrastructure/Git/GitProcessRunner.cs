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

    public async Task<string> GetFilePorcelainAsync(
        string repoPath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var result = await ProcessRunner.RunAsync(
            _gitExecutable, new[] { "status", "--porcelain", "--", relativePath },
            repoPath, cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0 ? result.StdOut : string.Empty;
    }
}
