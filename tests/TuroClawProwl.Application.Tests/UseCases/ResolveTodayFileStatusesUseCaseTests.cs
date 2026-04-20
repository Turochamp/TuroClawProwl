using FluentAssertions;
using Moq;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Tests.UseCases;

public class ResolveTodayFileStatusesUseCaseTests
{
    private const string RepoA = @"C:\Git\sample\repo-a";
    private const string RepoB = @"C:\Git\sample\repo-b";

    private readonly Mock<IGitRunner> _git = new(MockBehavior.Strict);
    private readonly Mock<ITrayView> _tray = new(MockBehavior.Strict);

    public ResolveTodayFileStatusesUseCaseTests()
    {
        _tray.Setup(t => t.SetTodayFileStatusesAsync(
                It.IsAny<IReadOnlyCollection<TodayFileStatus>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private ResolveTodayFileStatusesUseCase CreateUseCase() => new(_git.Object, _tray.Object);

    [Fact]
    public async Task Empty_input_still_pushes_empty_status_list_to_tray()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(Array.Empty<(string, string)>());

        result.Should().BeEmpty();
        _tray.Verify(t => t.SetTodayFileStatusesAsync(
                It.Is<IReadOnlyCollection<TodayFileStatus>>(l => l.Count == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task File_with_no_porcelain_output_and_clean_repo_is_reported_as_synced()
    {
        _git.Setup(g => g.GetRepoInfoAsync(RepoA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepoInfo("", 0, true));
        _git.Setup(g => g.GetFilePorcelainAsync(RepoA, "STATE.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync("");
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new[] { (@"C:\Git\sample\repo-a\STATE.md", RepoA) });

        result.Should().ContainSingle().Which.IsSynced.Should().BeTrue();
    }

    [Fact]
    public async Task File_with_nonempty_porcelain_output_is_reported_as_uncommitted()
    {
        _git.Setup(g => g.GetRepoInfoAsync(RepoA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepoInfo("", 0, true));
        _git.Setup(g => g.GetFilePorcelainAsync(RepoA, "STATE.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync(" M STATE.md");
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new[] { (@"C:\Git\sample\repo-a\STATE.md", RepoA) });

        result.Single().HasUncommitted.Should().BeTrue();
        result.Single().IsUnpushed.Should().BeFalse();
    }

    [Fact]
    public async Task File_in_repo_with_unpushed_commits_is_reported_as_unpushed()
    {
        _git.Setup(g => g.GetRepoInfoAsync(RepoA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepoInfo("", 2, true));
        _git.Setup(g => g.GetFilePorcelainAsync(RepoA, "STATE.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync("");
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new[] { (@"C:\Git\sample\repo-a\STATE.md", RepoA) });

        result.Single().IsUnpushed.Should().BeTrue();
        result.Single().HasUncommitted.Should().BeFalse();
    }

    [Fact]
    public async Task File_in_repo_without_upstream_is_reported_as_unpushed()
    {
        _git.Setup(g => g.GetRepoInfoAsync(RepoA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepoInfo("", 0, false));
        _git.Setup(g => g.GetFilePorcelainAsync(RepoA, "STATE.md", It.IsAny<CancellationToken>()))
            .ReturnsAsync("");
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync(new[] { (@"C:\Git\sample\repo-a\STATE.md", RepoA) });

        result.Single().IsUnpushed.Should().BeTrue();
    }

    [Fact]
    public async Task Repo_info_is_queried_once_per_repo_regardless_of_how_many_files_live_there()
    {
        _git.Setup(g => g.GetRepoInfoAsync(RepoA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepoInfo("", 0, true));
        _git.Setup(g => g.GetFilePorcelainAsync(RepoA, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("");
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new[]
        {
            (@"C:\Git\sample\repo-a\STATE.md", RepoA),
            (@"C:\Git\sample\repo-a\NOTES.md", RepoA),
            (@"C:\Git\sample\repo-a\TODO.md", RepoA),
        });

        _git.Verify(g => g.GetRepoInfoAsync(RepoA, It.IsAny<CancellationToken>()), Times.Once);
        _git.Verify(g => g.GetFilePorcelainAsync(RepoA, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Resolver_forwards_cancellation_to_every_git_and_tray_call()
    {
        using var cts = new CancellationTokenSource();
        _git.Setup(g => g.GetRepoInfoAsync(RepoA, cts.Token))
            .ReturnsAsync(new RepoInfo("", 0, true));
        _git.Setup(g => g.GetFilePorcelainAsync(RepoA, It.IsAny<string>(), cts.Token))
            .ReturnsAsync("");
        _tray.Setup(t => t.SetTodayFileStatusesAsync(It.IsAny<IReadOnlyCollection<TodayFileStatus>>(), cts.Token))
            .Returns(Task.CompletedTask);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new[] { (@"C:\Git\sample\repo-a\STATE.md", RepoA) }, cts.Token);

        _git.Verify(g => g.GetRepoInfoAsync(RepoA, cts.Token), Times.Once);
        _git.Verify(g => g.GetFilePorcelainAsync(RepoA, It.IsAny<string>(), cts.Token), Times.Once);
        _tray.Verify(t => t.SetTodayFileStatusesAsync(It.IsAny<IReadOnlyCollection<TodayFileStatus>>(), cts.Token), Times.Once);
    }

    [Fact]
    public void Constructor_rejects_null_git_runner()
    {
        Action act = () => new ResolveTodayFileStatusesUseCase(null!, _tray.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_tray_view()
    {
        Action act = () => new ResolveTodayFileStatusesUseCase(_git.Object, null!);
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
