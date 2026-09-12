namespace TuroClawProwl.Application.Ports;

public abstract record BundlePublishResult
{
    public sealed record Success(string CommitSha, int FileCount) : BundlePublishResult;

    public sealed record Misconfigured(string SettingName, string Detail) : BundlePublishResult;

    public sealed record Transient(string Detail) : BundlePublishResult;
}
