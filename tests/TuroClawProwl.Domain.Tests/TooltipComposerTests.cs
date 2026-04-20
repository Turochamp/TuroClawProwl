using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class TooltipComposerTests
{
    private static TodayFileStatus File(string repoName, string fileName, bool uncommitted, bool unpushed) =>
        new($@"C:\Git\{repoName}\{fileName}", $@"C:\Git\{repoName}", uncommitted, unpushed);

    private static readonly DateTimeOffset FixedNow = new(2026, 4, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tooltip_shows_gateway_never_reached_before_first_successful_poll()
    {
        var tip = TooltipComposer.Compose(new GatewayHealth.NeverReached(), Array.Empty<TodayFileStatus>());
        tip.Should().Contain("Gateway: never reached");
    }

    [Fact]
    public void Tooltip_shows_gateway_healthy_when_gateway_is_up()
    {
        var tip = TooltipComposer.Compose(new GatewayHealth.Healthy(FixedNow, null), Array.Empty<TodayFileStatus>());
        tip.Should().Contain("Gateway: healthy");
    }

    [Fact]
    public void Tooltip_shows_plain_unreachable_when_there_was_no_prior_healthy_state()
    {
        var tip = TooltipComposer.Compose(new GatewayHealth.Unreachable(null), Array.Empty<TodayFileStatus>());
        tip.Should().Contain("Gateway: unreachable");
        tip.Should().NotContain("last seen");
    }

    [Fact]
    public void Tooltip_shows_unreachable_with_formatted_last_seen_timestamp_after_prior_healthy_state()
    {
        var lastSeen = new DateTimeOffset(2026, 4, 18, 15, 23, 0, TimeSpan.Zero);
        var tip = TooltipComposer.Compose(new GatewayHealth.Unreachable(lastSeen), Array.Empty<TodayFileStatus>());
        tip.Should().Contain("last seen 2026-04-18 15:23 UTC");
    }

    [Fact]
    public void Tooltip_shows_not_configured_when_today_file_list_is_empty()
    {
        var tip = TooltipComposer.Compose(new GatewayHealth.NeverReached(), Array.Empty<TodayFileStatus>());
        tip.Should().Contain("Today: not configured");
    }

    [Fact]
    public void Tooltip_reports_all_synced_when_every_today_file_is_synced()
    {
        var tip = TooltipComposer.Compose(
            new GatewayHealth.Healthy(FixedNow, null),
            new[] { File("a", "STATE.md", false, false), File("b", "STATE.md", false, false) });
        tip.Should().Contain("Today: 2/2 synced");
        tip.Should().NotContain("uncommitted");
        tip.Should().NotContain("unpushed");
    }

    [Fact]
    public void Tooltip_lists_uncommitted_repos_by_name()
    {
        var tip = TooltipComposer.Compose(
            new GatewayHealth.Healthy(FixedNow, null),
            new[]
            {
                File("repo-a", "STATE.md", uncommitted: true, unpushed: false),
                File("repo-b", "STATE.md", uncommitted: true, unpushed: false),
                File("repo-c", "STATE.md", uncommitted: false, unpushed: false),
            });

        tip.Should().Contain("Today: 1/3 synced");
        tip.Should().Contain("uncommitted: repo-a, repo-b");
        tip.Should().NotContain("unpushed:");
    }

    [Fact]
    public void Tooltip_lists_unpushed_repos_under_unpushed_bucket_when_no_uncommitted_files()
    {
        var tip = TooltipComposer.Compose(
            new GatewayHealth.Healthy(FixedNow, null),
            new[]
            {
                File("repo-a", "STATE.md", uncommitted: false, unpushed: true),
                File("repo-b", "STATE.md", uncommitted: false, unpushed: false),
            });

        tip.Should().Contain("Today: 1/2 synced");
        tip.Should().Contain("unpushed: repo-a");
        tip.Should().NotContain("uncommitted:");
    }

    [Fact]
    public void Repo_with_both_problems_is_classified_as_uncommitted_only_and_not_duplicated()
    {
        var tip = TooltipComposer.Compose(
            new GatewayHealth.Healthy(FixedNow, null),
            new[] { File("repo-a", "STATE.md", uncommitted: true, unpushed: true) });

        tip.Should().Contain("uncommitted: repo-a");
        tip.Should().NotContain("unpushed:");
    }

    [Fact]
    public void Tooltip_deduplicates_repo_names_when_multiple_files_live_in_the_same_repo()
    {
        var tip = TooltipComposer.Compose(
            new GatewayHealth.Healthy(FixedNow, null),
            new[]
            {
                File("repo-a", "STATE.md", uncommitted: true, unpushed: false),
                File("repo-a", "OTHER.md", uncommitted: true, unpushed: false),
            });

        var occurrences = System.Text.RegularExpressions.Regex.Matches(tip, "repo-a").Count;
        occurrences.Should().Be(1);
    }

    [Fact]
    public void Tooltip_mixes_uncommitted_and_unpushed_buckets_separated_by_em_dash()
    {
        var tip = TooltipComposer.Compose(
            new GatewayHealth.Healthy(FixedNow, null),
            new[]
            {
                File("repo-a", "STATE.md", uncommitted: true, unpushed: false),
                File("repo-c", "STATE.md", uncommitted: false, unpushed: true),
            });

        tip.Should().Contain("uncommitted: repo-a");
        tip.Should().Contain("unpushed: repo-c");
    }

    [Fact]
    public void Tooltip_caps_repo_list_per_bucket_and_emits_more_counter_when_overflowing()
    {
        var files = Enumerable.Range(0, 7)
            .Select(i => File($"repo-{i}", "STATE.md", uncommitted: true, unpushed: false))
            .ToArray();

        var tip = TooltipComposer.Compose(new GatewayHealth.Healthy(FixedNow, null), files);

        tip.Should().Contain("Today: 0/7 synced");
        tip.Should().Contain("+3 more", "4 repos are listed and the rest are counted");
    }

    [Fact]
    public void Tooltip_is_two_lines()
    {
        var tip = TooltipComposer.Compose(new GatewayHealth.NeverReached(), Array.Empty<TodayFileStatus>());
        tip.Split('\n').Should().HaveCount(2);
    }

    [Fact]
    public void Composer_rejects_null_gateway()
    {
        Action act = () => TooltipComposer.Compose(null!, Array.Empty<TodayFileStatus>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Composer_rejects_null_today_files()
    {
        Action act = () => TooltipComposer.Compose(new GatewayHealth.NeverReached(), null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
