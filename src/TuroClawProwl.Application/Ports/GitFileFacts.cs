namespace TuroClawProwl.Application.Ports;

public sealed record GitFileFacts(
    bool InRepository,
    bool Tracked,
    bool Dirty,
    DateTimeOffset? LastCommitAuthorDate)
{
    public static GitFileFacts Untracked() =>
        new(InRepository: true, Tracked: false, Dirty: true, LastCommitAuthorDate: null);

    public static GitFileFacts OutsideRepository() =>
        new(InRepository: false, Tracked: false, Dirty: true, LastCommitAuthorDate: null);
}
