using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application;
using TuroClawProwl.Application.UseCases;

namespace TuroClawProwl.App;

public sealed class AppOrchestrator : IDisposable
{
    private readonly HandleHealthPollUseCase _healthUseCase;
    private readonly OpenControlUiUseCase _openControlUiUseCase;
    private readonly RestartGatewayUseCase _restartUseCase;
    private readonly ILogger<AppOrchestrator> _logger;

    private readonly System.Windows.Forms.Timer _pollTimer = new();
    private int _pollInFlight;

    public AppOrchestrator(
        TuroClawProwlConfig config,
        HandleHealthPollUseCase healthUseCase,
        OpenControlUiUseCase openControlUiUseCase,
        RestartGatewayUseCase restartUseCase,
        ILogger<AppOrchestrator>? logger = null)
    {
        _healthUseCase = healthUseCase;
        _openControlUiUseCase = openControlUiUseCase;
        _restartUseCase = restartUseCase;
        _logger = logger ?? NullLogger<AppOrchestrator>.Instance;

        _pollTimer.Interval = ClampInterval(config.PollInterval);
        _pollTimer.Tick += async (_, _) => await PollHealthOnceAsync();
    }

    public async Task StartAsync()
    {
        await PollHealthOnceAsync();
        _pollTimer.Start();
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

    private async Task PollHealthOnceAsync()
    {
        // Retries inside HttpGatewayClient can stretch a single poll past PollInterval;
        // this guard prevents the WinForms timer from stacking concurrent polls.
        if (Interlocked.Exchange(ref _pollInFlight, 1) == 1) return;
        try { await _healthUseCase.ExecuteAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Health poll threw"); }
        finally { Interlocked.Exchange(ref _pollInFlight, 0); }
    }

    private static int ClampInterval(TimeSpan interval) =>
        Math.Max(1000, (int)interval.TotalMilliseconds);

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
    }
}
