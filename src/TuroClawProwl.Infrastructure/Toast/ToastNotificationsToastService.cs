using Microsoft.Toolkit.Uwp.Notifications;
using TuroClawProwl.Application;
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

    public Task NotifyPushSummaryAsync(PushSummary summary, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(summary);

        // The toast API caps at 4 text lines; one is the header, leaving 3 for
        // content. We collapse successes and failures into one line each to fit.
        var builder = new ToastContentBuilder()
            .AddText($"Today sync: {summary.SuccessCount} ok, {summary.FailureCount} failed");

        var successes = summary.Outcomes.Where(o => o.IsSuccess).Select(o => RepoName(o.RepoKey)).ToArray();
        if (successes.Length > 0)
            builder.AddText("\u2713 " + FormatRepoList(successes, maxListed: 5));

        var failures = summary.Outcomes.Where(o => !o.IsSuccess).ToArray();
        if (failures.Length > 0)
        {
            var first = failures[0];
            var line = $"\u2717 {RepoName(first.RepoKey)}: {Clip(first.Error ?? "unknown", 80)}";
            if (failures.Length > 1) line += $" (+{failures.Length - 1} more)";
            builder.AddText(line);
        }

        builder.Show();
        return Task.CompletedTask;
    }

    private static string RepoName(string path)
    {
        var name = Path.GetFileName(path);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static string FormatRepoList(string[] items, int maxListed)
    {
        if (items.Length <= maxListed) return string.Join(", ", items);
        return string.Join(", ", items.Take(maxListed)) + $" +{items.Length - maxListed} more";
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
