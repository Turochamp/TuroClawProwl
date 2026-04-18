using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class TooltipComposerTests
{
    private static readonly RepoState Clean = new(0, false, true);
    private static readonly RepoState Unpushed = new(3, false, true);
    private static readonly RepoState Uncommitted = new(0, true, true);
    private static readonly RepoState NoUpstream = new(0, false, false);

    private static readonly DateTimeOffset FixedNow =
        new(2026, 4, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tooltip_shows_gateway_never_reached_before_first_successful_poll()
    {
        var tip = TooltipComposer.Compose(new GatewayHealth.NeverReached(), Array.Empty<RepoState>());
        tip.Should().Contain("Gateway: never reached");
    }

    [Fact]
    public void Tooltip_shows_gateway_healthy_when_gateway_is_up()
    {
        var tip = TooltipComposer.Compose(new GatewayHealth.Healthy(FixedNow, null), Array.Empty<RepoState>());
        tip.Should().Contain("Gateway: healthy");
    }

    [Fact]
    public void Tooltip_shows_plain_unreachable_when_there_was_no_prior_healthy_state()
    {
        var tip = TooltipComposer.Compose(new GatewayHealth.Unreachable(null), Array.Empty<RepoState>());
        tip.Should().Contain("Gateway: unreachable");
        tip.Should().NotContain("last seen");
    }

    [Fact]
    public void Tooltip_shows_unreachable_with_formatted_last_seen_timestamp_after_prior_healthy_state()
    {
        var lastSeen = new DateTimeOffset(2026, 4, 18, 15, 23, 0, TimeSpan.Zero);
        var tip = TooltipComposer.Compose(new GatewayHealth.Unreachable(lastSeen), Array.Empty<RepoState>());
        tip.Should().Contain("Gateway: unreachable");
        tip.Should().Contain("last seen 2026-04-18 15:23 UTC");
    }

    [Fact]
    public void Tooltip_includes_total_repo_count_line()
    {
        var tip = TooltipComposer.Compose(new GatewayHealth.NeverReached(), new[] { Clean, Clean, Unpushed });
        tip.Should().Contain("Repos: 3");
    }

    [Fact]
    public void Tooltip_includes_breakdown_counts_for_every_category()
    {
        var tip = TooltipComposer.Compose(
            new GatewayHealth.Healthy(FixedNow, null),
            new[] { Clean, Clean, Unpushed, Uncommitted, NoUpstream });

        tip.Should().Contain("2 clean");
        tip.Should().Contain("1 unpushed");
        tip.Should().Contain("1 uncommitted");
        tip.Should().Contain("1 no upstream");
    }

    [Fact]
    public void Tooltip_is_two_lines()
    {
        var tip = TooltipComposer.Compose(new GatewayHealth.NeverReached(), Array.Empty<RepoState>());
        tip.Split('\n').Should().HaveCount(2);
    }

    [Fact]
    public void Composer_rejects_null_gateway()
    {
        Action act = () => TooltipComposer.Compose(null!, Array.Empty<RepoState>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Composer_rejects_null_repos()
    {
        Action act = () => TooltipComposer.Compose(new GatewayHealth.NeverReached(), null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
