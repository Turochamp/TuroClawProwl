using FluentAssertions;
using Moq;
using TuroClawProwl.Application;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Tests.UseCases;

public class PushUnpushedReposUseCaseTests
{
    private readonly Mock<IGitRunner> _git = new(MockBehavior.Strict);
    private readonly Mock<IToastService> _toasts = new(MockBehavior.Strict);

    private PushUnpushedReposUseCase CreateUseCase() => new(_git.Object, _toasts.Object);

    private static RepoState Clean() => new(0, false, true);
    private static RepoState Unpushed(int count = 1) => new(count, false, true);
    private static RepoState UncommittedOnly() => new(0, true, true);
    private static RepoState NoUpstream() => new(0, false, false);
    private static RepoState DirtyAndUnpushed() => new(2, true, true);

    [Fact]
    public async Task Empty_state_dictionary_yields_empty_summary_and_no_toast()
    {
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(new Dictionary<string, RepoState>());

        summary.IsEmpty.Should().BeTrue();
        _git.VerifyNoOtherCalls();
        _toasts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task States_with_no_unpushed_commits_produce_no_git_calls_and_no_toast()
    {
        var states = new Dictionary<string, RepoState>
        {
            ["clean"]       = Clean(),
            ["dirty"]       = UncommittedOnly(),
            ["no-upstream"] = NoUpstream(),
        };
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(states);

        summary.IsEmpty.Should().BeTrue();
        _git.VerifyNoOtherCalls();
        _toasts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Uncommitted_only_and_no_upstream_repos_are_not_pushed()
    {
        _git.Setup(g => g.PushAsync("alpha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var states = new Dictionary<string, RepoState>
        {
            ["alpha"]       = Unpushed(),
            ["dirty"]       = UncommittedOnly(),
            ["no-upstream"] = NoUpstream(),
        };
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(states);

        summary.Outcomes.Should().ContainSingle().Which.RepoKey.Should().Be("alpha");
        _git.Verify(g => g.PushAsync("dirty", It.IsAny<CancellationToken>()), Times.Never);
        _git.Verify(g => g.PushAsync("no-upstream", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Successful_pushes_are_recorded_in_summary_and_reported_via_toast()
    {
        _git.Setup(g => g.PushAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var states = new Dictionary<string, RepoState>
        {
            ["alpha"] = Unpushed(),
            ["bravo"] = DirtyAndUnpushed(),
        };
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(states);

        summary.SuccessCount.Should().Be(2);
        summary.FailureCount.Should().Be(0);
        _toasts.Verify(
            t => t.NotifyPushSummaryAsync(It.Is<PushSummary>(s => s.SuccessCount == 2), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_push_failure_does_not_abort_remaining_pushes()
    {
        _git.Setup(g => g.PushAsync("alpha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Failure("remote rejected"));
        _git.Setup(g => g.PushAsync("bravo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Success());
        _git.Setup(g => g.PushAsync("charlie", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Failure("auth"));
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var states = new Dictionary<string, RepoState>
        {
            ["alpha"]   = Unpushed(),
            ["bravo"]   = Unpushed(3),
            ["charlie"] = DirtyAndUnpushed(),
        };
        var useCase = CreateUseCase();

        var summary = await useCase.ExecuteAsync(states);

        summary.Outcomes.Should().HaveCount(3);
        summary.SuccessCount.Should().Be(1);
        summary.FailureCount.Should().Be(2);
        summary.Outcomes.Single(o => o.RepoKey == "alpha").Error.Should().Be("remote rejected");
        summary.Outcomes.Single(o => o.RepoKey == "charlie").Error.Should().Be("auth");
    }

    [Fact]
    public async Task Pushes_run_in_push_planner_order_alphabetically()
    {
        var pushedOrder = new List<string>();
        _git.Setup(g => g.PushAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((path, _) => pushedOrder.Add(path))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var states = new Dictionary<string, RepoState>
        {
            ["zebra"] = Unpushed(),
            ["alpha"] = Unpushed(),
            ["mango"] = Unpushed(),
        };
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(states);

        pushedOrder.Should().ContainInOrder("alpha", "mango", "zebra");
    }

    [Fact]
    public async Task Toast_fires_exactly_once_at_the_end()
    {
        _git.Setup(g => g.PushAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var states = new Dictionary<string, RepoState>
        {
            ["a"] = Unpushed(),
            ["b"] = Unpushed(),
            ["c"] = Unpushed(),
        };
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(states);

        _toasts.Verify(
            t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_to_git_runner_and_toast_service()
    {
        using var cts = new CancellationTokenSource();
        _git.Setup(g => g.PushAsync(It.IsAny<string>(), cts.Token))
            .ReturnsAsync(new GitPushResult.Success());
        _toasts.Setup(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), cts.Token))
            .Returns(Task.CompletedTask);

        var states = new Dictionary<string, RepoState> { ["a"] = Unpushed() };
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(states, cts.Token);

        _git.Verify(g => g.PushAsync("a", cts.Token), Times.Once);
        _toasts.Verify(t => t.NotifyPushSummaryAsync(It.IsAny<PushSummary>(), cts.Token), Times.Once);
    }

    [Fact]
    public void Constructor_rejects_null_git_runner()
    {
        Action act = () => new PushUnpushedReposUseCase(null!, _toasts.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_toast_service()
    {
        Action act = () => new PushUnpushedReposUseCase(_git.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Execute_rejects_null_states()
    {
        var useCase = CreateUseCase();
        Func<Task> act = () => useCase.ExecuteAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
