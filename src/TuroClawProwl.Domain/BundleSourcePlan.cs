namespace TuroClawProwl.Domain;

public static class BundleSourcePlan
{
    // No hub file is published any more; the registry is watched only because a
    // change to its calendar table changes what the calendar window reads.
    public static IReadOnlyList<string> WatchedRelativePaths { get; } = [BundleLayout.RegistryRelativePath];
}
