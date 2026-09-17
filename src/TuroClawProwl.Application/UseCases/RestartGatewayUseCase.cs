using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Application.UseCases;

public sealed class RestartGatewayUseCase
{
    public const string RemoteCommand = "openclaw";
    public static readonly IReadOnlyList<string> RemoteArgs = new[] { "gateway", "restart" };

    private readonly ISshRunner _ssh;
    private readonly IToastService _toasts;
    private SshTarget _target;

    public RestartGatewayUseCase(SshTarget target, ISshRunner ssh, IToastService toasts)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(ssh);
        ArgumentNullException.ThrowIfNull(toasts);

        _target = target;
        _ssh = ssh;
        _toasts = toasts;
    }

    public void SetTarget(SshTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Volatile.Write(ref _target, target);
    }

    public async Task<SshCommandResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var target = Volatile.Read(ref _target);
        var result = await _ssh.RunCommandAsync(target, RemoteCommand, RemoteArgs, cancellationToken)
            .ConfigureAwait(false);

        await _toasts.NotifyGatewayRestartResultAsync(result, cancellationToken).ConfigureAwait(false);

        return result;
    }
}
