using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;

namespace TuroClawProwl.App;

public sealed class AppOrchestrator : IDisposable
{
    private readonly TuroClawProwlConfig _config;
    private readonly HandleHealthPollUseCase _healthUseCase;
    private readonly ReconcileRepoStatesUseCase _reconcileUseCase;
    private readonly PushUnpushedReposUseCase _pushUseCase;
    private readonly OpenTuiUseCase _openTuiUseCase;
    private readonly RestartGatewayUseCase _restartUseCase;
    private readonly IRepoWatcher _watcher;
    private readonly ILogger<AppOrchestrator> _logger;

    private readonly System.Windows.Forms.Timer _pollTimer = new();
    private IReadOnlyDictionary<string, RepoStateSnapshot> _lastStates = new Dictionary<string, RepoStateSnapshot>();
    private int _reconcileInFlight;

    public AppOrchestrator(
        TuroClawProwlConfig config,
        HandleHealthPollUseCase healthUseCase,
        ReconcileRepoStatesUseCase reconcileUseCase,
        PushUnpushedReposUseCase pushUseCase,
        OpenTuiUseCase openTuiUseCase,
        RestartGatewayUseCase restartUseCase,
        IRepoWatcher watcher,
        ILogger<AppOrchestrator>? logger = null)
    {
        _config = config;
        _healthUseCase = healthUseCase;
        _reconcileUseCase = reconcileUseCase;
        _pushUseCase = pushUseCase;
        _openTuiUseCase = openTuiUseCase;
        _restartUseCase = restartUseCase;
        _watcher = watcher;
        _logger = logger ?? NullLogger<AppOrchestrator>.Instance;

        _pollTimer.Interval = Math.Max(1000, (int)_config.PollInterval.TotalMilliseconds);
        _pollTimer.Tick += async (_, _) => await PollHealthOnceAsync();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await PollHealthOnceAsync();
        _pollTimer.Start();

        if (!string.IsNullOrWhiteSpace(_config.ReposRoot) && Directory.Exists(_config.ReposRoot))
        {
            _watcher.Changed += async (_, _) => await ReconcileRepoStatesAsync();
            _watcher.Start(_config.ReposRoot);
            await ReconcileRepoStatesAsync();
        }
    }

    public async Task PushUnpushedAsync()
    {
        var states = _lastStates.ToDictionary(kv => kv.Key, kv => kv.Value.ToRepoState());
        await _pushUseCase.ExecuteAsync(states);
        await ReconcileRepoStatesAsync();
    }

    public Task OpenTuiAsync() => _openTuiUseCase.ExecuteAsync();

    public Task RestartGatewayAsync() => _restartUseCase.ExecuteAsync();

    private async Task PollHealthOnceAsync()
    {
        try { await _healthUseCase.ExecuteAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Health poll threw"); }
    }

    private async Task ReconcileRepoStatesAsync()
    {
        if (Interlocked.Exchange(ref _reconcileInFlight, 1) == 1) return;
        try
        {
            var states = await _reconcileUseCase.ExecuteAsync(_config.ReposRoot);
            _lastStates = states.ToDictionary(
                kv => kv.Key,
                kv => new RepoStateSnapshot(kv.Value.UnpushedCount, kv.Value.HasUncommitted, kv.Value.HasUpstream));
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Reconcile threw"); }
        finally { Interlocked.Exchange(ref _reconcileInFlight, 0); }
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
        _watcher.Dispose();
    }

    private sealed record RepoStateSnapshot(int UnpushedCount, bool HasUncommitted, bool HasUpstream)
    {
        public RepoState ToRepoState() => new(UnpushedCount, HasUncommitted, HasUpstream);
    }
}
