namespace TuroClawProwl.Domain;

public static class RepoStateClassifier
{
    public static RepoState Classify(PorcelainSummary porcelain, int unpushedCount, bool hasUpstream)
    {
        ArgumentNullException.ThrowIfNull(porcelain);
        ArgumentOutOfRangeException.ThrowIfNegative(unpushedCount);

        return new RepoState(
            UnpushedCount: unpushedCount,
            HasUncommitted: porcelain.HasUncommitted,
            HasUpstream: hasUpstream);
    }
}
