namespace TuroClawProwl.Application;

public sealed record PushOutcome(string RepoKey, bool IsSuccess, string? Error)
{
    public static PushOutcome Success(string repoKey) => new(repoKey, IsSuccess: true, Error: null);

    public static PushOutcome Failure(string repoKey, string error) =>
        new(repoKey, IsSuccess: false, Error: error);
}
