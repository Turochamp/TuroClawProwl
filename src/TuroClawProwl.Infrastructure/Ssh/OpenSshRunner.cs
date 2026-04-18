using System.Diagnostics;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.Process;

namespace TuroClawProwl.Infrastructure.Ssh;

public sealed class OpenSshRunner : ISshRunner
{
    private readonly string _sshExecutable;

    public OpenSshRunner(string sshExecutable = "ssh")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sshExecutable);
        _sshExecutable = sshExecutable;
    }

    public async Task<SshCommandResult> RunCommandAsync(
        SshTarget target,
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(args);

        var psi = BuildStartInfo(_sshExecutable, target, command, args);
        var result = await ProcessRunner.RunAsync(psi.FileName, GetArgs(psi), workingDirectory: null, cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode == 0
            ? new SshCommandResult.Success(result.StdOut)
            : new SshCommandResult.Failure(result.ExitCode, string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr);
    }

    internal static ProcessStartInfo BuildStartInfo(
        string sshExecutable,
        SshTarget target,
        string command,
        IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(sshExecutable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add($"{target.User}@{target.Host}");
        psi.ArgumentList.Add(command);
        foreach (var a in args) psi.ArgumentList.Add(a);

        return psi;
    }

    private static IReadOnlyList<string> GetArgs(ProcessStartInfo psi) => psi.ArgumentList.ToArray();
}
