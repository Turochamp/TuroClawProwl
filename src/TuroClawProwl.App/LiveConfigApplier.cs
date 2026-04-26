using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Infrastructure.Gateway;

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
    private readonly TodaySyncerHandle _todaySyncerHandle;
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
        TodaySyncerHandle todaySyncerHandle,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(initialConfig);
        ArgumentNullException.ThrowIfNull(gatewayClient);
        ArgumentNullException.ThrowIfNull(openControlUi);
        ArgumentNullException.ThrowIfNull(restartUseCase);
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(todaySyncerHandle);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _current = initialConfig;
        _httpGateway = gatewayClient as HttpGatewayClient;
        _openControlUi = openControlUi;
        _restartUseCase = restartUseCase;
        _orchestrator = orchestrator;
        _todaySyncerHandle = todaySyncerHandle;
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

            var todayChanged =
                !string.Equals(previous.TodaySkillPath, newConfig.TodaySkillPath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previous.TodayCcaRoot, newConfig.TodayCcaRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(previous.TodayCrmIndexPath, newConfig.TodayCrmIndexPath, StringComparison.OrdinalIgnoreCase);

            if (todayChanged)
            {
                var newTrackedPaths = TodayPathsResolver.Resolve(newConfig, _loggerFactory);
                await _todaySyncerHandle.RebuildAsync(newTrackedPaths, cancellationToken).ConfigureAwait(false);
                await _orchestrator.SetTrackedFilesAsync(
                    TodayPathsResolver.ToOrchestratorPairs(newTrackedPaths)).ConfigureAwait(false);
                changes.Add("today");
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

// Owns the TodaySyncer lifetime so LiveConfigApplier can rebuild it
// when Today config changes. Program.cs hands ownership here instead
// of using `using var todaySyncer = ...` directly.
public sealed class TodaySyncerHandle : IDisposable
{
    private readonly IGitRunner _git;
    private readonly IToastService _toasts;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TodaySyncer? _current;

    public TodaySyncerHandle(IGitRunner git, IToastService toasts, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _git = git;
        _toasts = toasts;
        _loggerFactory = loggerFactory;
    }

    public void Start(IReadOnlyList<string> trackedPaths)
    {
        ArgumentNullException.ThrowIfNull(trackedPaths);
        if (trackedPaths.Count == 0) return;

        _current = new TodaySyncer(trackedPaths, _git, _toasts, _loggerFactory.CreateLogger<TodaySyncer>());
        _current.Start();
    }

    public async Task RebuildAsync(IReadOnlyList<string> trackedPaths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trackedPaths);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_current is null)
            {
                if (trackedPaths.Count > 0) Start(trackedPaths);
                return;
            }

            if (trackedPaths.Count == 0)
            {
                _current.Dispose();
                _current = null;
                return;
            }

            await _current.RestartWithPathsAsync(trackedPaths, cancellationToken).ConfigureAwait(false);
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
