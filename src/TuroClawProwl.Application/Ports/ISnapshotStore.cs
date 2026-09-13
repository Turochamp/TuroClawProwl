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

// Distinguishes "nothing has ever been stored for this id" from "a snapshot is
// stored but could not be read back" -- collapsing the two into a single null
// is what let a corrupt or locked snapshot file be treated as a first read
// (see PublishStateBundleUseCase.CollectSnapshotAsync): the caller must never
// be able to mistake Unreadable for NotFound, because only NotFound is safe to
// treat as "there is nothing to compare against yet".
public abstract record SnapshotReadResult
{
    private SnapshotReadResult()
    {
    }

    public sealed record Found(StoredSnapshot Snapshot) : SnapshotReadResult;

    public sealed record NotFound : SnapshotReadResult;

    public sealed record Unreadable(string Reason) : SnapshotReadResult;
}

public interface ISnapshotStore
{
    Task<SnapshotReadResult> GetAsync(string id, CancellationToken cancellationToken = default);

    Task SaveAsync(StoredSnapshot snapshot, CancellationToken cancellationToken = default);
}
