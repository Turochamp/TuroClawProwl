namespace TuroClawProwl.Application.Ports;

// ContentReadAt is the timestamp of the read that first produced this exact
// content, and becomes the manifest's `modified`. VerifiedAt is the timestamp of
// the most recent successful read of any kind, and becomes `verified`. A
// byte-identical re-read advances only VerifiedAt; a failed read advances
// neither. Both are persisted so a restart does not lose either answer.
public sealed record StoredSnapshot(
    string Id,
    string Content,
    DateTimeOffset ContentReadAt,
    DateTimeOffset VerifiedAt);

public interface ISnapshotStore
{
    Task<StoredSnapshot?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task SaveAsync(StoredSnapshot snapshot, CancellationToken cancellationToken = default);
}
