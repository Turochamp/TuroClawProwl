using FluentAssertions;
using TuroClawProwl.Infrastructure.Time;

namespace TuroClawProwl.Infrastructure.Tests.Time;

public class WallClockTests
{
    [Fact]
    public void Utc_now_returns_current_utc_time_within_a_second()
    {
        var clock = new WallClock();
        var before = DateTimeOffset.UtcNow;
        var value = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        value.Offset.Should().Be(TimeSpan.Zero);
    }
}
