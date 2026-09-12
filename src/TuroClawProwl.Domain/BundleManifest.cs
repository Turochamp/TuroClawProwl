using System.Text.Json.Serialization;

namespace TuroClawProwl.Domain;

// Path is where the source lives in the working tree; BundlePath is where its
// copy lives inside hub/bundle/. Both are stated so the renderer never infers a
// file location from an id.
//
// Modified is when the content last changed. Verified is when it was last
// confirmed current. They are equal for a file source and diverge for a snapshot
// the moment a re-read returns identical content; collapsing them would either
// claim freshness the content does not have or call correct data stale. A failed
// read advances neither.
//
// Committed is the author date of the last commit touching the source, and is
// null where the file is untracked or its directory is not a repository at all.
public sealed record BundleSource(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("bundle_path")] string BundlePath,
    [property: JsonPropertyName("modified")] DateTimeOffset Modified,
    [property: JsonPropertyName("verified")] DateTimeOffset Verified,
    [property: JsonPropertyName("committed")] DateTimeOffset? Committed,
    [property: JsonPropertyName("dirty")] bool Dirty);

public sealed record BundleFailure(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record BundleManifest(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("publisher")] string Publisher,
    [property: JsonPropertyName("source_branch")] string SourceBranch,
    [property: JsonPropertyName("sources")] IReadOnlyList<BundleSource> Sources,
    [property: JsonPropertyName("failures")] IReadOnlyList<BundleFailure> Failures)
{
    // The renderer treats any other value as a hard stop, so this is never
    // omitted and never defaulted away.
    public const int CurrentSchemaVersion = 1;
}
