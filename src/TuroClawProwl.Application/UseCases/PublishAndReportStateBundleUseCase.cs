using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Application.UseCases;

public sealed class PublishAndReportStateBundleUseCase
{
    private readonly PublishStateBundleUseCase _publish;
    private readonly ReportBundlePublishUseCase _report;

    public PublishAndReportStateBundleUseCase(
        PublishStateBundleUseCase publish,
        ReportBundlePublishUseCase report)
    {
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(report);

        _publish = publish;
        _report = report;
    }

    public async Task<BundlePublishResult> ExecuteAsync(
        StateBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _publish.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        await _report.ExecuteAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }
}
