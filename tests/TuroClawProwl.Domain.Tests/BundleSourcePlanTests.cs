using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class BundleSourcePlanTests
{
    private static readonly DateTimeOffset LocalNow =
        new(2026, 9, 12, 8, 0, 0, TimeSpan.FromHours(2));

    private const string Registry = """
        ## Active set

        | CCA | Branch | Cadence | Status | State file |
        |-----|--------|---------|--------|-----------|
        | YNE | professional | weekly | active | CCA-YNE/STATE.md |
        | SmoEms | professional | weekly | flagged | CCA-SmoEms/STATE.md |

        ## Quiet (kept on disk, excluded from all roll-ups)

        | CCA | Reason |
        |-----|--------|
        | ChromeBookmarks | Utility, not goal-bearing |
        """;

    [Fact]
    public void The_fixed_sources_are_the_registry_the_crm_index_and_this_weeks_week_file()
    {
        var items = BundleSourcePlan.FixedSources(LocalNow);

        items.Should().BeEquivalentTo(new[]
        {
            new BundleSourcePlanItem("registry", "hub/registry.md", "registry.md"),
            new BundleSourcePlanItem("crm-index", "crm/data/contacts/_index.md", "crm-index.md"),
            new BundleSourcePlanItem("weekly/2026-W37", "hub/weekly/2026-W37.md", "weekly/2026-W37.md"),
        }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void No_fixed_source_path_carries_a_placeholder_or_a_glob()
    {
        var items = BundleSourcePlan.FixedSources(LocalNow);

        items.Should().AllSatisfy(i =>
        {
            i.SourceRelativePath.Should().NotContain("{");
            i.SourceRelativePath.Should().NotContain("*");
        });
    }

    [Fact]
    public void Registry_sources_cover_every_active_and_flagged_cca()
    {
        var items = BundleSourcePlan.FromRegistry(Registry);

        items.Should().BeEquivalentTo(new[]
        {
            new BundleSourcePlanItem("cca/YNE", "CCA-YNE/STATE.md", "cca/YNE.STATE.md"),
            new BundleSourcePlanItem("cca/SmoEms", "CCA-SmoEms/STATE.md", "cca/SmoEms.STATE.md"),
        }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Registry_sources_exclude_quiet_ccas()
    {
        var items = BundleSourcePlan.FromRegistry(Registry);

        items.Should().NotContain(i => i.Id.Contains("ChromeBookmarks", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_registry_yields_no_cca_sources_rather_than_throwing()
    {
        var items = BundleSourcePlan.FromRegistry("# nothing here");

        items.Should().BeEmpty();
    }

    [Fact]
    public void Every_source_id_is_unique()
    {
        var all = BundleSourcePlan.FixedSources(LocalNow)
            .Concat(BundleSourcePlan.FromRegistry(Registry))
            .ToArray();

        all.Select(i => i.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Null_registry_markdown_is_rejected()
    {
        Action act = () => BundleSourcePlan.FromRegistry(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
