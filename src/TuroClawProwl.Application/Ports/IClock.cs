namespace TuroClawProwl.Application.Ports;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
