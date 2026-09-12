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

        await _report.ExecuteAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
