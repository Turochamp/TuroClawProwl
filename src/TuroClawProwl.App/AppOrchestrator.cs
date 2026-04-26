using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;

namespace TuroClawProwl.App;

public sealed class AppOrchestrator : IDisposable
{
    private static readonly TimeSpan TodayStatusInterval = TimeSpan.FromSeconds(30);

    private readonly HandleHealthPollUseCase _healthUseCase;
    private readonly ResolveTodayFileStatusesUseCase _resolveTodayUseCase;
    private readonly PushTodayFilesUseCase _pushUseCase;
    private readonly OpenControlUiUseCase _openControlUiUseCase;
    private readonly RestartGatewayUseCase _restartUseCase;
    private readonly ILogger<AppOrchestrator> _logger;

    private readonly System.Windows.Forms.Timer _pollTimer = new();
    private readonly System.Windows.Forms.Timer _todayTimer = new();
    private IReadOnlyList<(string AbsolutePath, string RepoPath)> _trackedFiles;
    private IReadOnlyList<TodayFileStatus> _lastStatuses = Array.Empty<TodayFileStatus>();
    private int _resolveInFlight;
    private int _pollInFlight;

    public AppOrchestrator(
        TuroClawProwlConfig config,
        HandleHealthPollUseCase healthUseCase,
        ResolveTodayFileStatusesUseCase resolveTodayUseCase,
        PushTodayFilesUseCase pushUseCase,
        OpenControlUiUseCase openControlUiUseCase,
        RestartGatewayUseCase restartUseCase,
        IReadOnlyList<(string AbsolutePath, string RepoPath)> trackedFiles,
        ILogger<AppOrchestrator>? logger = null)
    {
        _healthUseCase = healthUseCase;
        _resolveTodayUseCase = resolveTodayUseCase;
        _pushUseCase = pushUseCase;
        _openControlUiUseCase = openControlUiUseCase;
        _restartUseCase = restartUseCase;
        _trackedFiles = trackedFiles;
        _logger = logger ?? NullLogger<AppOrchestrator>.Instance;

        _pollTimer.Interval = ClampInterval(config.PollInterval);
        _pollTimer.Tick += async (_, _) => await PollHealthOnceAsync();

        _todayTimer.Interval = (int)TodayStatusInterval.TotalMilliseconds;
        _todayTimer.Tick += async (_, _) => await ResolveTodayStatusesAsync();
    }

    public async Task StartAsync()
    {
        await PollHealthOnceAsync();
        _pollTimer.Start();

        if (_trackedFiles.Count > 0)
        {
            await ResolveTodayStatusesAsync();
            _todayTimer.Start();
        }
    }

    public async Task PushTodayFilesAsync()
    {
        await _pushUseCase.ExecuteAsync(_lastStatuses);
        await ResolveTodayStatusesAsync();
    }

    public Task OpenControlUiAsync() => _openControlUiUseCase.ExecuteAsync();

    public Task RestartGatewayAsync() => _restartUseCase.ExecuteAsync();

    // Used by LiveConfigApplier after the GatewayUrl changes so the user
    // sees an immediate Healthy/Unreachable update without waiting up to
    // PollInterval.
    public Task PollNowAsync() => PollHealthOnceAsync();

    public void SetPollInterval(TimeSpan pollInterval)
    {
        var newInterval = ClampInterval(pollInterval);
        if (_pollTimer.Interval == newInterval) return;
        _pollTimer.Interval = newInterval;
        _logger.LogInformation("Poll interval updated to {Interval}", pollInterval);
    }

    public async Task SetTrackedFilesAsync(
        IReadOnlyList<(string AbsolutePath, string RepoPath)> trackedFiles)
    {
        ArgumentNullException.ThrowIfNull(trackedFiles);
        _trackedFiles = trackedFiles;

        if (trackedFiles.Count == 0)
        {
            _todayTimer.Stop();
            _lastStatuses = Array.Empty<TodayFileStatus>();
            return;
        }

        await ResolveTodayStatusesAsync();
        _todayTimer.Start();
    }

    private async Task PollHealthOnceAsync()
    {
        // Retries inside HttpGatewayClient can stretch a single poll past PollInterval;
        // this guard prevents the WinForms timer from stacking concurrent polls.
        if (Interlocked.Exchange(ref _pollInFlight, 1) == 1) return;
        try { await _healthUseCase.ExecuteAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Health poll threw"); }
        finally { Interlocked.Exchange(ref _pollInFlight, 0); }
    }

    private async Task ResolveTodayStatusesAsync()
    {
        if (Interlocked.Exchange(ref _resolveInFlight, 1) == 1) return;
        try
        {
            _lastStatuses = await _resolveTodayUseCase.ExecuteAsync(_trackedFiles);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Today file status resolve threw"); }
        finally { Interlocked.Exchange(ref _resolveInFlight, 0); }
    }

    private static int ClampInterval(TimeSpan interval) =>
        Math.Max(1000, (int)interval.TotalMilliseconds);

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
        _todayTimer.Stop();
        _todayTimer.Dispose();
    }
}
