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
    public string HubRepoPath { get; init; } = "";
    public string BundleWorktreePath { get; init; } = "";
    public string BundlePublishBranch { get; init; } = "main";
    public TimeSpan SnapshotInterval { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromHours(6);
    public string GwsExecutablePath { get; init; } = "C:/Users/micha/bin/gws.cmd";
    public string GitExecutablePath { get; init; } = "git";
}
