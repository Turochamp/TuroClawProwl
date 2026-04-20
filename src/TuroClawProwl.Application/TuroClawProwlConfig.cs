namespace TuroClawProwl.Application;

public sealed record TuroClawProwlConfig
{
    public string GatewayUrl { get; init; } = "";
    public string SshHost { get; init; } = "";
    public string SshUser { get; init; } = "";
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(15);
    public bool AutostartEnabled { get; init; } = true;
    public string TodaySkillPath { get; init; } = "";
    public string TodayCcaRoot { get; init; } = "";
    public string TodayCrmIndexPath { get; init; } = "";
}
