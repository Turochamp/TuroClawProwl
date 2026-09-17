using Microsoft.Toolkit.Uwp.Notifications;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Infrastructure.Toast;

public sealed class ToastNotificationsToastService : IToastService
{
    public Task NotifyGatewayTransitionAsync(
        StateTransition<GatewayHealth> transition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var title = transition.To switch
        {
            GatewayHealth.Healthy => "Gateway is back",
            GatewayHealth.Unreachable => "Gateway unreachable",
            _ => "Gateway state change",
        };
        var body = $"{Label(transition.From)} \u2192 {Label(transition.To)}";

        new ToastContentBuilder()
            .AddText(title)
            .AddText(body)
            .Show();
        return Task.CompletedTask;
    }

    public Task NotifyTodaySyncFailureAsync(
        string title,
        string detail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(detail);

        new ToastContentBuilder()
            .AddText(title)
            .AddText(Clip(detail, 200))
            .Show();
        return Task.CompletedTask;
    }

    public Task NotifyGatewayRestartResultAsync(SshCommandResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var (title, body) = result switch
        {
            SshCommandResult.Success => ("Gateway restart", "Restart command completed."),
            SshCommandResult.Failure f => ("Gateway restart failed", $"Exit {f.ExitCode}: {Clip(f.StdErr)}"),
            _ => ("Gateway restart", "Unknown result."),
        };

        new ToastContentBuilder()
            .AddText(title)
            .AddText(body)
            .Show();
        return Task.CompletedTask;
    }

    private static string Label(GatewayHealth health) => health switch
    {
        GatewayHealth.NeverReached => "never reached",
        GatewayHealth.Healthy => "healthy",
        GatewayHealth.Unreachable => "unreachable",
        _ => health.GetType().Name,
    };

    private static string Clip(string text, int max = 200) =>
        text.Length <= max ? text : text[..max] + "…";

    public Task NotifyBundlePublishFailureAsync(
        PublishHealth.Failed failure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var builder = new ToastContentBuilder();
        foreach (var line in PublishFailureToast.Compose(failure))
            builder.AddText(line);

        builder.Show();
        return Task.CompletedTask;
    }
}
