namespace TuroClawProwl.Application.Ports;

public abstract record GitCommitResult
{
    public sealed record Success(bool HadChangesToCommit) : GitCommitResult;

    public sealed record Failure(string Error) : GitCommitResult;
}
