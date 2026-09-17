namespace TuroClawProwl.Application.Ports;

public abstract record GoogleReadResult
{
    public sealed record Success(string Json) : GoogleReadResult;

    // Exit code 2 from gws. A failed read, never a crash.
    public sealed record NotAuthenticated(string Detail) : GoogleReadResult;

    public sealed record Misconfigured(string SettingName, string Detail) : GoogleReadResult;

    public sealed record Failure(string Detail) : GoogleReadResult;
}
