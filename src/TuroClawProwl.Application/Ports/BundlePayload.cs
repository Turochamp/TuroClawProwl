using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Ports;

public sealed record BundleFile(string RelativePath, string Content);

// PublishEvenIfUnchanged is the heartbeat: normally the publisher commits only
// when a bundle FILE changed, because published_at and the verified timestamps
// move on every run and would otherwise commit hourly. On a heartbeat tick a
// manifest-only commit is exactly what is wanted, so the verified timestamps on
// record do not go stale.
public sealed record BundlePayload(
    IReadOnlyList<BundleFile> Files,
    BundleManifest Manifest,
    bool PublishEvenIfUnchanged);
