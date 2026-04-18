using System.Diagnostics;
using TuroClawProwl.Application;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Infrastructure.Toast;

// Placeholder. DD-2 amendment: swap for Microsoft.Toolkit.Uwp.Notifications
// (archived but functional on .NET 10) during the Presentation step, when
// AUMID registration and the Start Menu shortcut are in place.
public sealed class TraceToastService : IToastService
{
    public Task NotifyGatewayTransitionAsync(
        StateTransition<GatewayHealth> transition,
        CancellationToken cancellationToken = default)
    {
        Trace.WriteLine($"[toast] gateway: {transition.From.GetType().Name} -> {transition.To.GetType().Name}");
        return Task.CompletedTask;
    }

    public Task NotifyPushSummaryAsync(PushSummary summary, CancellationToken cancellationToken = default)
    {
        Trace.WriteLine($"[toast] push: {summary.SuccessCount} ok, {summary.FailureCount} failed");
        return Task.CompletedTask;
    }

    public Task NotifyGatewayRestartResultAsync(SshCommandResult result, CancellationToken cancellationToken = default)
    {
        var label = result switch
        {
            SshCommandResult.Success => "success",
            SshCommandResult.Failure f => $"failure exit={f.ExitCode}",
            _ => "unknown",
        };
        Trace.WriteLine($"[toast] gateway-restart: {label}");
        return Task.CompletedTask;
    }
}
