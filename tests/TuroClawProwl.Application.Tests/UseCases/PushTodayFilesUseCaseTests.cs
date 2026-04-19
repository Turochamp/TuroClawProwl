using FluentAssertions;
using Moq;
using TuroClawProwl.Application;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Tests.UseCases;

public class PushTodayFilesUseCaseTests
{
    private const string RepoA = @"C:\Git\CCA\CCA-AgentBrew";
    private const string RepoB = @"C:\Git\CCA\crm";
    private const string RepoC = @"C:\Git\CCA\CCA-HomeBase";

    private readonly Mock<IGitRunner> _git = new(MockBehavior.Strict);
    private readonly Mock<IToastService> _toasts = new(MockBehavior.Strict);

    private PushTodayFilesUseCase CreateUseCase() => new(_git.Object, _toasts.Object);

    private static TodayFileStatus Synced(string repo) => new($@"{repo}\a.md", repo, false, false);
    private static TodayFileStatus Uncommitted(string repo) => new($@"{repo}\d.md", repo, true, false);
    private static TodayFileStatus Unpushed(string repo) => new($@"{repo}\u.md", repo, false, true);

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
    public async Task All_files_synced_means_no_repos_are_pushed_and_no_toast()
    {
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(new[] { Synced(RepoA), Synced(RepoB) });

        summary.IsEmpty.Should().BeTrue();
        _git.VerifyNoOtherCalls();
        _toasts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Repos_with_unpushed_files_are_pushed_once_each_and_summary_is_toasted()
    {
        _git.Setup(g => g.PushAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(new[]
        {
            Unpushed(RepoA), Unpushed(RepoA),
            Synced(RepoB),
            Unpushed(RepoC),
        });

        summary.SuccessCount.Should().Be(2);
        _git.Verify(g => g.PushAsync(RepoA, It.IsAny<CancellationToken>()), Times.Once);
        _git.Verify(g => g.PushAsync(RepoC, It.IsAny<CancellationToken>()), Times.Once);
        _git.Verify(g => g.PushAsync(RepoB, It.IsAny<CancellationToken>()), Times.Never);
        _toasts.Verify(
            t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Uncommitted_only_files_do_not_cause_a_push()
    {
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(new[] { Uncommitted(RepoA) });

        summary.IsEmpty.Should().BeTrue();
        _git.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_push_failure_does_not_abort_remaining_pushes()
    {
        _git.Setup(g => g.PushAsync(RepoA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Failure("remote rejected"));
        _git.Setup(g => g.PushAsync(RepoB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(new[] { Unpushed(RepoA), Unpushed(RepoB) });

        summary.Outcomes.Should().HaveCount(2);
        summary.SuccessCount.Should().Be(1);
        summary.FailureCount.Should().Be(1);
        summary.Outcomes.Single(o => o.RepoKey == RepoA).Error.Should().Be("remote rejected");
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_to_git_runner_and_toast_service()
    {
        using var cts = new CancellationTokenSource();
        _git.Setup(g => g.PushAsync(RepoA, cts.Token))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), cts.Token))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new[] { Unpushed(RepoA) }, cts.Token);

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
