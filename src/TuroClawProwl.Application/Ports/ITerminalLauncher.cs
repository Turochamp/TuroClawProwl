namespace TuroClawProwl.Application.Ports;

public interface ITerminalLauncher
{
    Task LaunchSshInteractiveAsync(
        SshTarget target,
        string remoteCommand,
        CancellationToken cancellationToken = default);
}
