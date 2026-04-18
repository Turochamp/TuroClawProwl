namespace TuroClawProwl.Domain;

public sealed record RepoState(int UnpushedCount, bool HasUncommitted, bool HasUpstream)
{
    public bool IsClean => UnpushedCount == 0 && !HasUncommitted && HasUpstream;

    public bool NeedsAttention => !IsClean;
}
