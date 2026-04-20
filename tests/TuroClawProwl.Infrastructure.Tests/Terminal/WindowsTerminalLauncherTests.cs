using FluentAssertions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.Terminal;

namespace TuroClawProwl.Infrastructure.Tests.Terminal;

public class WindowsTerminalLauncherTests
{
    private static readonly SshTarget Target = new(Host: "macmini.lan", User: "turo");

    [Fact]
    public void Build_start_info_with_wt_path_uses_windows_terminal_and_argument_list()
    {
        var psi = WindowsTerminalLauncher.BuildStartInfo(
            windowsTerminalPath: @"C:\Users\turo\AppData\Local\Microsoft\WindowsApps\wt.exe",
            sshExecutable: "ssh",
            target: Target,
            remoteCommand: "openclaw tui");

        psi.FileName.Should().EndWith("wt.exe");
        psi.Arguments.Should().BeEmpty();
        psi.ArgumentList.Should().ContainInOrder("ssh", "turo@macmini.lan", "openclaw tui");
    }

    [Fact]
    public void Build_start_info_without_wt_falls_back_to_ssh_directly()
    {
        var psi = WindowsTerminalLauncher.BuildStartInfo(
            windowsTerminalPath: null,
            sshExecutable: "ssh",
            target: Target,
            remoteCommand: "openclaw tui");

        psi.FileName.Should().Be("ssh");
        psi.Arguments.Should().BeEmpty();
        psi.ArgumentList.Should().ContainInOrder("turo@macmini.lan", "openclaw tui");
    }

    [Fact]
    public void Constructor_rejects_empty_ssh_executable()
    {
        Action act = () => new WindowsTerminalLauncher(sshExecutable: "");
        act.Should().Throw<ArgumentException>();
    }
}
