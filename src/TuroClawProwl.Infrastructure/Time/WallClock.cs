using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Infrastructure.Time;

public sealed class WallClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
