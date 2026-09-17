using System.Text.Json.Serialization;

namespace TuroClawProwl.Domain;

// Path is the source's API identity; BundlePath is where its copy lives inside
// hub/bundle/. Both are stated so the renderer never infers a file location from
// an id.
//
// Modified is when the content last changed. Verified is when it was last
// confirmed current. They diverge the moment a re-read returns identical
// content; collapsing them would either claim freshness the content does not
// have or call correct data stale. A failed read advances neither.
//
// Committed and Dirty described hub file sources, which are no longer published.
// They stay in schema version 1 so existing readers keep parsing, and every
// snapshot source carries null and false.
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
