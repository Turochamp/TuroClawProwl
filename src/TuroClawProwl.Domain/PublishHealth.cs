namespace TuroClawProwl.Domain;

public abstract record PublishHealth
{
    public sealed record NeverPublished : PublishHealth;

    public sealed record Healthy(DateTimeOffset LastPublishedAt) : PublishHealth;

    // SettingName is empty for a transient failure; a misconfiguration always names it.
    public sealed record Failed(
        DateTimeOffset At,
        bool IsMisconfiguration,
        string SettingName,
        string Detail) : PublishHealth;
}
