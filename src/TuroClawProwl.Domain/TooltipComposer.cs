using System.Globalization;

namespace TuroClawProwl.Domain;

public static class TooltipComposer
{
    public const string LineSeparator = "\n";

    public static string Compose(GatewayHealth gateway, IReadOnlyCollection<RepoState> repos)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(repos);

        var gatewayLine = gateway switch
        {
            GatewayHealth.NeverReached => "Gateway: never reached",
            GatewayHealth.Healthy => "Gateway: healthy",
            GatewayHealth.Unreachable { LastSeenHealthy: { } lastSeen } =>
                $"Gateway: unreachable (last seen {lastSeen.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC)",
            GatewayHealth.Unreachable => "Gateway: unreachable",
            _ => throw new ArgumentException(
                $"Unknown gateway health variant: {gateway.GetType().Name}",
                nameof(gateway)),
        };

        var total = repos.Count;
        var unpushed = repos.Count(r => r.UnpushedCount > 0);
        var uncommitted = repos.Count(r => r.HasUncommitted);
        var noUpstream = repos.Count(r => !r.HasUpstream);
        var clean = repos.Count(r => r.IsClean);

        var reposLine =
            $"Repos: {total} ({clean} clean, {unpushed} unpushed, {uncommitted} uncommitted, {noUpstream} no upstream)";

        return gatewayLine + LineSeparator + reposLine;
    }
}
