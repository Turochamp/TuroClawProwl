using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Infrastructure.Gateway;
using TuroClawProwl.Infrastructure.Google;

namespace TuroClawProwl.App;

// Applies a saved TuroClawProwlConfig to the running app's components
// without requiring a restart. Subscribes to IConfigStore.ConfigSaved
// in Program.cs. Diffs the previous config against the new one and
// only touches components whose inputs changed. Serializes calls
// through a SemaphoreSlim so two rapid Saves can't interleave watcher
// rebuilds.
public sealed class LiveConfigApplier : IDisposable
{
    private readonly HttpGatewayClient? _httpGateway;
    private readonly OpenControlUiUseCase _openControlUi;
    private readonly RestartGatewayUseCase _restartUseCase;
    private readonly AppOrchestrator _orchestrator;
    private readonly StateBundleSyncerHandle _bundleSyncerHandle;
    private readonly GwsWorkspaceReader? _googleReader;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<LiveConfigApplier> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private TuroClawProwlConfig _current;

    public LiveConfigApplier(
        TuroClawProwlConfig initialConfig,
        IGatewayClient gatewayClient,
        OpenControlUiUseCase openControlUi,
        RestartGatewayUseCase restartUseCase,
        AppOrchestrator orchestrator,
        StateBundleSyncerHandle bundleSyncerHandle,
        GwsWorkspaceReader? googleReader,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(initialConfig);
        ArgumentNullException.ThrowIfNull(gatewayClient);
        ArgumentNullException.ThrowIfNull(openControlUi);
        ArgumentNullException.ThrowIfNull(restartUseCase);
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(bundleSyncerHandle);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _current = initialConfig;
        _httpGateway = gatewayClient as HttpGatewayClient;
        _openControlUi = openControlUi;
        _restartUseCase = restartUseCase;
        _orchestrator = orchestrator;
        _bundleSyncerHandle = bundleSyncerHandle;
        _googleReader = googleReader;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<LiveConfigApplier>();
    }

    public async Task ApplyAsync(TuroClawProwlConfig newConfig, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newConfig);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = _current;
            var changes = new List<string>();

            var gatewayUrlChanged = !string.Equals(
                previous.GatewayUrl, newConfig.GatewayUrl, StringComparison.Ordinal);
            if (gatewayUrlChanged && !string.IsNullOrWhiteSpace(newConfig.GatewayUrl))
            {
                if (Uri.TryCreate(newConfig.GatewayUrl, UriKind.Absolute, out var newUri))
                {
                    _httpGateway?.SetBaseAddress(newUri);
                    _openControlUi.SetGatewayBaseUrl(newUri);
                    changes.Add("gatewayUrl");
                }
                else
                {
                    _logger.LogWarning(
                        "Live reload skipped invalid gatewayUrl {Url}", newConfig.GatewayUrl);
                }
            }

            if (!string.Equals(previous.SshHost, newConfig.SshHost, StringComparison.Ordinal) ||
                !string.Equals(previous.SshUser, newConfig.SshUser, StringComparison.Ordinal))
            {
                _restartUseCase.SetTarget(new SshTarget(newConfig.SshHost, newConfig.SshUser));
                changes.Add("ssh");
            }

            if (previous.PollInterval != newConfig.PollInterval)
            {
                _orchestrator.SetPollInterval(newConfig.PollInterval);
                changes.Add("pollInterval");
            }

