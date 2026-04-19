using FluentAssertions;
using TuroClawProwl.Infrastructure.Git;

namespace TuroClawProwl.Infrastructure.Tests.Live;

[Trait("Category", "Live")]
public class GitProcessRunnerLiveTests
{
    [SkippableFact]
    public async Task Discovery_plus_repo_info_works_against_a_real_repo_under_the_root()
    {
        Skip.If(string.IsNullOrWhiteSpace(LiveEnv.LiveRepoPath), "TURO_LIVE_REPO_PATH not set");
        Skip.IfNot(Directory.Exists(LiveEnv.LiveRepoPath!), $"Live repo path does not exist: {LiveEnv.LiveRepoPath}");

        var discovery = new FileSystemRepoDiscovery();
        var runner = new GitProcessRunner();

        var repos = await discovery.DiscoverAsync(LiveEnv.LiveRepoPath!);
        Skip.If(repos.Count == 0, $"No git repos found under {LiveEnv.LiveRepoPath}");

        var firstRepo = repos[0];
        var info = await runner.GetRepoInfoAsync(firstRepo);

        info.UnpushedCount.Should().BeGreaterThanOrEqualTo(0);
        info.PorcelainOutput.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task Every_discovered_repo_under_the_root_returns_consistent_info()
    {
        Skip.If(string.IsNullOrWhiteSpace(LiveEnv.LiveRepoPath), "TURO_LIVE_REPO_PATH not set");
        Skip.IfNot(Directory.Exists(LiveEnv.LiveRepoPath!), $"Live repo path does not exist: {LiveEnv.LiveRepoPath}");

        var discovery = new FileSystemRepoDiscovery();
        var runner = new GitProcessRunner();

        var repos = await discovery.DiscoverAsync(LiveEnv.LiveRepoPath!);
        Skip.If(repos.Count == 0, $"No git repos found under {LiveEnv.LiveRepoPath}");

        foreach (var repoPath in repos)
        {
            var info = await runner.GetRepoInfoAsync(repoPath);

            info.UnpushedCount.Should().BeGreaterThanOrEqualTo(0, $"{repoPath} unpushed count must be non-negative");
            info.PorcelainOutput.Should().NotBeNull($"{repoPath} porcelain output must not be null");
            if (!info.HasUpstream)
            {
                info.UnpushedCount.Should().Be(0,
                    $"{repoPath} has no upstream, so UnpushedCount must be 0 per GitProcessRunner contract");
            }
        }
    }
}
