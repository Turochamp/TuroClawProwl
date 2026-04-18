using FluentAssertions;
using Moq;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Tests.UseCases;

public class ReconcileRepoStatesUseCaseTests
{
    private const string Root = @"C:\Git\ClaudeCodeAssistants";

    private readonly Mock<IRepoDiscovery> _discovery = new(MockBehavior.Strict);
    private readonly Mock<IGitRunner> _git = new(MockBehavior.Strict);
    private readonly Mock<ITrayView> _tray = new(MockBehavior.Strict);

    public ReconcileRepoStatesUseCaseTests()
    {
        _tray.Setup(t => t.SetRepoStatesAsync(
                It.IsAny<IReadOnlyDictionary<string, RepoState>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private ReconcileRepoStatesUseCase CreateUseCase() =>
        new(_discovery.Object, _git.Object, _tray.Object);

    private void QueueDiscovery(params string[] repos)
    {
        _discovery.Setup(d => d.DiscoverAsync(Root, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repos);
    }

    private void SetupRepo(string repoPath, string porcelain, int unpushed, bool hasUpstream)
    {
        _git.Setup(g => g.GetRepoInfoAsync(repoPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepoInfo(porcelain, unpushed, hasUpstream));
    }

    [Fact]
    public async Task Empty_root_produces_empty_state_and_updates_tray_with_empty_dictionary()
    {
        QueueDiscovery();
        var useCase = CreateUseCase();

        var states = await useCase.ExecuteAsync(Root);

        states.Should().BeEmpty();
        _tray.Verify(t => t.SetRepoStatesAsync(
                It.Is<IReadOnlyDictionary<string, RepoState>>(d => d.Count == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Single_pristine_repo_is_classified_as_clean()
    {
        QueueDiscovery(@"C:\Git\repo-a");
        SetupRepo(@"C:\Git\repo-a", porcelain: "", unpushed: 0, hasUpstream: true);
        var useCase = CreateUseCase();

        var states = await useCase.ExecuteAsync(Root);

        states[@"C:\Git\repo-a"].IsClean.Should().BeTrue();
    }

    [Fact]
    public async Task Repo_with_unpushed_commits_is_classified_with_correct_count()
    {
        QueueDiscovery(@"C:\Git\repo-a");
        SetupRepo(@"C:\Git\repo-a", porcelain: "", unpushed: 3, hasUpstream: true);
        var useCase = CreateUseCase();

        var states = await useCase.ExecuteAsync(Root);

        states[@"C:\Git\repo-a"].UnpushedCount.Should().Be(3);
        states[@"C:\Git\repo-a"].NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task Repo_with_uncommitted_changes_is_classified_as_dirty()
    {
        QueueDiscovery(@"C:\Git\repo-a");
        SetupRepo(@"C:\Git\repo-a", porcelain: " M file.txt\n?? new.txt", unpushed: 0, hasUpstream: true);
        var useCase = CreateUseCase();

        var states = await useCase.ExecuteAsync(Root);

        states[@"C:\Git\repo-a"].HasUncommitted.Should().BeTrue();
    }

    [Fact]
    public async Task Repo_without_upstream_needs_attention_even_when_working_tree_is_clean()
    {
        QueueDiscovery(@"C:\Git\repo-a");
        SetupRepo(@"C:\Git\repo-a", porcelain: "", unpushed: 0, hasUpstream: false);
        var useCase = CreateUseCase();

        var states = await useCase.ExecuteAsync(Root);

        states[@"C:\Git\repo-a"].HasUpstream.Should().BeFalse();
        states[@"C:\Git\repo-a"].NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task Multiple_repos_are_all_classified_and_forwarded_to_the_tray()
    {
        QueueDiscovery(@"C:\Git\repo-a", @"C:\Git\repo-b", @"C:\Git\repo-c");
        SetupRepo(@"C:\Git\repo-a", "", 0, true);
        SetupRepo(@"C:\Git\repo-b", " M x", 2, true);
        SetupRepo(@"C:\Git\repo-c", "", 0, false);
        var useCase = CreateUseCase();

        var states = await useCase.ExecuteAsync(Root);

        states.Should().HaveCount(3);
        states[@"C:\Git\repo-a"].IsClean.Should().BeTrue();
        states[@"C:\Git\repo-b"].UnpushedCount.Should().Be(2);
        states[@"C:\Git\repo-b"].HasUncommitted.Should().BeTrue();
        states[@"C:\Git\repo-c"].HasUpstream.Should().BeFalse();

        _tray.Verify(t => t.SetRepoStatesAsync(
                It.Is<IReadOnlyDictionary<string, RepoState>>(d => d.Count == 3),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Cancellation_token_is_forwarded_to_discovery_git_runner_and_tray()
    {
        using var cts = new CancellationTokenSource();
        _discovery.Setup(d => d.DiscoverAsync(Root, cts.Token))
            .ReturnsAsync(new[] { @"C:\Git\repo-a" });
        _git.Setup(g => g.GetRepoInfoAsync(@"C:\Git\repo-a", cts.Token))
            .ReturnsAsync(new RepoInfo("", 0, true));
        _tray.Setup(t => t.SetRepoStatesAsync(It.IsAny<IReadOnlyDictionary<string, RepoState>>(), cts.Token))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(Root, cts.Token);

        _discovery.Verify(d => d.DiscoverAsync(Root, cts.Token), Times.Once);
        _git.Verify(g => g.GetRepoInfoAsync(@"C:\Git\repo-a", cts.Token), Times.Once);
        _tray.Verify(t => t.SetRepoStatesAsync(It.IsAny<IReadOnlyDictionary<string, RepoState>>(), cts.Token), Times.Once);
    }

    [Fact]
    public void Constructor_rejects_null_discovery()
    {
        Action act = () => new ReconcileRepoStatesUseCase(null!, _git.Object, _tray.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_git_runner()
    {
        Action act = () => new ReconcileRepoStatesUseCase(_discovery.Object, null!, _tray.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_tray_view()
    {
        Action act = () => new ReconcileRepoStatesUseCase(_discovery.Object, _git.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Execute_rejects_empty_root_path()
    {
        var useCase = CreateUseCase();
        Func<Task> act = () => useCase.ExecuteAsync("");
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
