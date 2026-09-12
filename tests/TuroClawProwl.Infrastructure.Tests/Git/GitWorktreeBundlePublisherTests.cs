using System.Text.Json;
using FluentAssertions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;
using TuroClawProwl.Infrastructure.Git;
using TuroClawProwl.Infrastructure.Tests.TestSupport;

namespace TuroClawProwl.Infrastructure.Tests.Git;

[Trait("Category", "Integration")]
public class GitWorktreeBundlePublisherTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 6, 30, 0, TimeSpan.Zero);

    private static BundlePayload PayloadOn(string sourceBranch) => new(
        Files:
        [
            new BundleFile("registry.md", "# Registry"),
            new BundleFile("cca/HomeBase.STATE.md", "# HomeBase state"),
            new BundleFile("weekly/2026-W37.md", "# Week 37"),
        ],
        Manifest: new BundleManifest(
            SchemaVersion: BundleManifest.CurrentSchemaVersion,
            PublishedAt: Now,
            Publisher: "TuroClawProwl/0.3.0",
            SourceBranch: sourceBranch,
            Sources:
            [
                new BundleSource("registry", "hub/registry.md", "registry.md", Now, Now, null, true),
            ],
            Failures: []),
        PublishEvenIfUnchanged: true);

    [Fact]
    public async Task Publishing_from_a_feature_branch_with_no_upstream_succeeds()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");

        var result = await publisher.PublishAsync(PayloadOn("feature/humanize-design"));

        result.Should().BeOfType<BundlePublishResult.Success>()
            .Which.CommitSha.Should().NotBeEmpty();
    }

    [Fact]
    public async Task The_bundle_lands_on_main_in_the_remote()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");

        await publisher.PublishAsync(PayloadOn("feature/humanize-design"));

        var listing = GitCli.Read(repo.RemotePath!, "ls-tree", "-r", "--name-only", "main");
        listing.Should().Contain("hub/bundle/manifest.json");
        listing.Should().Contain("hub/bundle/registry.md");
        listing.Should().Contain("hub/bundle/cca/HomeBase.STATE.md");
        listing.Should().Contain("hub/bundle/weekly/2026-W37.md");
    }

    [Fact]
    public async Task The_published_manifest_is_the_payload_manifest()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");

        await publisher.PublishAsync(PayloadOn("feature/humanize-design"));

        var json = GitCli.Read(repo.RemotePath!, "show", "main:hub/bundle/manifest.json");
        var manifest = JsonSerializer.Deserialize<BundleManifest>(json)!;
        manifest.SchemaVersion.Should().Be(1);
        manifest.SourceBranch.Should().Be("feature/humanize-design");
        manifest.Publisher.Should().Be("TuroClawProwl/0.3.0");
        manifest.PublishedAt.Should().Be(Now);
    }

    [Fact]
    public async Task The_published_manifest_names_a_bundle_path_that_exists_in_the_remote()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");

        await publisher.PublishAsync(PayloadOn("feature/humanize-design"));

        var json = GitCli.Read(repo.RemotePath!, "show", "main:hub/bundle/manifest.json");
        var manifest = JsonSerializer.Deserialize<BundleManifest>(json)!;
        var bundlePath = manifest.Sources.Single(s => s.Id == "registry").BundlePath;
        bundlePath.Should().Be("registry.md");
        GitCli.Read(repo.RemotePath!, "show", $"main:hub/bundle/{bundlePath}").Should().Be("# Registry");
    }

    [Fact]
    public async Task The_users_branch_keeps_its_own_history_and_stays_checked_out()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        repo.Commit("user work", "notes.md", "# mine");
        var headBefore = GitCli.Read(repo.Path, "rev-parse", "HEAD");
        var countBefore = GitCli.Read(repo.Path, "rev-list", "--count", "HEAD");
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");

        await publisher.PublishAsync(PayloadOn("feature/humanize-design"));

        GitCli.Read(repo.Path, "rev-parse", "--abbrev-ref", "HEAD")
            .Should().Be("feature/humanize-design");
        GitCli.Read(repo.Path, "rev-parse", "HEAD").Should().Be(headBefore);
        GitCli.Read(repo.Path, "rev-list", "--count", "HEAD").Should().Be(countBefore);
    }

    [Fact]
    public async Task The_users_working_tree_is_left_untouched()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        repo.WriteFile("scratch.md", "# uncommitted scratch");
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");

        await publisher.PublishAsync(PayloadOn("feature/humanize-design"));

        Directory.Exists(Path.Combine(repo.Path, "hub")).Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(repo.Path, "scratch.md")))
            .Should().Be("# uncommitted scratch");
        GitCli.Read(repo.Path, "status", "--porcelain").Should().Be("?? scratch.md");
    }

    [Fact]
    public async Task Publishing_works_even_when_main_is_checked_out_in_the_main_tree()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");

        var result = await publisher.PublishAsync(PayloadOn("main"));

        result.Should().BeOfType<BundlePublishResult.Success>();
        GitCli.Read(repo.RemotePath!, "ls-tree", "-r", "--name-only", "main")
            .Should().Contain("hub/bundle/manifest.json");
    }

    [Fact]
    public async Task The_source_branch_is_read_from_the_source_repository()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");

        var branch = await publisher.GetSourceBranchAsync();

        branch.Should().Be("feature/humanize-design");
    }

    [Fact]
    public async Task A_source_no_longer_in_the_payload_is_removed_from_the_bundle()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");
        await publisher.PublishAsync(PayloadOn("feature/humanize-design"));

        var shrunk = PayloadOn("feature/humanize-design") with
        {
            Files = [new BundleFile("registry.md", "# Registry")],
        };
        await publisher.PublishAsync(shrunk);

        var listing = GitCli.Read(repo.RemotePath!, "ls-tree", "-r", "--name-only", "main");
        listing.Should().Contain("hub/bundle/registry.md");
        listing.Should().NotContain("hub/bundle/cca/HomeBase.STATE.md");
    }

    [Fact]
    public async Task Null_payload_is_rejected()
    {
        using var repo = new TempGitRepo();
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");

        Func<Task> act = () => publisher.PublishAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
