using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.App;

// Watches a fixed list of tracked file paths (parsed from the Today
// SKILL.md), debounces per-repo for 60s after any change, then stages
// each tracked file, commits per-repo, and pushes. File changes that
// aren't in the tracked list are ignored.
//
// One failed file does NOT abort the rest of the per-repo batch — we
// accumulate per-file outcomes, push if anything succeeded, and emit
// one structured outcome log per sync. Failures fire a toast (deduped
// per (repo,path,kind) within FailureToastDedupWindow) so the user
// finds out without log diving. A per-repo SemaphoreSlim guarantees
// only one git operation runs against a given repo at a time, even if
// a debounce CTS cancellation arrives too late to stop an in-flight
// SyncRepoAsync.
public sealed class TodaySyncer : IDisposable
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FailureToastDedupWindow = TimeSpan.FromMinutes(10);

    private IReadOnlyList<string> _trackedPaths;
    private readonly IGitRunner _git;
    private readonly IToastService? _toasts;
    private readonly ILogger<TodaySyncer> _logger;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, CancellationTokenSource> _perRepoDebounce =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _perRepoSemaphores =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastFailureToastAt =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _suppressedPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private bool _disposed;

    public TodaySyncer(
        IReadOnlyList<string> trackedPaths,
        IGitRunner git,
        IToastService? toasts = null,
        ILogger<TodaySyncer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(trackedPaths);
        ArgumentNullException.ThrowIfNull(git);

        _trackedPaths = trackedPaths;
        _git = git;
        _toasts = toasts;
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

    // Tear down current watchers, drain in-flight syncs, and rebuild with
    // a new tracked-path set. Used by LiveConfigApplier when Today config
    // changes. Safe to call concurrently with watcher events; subsequent
    // events that race the rebuild may be missed but the next file edit
    // re-triggers a sync.
    public async Task RestartWithPathsAsync(
        IReadOnlyList<string> trackedPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trackedPaths);

        List<SemaphoreSlim> semaphoresToDrain;
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var cts in _perRepoDebounce.Values)
            {
                try { cts.Cancel(); cts.Dispose(); } catch { }
            }
            _perRepoDebounce.Clear();
            semaphoresToDrain = _perRepoSemaphores.Values.ToList();
        }

        // Drain outside the lock so we don't block watcher callbacks.
        foreach (var sem in semaphoresToDrain)
        {
            await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
            sem.Release();
        }

        lock (_gate)
        {
            foreach (var sem in _perRepoSemaphores.Values)
            {
                try { sem.Dispose(); } catch { }
            }
            _perRepoSemaphores.Clear();
            _lastFailureToastAt.Clear();
            _suppressedPaths.Clear();
            _trackedPaths = trackedPaths;
        }

        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();

        Start();
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        _logger.LogDebug("Today syncer: watcher event {ChangeType} {Path}", e.ChangeType, e.FullPath);

        if (!_trackedPaths.Any(p => string.Equals(p, e.FullPath, StringComparison.OrdinalIgnoreCase)))
            return;

        if (_suppressedPaths.Contains(e.FullPath))
        {
            _logger.LogDebug("Today syncer: skipping suppressed path {Path}", e.FullPath);
            return;
        }

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
            .Where(p => !_suppressedPaths.Contains(p))
            .Select(p => (Absolute: p, Relative: Path.GetRelativePath(repoPath, p).Replace('\\', '/')))
            .ToArray();
        if (relatives.Length == 0) return;

        var sem = GetOrCreateSemaphore(repoPath);
        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var message = $"auto: today sync {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}";
            var committed = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var (absolute, rel) in relatives)
            {
                var commit = await _git.CommitPathAsync(repoPath, rel, message, cancellationToken)
                    .ConfigureAwait(false);
                switch (commit)
                {
                    case GitCommitResult.Success s:
                        if (s.HadChangesToCommit) committed++;
                        else skipped++;
                        break;
                    case GitCommitResult.Failure f:
                        failed++;
                        _logger.LogWarning("Today sync commit failed in {Repo} for {Path}: {Error}",
                            repoPath, rel, f.Error);
                        await TryNotifyFailureAsync(
                            repoPath, rel, kind: "commit",
                            title: $"Today sync: commit failed in {RepoName(repoPath)}",
                            detail: $"{rel}: {f.Error}",
                            cancellationToken).ConfigureAwait(false);
                        // If this looks like a parent-repo gitignore situation,
                        // suppress the path for the rest of the process so we
                        // don't fire the same toast again every debounce window.
                        if (LooksLikeGitignoreFailure(f.Error))
                        {
                            _suppressedPaths.Add(absolute);
                            _logger.LogWarning(
                                "Today syncer: suppressing {Path} for the rest of this process " +
                                "(parent-repo gitignore; needs an inner .git)", absolute);
                        }
                        break;
                }
            }

            var pushed = false;
            if (committed > 0)
            {
                var push = await _git.PushAsync(repoPath, cancellationToken).ConfigureAwait(false);
                switch (push)
                {
                    case GitPushResult.Success:
                        pushed = true;
                        _logger.LogInformation("Today sync pushed {Repo}", repoPath);
                        break;
                    case GitPushResult.Failure f:
                        _logger.LogWarning("Today sync push failed in {Repo}: {Error}", repoPath, f.Error);
                        await TryNotifyFailureAsync(
                            repoPath, path: null, kind: "push",
                            title: $"Today sync: push failed in {RepoName(repoPath)}",
                            detail: f.Error,
                            cancellationToken).ConfigureAwait(false);
                        break;
                }
            }

            _logger.LogInformation(
                "Today sync: {Repo} committed={Committed} skipped={Skipped} failed={Failed} pushed={Pushed}",
                repoPath, committed, skipped, failed, pushed);
        }
        finally
        {
            sem.Release();
        }
    }

    private SemaphoreSlim GetOrCreateSemaphore(string repoPath)
    {
        lock (_gate)
        {
            if (!_perRepoSemaphores.TryGetValue(repoPath, out var sem))
            {
                sem = new SemaphoreSlim(1, 1);
                _perRepoSemaphores[repoPath] = sem;
            }
            return sem;
        }
    }

    private async Task TryNotifyFailureAsync(
        string repoPath,
        string? path,
        string kind,
        string title,
        string detail,
        CancellationToken cancellationToken)
    {
        if (_toasts is null) return;

        var key = $"{repoPath}|{path ?? string.Empty}|{kind}";
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_lastFailureToastAt.TryGetValue(key, out var last) &&
                now - last < FailureToastDedupWindow)
            {
                return;
            }
            _lastFailureToastAt[key] = now;
        }

        try
        {
            await _toasts.NotifyTodaySyncFailureAsync(title, detail, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Today syncer: failed to dispatch failure toast for {Key}", key);
        }
    }

    private static bool LooksLikeGitignoreFailure(string error) =>
        error.Contains("ignored by one of your .gitignore", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("paths are ignored", StringComparison.OrdinalIgnoreCase);

    private static string RepoName(string repoPath)
    {
        var name = Path.GetFileName(repoPath);
        return string.IsNullOrEmpty(name) ? repoPath : name;
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
            foreach (var sem in _perRepoSemaphores.Values)
            {
                try { sem.Dispose(); } catch { }
            }
            _perRepoSemaphores.Clear();
        }
        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
    }
}
