using System.Globalization;

namespace TuroClawProwl.Domain;

public static class TooltipComposer
{
    public const string LineSeparator = "\n";

    public static string Compose(GatewayHealth gateway, PublishHealth publish, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(publish);

        return ComposeGatewayLine(gateway) + LineSeparator + ComposePublishLine(publish, asOf);
    }

    private static string ComposeGatewayLine(GatewayHealth gateway) => gateway switch
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

    private static string ComposePublishLine(PublishHealth publish, DateTimeOffset asOf) => publish switch
    {
        PublishHealth.NeverPublished => "Bundle: not published yet",
        PublishHealth.Healthy h =>
            $"Bundle: published {BundleStaleness.FormatAge(BundleStaleness.AgeAt(asOf, h.LastPublishedAt))} ago",
        PublishHealth.Failed { IsMisconfiguration: true } f =>
            $"Bundle: misconfigured — check {f.SettingName}",
        PublishHealth.Failed f =>
            $"Bundle: publish failed — {Clip(f.Detail, 60)}",
        _ => throw new ArgumentException(
            $"Unknown publish health variant: {publish.GetType().Name}",
            nameof(publish)),
    };

    private static string Clip(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
