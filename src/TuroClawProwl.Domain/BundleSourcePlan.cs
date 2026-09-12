namespace TuroClawProwl.Domain;

public sealed record BundleSourcePlanItem(
    string Id,
    string SourceRelativePath,
    string BundleRelativePath);

public static class BundleSourcePlan
{
    public static IReadOnlyList<BundleSourcePlanItem> FixedSources(DateTimeOffset localNow) =>
    [
        new BundleSourcePlanItem(
            BundleLayout.RegistrySourceId,
            BundleLayout.RegistryRelativePath,
            BundleLayout.RegistryBundlePath),
        new BundleSourcePlanItem(
            BundleLayout.CrmIndexSourceId,
            BundleLayout.CrmIndexRelativePath,
            BundleLayout.CrmIndexBundlePath),
        new BundleSourcePlanItem(
            BundleLayout.WeeklySourceId(localNow),
            BundleLayout.WeeklyRelativePath(localNow),
            BundleLayout.WeeklyBundlePath(localNow)),
    ];

    public static IReadOnlyList<BundleSourcePlanItem> FromRegistry(string registryMarkdown)
    {
        ArgumentNullException.ThrowIfNull(registryMarkdown);

        return RegistryParser.ParseActiveSet(registryMarkdown)
            .Select(e => new BundleSourcePlanItem(
                BundleLayout.CcaSourceId(e.Name),
                e.StateFileRelativePath,
                BundleLayout.CcaBundlePath(e.Name)))
            .ToArray();
    }
}