            if (!string.Equals(previous.GwsExecutablePath, newConfig.GwsExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                _googleReader?.SetExecutablePath(newConfig.GwsExecutablePath);
                changes.Add("gwsExecutablePath");
            }

            var sourcesChanged =
                !string.Equals(previous.HubRepoPath, newConfig.HubRepoPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previous.TodayCcaRoot, newConfig.TodayCcaRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previous.BundleWorktreePath, newConfig.BundleWorktreePath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previous.BundlePublishBranch, newConfig.BundlePublishBranch, StringComparison.Ordinal) ||
                !string.Equals(previous.GwsExecutablePath, newConfig.GwsExecutablePath, StringComparison.OrdinalIgnoreCase) ||
                previous.SnapshotInterval != newConfig.SnapshotInterval ||
                previous.HeartbeatInterval != newConfig.HeartbeatInterval;

            if (sourcesChanged)
            {
                var newPaths = BundleSourcePathsResolver.Resolve(newConfig, _loggerFactory);
                await _bundleSyncerHandle
                    .RebuildAsync(
                        newPaths,
                        ConfigIntervals.EffectiveSnapshotInterval(newConfig),
                        ConfigIntervals.EffectiveHeartbeatInterval(newConfig),
                        cancellationToken)
                    .ConfigureAwait(false);
                await _orchestrator.SetTrackedFilesAsync(
                    TodayPathsResolver.ToOrchestratorPairs(newPaths)).ConfigureAwait(false);
                changes.Add("bundleSources");
            }

            _current = newConfig;

            if (changes.Count == 0)
            {
                _logger.LogDebug("Config saved with no live-reloadable changes");
                return;
            }

            _logger.LogInformation("Live-reloaded config fields: {Fields}", string.Join(", ", changes));

            // Trigger an immediate poll so the user sees the impact of a
            // gateway/ssh change without waiting for the next timer tick.
            if (gatewayUrlChanged)
            {
                try { await _orchestrator.PollNowAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogWarning(ex, "Immediate poll after live reload threw"); }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}

// Owns the StateBundleSyncer lifetime so LiveConfigApplier can rebuild it when
// the hub path or publish settings change.
public sealed class StateBundleSyncerHandle : IDisposable
{
    private readonly PublishAndReportStateBundleUseCase _publishAndReport;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private StateBundleRequest _request;
    private StateBundleSyncer? _current;

    public StateBundleSyncerHandle(
        StateBundleRequest request,
        PublishAndReportStateBundleUseCase publishAndReport,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(publishAndReport);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _request = request;
        _publishAndReport = publishAndReport;
        _loggerFactory = loggerFactory;
    }

    // Started even with no watched paths: the interval trigger still has to
    // refresh the cloud snapshots.
    public void Start(IReadOnlyList<string> watchedPaths, TimeSpan snapshotInterval)
    {
        ArgumentNullException.ThrowIfNull(watchedPaths);

        _current = new StateBundleSyncer(
            watchedPaths, _request, snapshotInterval, _publishAndReport,
            _loggerFactory.CreateLogger<StateBundleSyncer>());
        _current.Start();
    }

    public Task PublishNowAsync(CancellationToken cancellationToken = default) =>
        _current?.PublishNowAsync(cancellationToken) ?? Task.CompletedTask;

    public async Task RebuildAsync(
        IReadOnlyList<string> watchedPaths,
        TimeSpan snapshotInterval,
        TimeSpan heartbeatInterval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(watchedPaths);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _request = _request with { HeartbeatInterval = heartbeatInterval };

            // The syncer holds the request, so a changed heartbeat means a new
            // syncer rather than a restart of the old one. The use case, the
            // git worktree and the heartbeat timestamp are shared with the
            // replacement. Dispose (StateBundleSyncer.Dispose) cancels the old
            // syncer's in-flight publish and stops it from *waiting* -- it
            // does not kill an already-spawned git subprocess
            // (ProcessRunner.RunAsync cancels the wait on the process via
            // WaitForExitAsync, not the process itself), so an abandoned
            // `git push` can briefly keep running against the worktree after
            // Dispose returns. What actually keeps the old and new syncers
            // from colliding on that worktree is that Start() below does not
            // publish immediately: the replacement's earliest publish is at
            // least a 60s debounce after a watched file changes, or its
            // first snapshot-interval tick -- a gap the abandoned subprocess
            // has to finish naturally in. That makes Start() not publishing
            // on its own load-bearing, not a cosmetic minor: making this
            // method (or Start) publish right away would reopen the race
            // this comment describes.
            _current?.Dispose();
            _current = null;
            Start(watchedPaths, snapshotInterval);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _current?.Dispose();
        _current = null;
        _gate.Dispose();
    }
}
