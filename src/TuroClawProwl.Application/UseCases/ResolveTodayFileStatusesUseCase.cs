using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.UseCases;

public sealed class ResolveTodayFileStatusesUseCase
{
    private readonly IGitRunner _git;
    private readonly ITrayView _tray;
    private readonly ILogger<ResolveTodayFileStatusesUseCase> _logger;

    public ResolveTodayFileStatusesUseCase(
        IGitRunner git,
        ITrayView tray,
        ILogger<ResolveTodayFileStatusesUseCase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(tray);

        _git = git;
        _tray = tray;
        _logger = logger ?? NullLogger<ResolveTodayFileStatusesUseCase>.Instance;
    }

    public async Task<IReadOnlyList<TodayFileStatus>> ExecuteAsync(
        IReadOnlyList<(string AbsolutePath, string RepoPath)> trackedFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackedFiles);

        var statuses = new List<TodayFileStatus>(trackedFiles.Count);
        var repoInfoCache = new Dictionary<string, RepoInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var (absolutePath, repoPath) in trackedFiles)
        {
            if (!repoInfoCache.TryGetValue(repoPath, out var repoInfo))
            {
                repoInfo = await _git.GetRepoInfoAsync(repoPath, cancellationToken).ConfigureAwait(false);
                repoInfoCache[repoPath] = repoInfo;
            }

            var relative = Path.GetRelativePath(repoPath, absolutePath).Replace('\\', '/');
            var porcelain = await _git.GetFilePorcelainAsync(repoPath, relative, cancellationToken).ConfigureAwait(false);

            var hasUncommitted = !string.IsNullOrWhiteSpace(porcelain);
            var isUnpushed = !repoInfo.HasUpstream || repoInfo.UnpushedCount > 0;

            statuses.Add(new TodayFileStatus(absolutePath, repoPath, hasUncommitted, isUnpushed));
        }

        await _tray.SetTodayFileStatusesAsync(statuses, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug(
            "Resolved {Total} Today files: {Synced} synced, {Pending} pending",
            statuses.Count,
            statuses.Count(s => s.IsSynced),
            statuses.Count(s => s.NeedsAttention));

        return statuses;
    }
}
