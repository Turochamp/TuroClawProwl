using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class BundleLayoutTests
{
    [Fact]
    public void Bundle_directory_and_manifest_name_are_the_published_contract()
    {
        BundleLayout.BundleDirectory.Should().Be("hub/bundle");
        BundleLayout.ManifestFileName.Should().Be("manifest.json");
    }

    [Fact]
    public void Fixed_source_paths_carry_no_placeholders()
    {
        BundleLayout.RegistryRelativePath.Should().Be("hub/registry.md");
        BundleLayout.CrmIndexRelativePath.Should().Be("crm/data/contacts/_index.md");
        BundleLayout.RegistryRelativePath.Should().NotContain("{");
        BundleLayout.CrmIndexRelativePath.Should().NotContain("{");
    }

    [Fact]
    public void Weekly_file_name_uses_the_iso_week_of_the_instant()
    {
        var saturday = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.FromHours(2));

        BundleLayout.WeeklyFileName(saturday).Should().Be("2026-W37.md");
        BundleLayout.WeeklyRelativePath(saturday).Should().Be("hub/weekly/2026-W37.md");
        BundleLayout.WeeklyBundlePath(saturday).Should().Be("weekly/2026-W37.md");
    }

    [Fact]
    public void Single_digit_iso_weeks_are_zero_padded()
    {
        var january = new DateTimeOffset(2026, 1, 8, 9, 0, 0, TimeSpan.FromHours(1));

        BundleLayout.WeeklyFileName(january).Should().Be("2026-W02.md");
    }

    [Fact]
    public void Weekly_path_never_contains_an_unresolved_placeholder()
    {
        var instant = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.FromHours(2));

        BundleLayout.WeeklyRelativePath(instant).Should().NotContain("{");
        BundleLayout.WeeklyRelativePath(instant).Should().NotContain("*");
    }

    [Fact]
    public void Cca_source_id_and_bundle_path_are_derived_from_the_registry_name()
    {
        BundleLayout.CcaSourceId("HomeBase").Should().Be("cca/HomeBase");
        BundleLayout.CcaBundlePath("HomeBase").Should().Be("cca/HomeBase.STATE.md");
    }

    [Fact]
    public void The_weekly_source_id_carries_the_iso_week()
    {
        var saturday = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.FromHours(2));

        BundleLayout.WeeklySourceId(saturday).Should().Be("weekly/2026-W37");
    }

    [Fact]
    public void The_canonical_id_to_path_to_bundle_path_mapping_holds_for_all_four_source_kinds()
    {
        var saturday = new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.FromHours(2));

        var mapping = new[]
        {
            (BundleLayout.RegistrySourceId, BundleLayout.RegistryRelativePath, BundleLayout.RegistryBundlePath),
            (BundleLayout.CrmIndexSourceId, BundleLayout.CrmIndexRelativePath, BundleLayout.CrmIndexBundlePath),
            (BundleLayout.WeeklySourceId(saturday), BundleLayout.WeeklyRelativePath(saturday), BundleLayout.WeeklyBundlePath(saturday)),
            (BundleLayout.CcaSourceId("YNE"), "CCA-YNE/STATE.md", BundleLayout.CcaBundlePath("YNE")),
        };

        mapping.Should().BeEquivalentTo(new[]
        {
            ("registry", "hub/registry.md", "registry.md"),
            ("crm-index", "crm/data/contacts/_index.md", "crm-index.md"),
            ("weekly/2026-W37", "hub/weekly/2026-W37.md", "weekly/2026-W37.md"),
            ("cca/YNE", "CCA-YNE/STATE.md", "cca/YNE.STATE.md"),
        }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Empty_cca_name_is_rejected()
    {
        Action act = () => BundleLayout.CcaBundlePath("  ");

        act.Should().Throw<ArgumentException>();
    }
}
