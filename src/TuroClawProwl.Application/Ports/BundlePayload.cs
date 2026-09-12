using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Ports;

public sealed record BundleFile(string RelativePath, string Content);

public sealed record BundlePayload(IReadOnlyList<BundleFile> Files, BundleManifest Manifest);
