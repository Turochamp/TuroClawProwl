using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class TooltipComposerTests
{
    private static readonly string Repo = @"C:\repo";
    private static readonly TodayFileStatus Synced = new(@"C:\repo\a.md", Repo, false, false);
    private static readonly TodayFileStatus Uncommitted = new(@"C:\repo\dirty.md", Repo, true, false);
    private static readonly TodayFileStatus Unpushed = new(@"C:\repo\ahead.md", Repo, false, true);
    private static readonly TodayFileStatus Both = new(@"C:\repo\both.md", Repo, true, true);

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
        tip.Should().Contain("Today repo files: not configured");
    }

    [Fact]
    public void Tooltip_reports_all_synced_when_every_today_file_is_synced()
    {
        var tip = TooltipComposer.Compose(
            new GatewayHealth.Healthy(FixedNow, null),
            new[] { Synced, Synced, Synced });
        tip.Should().Contain("Today repo files: 3/3 synced");
        tip.Should().NotContain("pending");
    }

    [Fact]
    public void Tooltip_lists_pending_files_with_reasons_when_some_are_not_synced()
    {
        var tip = TooltipComposer.Compose(
            new GatewayHealth.Healthy(FixedNow, null),
            new[] { Synced, Uncommitted, Unpushed });

        tip.Should().Contain("Today repo files: 1/3 synced");
        tip.Should().Contain("pending:");
        tip.Should().Contain("dirty.md (uncommitted)");
        tip.Should().Contain("ahead.md (unpushed)");
    }

    [Fact]
    public void Tooltip_lists_combined_reasons_for_files_that_are_both_uncommitted_and_unpushed()
    {
        var tip = TooltipComposer.Compose(new GatewayHealth.Healthy(FixedNow, null), new[] { Both });
        tip.Should().Contain("both.md (uncommitted+unpushed)");
    }

    [Fact]
    public void Tooltip_caps_pending_list_and_summarises_the_rest_with_a_more_counter()
    {
        var files = Enumerable.Range(0, 7)
            .Select(i => new TodayFileStatus($@"C:\repo\{i}.md", Repo, true, false))
            .ToArray();

        var tip = TooltipComposer.Compose(new GatewayHealth.Healthy(FixedNow, null), files);

        tip.Should().Contain("Today repo files: 0/7 synced");
        tip.Should().Contain("+4 more", "3 pending are listed and the rest are counted");
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
