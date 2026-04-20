using FluentAssertions;
using Moq;
using TuroClawProwl.Application;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Tests.UseCases;

public class PushTodayFilesUseCaseTests
{
    private const string RepoA = @"C:\Git\sample\repo-a";
    private const string RepoB = @"C:\Git\sample\repo-b";
    private const string RepoC = @"C:\Git\sample\repo-c";

    private readonly Mock<IGitRunner> _git = new(MockBehavior.Strict);
    private readonly Mock<IToastService> _toasts = new(MockBehavior.Strict);

    private PushTodayFilesUseCase CreateUseCase() => new(_git.Object, _toasts.Object);

    private static TodayFileStatus Synced(string repo, string name) =>
        new($@"{repo}\{name}", repo, HasUncommitted: false, IsUnpushed: false);
    private static TodayFileStatus Uncommitted(string repo, string name) =>
        new($@"{repo}\{name}", repo, HasUncommitted: true, IsUnpushed: false);
    private static TodayFileStatus Unpushed(string repo, string name) =>
        new($@"{repo}\{name}", repo, HasUncommitted: false, IsUnpushed: true);
    private static TodayFileStatus Both(string repo, string name) =>
        new($@"{repo}\{name}", repo, HasUncommitted: true, IsUnpushed: true);

    [Fact]
    public async Task Empty_input_yields_empty_summary_and_no_toast()
    {
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(Array.Empty<TodayFileStatus>());

        summary.IsEmpty.Should().BeTrue();
        _git.VerifyNoOtherCalls();
        _toasts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task All_files_synced_means_no_work_and_no_toast()
    {
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(new[]
        {
            Synced(RepoA, "STATE.md"),
            Synced(RepoB, "_index.md"),
        });

        summary.IsEmpty.Should().BeTrue();
        _git.VerifyNoOtherCalls();
        _toasts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Uncommitted_only_file_is_committed_then_pushed()
    {
        _git.Setup(g => g.CommitPathAsync(RepoA, "STATE.md", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommitResult.Success(HadChangesToCommit: true));
        _git.Setup(g => g.PushAsync(RepoA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(new[] { Uncommitted(RepoA, "STATE.md") });

        summary.SuccessCount.Should().Be(1);
        _git.Verify(g => g.CommitPathAsync(RepoA, "STATE.md", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _git.Verify(g => g.PushAsync(RepoA, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unpushed_only_file_triggers_push_without_commit()
    {
        _git.Setup(g => g.PushAsync(RepoA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(new[] { Unpushed(RepoA, "STATE.md") });

        summary.SuccessCount.Should().Be(1);
        _git.Verify(
            g => g.CommitPathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _git.Verify(g => g.PushAsync(RepoA, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Multiple_dirty_files_in_the_same_repo_produce_multiple_commits_and_one_push()
    {
        _git.Setup(g => g.CommitPathAsync(RepoA, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommitResult.Success(HadChangesToCommit: true));
        _git.Setup(g => g.PushAsync(RepoA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new[]
        {
            Uncommitted(RepoA, "a.md"),
            Uncommitted(RepoA, "b.md"),
            Uncommitted(RepoA, "c.md"),
        });

        _git.Verify(
            g => g.CommitPathAsync(RepoA, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        _git.Verify(g => g.PushAsync(RepoA, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Commit_failure_in_a_repo_skips_that_repos_push_but_other_repos_proceed()
    {
        _git.Setup(g => g.CommitPathAsync(RepoA, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommitResult.Failure("merge conflict"));
        _git.Setup(g => g.CommitPathAsync(RepoB, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommitResult.Success(HadChangesToCommit: true));
        _git.Setup(g => g.PushAsync(RepoB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(new[]
        {
            Uncommitted(RepoA, "x.md"),
            Uncommitted(RepoB, "y.md"),
        });

        summary.FailureCount.Should().Be(1);
        summary.SuccessCount.Should().Be(1);
        _git.Verify(g => g.PushAsync(RepoA, It.IsAny<CancellationToken>()), Times.Never);
        _git.Verify(g => g.PushAsync(RepoB, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Push_failure_is_recorded_but_does_not_abort_other_repos()
    {
        _git.Setup(g => g.PushAsync(RepoA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Failure("remote rejected"));
        _git.Setup(g => g.PushAsync(RepoB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(new[] { Unpushed(RepoA, "a.md"), Unpushed(RepoB, "b.md") });

        summary.Outcomes.Should().HaveCount(2);
        summary.SuccessCount.Should().Be(1);
        summary.FailureCount.Should().Be(1);
        summary.Outcomes.Single(o => o.RepoKey == RepoA).Error.Should().Contain("remote rejected");
    }

    [Fact]
    public async Task Mixed_repos_with_uncommitted_and_unpushed_files_commit_the_dirty_then_push_once_per_repo()
    {
        _git.Setup(g => g.CommitPathAsync(RepoA, "x.md", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommitResult.Success(true));
        _git.Setup(g => g.PushAsync(RepoA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Success());
        _git.Setup(g => g.PushAsync(RepoC, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        // RepoA has one dirty and one already-unpushed file -> commit dirty, push once.
        // RepoB has only synced files -> neither commit nor push.
        // RepoC has one unpushed file -> push only, no commit.
        var summary = await useCase.ExecuteAsync(new[]
        {
            Uncommitted(RepoA, "x.md"),
            Unpushed(RepoA, "y.md"),
            Synced(RepoB, "clean.md"),
            Unpushed(RepoC, "z.md"),
        });

        summary.SuccessCount.Should().Be(2);
        _git.Verify(g => g.CommitPathAsync(RepoA, "x.md", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _git.Verify(g => g.CommitPathAsync(It.IsAny<string>(), It.Is<string>(s => s != "x.md"), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _git.Verify(g => g.PushAsync(RepoA, It.IsAny<CancellationToken>()), Times.Once);
        _git.Verify(g => g.PushAsync(RepoC, It.IsAny<CancellationToken>()), Times.Once);
        _git.Verify(g => g.PushAsync(RepoB, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task File_that_is_both_uncommitted_and_unpushed_gets_committed_then_pushed_exactly_once()
    {
        _git.Setup(g => g.CommitPathAsync(RepoA, "both.md", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommitResult.Success(true));
        _git.Setup(g => g.PushAsync(RepoA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new[] { Both(RepoA, "both.md") });

        _git.Verify(g => g.CommitPathAsync(RepoA, "both.md", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _git.Verify(g => g.PushAsync(RepoA, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_to_git_runner_and_toast_service()
    {
        using var cts = new CancellationTokenSource();
        _git.Setup(g => g.CommitPathAsync(RepoA, "a.md", It.IsAny<string>(), cts.Token))
            .ReturnsAsync(new GitCommitResult.Success(true));
        _git.Setup(g => g.PushAsync(RepoA, cts.Token))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), cts.Token))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new[] { Uncommitted(RepoA, "a.md") }, cts.Token);

        _git.Verify(g => g.CommitPathAsync(RepoA, "a.md", It.IsAny<string>(), cts.Token), Times.Once);
        _git.Verify(g => g.PushAsync(RepoA, cts.Token), Times.Once);
        _toasts.Verify(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), cts.Token), Times.Once);
    }

    [Fact]
    public void Constructor_rejects_null_git_runner()
    {
        Action act = () => new PushTodayFilesUseCase(null!, _toasts.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_toast_service()
    {
        Action act = () => new PushTodayFilesUseCase(_git.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Execute_rejects_null_input()
    {
        var useCase = CreateUseCase();
        Func<Task> act = () => useCase.ExecuteAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
