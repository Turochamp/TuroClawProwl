using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.App;

// Watches a fixed list of tracked file paths (parsed from the Today
// SKILL.md), debounces per-repo for 10s after any change, then stages
// each tracked file, commits per-repo, and pushes. File changes that
// aren't in the tracked list are ignored.
public sealed class TodaySyncer : IDisposable
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(60);

    private readonly IReadOnlyList<string> _trackedPaths;
    private readonly IGitRunner _git;
    private readonly ILogger<TodaySyncer> _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, CancellationTokenSource> _perRepoDebounce =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private bool _disposed;

    public TodaySyncer(
        IReadOnlyList<string> trackedPaths,
        IGitRunner git,
        ILogger<TodaySyncer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(trackedPaths);
        ArgumentNullException.ThrowIfNull(git);

        _trackedPaths = trackedPaths;
        _git = git;
        _logger = logger ?? NullLogger<TodaySyncer>.Instance;
    }

    public void Start()
    {
        if (_trackedPaths.Count == 0)
        {
            _logger.LogInformation("Today syncer disabled (no tracked paths)");
            return;
        }

        // One watcher per containing directory of the tracked files, filtered
        // to that specific file. Works across multiple repos without needing
        // a shared root.
        var directories = _trackedPaths
            .Select(Path.GetDirectoryName)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir!))
            {
                _logger.LogWarning("Today syncer: tracked directory missing {Directory}", dir);
                continue;
            }

            var watcher = new FileSystemWatcher(dir!)
            {
                InternalBufferSize = 65536,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            watcher.Changed += OnFileChanged;
            watcher.Created += OnFileChanged;
            watcher.Renamed += OnFileChanged;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }

        _logger.LogInformation("Today syncer watching {Count} paths across {WatcherCount} directories",
            _trackedPaths.Count, _watchers.Count);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!_trackedPaths.Any(p => string.Equals(p, e.FullPath, StringComparison.OrdinalIgnoreCase)))
            return;

        var repoPath = FindContainingRepo(e.FullPath);
        if (repoPath is null)
        {
            _logger.LogWarning("Today syncer: no git repo found for {Path}", e.FullPath);
            return;
        }

        CancellationToken token;
        lock (_gate)
        {
            if (_disposed) return;
            if (_perRepoDebounce.TryGetValue(repoPath, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }
            var cts = new CancellationTokenSource();
            _perRepoDebounce[repoPath] = cts;
            token = cts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceWindow, token).ConfigureAwait(false);
                await SyncRepoAsync(repoPath, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // replaced by a newer debounce window
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Today syncer failed for {Repo}", repoPath);
            }
        });
    }

    private async Task SyncRepoAsync(string repoPath, CancellationToken cancellationToken)
    {
        var relatives = _trackedPaths
            .Where(p => p.StartsWith(repoPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetRelativePath(repoPath, p).Replace('\\', '/'))
            .ToArray();
        if (relatives.Length == 0) return;

        var message = $"auto: today sync {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}";
        var committedAny = false;
        foreach (var rel in relatives)
        {
            var commit = await _git.CommitPathAsync(repoPath, rel, message, cancellationToken)
                .ConfigureAwait(false);
            switch (commit)
            {
                case GitCommitResult.Success s:
                    if (s.HadChangesToCommit) committedAny = true;
                    break;
                case GitCommitResult.Failure f:
                    _logger.LogWarning("Today sync commit failed in {Repo} for {Path}: {Error}",
                        repoPath, rel, f.Error);
                    return;
            }
        }

        if (!committedAny)
        {
            _logger.LogDebug("Today sync: nothing to commit in {Repo}", repoPath);
            return;
        }

        var push = await _git.PushAsync(repoPath, cancellationToken).ConfigureAwait(false);
        switch (push)
        {
            case GitPushResult.Success:
                _logger.LogInformation("Today sync pushed {Repo}", repoPath);
                break;
            case GitPushResult.Failure f:
                _logger.LogWarning("Today sync push failed in {Repo}: {Error}", repoPath, f.Error);
                break;
        }
    }

    private static string? FindContainingRepo(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var cts in _perRepoDebounce.Values)
            {
                try { cts.Cancel(); cts.Dispose(); } catch { }
            }
            _perRepoDebounce.Clear();
        }
        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
    }
}
