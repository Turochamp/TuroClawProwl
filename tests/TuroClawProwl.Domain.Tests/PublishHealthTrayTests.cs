using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class PublishHealthTrayTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 7, 15, 0, TimeSpan.Zero);

    private static GatewayHealth HealthyGateway() =>
        new GatewayHealth.Healthy(Now, TimeSpan.FromHours(3));

    private static PublishHealth Misconfigured() =>
        new PublishHealth.Failed(Now, IsMisconfiguration: true, "bundlePublishBranch", "branch nope does not exist");

    private static PublishHealth Transient() =>
        new PublishHealth.Failed(Now, IsMisconfiguration: false, string.Empty, "could not resolve host github.com");

    [Fact]
    public void A_healthy_gateway_with_a_healthy_publish_is_green()
    {
        var color = TrayColorResolver.Resolve(HealthyGateway(), new PublishHealth.Healthy(Now.AddMinutes(-5)));

        color.Should().Be(TrayColor.Green);
    }

    [Fact]
    public void A_failed_publish_turns_a_healthy_tray_amber()
    {
        var color = TrayColorResolver.Resolve(HealthyGateway(), Transient());

        color.Should().Be(TrayColor.Yellow);
    }

    [Fact]
    public void A_misconfigured_publish_also_turns_the_tray_amber()
    {
        var color = TrayColorResolver.Resolve(HealthyGateway(), Misconfigured());

        color.Should().Be(TrayColor.Yellow);
    }

    [Fact]
    public void A_publish_that_succeeds_after_failing_returns_the_tray_to_green()
    {
        TrayColorResolver.Resolve(HealthyGateway(), Transient()).Should().Be(TrayColor.Yellow);

        TrayColorResolver.Resolve(HealthyGateway(), new PublishHealth.Healthy(Now))
            .Should().Be(TrayColor.Green);
    }

    [Fact]
    public void An_unreachable_gateway_stays_red_whatever_the_publish_state()
    {
        var color = TrayColorResolver.Resolve(new GatewayHealth.Unreachable(Now.AddHours(-1)), Transient());

        color.Should().Be(TrayColor.Red);
    }

    [Fact]
    public void A_gateway_never_reached_stays_grey()
    {
        var color = TrayColorResolver.Resolve(new GatewayHealth.NeverReached(), new PublishHealth.NeverPublished());

        color.Should().Be(TrayColor.Grey);
    }

    [Fact]
    public void Nothing_published_yet_does_not_by_itself_degrade_the_tray()
    {
        var color = TrayColorResolver.Resolve(HealthyGateway(), new PublishHealth.NeverPublished());

        color.Should().Be(TrayColor.Green);
    }

    [Fact]
    public void Tooltip_reports_the_age_of_the_last_successful_publish()
    {
        var tooltip = TooltipComposer.Compose(
            HealthyGateway(),
            new PublishHealth.Healthy(Now.AddHours(-2)),
            Now);

        tooltip.Should().Contain("Bundle: published 2h ago");
    }

    [Fact]
    public void Tooltip_names_the_setting_at_fault_for_a_misconfiguration()
    {
        var tooltip = TooltipComposer.Compose(
            HealthyGateway(), Misconfigured(), Now);

        tooltip.Should().Contain("bundlePublishBranch");
        tooltip.Should().Contain("misconfigured");
    }

    [Fact]
    public void Tooltip_marks_a_transient_failure_as_transient()
    {
        var tooltip = TooltipComposer.Compose(
            HealthyGateway(), Transient(), Now);

        tooltip.Should().Contain("Bundle: publish failed");
        tooltip.Should().NotContain("misconfigured");
    }

    [Fact]
    public void Tooltip_says_so_when_nothing_has_been_published_yet()
    {
        var tooltip = TooltipComposer.Compose(
            HealthyGateway(), new PublishHealth.NeverPublished(), Now);

        tooltip.Should().Contain("Bundle: not published yet");
    }

    [Fact]
    public void Null_gateway_health_is_rejected_by_the_resolver()
    {
        Action act = () => TrayColorResolver.Resolve(null!, new PublishHealth.NeverPublished());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Null_publish_health_is_rejected_by_the_resolver()
    {
        Action act = () => TrayColorResolver.Resolve(HealthyGateway(), null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
