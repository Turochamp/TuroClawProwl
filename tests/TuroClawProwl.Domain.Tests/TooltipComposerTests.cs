using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class TooltipComposerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 18, 12, 0, 0, TimeSpan.Zero);

    private static string Compose(GatewayHealth gateway) =>
        TooltipComposer.Compose(gateway, new PublishHealth.NeverPublished(), FixedNow);

    [Fact]
    public void Tooltip_shows_gateway_never_reached_before_first_successful_poll()
    {
        Compose(new GatewayHealth.NeverReached()).Should().Contain("Gateway: never reached");
    }

    [Fact]
    public void Tooltip_shows_gateway_healthy_when_gateway_is_up()
    {
        Compose(new GatewayHealth.Healthy(FixedNow, null)).Should().Contain("Gateway: healthy");
    }

    [Fact]
    public void Tooltip_shows_plain_unreachable_when_there_was_no_prior_healthy_state()
    {
        var tip = Compose(new GatewayHealth.Unreachable(null));
        tip.Should().Contain("Gateway: unreachable");
        tip.Should().NotContain("last seen");
    }

    [Fact]
    public void Tooltip_shows_unreachable_with_formatted_last_seen_timestamp_after_prior_healthy_state()
    {
        var lastSeen = new DateTimeOffset(2026, 4, 18, 15, 23, 0, TimeSpan.Zero);
        Compose(new GatewayHealth.Unreachable(lastSeen)).Should().Contain("last seen 2026-04-18 15:23 UTC");
    }

    [Fact]
    public void Tooltip_is_exactly_the_gateway_line_and_the_bundle_line()
    {
        var tip = TooltipComposer.Compose(
            new GatewayHealth.Healthy(FixedNow, null), new PublishHealth.Healthy(FixedNow.AddHours(-2)), FixedNow);

        tip.Split(TooltipComposer.LineSeparator).Should().Equal("Gateway: healthy", "Bundle: published 2h ago");
    }

    [Fact]
    public void Tooltip_no_longer_carries_a_today_line()
    {
        Compose(new GatewayHealth.NeverReached()).Should().NotContain("Today");
    }

    [Fact]
    public void Composer_rejects_null_gateway()
    {
        Action act = () => TooltipComposer.Compose(null!, new PublishHealth.NeverPublished(), FixedNow);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Composer_rejects_null_publish_health()
    {
        Action act = () => TooltipComposer.Compose(new GatewayHealth.NeverReached(), null!, FixedNow);
        act.Should().Throw<ArgumentNullException>();
    }
}
