using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class TrayColorResolverTests
{
    private static readonly RepoState Clean = new(0, false, true);
    private static readonly RepoState Unpushed = new(3, false, true);
    private static readonly RepoState Uncommitted = new(0, true, true);
    private static readonly RepoState NoUpstream = new(0, false, false);

    private static readonly DateTimeOffset FixedNow =
        new(2026, 4, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tray_is_grey_when_gateway_never_reached_regardless_of_repos()
    {
        TrayColorResolver.Resolve(new GatewayHealth.NeverReached(), Array.Empty<RepoState>())
            .Should().Be(TrayColor.Grey);
        TrayColorResolver.Resolve(new GatewayHealth.NeverReached(), new[] { Unpushed })
            .Should().Be(TrayColor.Grey);
    }

    [Fact]
    public void Tray_is_red_when_most_recent_health_poll_failed_regardless_of_repos()
    {
        TrayColorResolver.Resolve(new GatewayHealth.Unreachable(null), Array.Empty<RepoState>())
            .Should().Be(TrayColor.Red);
        TrayColorResolver.Resolve(new GatewayHealth.Unreachable(FixedNow), new[] { Clean, Unpushed })
            .Should().Be(TrayColor.Red);
    }

    [Fact]
    public void Tray_is_green_when_gateway_healthy_and_no_repos()
    {
        var healthy = new GatewayHealth.Healthy(FixedNow, TimeSpan.FromMinutes(5));
        TrayColorResolver.Resolve(healthy, Array.Empty<RepoState>())
            .Should().Be(TrayColor.Green);
    }

    [Fact]
    public void Tray_is_green_when_gateway_healthy_and_all_repos_clean()
    {
        var healthy = new GatewayHealth.Healthy(FixedNow, null);
        TrayColorResolver.Resolve(healthy, new[] { Clean, Clean, Clean })
            .Should().Be(TrayColor.Green);
    }

    [Theory]
    [MemberData(nameof(UnhealthyRepoSets))]
    public void Tray_is_yellow_when_gateway_healthy_and_any_repo_needs_attention(RepoState[] repos, string name)
    {
        var healthy = new GatewayHealth.Healthy(FixedNow, null);
        TrayColorResolver.Resolve(healthy, repos).Should().Be(TrayColor.Yellow, name);
    }

    public static IEnumerable<object[]> UnhealthyRepoSets => new[]
    {
        new object[] { new[] { Unpushed },                             "one unpushed" },
        new object[] { new[] { Uncommitted },                          "one uncommitted" },
        new object[] { new[] { NoUpstream },                           "one no-upstream" },
        new object[] { new[] { Clean, Unpushed },                      "clean plus unpushed" },
        new object[] { new[] { Clean, Uncommitted, Clean },            "uncommitted in middle" },
        new object[] { new[] { Unpushed, Uncommitted, NoUpstream },    "all attention types mixed" },
    };

    [Property(MaxTest = 500)]
    public Property Tray_is_green_iff_healthy_and_every_repo_is_clean(
        NonNegativeInt firstUnpushed,
        bool firstUncommitted,
        bool firstHasUpstream,
        NonNegativeInt secondUnpushed,
        bool secondUncommitted,
        bool secondHasUpstream)
    {
        var first = new RepoState(firstUnpushed.Item, firstUncommitted, firstHasUpstream);
        var second = new RepoState(secondUnpushed.Item, secondUncommitted, secondHasUpstream);
        var healthy = new GatewayHealth.Healthy(FixedNow, null);

        var color = TrayColorResolver.Resolve(healthy, new[] { first, second });
        var expectedGreen = first.IsClean && second.IsClean;

        return ((color == TrayColor.Green) == expectedGreen).ToProperty();
    }

    [Fact]
    public void Resolver_rejects_null_gateway()
    {
        Action act = () => TrayColorResolver.Resolve(null!, Array.Empty<RepoState>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Resolver_rejects_null_repos()
    {
        Action act = () => TrayColorResolver.Resolve(new GatewayHealth.NeverReached(), null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
