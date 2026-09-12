using System.Globalization;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.Process;

namespace TuroClawProwl.Infrastructure.Git;

public sealed class GitFileFactsReader : IGitFileFactsReader
{
    private readonly string _gitExecutable;

    public GitFileFactsReader(string gitExecutable = "git")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutable);
        _gitExecutable = gitExecutable;
    }

    public async Task<GitFileFacts> GetFileFactsAsync(
        string absolutePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        var repoPath = FindContainingRepo(absolutePath);
        if (repoPath is null)
            return GitFileFacts.OutsideRepository();

        var relative = Path.GetRelativePath(repoPath, absolutePath).Replace('\\', '/');

        var tracked = await ProcessRunner.RunAsync(
            _gitExecutable, new[] { "ls-files", "--error-unmatch", "--", relative },
            repoPath, cancellationToken).ConfigureAwait(false);
        if (tracked.ExitCode != 0)
            return GitFileFacts.Untracked();

        var status = await ProcessRunner.RunAsync(
            _gitExecutable, new[] { "status", "--porcelain", "--", relative },
            repoPath, cancellationToken).ConfigureAwait(false);
        var dirty = status.ExitCode != 0 || !string.IsNullOrWhiteSpace(status.StdOut);

        var log = await ProcessRunner.RunAsync(
            _gitExecutable, new[] { "log", "-1", "--format=%aI", "--", relative },
            repoPath, cancellationToken).ConfigureAwait(false);

        DateTimeOffset? committed = null;
        if (log.ExitCode == 0 &&
            DateTimeOffset.TryParse(
                log.StdOut.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            committed = parsed;
        }

        return new GitFileFacts(
            InRepository: true, Tracked: true, Dirty: dirty, LastCommitAuthorDate: committed);
    }

    // A linked worktree carries a .git FILE rather than a directory, so both count.
    private static string? FindContainingRepo(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(dir))
        {
            var dotGit = Path.Combine(dir, ".git");
            if (Directory.Exists(dotGit) || File.Exists(dotGit))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
