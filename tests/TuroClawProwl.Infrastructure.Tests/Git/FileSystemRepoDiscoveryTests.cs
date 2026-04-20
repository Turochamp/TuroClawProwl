using FluentAssertions;
using TuroClawProwl.Infrastructure.Git;
using TuroClawProwl.Infrastructure.Tests.TestSupport;

namespace TuroClawProwl.Infrastructure.Tests.Git;

public class FileSystemRepoDiscoveryTests
{
    [Fact]
    public async Task Empty_root_directory_returns_empty_list()
    {
        using var tmp = new TempDirectory();
        var discovery = new FileSystemRepoDiscovery();

        var repos = await discovery.DiscoverAsync(tmp.Path);

        repos.Should().BeEmpty();
    }

    [Fact]
    public async Task Non_existent_root_returns_empty_list()
    {
        var discovery = new FileSystemRepoDiscovery();
        var nonExistent = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N"));

        var repos = await discovery.DiscoverAsync(nonExistent);

        repos.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_direct_children_containing_dot_git_are_returned()
    {
        using var tmp = new TempDirectory();
        var repoA = tmp.CreateRepoFolder("repo-a");
        var repoB = tmp.CreateRepoFolder("repo-b");
        tmp.CreateSubDirectory("not-a-repo");
        var discovery = new FileSystemRepoDiscovery();

        var repos = await discovery.DiscoverAsync(tmp.Path);

        repos.Should().BeEquivalentTo(new[] { repoA, repoB });
    }

    [Fact]
    public async Task Root_itself_is_returned_when_it_contains_dot_git()
    {
        using var tmp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tmp.Path, ".git"));
        var discovery = new FileSystemRepoDiscovery();

        var repos = await discovery.DiscoverAsync(tmp.Path);

        repos.Should().ContainSingle().Which.Should().Be(tmp.Path);
    }

    [Fact]
    public async Task Root_is_returned_alongside_child_repos_when_both_are_repos()
    {
        using var tmp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(tmp.Path, ".git"));
        var childA = tmp.CreateRepoFolder("child-a");
        var childB = tmp.CreateRepoFolder("child-b");
        var discovery = new FileSystemRepoDiscovery();

        var repos = await discovery.DiscoverAsync(tmp.Path);

        repos.Should().BeEquivalentTo(new[] { tmp.Path, childA, childB });
    }

    [Fact]
    public async Task Repos_nested_two_levels_deep_are_not_returned()
    {
        using var tmp = new TempDirectory();
        var outer = tmp.CreateSubDirectory("outer");
        Directory.CreateDirectory(Path.Combine(outer, "inner-repo", ".git"));
        var discovery = new FileSystemRepoDiscovery();

        var repos = await discovery.DiscoverAsync(tmp.Path);

        repos.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_throws_operation_canceled()
    {
        using var tmp = new TempDirectory();
        for (var i = 0; i < 5; i++) tmp.CreateRepoFolder($"repo-{i}");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var discovery = new FileSystemRepoDiscovery();

        Func<Task> act = () => discovery.DiscoverAsync(tmp.Path, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Empty_root_path_throws_argument_exception()
    {
        var discovery = new FileSystemRepoDiscovery();
        Func<Task> act = () => discovery.DiscoverAsync("");
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
