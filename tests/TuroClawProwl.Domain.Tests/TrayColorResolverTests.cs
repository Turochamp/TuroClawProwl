using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class TrayColorResolverTests
{
    private static readonly string Repo = @"C:\repo";
    private static readonly TodayFileStatus Synced = new(@"C:\repo\a.md", Repo, HasUncommitted: false, IsUnpushed: false);
    private static readonly TodayFileStatus Uncommitted = new(@"C:\repo\b.md", Repo, HasUncommitted: true, IsUnpushed: false);
    private static readonly TodayFileStatus Unpushed = new(@"C:\repo\c.md", Repo, HasUncommitted: false, IsUnpushed: true);
    private static readonly TodayFileStatus UncommittedAndUnpushed = new(@"C:\repo\d.md", Repo, HasUncommitted: true, IsUnpushed: true);

    private static readonly DateTimeOffset FixedNow = new(2026, 4, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tray_is_grey_when_gateway_never_reached_regardless_of_today_files()
    {
        TrayColorResolver.Resolve(new GatewayHealth.NeverReached(), Array.Empty<TodayFileStatus>())
            .Should().Be(TrayColor.Grey);
        TrayColorResolver.Resolve(new GatewayHealth.NeverReached(), new[] { Uncommitted })
            .Should().Be(TrayColor.Grey);
    }

    [Fact]
    public void Tray_is_red_when_most_recent_health_poll_failed_regardless_of_today_files()
    {
        TrayColorResolver.Resolve(new GatewayHealth.Unreachable(null), Array.Empty<TodayFileStatus>())
            .Should().Be(TrayColor.Red);
        TrayColorResolver.Resolve(new GatewayHealth.Unreachable(FixedNow), new[] { Synced, Synced })
            .Should().Be(TrayColor.Red);
    }

    [Fact]
    public void Tray_is_green_when_gateway_healthy_and_no_today_files()
    {
        var healthy = new GatewayHealth.Healthy(FixedNow, null);
        TrayColorResolver.Resolve(healthy, Array.Empty<TodayFileStatus>())
            .Should().Be(TrayColor.Green);
    }

    [Fact]
    public void Tray_is_green_when_gateway_healthy_and_every_today_file_is_synced()
    {
        var healthy = new GatewayHealth.Healthy(FixedNow, null);
        TrayColorResolver.Resolve(healthy, new[] { Synced, Synced, Synced })
            .Should().Be(TrayColor.Green);
    }

    [Theory]
    [MemberData(nameof(UnsyncedSets))]
    public void Tray_is_yellow_when_gateway_healthy_and_any_today_file_needs_attention(
        TodayFileStatus[] files,
        string name)
    {
        var healthy = new GatewayHealth.Healthy(FixedNow, null);
        TrayColorResolver.Resolve(healthy, files).Should().Be(TrayColor.Yellow, name);
    }

    public static IEnumerable<object[]> UnsyncedSets => new[]
    {
        new object[] { new[] { Uncommitted }, "one uncommitted" },
        new object[] { new[] { Unpushed }, "one unpushed" },
        new object[] { new[] { UncommittedAndUnpushed }, "one both" },
        new object[] { new[] { Synced, Uncommitted }, "synced + uncommitted" },
        new object[] { new[] { Synced, Unpushed, Synced }, "unpushed in middle" },
        new object[] { new[] { Uncommitted, Unpushed, UncommittedAndUnpushed }, "mixed attention" },
    };

    [Property(MaxTest = 500)]
    public Property Tray_is_green_iff_healthy_and_every_today_file_is_synced(
        bool aUncommitted, bool aUnpushed,
        bool bUncommitted, bool bUnpushed)
    {
        var first = new TodayFileStatus(@"C:\r\1.md", Repo, aUncommitted, aUnpushed);
        var second = new TodayFileStatus(@"C:\r\2.md", Repo, bUncommitted, bUnpushed);
        var healthy = new GatewayHealth.Healthy(FixedNow, null);

        var color = TrayColorResolver.Resolve(healthy, new[] { first, second });
        var expectedGreen = first.IsSynced && second.IsSynced;

        return ((color == TrayColor.Green) == expectedGreen).ToProperty();
    }

    [Fact]
    public void Resolver_rejects_null_gateway()
    {
        Action act = () => TrayColorResolver.Resolve(null!, Array.Empty<TodayFileStatus>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Resolver_rejects_null_today_files()
    {
        Action act = () => TrayColorResolver.Resolve(new GatewayHealth.NeverReached(), null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
