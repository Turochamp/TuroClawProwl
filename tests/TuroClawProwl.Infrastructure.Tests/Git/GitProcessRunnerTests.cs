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
    public async Task CommitPath_returns_success_with_HadChanges_false_when_file_is_unchanged()
    {
        using var repo = new TempGitRepo();
        repo.Commit("add baseline", "a.txt", "1");
        var runner = new GitProcessRunner();

        var result = await runner.CommitPathAsync(repo.Path, "a.txt", "auto: nothing");

        result.Should().BeOfType<GitCommitResult.Success>()
            .Which.HadChangesToCommit.Should().BeFalse();
    }

    [Fact]
    public async Task CommitPath_stages_and_commits_a_single_modified_file()
    {
        using var repo = new TempGitRepo();
        repo.Commit("add baseline", "a.txt", "1");
        repo.WriteFile("a.txt", "2");
        var runner = new GitProcessRunner();

        var result = await runner.CommitPathAsync(repo.Path, "a.txt", "auto: today sync");

        result.Should().BeOfType<GitCommitResult.Success>()
            .Which.HadChangesToCommit.Should().BeTrue();
    }

    [Fact]
    public async Task CommitPath_leaves_other_dirty_files_unstaged()
    {
        using var repo = new TempGitRepo();
        repo.Commit("add baselines", "a.txt", "1");
        repo.Commit("add other", "b.txt", "1");
        repo.WriteFile("a.txt", "2");
        repo.WriteFile("b.txt", "2");
        var runner = new GitProcessRunner();

        await runner.CommitPathAsync(repo.Path, "a.txt", "auto: only a");

        var info = await runner.GetRepoInfoAsync(repo.Path);
        info.PorcelainOutput.Should().Contain("b.txt",
            "b.txt was not part of the commit and must remain dirty");
        info.PorcelainOutput.Should().NotContain(" M a.txt",
            "a.txt should be fully committed after CommitPathAsync");
    }

    [Fact]
    public void Constructor_rejects_empty_executable_path()
    {
        Action act = () => new GitProcessRunner("");
        act.Should().Throw<ArgumentException>();
    }
}
