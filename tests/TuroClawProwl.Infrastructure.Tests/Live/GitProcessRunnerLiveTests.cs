using FluentAssertions;
using TuroClawProwl.Infrastructure.Git;

namespace TuroClawProwl.Infrastructure.Tests.Live;

[Trait("Category", "Live")]
public class GitProcessRunnerLiveTests
{
    [SkippableFact]
    public async Task Real_repo_info_query_returns_plausible_values_and_does_not_throw()
    {
        Skip.If(string.IsNullOrWhiteSpace(LiveEnv.LiveRepoPath), "TURO_LIVE_REPO_PATH not set");
        Skip.IfNot(Directory.Exists(LiveEnv.LiveRepoPath!), $"Live repo path does not exist: {LiveEnv.LiveRepoPath}");

        var runner = new GitProcessRunner();

        var info = await runner.GetRepoInfoAsync(LiveEnv.LiveRepoPath!);

        info.UnpushedCount.Should().BeGreaterThanOrEqualTo(0);
        info.PorcelainOutput.Should().NotBeNull();
    }
}
