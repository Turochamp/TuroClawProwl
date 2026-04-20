using FluentAssertions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.Ssh;

namespace TuroClawProwl.Infrastructure.Tests.Ssh;

public class OpenSshTunnelLauncherTests
{
    private static readonly SshTarget Target = new(Host: "fox.local", User: "michael");

    [Fact]
    public void Build_start_info_issues_dash_N_and_forward_spec_with_argument_list()
    {
        var psi = OpenSshTunnelLauncher.BuildStartInfo(
            "ssh", Target, localPort: 18790, remoteBindHost: "127.0.0.1", remotePort: 18789);

        psi.Arguments.Should().BeEmpty();
        psi.ArgumentList.Should().Contain("-N");
        psi.ArgumentList.Should().Contain("-L");
        psi.ArgumentList.Should().Contain("18790:127.0.0.1:18789");
        psi.ArgumentList.Should().Contain("michael@fox.local");
    }

    [Fact]
    public void Build_start_info_sets_exit_on_forward_failure_so_ssh_exits_if_local_port_is_busy()
    {
        var psi = OpenSshTunnelLauncher.BuildStartInfo(
            "ssh", Target, localPort: 18790, remoteBindHost: "127.0.0.1", remotePort: 18789);

        psi.ArgumentList.Should().ContainInOrder("-o", "ExitOnForwardFailure=yes");
    }

    [Fact]
    public void Build_start_info_disables_shell_execute_and_redirects_streams()
    {
        var psi = OpenSshTunnelLauncher.BuildStartInfo(
            "ssh", Target, localPort: 18790, remoteBindHost: "127.0.0.1", remotePort: 18789);

        psi.UseShellExecute.Should().BeFalse();
        psi.RedirectStandardOutput.Should().BeTrue();
        psi.RedirectStandardError.Should().BeTrue();
        psi.CreateNoWindow.Should().BeTrue();
    }

    [Fact]
    public void Constructor_rejects_empty_ssh_executable()
    {
        Action act = () => new OpenSshTunnelLauncher("");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public async Task Open_async_rejects_invalid_local_port(int port)
    {
        var launcher = new OpenSshTunnelLauncher();
        Func<Task> act = () => launcher.OpenAsync(Target, port, "127.0.0.1", 18789);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public async Task Open_async_rejects_invalid_remote_port(int port)
    {
        var launcher = new OpenSshTunnelLauncher();
        Func<Task> act = () => launcher.OpenAsync(Target, 18790, "127.0.0.1", port);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
