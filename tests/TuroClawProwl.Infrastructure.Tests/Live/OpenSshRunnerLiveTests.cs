using FluentAssertions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.Ssh;

namespace TuroClawProwl.Infrastructure.Tests.Live;

[Trait("Category", "Live")]
public class OpenSshRunnerLiveTests
{
    [SkippableFact]
    public async Task Real_ssh_echo_command_returns_success_and_expected_stdout()
    {
        Skip.If(
            string.IsNullOrWhiteSpace(LiveEnv.SshHost) || string.IsNullOrWhiteSpace(LiveEnv.SshUser),
            "TURO_LIVE_SSH_HOST or TURO_LIVE_SSH_USER not set");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var runner = new OpenSshRunner();
        var target = new SshTarget(LiveEnv.SshHost!, LiveEnv.SshUser!);

        var result = await runner.RunCommandAsync(
            target, "echo", new[] { "hello-turo" }, cts.Token);

        result.Should().BeOfType<SshCommandResult.Success>(
            "SSH to the configured host must succeed (assumes key-based auth is set up)")
            .Which.StdOut.Should().Contain("hello-turo");
    }
}
