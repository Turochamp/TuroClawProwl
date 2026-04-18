namespace TuroClawProwl.Application.Ports;

public abstract record SshCommandResult
{
    public sealed record Success(string StdOut) : SshCommandResult;

    public sealed record Failure(int ExitCode, string StdErr) : SshCommandResult;
}
