using System.Diagnostics;
using TuroClawProwl.Application;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Infrastructure.Toast;

// Placeholder pending the DD-2 spike verifying the unpackaged-desktop
// toast path on .NET 10. Swap in CommunityToolkit.WinUI.Notifications (or
// Windows App SDK fallback) once that decision is made.
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
