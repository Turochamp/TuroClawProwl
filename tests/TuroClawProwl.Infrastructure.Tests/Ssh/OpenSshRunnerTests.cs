using FluentAssertions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.Ssh;

namespace TuroClawProwl.Infrastructure.Tests.Ssh;

public class OpenSshRunnerTests
{
    private static readonly SshTarget Target = new(Host: "macmini.lan", User: "turo");

    [Fact]
    public void Build_start_info_uses_argument_list_not_the_arguments_string()
    {
        var psi = OpenSshRunner.BuildStartInfo("ssh", Target, "openclaw", new[] { "gateway", "restart" });

        psi.Arguments.Should().BeEmpty();
        psi.ArgumentList.Should().ContainInOrder("turo@macmini.lan", "openclaw", "gateway", "restart");
    }

    [Fact]
    public void Build_start_info_disables_shell_execute_and_redirects_streams()
    {
        var psi = OpenSshRunner.BuildStartInfo("ssh", Target, "openclaw", Array.Empty<string>());

        psi.UseShellExecute.Should().BeFalse();
        psi.RedirectStandardOutput.Should().BeTrue();
        psi.RedirectStandardError.Should().BeTrue();
    }

    [Fact]
    public void Build_start_info_handles_empty_trailing_args()
    {
        var psi = OpenSshRunner.BuildStartInfo("ssh", Target, "tui", Array.Empty<string>());

        psi.ArgumentList.Should().ContainInOrder("turo@macmini.lan", "tui");
        psi.ArgumentList.Should().HaveCount(2);
    }

    [Fact]
    public void Constructor_rejects_empty_ssh_executable()
    {
        Action act = () => new OpenSshRunner("");
        act.Should().Throw<ArgumentException>();
    }
}
