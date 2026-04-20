using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Application.Tests.TestSupport;

public sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow { get; set; }

    public FixedClock(DateTimeOffset initial)
    {
        UtcNow = initial;
    }

    public void Advance(TimeSpan delta) => UtcNow += delta;
}
