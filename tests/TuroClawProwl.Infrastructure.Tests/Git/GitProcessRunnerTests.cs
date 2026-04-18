using FluentAssertions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.Git;
using TuroClawProwl.Infrastructure.Tests.TestSupport;

namespace TuroClawProwl.Infrastructure.Tests.Git;

[Trait("Category", "Integration")]
public class GitProcessRunnerTests
{
    [Fact]
    public async Task Fresh_repo_without_upstream_reports_no_upstream_and_zero_unpushed()
    {
        using var repo = new TempGitRepo();
        var runner = new GitProcessRunner();

        var info = await runner.GetRepoInfoAsync(repo.Path);

        info.HasUpstream.Should().BeFalse();
        info.UnpushedCount.Should().Be(0);
        info.PorcelainOutput.Should().BeEmpty();
    }

    [Fact]
    public async Task Repo_with_upstream_and_no_new_commits_reports_zero_unpushed()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        var runner = new GitProcessRunner();

        var info = await runner.GetRepoInfoAsync(repo.Path);

        info.HasUpstream.Should().BeTrue();
        info.UnpushedCount.Should().Be(0);
    }

    [Fact]
    public async Task Repo_with_upstream_and_two_new_commits_reports_two_unpushed()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Commit("add a", "a.txt", "1");
        repo.Commit("add b", "b.txt", "2");
        var runner = new GitProcessRunner();

        var info = await runner.GetRepoInfoAsync(repo.Path);

        info.HasUpstream.Should().BeTrue();
        info.UnpushedCount.Should().Be(2);
    }

    [Fact]
    public async Task Repo_with_uncommitted_changes_returns_nonempty_porcelain()
    {
        using var repo = new TempGitRepo();
        repo.WriteFile("dirty.txt", "x");
        var runner = new GitProcessRunner();

        var info = await runner.GetRepoInfoAsync(repo.Path);

        info.PorcelainOutput.Should().Contain("dirty.txt");
    }

    [Fact]
    public async Task Push_succeeds_for_repo_with_pushable_commits()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Commit("add a", "a.txt", "1");
        var runner = new GitProcessRunner();

        var result = await runner.PushAsync(repo.Path);

        result.Should().BeOfType<GitPushResult.Success>();
    }

    [Fact]
    public async Task Push_returns_failure_with_error_for_repo_without_upstream()
    {
        using var repo = new TempGitRepo();
        var runner = new GitProcessRunner();

        var result = await runner.PushAsync(repo.Path);

        result.Should().BeOfType<GitPushResult.Failure>()
            .Which.Error.Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_rejects_empty_executable_path()
    {
        Action act = () => new GitProcessRunner("");
        act.Should().Throw<ArgumentException>();
    }
}
