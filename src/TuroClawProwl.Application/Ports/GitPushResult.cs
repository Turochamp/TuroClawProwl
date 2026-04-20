namespace TuroClawProwl.Application.Ports;

public abstract record GitPushResult
{
    public sealed record Success : GitPushResult;

    public sealed record Failure(string Error) : GitPushResult;
}
