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

    private readonly StateBundleRequest _request;
    private readonly PublishAndReportStateBundleUseCase _publishAndReport;
    private readonly ILogger<StateBundleSyncer> _logger;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly SemaphoreSlim _publishGate = new(1, 1);
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
        await _publishGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _publishAndReport.ExecuteAsync(_request, cancellationToken).ConfigureAwait(false);
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

        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
        _publishGate.Dispose();
    }
}
