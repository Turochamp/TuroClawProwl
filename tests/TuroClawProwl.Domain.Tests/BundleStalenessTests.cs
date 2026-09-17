using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class BundleStalenessTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 7, 15, 0, TimeSpan.Zero);

    private static BundleManifest ManifestPublishedAt(DateTimeOffset publishedAt, params BundleSource[] sources) =>
        new(BundleManifest.CurrentSchemaVersion, publishedAt, "TuroClawProwl/0.3.0", "main", sources, []);

    private static BundleSource FileSourceAt(string id, DateTimeOffset modified) =>
        new(id, "hub/registry.md", "registry.md", modified, modified, Committed: null, Dirty: false);

    private static BundleSource SnapshotSourceAt(
        string id, DateTimeOffset modified, DateTimeOffset verified) =>
        new(id, "googleapis://calendar/window", "calendar/window.json",
            modified, verified, Committed: null, Dirty: false);

    [Fact]
    public void Thresholds_match_the_reporting_contract()
    {
        BundleStaleness.BundleStaleAfter.Should().Be(TimeSpan.FromHours(36));
        BundleStaleness.SourceStaleAfter.Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public void Age_is_the_elapsed_span_since_the_timestamp()
    {
        var age = BundleStaleness.AgeAt(Now, Now.AddHours(-5));

        age.Should().Be(TimeSpan.FromHours(5));
    }

    [Fact]
    public void A_timestamp_in_the_future_reports_zero_age_rather_than_a_negative_one()
    {
        var age = BundleStaleness.AgeAt(Now, Now.AddMinutes(30));

        age.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void A_bundle_published_within_36_hours_is_not_stale()
    {
        var manifest = ManifestPublishedAt(Now.AddHours(-35));

        BundleStaleness.IsBundleStale(Now, manifest).Should().BeFalse();
    }

    [Fact]
    public void A_bundle_published_over_36_hours_ago_is_stale()
    {
        var manifest = ManifestPublishedAt(Now.AddHours(-37));

        BundleStaleness.IsBundleStale(Now, manifest).Should().BeTrue();
    }

    [Fact]
    public void A_single_source_over_7_days_old_is_named_even_when_the_bundle_is_fresh()
    {
        var manifest = ManifestPublishedAt(
            Now.AddMinutes(-10),
            FileSourceAt("registry", Now.AddHours(-2)),
            FileSourceAt("cca/CingRocket", Now.AddDays(-9)));

        var stale = BundleStaleness.StaleSources(Now, manifest);

        stale.Should().ContainSingle().Which.Id.Should().Be("cca/CingRocket");
    }

    [Fact]
    public void A_snapshot_unchanged_for_a_fortnight_but_read_this_morning_is_not_stale()
    {
        var manifest = ManifestPublishedAt(
            Now.AddMinutes(-10),
            SnapshotSourceAt("calendar/window", Now.AddDays(-14), Now.AddHours(-1)));

        BundleStaleness.StaleSources(Now, manifest).Should().BeEmpty();
    }

    [Fact]
    public void A_snapshot_whose_last_successful_read_is_old_is_stale_whatever_its_content_says()
    {
        var manifest = ManifestPublishedAt(
            Now.AddMinutes(-10),
            SnapshotSourceAt("calendar/window", Now.AddMinutes(-30), Now.AddDays(-9)));

        BundleStaleness.StaleSources(Now, manifest)
            .Should().ContainSingle().Which.Id.Should().Be("calendar/window");
    }

    [Theory]
    [InlineData(0, 45, "45m")]
    [InlineData(0, 90, "1h")]
    [InlineData(2, 0, "2h")]
    [InlineData(72, 0, "3d")]
    public void Age_formats_compactly(int hours, int minutes, string expected)
    {
        var age = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);

        BundleStaleness.FormatAge(age).Should().Be(expected);
    }

    [Fact]
    public void Null_manifest_is_rejected()
    {
        Action act = () => BundleStaleness.IsBundleStale(Now, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
