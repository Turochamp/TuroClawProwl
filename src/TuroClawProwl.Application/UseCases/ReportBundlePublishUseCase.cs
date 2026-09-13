using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.UseCases;

public sealed class ReportBundlePublishUseCase
{
    private static readonly TimeSpan RepeatToastWindow = TimeSpan.FromMinutes(10);

    private readonly ITrayView _tray;
    private readonly IToastService _toasts;
    private readonly IClock _clock;
    private readonly ILogger<ReportBundlePublishUseCase> _logger;

    private PublishHealth _health = new PublishHealth.NeverPublished();
    private DateTimeOffset? _lastToastAt;
    private string _lastToastKey = string.Empty;

    public ReportBundlePublishUseCase(
        ITrayView tray,
        IToastService toasts,
        IClock clock,
        ILogger<ReportBundlePublishUseCase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(tray);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(clock);

        _tray = tray;
        _toasts = toasts;
        _clock = clock;
        _logger = logger ?? NullLogger<ReportBundlePublishUseCase>.Instance;
    }

    public PublishHealth Health => _health;

    public async Task ExecuteAsync(
        BundlePublishResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var now = _clock.UtcNow;

        switch (result)
        {
            case BundlePublishResult.Success s:
                _health = new PublishHealth.Healthy(now);
                _lastToastAt = null;
                _lastToastKey = string.Empty;
                _logger.LogInformation(
                    "State bundle published {Sha} with {FileCount} files", s.CommitSha, s.FileCount);
                break;

            case BundlePublishResult.Misconfigured m:
                await FailAsync(
                    new PublishHealth.Failed(now, IsMisconfiguration: true, m.SettingName, m.Detail),
                    cancellationToken).ConfigureAwait(false);
                break;

            case BundlePublishResult.Transient t:
                await FailAsync(
                    new PublishHealth.Failed(now, IsMisconfiguration: false, string.Empty, t.Detail),
                    cancellationToken).ConfigureAwait(false);
                break;

            default:
                throw new ArgumentException(
                    $"Unknown publish result variant: {result.GetType().Name}", nameof(result));
        }

        await _tray.SetPublishHealthAsync(_health, cancellationToken).ConfigureAwait(false);
    }

    private async Task FailAsync(PublishHealth.Failed failed, CancellationToken cancellationToken)
    {
        _health = failed;

        _logger.LogWarning(
            "State bundle publish failed (misconfiguration {IsMisconfiguration}, setting {Setting}): {Detail}",
            failed.IsMisconfiguration, failed.SettingName, failed.Detail);

        var key = $"{failed.IsMisconfiguration}|{failed.SettingName}|{failed.Detail}";
        if (string.Equals(key, _lastToastKey, StringComparison.Ordinal) &&
            _lastToastAt is { } last &&
            failed.At - last < RepeatToastWindow)
        {
            return;
        }

        _lastToastKey = key;
        _lastToastAt = failed.At;

        try
        {
            await _toasts.NotifyBundlePublishFailureAsync(failed, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not dispatch the bundle publish failure toast");
        }
    }
}
