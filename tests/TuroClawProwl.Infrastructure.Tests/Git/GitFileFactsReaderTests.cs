using FluentAssertions;
using TuroClawProwl.Infrastructure.Git;
using TuroClawProwl.Infrastructure.Tests.TestSupport;

namespace TuroClawProwl.Infrastructure.Tests.Git;

[Trait("Category", "Integration")]
public class GitFileFactsReaderTests
{
    [Fact]
    public async Task A_committed_clean_file_reports_its_last_commit_author_date_and_is_not_dirty()
    {
        using var repo = new TempGitRepo();
        repo.Commit("add state", "STATE.md", "# state");

        var facts = await new GitFileFactsReader().GetFileFactsAsync(Path.Combine(repo.Path, "STATE.md"));

        facts.InRepository.Should().BeTrue();
        facts.Tracked.Should().BeTrue();
        facts.Dirty.Should().BeFalse();
        facts.LastCommitAuthorDate.Should().NotBeNull();
        facts.LastCommitAuthorDate!.Value.Should().BeCloseTo(DateTimeOffset.Now, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task A_repository_with_no_remote_still_reports_full_facts()
    {
        using var repo = new TempGitRepo();
        repo.Commit("add state", "STATE.md", "# state");

        var facts = await new GitFileFactsReader().GetFileFactsAsync(Path.Combine(repo.Path, "STATE.md"));

        facts.InRepository.Should().BeTrue();
        facts.LastCommitAuthorDate.Should().NotBeNull();
    }

    [Fact]
    public async Task A_tracked_file_with_uncommitted_edits_is_dirty()
    {
        using var repo = new TempGitRepo();
        repo.Commit("add state", "STATE.md", "# state");
        repo.WriteFile("STATE.md", "# edited");

        var facts = await new GitFileFactsReader().GetFileFactsAsync(Path.Combine(repo.Path, "STATE.md"));

        facts.Tracked.Should().BeTrue();
        facts.Dirty.Should().BeTrue();
        facts.LastCommitAuthorDate.Should().NotBeNull();
    }

    [Fact]
    public async Task An_untracked_file_inside_a_repository_has_no_committed_date()
    {
        using var repo = new TempGitRepo();
        repo.WriteFile("STATE.md", "# never added");

        var facts = await new GitFileFactsReader().GetFileFactsAsync(Path.Combine(repo.Path, "STATE.md"));

        facts.InRepository.Should().BeTrue();
        facts.Tracked.Should().BeFalse();
        facts.Dirty.Should().BeTrue();
        facts.LastCommitAuthorDate.Should().BeNull();
    }

    [Fact]
    public async Task A_file_outside_any_repository_has_no_committed_date()
    {
        using var tmp = new TempDirectory();
        var path = Path.Combine(tmp.Path, "STATE.md");
        await File.WriteAllTextAsync(path, "# state");

        var facts = await new GitFileFactsReader().GetFileFactsAsync(path);

        facts.InRepository.Should().BeFalse();
        facts.Tracked.Should().BeFalse();
        facts.Dirty.Should().BeTrue();
        facts.LastCommitAuthorDate.Should().BeNull();
    }

    [Fact]
    public async Task An_empty_path_is_rejected()
    {
        var reader = new GitFileFactsReader();

        Func<Task> act = () => reader.GetFileFactsAsync("  ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task A_file_inside_a_linked_worktree_is_found_via_its_git_file()
    {
        using var repo = new TempGitRepo();
        repo.Commit("add state", "STATE.md", "# state");
        using var home = new TempDirectory();
        var worktreePath = Path.Combine(home.Path, "linked-worktree");

        GitCli.Read(repo.Path, "worktree", "add", "--detach", worktreePath, "HEAD");
        File.WriteAllText(Path.Combine(worktreePath, "STATE.md"), "# state, from the worktree");
        GitCli.Read(worktreePath, "add", "STATE.md");
        GitCli.Read(worktreePath, "commit", "-m", "edit state from the linked worktree");

        var facts = await new GitFileFactsReader()
            .GetFileFactsAsync(Path.Combine(worktreePath, "STATE.md"));

        facts.InRepository.Should().BeTrue();
        facts.LastCommitAuthorDate.Should().NotBeNull();
    }
}
