using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application;
using TuroClawProwl.Application.UseCases;

namespace TuroClawProwl.App;

// Two triggers, one publish path. A watched file change debounces for 60s; the
// interval timer covers the cloud snapshots, which no local file change can
// signal — nothing on this disk moves when a task is added on a phone. Unlike
// the retired TodaySyncer this never commits in the user's own repository: the
// publisher owns its worktree, so a source edit publishes as its working-tree
// content and the user's branch is left alone.
public sealed class StateBundleSyncer : IDisposable
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinimumSnapshotInterval = TimeSpan.FromMinutes(5);

    // Bounds Dispose's drain (see Dispose). Long enough to cover a healthy
    // publish's git add/commit/push and its gws task/calendar reads without
    // false-positive abandonment; short enough that an app exit or a
    // config-triggered rebuild that lands mid-publish is a brief pause, not
    // a hang. Disposal also cancels _disposalCts first, so in the common
    // case (the publish is simply waiting to post its result to a tray that
    // can no longer pump) the drain finishes almost immediately and this
    // timeout is only the fallback for a publish that does not unwind on
    // cancellation, e.g. one stuck inside an unresponsive git/gws process.
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    private readonly StateBundleRequest _request;
    private readonly PublishAndReportStateBundleUseCase _publishAndReport;
    private readonly ILogger<StateBundleSyncer> _logger;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly Lock _gate = new();

    private IReadOnlyList<string> _watchedPaths;
    private TimeSpan _snapshotInterval;
    private CancellationTokenSource? _debounce;
    private CancellationTokenSource? _intervalLoop;
    private bool _disposed;

    public StateBundleSyncer(
        IReadOnlyList<string> watchedPaths,
        StateBundleRequest request,
        TimeSpan snapshotInterval,
        PublishAndReportStateBundleUseCase publishAndReport,
        ILogger<StateBundleSyncer>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(watchedPaths);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(publishAndReport);

        _watchedPaths = watchedPaths;
        _request = request;
        _snapshotInterval = snapshotInterval < MinimumSnapshotInterval
            ? MinimumSnapshotInterval
            : snapshotInterval;
        _publishAndReport = publishAndReport;
        _logger = logger ?? NullLogger<StateBundleSyncer>.Instance;
    }

    public void Start()
    {
        StartIntervalLoop();

        if (_watchedPaths.Count == 0)
        {
            _logger.LogInformation(
                "State bundle syncer has no watched paths; refreshing snapshots every {Interval}",
                _snapshotInterval);
            return;
        }

        var directories = _watchedPaths
            .Select(Path.GetDirectoryName)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir!))
            {
                _logger.LogWarning("State bundle syncer: watched directory missing {Directory}", dir);
                continue;
            }

            var watcher = new FileSystemWatcher(dir!)
            {
                InternalBufferSize = 65536,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            watcher.Changed += OnSourceChanged;
            watcher.Created += OnSourceChanged;
            watcher.Renamed += OnSourceChanged;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }

        _logger.LogInformation(
            "State bundle syncer watching {Count} sources across {WatcherCount} directories, " +
            "refreshing snapshots every {Interval}",
            _watchedPaths.Count, _watchers.Count, _snapshotInterval);
    }

    private void StartIntervalLoop()
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed) return;
            CancelIntervalLoop();
            _intervalLoop = new CancellationTokenSource();
            token = _intervalLoop.Token;
        }

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(_snapshotInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    _logger.LogDebug("State bundle syncer: snapshot interval elapsed");
                    await PublishNowAsync(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // shutting down or being rebuilt
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "State bundle snapshot interval threw");
            }
        }, CancellationToken.None);
    }

    public async Task PublishNowAsync(CancellationToken cancellationToken = default)
    {
        // Linked so Dispose has something to cancel out of rather than
        // something to outlast, no matter which token (if any) the caller
        // passed -- including the startup call, which passes none.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposalCts.Token);
        var token = linked.Token;

        await _publishGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await _publishAndReport.ExecuteAsync(_request, token).ConfigureAwait(false);
        }
        finally
        {
            _publishGate.Release();
        }
    }

    private void OnSourceChanged(object sender, FileSystemEventArgs e)
    {
        if (!_watchedPaths.Any(p => string.Equals(p, e.FullPath, StringComparison.OrdinalIgnoreCase)))
            return;

        _logger.LogDebug("State bundle syncer: source changed {Path}", e.FullPath);

        CancellationToken token;
        lock (_gate)
        {
            if (_disposed) return;
            CancelDebounce();
            _debounce = new CancellationTokenSource();
            token = _debounce.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceWindow, token).ConfigureAwait(false);
                await PublishNowAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer debounce window
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "State bundle publish threw");
            }
        }, CancellationToken.None);
    }

    private void CancelDebounce()
    {
        if (_debounce is null) return;
        try { _debounce.Cancel(); _debounce.Dispose(); } catch { }
        _debounce = null;
    }

    private void CancelIntervalLoop()
    {
        if (_intervalLoop is null) return;
        try { _intervalLoop.Cancel(); _intervalLoop.Dispose(); } catch { }
        _intervalLoop = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            CancelDebounce();
            CancelIntervalLoop();
        }

        // Trip any in-flight PublishNowAsync call before draining, including
        // one started with CancellationToken.None (the startup publish). An
        // in-flight publish's last step is posting its result to the tray,
        // which awaits the WinForms synchronization context; if Dispose runs
        // on the UI thread (app exit, or a Settings save that lands while
        // the startup publish is still running) that context can never pump
        // again, so an unbounded wait below would deadlock the very thread
        // the publish is waiting on. Cancelling first gives that await
        // something to unwind from instead.
        _disposalCts.Cancel();

        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();

        // Drain, bounded by DrainTimeout: a publish that is already past
        // WaitAsync and mid-flight must finish -- and hit its own finally's
        // Release -- before the gate goes away, or that Release throws
        // ObjectDisposedException, which would surface as a spurious error on
        // every app exit or config-triggered rebuild that lands mid-publish.
        // The cancellation above should make this fast in the common case;
        // the timeout is the fallback for a publish that does not unwind on
        // cancellation (e.g. one stuck inside an unresponsive git/gws
        // process) -- outliving the timeout is logged and the gate is
        // disposed anyway, since blocking the UI thread forever is worse
        // than an occasional abandoned-publish log line. This also means
        // StateBundleSyncerHandle.RebuildAsync can rely on Dispose to
        // guarantee this syncer's git work is finished (or abandoned) before
        // the replacement, which shares the same publish use case and
        // worktree, is started, so the two can never race the same worktree
        // for longer than DrainTimeout.
        if (_publishGate.Wait(DrainTimeout))
        {
            _publishGate.Release();
        }
        else
        {
            _logger.LogWarning(
                "State bundle syncer: a publish was still in flight after {Timeout}; disposing anyway",
                DrainTimeout);
        }

        _publishGate.Dispose();
        _disposalCts.Dispose();
    }
}
