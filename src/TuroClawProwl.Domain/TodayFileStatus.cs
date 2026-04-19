namespace TuroClawProwl.Domain;

public sealed record TodayFileStatus(
    string Path,
    string RepoPath,
    bool HasUncommitted,
    bool IsUnpushed)
{
    public bool IsSynced => !HasUncommitted && !IsUnpushed;

    public bool NeedsAttention => !IsSynced;
}
