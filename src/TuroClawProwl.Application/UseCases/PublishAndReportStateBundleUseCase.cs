using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Application.UseCases;

public sealed class PublishAndReportStateBundleUseCase
{
    private readonly PublishStateBundleUseCase _publish;
    private readonly ReportBundlePublishUseCase _report;
    private readonly ILogger<PublishAndReportStateBundleUseCase> _logger;

    public PublishAndReportStateBundleUseCase(
        PublishStateBundleUseCase publish,
        ReportBundlePublishUseCase report,
        ILogger<PublishAndReportStateBundleUseCase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(report);

        _publish = publish;
        _report = report;
        _logger = logger ?? NullLogger<PublishAndReportStateBundleUseCase>.Instance;
    }

    // A publish that throws (git or gws missing from PATH, a snapshot-store IO
    // fault, an unknown-variant bug in the collector) must still reach the
    // tray and the toast -- the entire point of pairing publish with report is
    // that an attempt always ends in a reported outcome. Silently losing that
    // outcome to an unobserved exception is the exact defect this project
    // exists to remove, so a throw here is downgraded to a reported Transient
    // failure rather than left to escape. Cancellation is not a failure and
    // is left to propagate unreported, so a superseded debounce or a
    // shutdown drain behaves the same as before.
    public async Task<BundlePublishResult> ExecuteAsync(
        StateBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        BundlePublishResult result;
        try
        {
            result = await _publish.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "State bundle publish threw before it could produce a result");
            result = new BundlePublishResult.Transient(ex.Message);
        }

        // Reporting is the last stop for a publish attempt, so it gets the
        // same guard as the publish step: an unknown-variant bug in
        // ReportBundlePublishUseCase, or a tray post that throws because the
        // UI thread is already gone at shutdown, must not escape into a
        // fire-and-forget caller with the outcome never logged anywhere.
        // Cancellation still propagates -- it is not a failure to report.
        try
        {
            await _report.ExecuteAsync(result, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "State bundle publish result {Result} could not be reported", result.GetType().Name);
        }

        return result;
    }
}
