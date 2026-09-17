using System.Text.Json;
using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class BundleManifestTests
{
    private static BundleManifest SampleManifest() => new(
        SchemaVersion: BundleManifest.CurrentSchemaVersion,
        PublishedAt: new DateTimeOffset(2026, 9, 12, 6, 30, 0, TimeSpan.Zero),
        Publisher: "TuroClawProwl/0.3.0",
        SourceBranch: "feature/humanize-design",
        Sources:
        [
            new BundleSource(
                Id: "registry",
                Path: "hub/registry.md",
                BundlePath: "registry.md",
                Modified: new DateTimeOffset(2026, 9, 12, 6, 0, 0, TimeSpan.Zero),
                Verified: new DateTimeOffset(2026, 9, 12, 6, 0, 0, TimeSpan.Zero),
                Committed: new DateTimeOffset(2026, 9, 10, 9, 33, 29, TimeSpan.Zero),
                Dirty: false),
            new BundleSource(
                Id: "cca/HomeBase",
                Path: "CCA-HomeBase/STATE.md",
                BundlePath: "cca/HomeBase.STATE.md",
                Modified: new DateTimeOffset(2026, 9, 11, 20, 15, 0, TimeSpan.Zero),
                Verified: new DateTimeOffset(2026, 9, 11, 20, 15, 0, TimeSpan.Zero),
                Committed: null,
                Dirty: true),
            new BundleSource(
                Id: "calendar/window",
                Path: "googleapis://calendar/window",
                BundlePath: "calendar/window.json",
                Modified: new DateTimeOffset(2026, 8, 29, 7, 0, 0, TimeSpan.Zero),
                Verified: new DateTimeOffset(2026, 9, 12, 6, 15, 0, TimeSpan.Zero),
                Committed: null,
                Dirty: false),
        ],
        Failures:
        [
            new BundleFailure("cca/CarGarage", "missing"),
        ]);

    [Fact]
    public void Manifest_serialises_to_the_snake_case_contract_names()
    {
        var json = JsonSerializer.Serialize(SampleManifest());

        json.Should().Contain("\"schema_version\"");
        json.Should().Contain("\"published_at\"");
        json.Should().Contain("\"publisher\"");
        json.Should().Contain("\"source_branch\"");
        json.Should().Contain("\"sources\"");
        json.Should().Contain("\"failures\"");
    }

    [Fact]
    public void The_schema_version_is_one_and_is_always_serialised()
    {
        BundleManifest.CurrentSchemaVersion.Should().Be(1);

        var json = JsonSerializer.Serialize(SampleManifest());

        json.Should().Contain("\"schema_version\":1");
    }

    [Fact]
    public void Schema_version_is_the_first_property_in_the_serialised_manifest()
    {
        var json = JsonSerializer.Serialize(SampleManifest());

        json.TrimStart().Should().StartWith("{\"schema_version\":1");
    }

    [Fact]
    public void Source_entries_carry_id_path_bundle_path_modified_verified_committed_and_dirty()
    {
        var json = JsonSerializer.Serialize(SampleManifest());

        json.Should().Contain("\"id\"");
        json.Should().Contain("\"path\"");
        json.Should().Contain("\"bundle_path\"");
        json.Should().Contain("\"modified\"");
        json.Should().Contain("\"verified\"");
        json.Should().Contain("\"committed\"");
        json.Should().Contain("\"dirty\"");
    }

    [Fact]
    public void Verified_is_serialised_immediately_after_modified()
    {
        var json = JsonSerializer.Serialize(SampleManifest());

        json.IndexOf("\"verified\"", StringComparison.Ordinal)
            .Should().BeGreaterThan(json.IndexOf("\"modified\"", StringComparison.Ordinal));
        json.IndexOf("\"verified\"", StringComparison.Ordinal)
            .Should().BeLessThan(json.IndexOf("\"committed\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_source_carries_the_same_value_for_modified_and_verified()
    {
        var manifest = SampleManifest();

        var registry = manifest.Sources.Single(s => s.Id == "registry");
        registry.Verified.Should().Be(registry.Modified);

        var homeBase = manifest.Sources.Single(s => s.Id == "cca/HomeBase");
        homeBase.Verified.Should().Be(homeBase.Modified);
    }

    [Fact]
    public void A_snapshot_source_may_carry_an_old_modified_with_a_recent_verified()
    {
        var window = SampleManifest().Sources.Single(s => s.Id == "calendar/window");

        window.Verified.Should().BeAfter(window.Modified);
        window.Verified.Should().Be(new DateTimeOffset(2026, 9, 12, 6, 15, 0, TimeSpan.Zero));
        window.Modified.Should().Be(new DateTimeOffset(2026, 8, 29, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Verified_is_not_nullable_so_the_renderer_needs_no_special_case()
    {
        typeof(BundleSource).GetProperty(nameof(BundleSource.Verified))!
            .PropertyType.Should().Be<DateTimeOffset>();
    }

    [Fact]
    public void Bundle_path_states_the_location_inside_the_bundle_so_it_need_never_be_inferred()
    {
        var manifest = SampleManifest();

        manifest.Sources.Single(s => s.Id == "registry").BundlePath.Should().Be("registry.md");
        manifest.Sources.Single(s => s.Id == "cca/HomeBase").BundlePath
            .Should().Be("cca/HomeBase.STATE.md");
    }

    [Fact]
    public void Path_and_bundle_path_are_different_and_both_present_on_every_source()
    {
        var manifest = SampleManifest();

        manifest.Sources.Should().AllSatisfy(s =>
        {
            s.Path.Should().NotBeEmpty();
            s.BundlePath.Should().NotBeEmpty();
            s.BundlePath.Should().NotBe(s.Path);
        });
    }

    [Fact]
    public void A_source_that_is_untracked_or_outside_a_repo_serialises_committed_as_null()
    {
        var json = JsonSerializer.Serialize(SampleManifest());

        json.Should().Contain("\"committed\":null");
    }

    [Fact]
    public void Failure_entries_carry_id_and_reason()
    {
        var json = JsonSerializer.Serialize(SampleManifest());

        json.Should().Contain("\"reason\":\"missing\"");
    }

    [Fact]
    public void Manifest_round_trips_without_loss()
    {
        var json = JsonSerializer.Serialize(SampleManifest());

        var loaded = JsonSerializer.Deserialize<BundleManifest>(json);

        loaded.Should().BeEquivalentTo(SampleManifest());
    }

    [Fact]
    public void Source_branch_records_the_branch_the_sources_were_read_from()
    {
        SampleManifest().SourceBranch.Should().Be("feature/humanize-design");
    }
}
