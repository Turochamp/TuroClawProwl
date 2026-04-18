using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Application.UseCases;

public sealed class OpenTuiUseCase
{
    public const string RemoteCommand = "openclaw tui";

    private readonly SshTarget _target;
    private readonly ITerminalLauncher _launcher;

    public OpenTuiUseCase(SshTarget target, ITerminalLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(launcher);

        _target = target;
        _launcher = launcher;
    }

    public Task ExecuteAsync(CancellationToken cancellationToken = default) =>
        _launcher.LaunchSshInteractiveAsync(_target, RemoteCommand, cancellationToken);
}
