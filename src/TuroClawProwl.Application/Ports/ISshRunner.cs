namespace TuroClawProwl.Application.Ports;

public interface ISshRunner
{
    Task<SshCommandResult> RunCommandAsync(
        SshTarget target,
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default);
}
