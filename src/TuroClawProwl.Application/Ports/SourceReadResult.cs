namespace TuroClawProwl.Application.Ports;

public abstract record SourceReadResult
{
    public sealed record Found(string Content, DateTimeOffset LastModified) : SourceReadResult;

    public sealed record Missing : SourceReadResult;

    public sealed record Unreadable(string Error) : SourceReadResult;
}
