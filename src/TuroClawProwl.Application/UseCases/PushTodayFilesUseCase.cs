using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.UseCases;

// Two-phase operation ("Push" is shorthand for "sync to remote"):
//  1. For every tracked file with uncommitted changes, run a pathspec-scoped
//     `git add + commit` so only that file is staged (unrelated dirty files in
//     the same repo are untouched).
//  2. For every repo that either had an unpushed commit at entry or just
//     received a new commit in phase 1, run `git push` once.
// A commit failure in a repo skips that repo's push; one failure does not
// abort the remaining repos.
public sealed class PushTodayFilesUseCase
{
    private readonly IGitRunner _git;
    private readonly IToastService _toasts;
    private readonly ILogger<PushTodayFilesUseCase> _logger;

    public PushTodayFilesUseCase(
        IGitRunner git,
        IToastService toasts,
        ILogger<PushTodayFilesUseCase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(toasts);

        _git = git;
        _toasts = toasts;
        _logger = logger ?? NullLogger<PushTodayFilesUseCase>.Instance;
    }

    public async Task<PushSummary> ExecuteAsync(
        IReadOnlyCollection<TodayFileStatus> statuses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        var outcomes = new List<PushOutcome>();
        var failedRepos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var commitMessage = $"auto: today sync {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}";

        // Phase 1: commit any dirty tracked files, pathspec-scoped, one file per commit.
        var dirty = statuses
            .Where(s => s.HasUncommitted)
            .OrderBy(s => s.RepoPath, StringComparer.Ordinal)
            .ThenBy(s => s.Path, StringComparer.Ordinal)
            .ToArray();

        foreach (var file in dirty)
        {
            if (failedRepos.Contains(file.RepoPath)) continue;

            var relative = Path.GetRelativePath(file.RepoPath, file.Path).Replace('\\', '/');
            var result = await _git.CommitPathAsync(file.RepoPath, relative, commitMessage, cancellationToken)
                .ConfigureAwait(false);

            if (result is GitCommitResult.Failure f)
            {
                _logger.LogWarning("Today sync commit failed in {Repo} for {File}: {Error}",
                    file.RepoPath, relative, f.Error);
                outcomes.Add(PushOutcome.Failure(file.RepoPath, $"commit {relative}: {f.Error}"));
                failedRepos.Add(file.RepoPath);
            }
        }

        // Phase 2: push every repo that either started with unpushed commits or
        // just got a new commit from phase 1. Skip repos whose commit failed.
        var reposToPush = statuses
            .Where(s => s.IsUnpushed || s.HasUncommitted)
            .Select(s => s.RepoPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(r => !failedRepos.Contains(r))
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();

        foreach (var repoPath in reposToPush)
        {
            var push = await _git.PushAsync(repoPath, cancellationToken).ConfigureAwait(false);
            outcomes.Add(push switch
            {
                GitPushResult.Success => PushOutcome.Success(repoPath),
                GitPushResult.Failure f => PushOutcome.Failure(repoPath, $"push: {f.Error}"),
                _ => throw new ArgumentException(
                    $"Unknown git push result variant: {push.GetType().Name}",
                    nameof(push)),
            });
        }

        var summary = new PushSummary(outcomes);
        if (!summary.IsEmpty)
        {
            _logger.LogInformation(
                "Today sync: {SuccessCount} succeeded, {FailureCount} failed (commits: {CommitCount})",
                summary.SuccessCount, summary.FailureCount, dirty.Length - failedRepos.Count);
            await _toasts.NotifyPushSummaryAsync(summary, cancellationToken).ConfigureAwait(false);
        }
        return summary;
    }
}
