namespace TuroClawProwl.Application.Ports;

public interface IBundlePublisher
{
    Task<string> GetSourceBranchAsync(CancellationToken cancellationToken = default);

    Task<BundlePublishResult> PublishAsync(BundlePayload payload, CancellationToken cancellationToken = default);
}
