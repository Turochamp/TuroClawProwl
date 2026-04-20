using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class GatewayHealthKindComparerTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 4, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly GatewayHealthKindComparer _comparer = GatewayHealthKindComparer.Instance;

    [Fact]
    public void Two_healthy_instances_with_different_timestamps_are_equal_by_kind()
    {
        var a = new GatewayHealth.Healthy(T0, null);
        var b = new GatewayHealth.Healthy(T0.AddMinutes(5), TimeSpan.FromHours(2));
        _comparer.Equals(a, b).Should().BeTrue();
    }

    [Fact]
    public void Two_unreachable_instances_with_different_anchors_are_equal_by_kind()
    {
        var a = new GatewayHealth.Unreachable(null);
        var b = new GatewayHealth.Unreachable(T0);
        _comparer.Equals(a, b).Should().BeTrue();
    }

    [Fact]
    public void Two_never_reached_instances_are_equal_by_kind()
    {
        _comparer.Equals(new GatewayHealth.NeverReached(), new GatewayHealth.NeverReached()).Should().BeTrue();
    }

    [Fact]
    public void Healthy_and_unreachable_are_not_equal_by_kind()
    {
        _comparer.Equals(new GatewayHealth.Healthy(T0, null), new GatewayHealth.Unreachable(null))
            .Should().BeFalse();
    }

    [Fact]
    public void Healthy_and_never_reached_are_not_equal_by_kind()
    {
        _comparer.Equals(new GatewayHealth.Healthy(T0, null), new GatewayHealth.NeverReached())
            .Should().BeFalse();
    }

    [Fact]
    public void Unreachable_and_never_reached_are_not_equal_by_kind()
    {
        _comparer.Equals(new GatewayHealth.Unreachable(null), new GatewayHealth.NeverReached())
            .Should().BeFalse();
    }

    [Fact]
    public void Hash_codes_match_for_same_kind_with_different_fields()
    {
        var a = new GatewayHealth.Healthy(T0, null);
        var b = new GatewayHealth.Healthy(T0.AddMinutes(5), TimeSpan.FromHours(2));
        _comparer.GetHashCode(a).Should().Be(_comparer.GetHashCode(b));
    }
}
