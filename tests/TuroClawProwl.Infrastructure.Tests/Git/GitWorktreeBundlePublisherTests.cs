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

    [Fact]
    public async Task A_second_publish_reuses_the_existing_worktree_and_adds_one_commit()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        using var home = new TempDirectory();
        var worktree = Path.Combine(home.Path, "bundle-worktree");
        var publisher = new GitWorktreeBundlePublisher(repo.Path, worktree, "main");
        await publisher.PublishAsync(PayloadOn("feature/humanize-design"));
        var afterFirst = int.Parse(GitCli.Read(repo.RemotePath!, "rev-list", "--count", "main"));

        var second = PayloadOn("feature/humanize-design") with
        {
            Files = [new BundleFile("registry.md", "# Registry, revised")],
            Manifest = PayloadOn("feature/humanize-design").Manifest with { PublishedAt = Now.AddMinutes(5) },
        };
        var result = await publisher.PublishAsync(second);

        result.Should().BeOfType<BundlePublishResult.Success>();
        int.Parse(GitCli.Read(repo.RemotePath!, "rev-list", "--count", "main"))
            .Should().Be(afterFirst + 1);
        GitCli.Read(repo.RemotePath!, "show", "main:hub/bundle/registry.md")
            .Should().Be("# Registry, revised");
    }

    [Fact]
    public async Task A_publish_after_the_worktree_was_left_dirty_still_succeeds()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        using var home = new TempDirectory();
        var worktree = Path.Combine(home.Path, "bundle-worktree");
        var publisher = new GitWorktreeBundlePublisher(repo.Path, worktree, "main");
        await publisher.PublishAsync(PayloadOn("feature/humanize-design"));
        await File.WriteAllTextAsync(Path.Combine(worktree, "junk.md"), "# left behind");
        await File.WriteAllTextAsync(
            Path.Combine(worktree, "hub", "bundle", "registry.md"), "# clobbered by hand");

        var result = await publisher.PublishAsync(PayloadOn("feature/humanize-design") with
        {
            Files = [new BundleFile("registry.md", "# Registry")],
            Manifest = PayloadOn("feature/humanize-design").Manifest with { PublishedAt = Now.AddMinutes(9) },
        });

        result.Should().BeOfType<BundlePublishResult.Success>();
        GitCli.Read(repo.RemotePath!, "show", "main:hub/bundle/registry.md").Should().Be("# Registry");
        File.Exists(Path.Combine(worktree, "junk.md")).Should().BeFalse();
    }

    [Fact]
    public async Task A_worktree_path_holding_unrelated_files_is_reported_as_a_misconfiguration()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        using var home = new TempDirectory();
        var occupied = home.CreateSubDirectory("occupied");
        await File.WriteAllTextAsync(Path.Combine(occupied, "someones-notes.md"), "# not ours");
        var publisher = new GitWorktreeBundlePublisher(repo.Path, occupied, "main");

        var result = await publisher.PublishAsync(PayloadOn("main"));

        var misconfigured = result.Should().BeOfType<BundlePublishResult.Misconfigured>().Subject;
        misconfigured.SettingName.Should().Be(GitWorktreeBundlePublisher.WorktreeSetting);
        misconfigured.Detail.Should().Contain(occupied);
        File.Exists(Path.Combine(occupied, "someones-notes.md")).Should().BeTrue();
    }

    [Fact]
    public async Task A_publish_branch_that_does_not_exist_is_reported_as_a_misconfiguration()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "no-such-branch");

        var result = await publisher.PublishAsync(PayloadOn("main"));

        var misconfigured = result.Should().BeOfType<BundlePublishResult.Misconfigured>().Subject;
        misconfigured.SettingName.Should().Be(GitWorktreeBundlePublisher.BranchSetting);
        misconfigured.Detail.Should().Contain("no-such-branch");
    }

    [Fact]
    public async Task A_source_repository_with_no_remote_is_reported_as_a_misconfiguration()
    {
        using var repo = new TempGitRepo();
        repo.Run("checkout", "-b", "feature/humanize-design");
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");

        var result = await publisher.PublishAsync(PayloadOn("feature/humanize-design"));

        result.Should().BeOfType<BundlePublishResult.Misconfigured>()
            .Which.SettingName.Should().Be(GitWorktreeBundlePublisher.SourceRepoSetting);
    }

    [Fact]
    public async Task A_source_path_that_is_not_a_repository_is_reported_as_a_misconfiguration()
    {
        using var notARepo = new TempDirectory();
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            notARepo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");

        var result = await publisher.PublishAsync(PayloadOn("main"));

        result.Should().BeOfType<BundlePublishResult.Misconfigured>()
            .Which.SettingName.Should().Be(GitWorktreeBundlePublisher.SourceRepoSetting);
    }

    [Fact]
    public async Task A_republish_of_byte_identical_content_before_the_heartbeat_produces_no_commit()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");
        await publisher.PublishAsync(PayloadOn("feature/humanize-design"));
        var afterFirst = int.Parse(GitCli.Read(repo.RemotePath!, "rev-list", "--count", "main"));

        // Same files, later published_at and later verified timestamps -- exactly
        // what an hourly snapshot refresh looks like when nothing has changed,
        // and no heartbeat is due.
        var result = await publisher.PublishAsync(PayloadOn("feature/humanize-design") with
        {
            Manifest = PayloadOn("feature/humanize-design").Manifest with
            {
                PublishedAt = Now.AddHours(1),
                Sources =
                [
                    new BundleSource(
                        "registry", "hub/registry.md", "registry.md",
                        Now, Now.AddHours(1), null, true),
                ],
            },
            PublishEvenIfUnchanged = false,
        });

        result.Should().BeOfType<BundlePublishResult.Success>().Which.FileCount.Should().Be(0);
        int.Parse(GitCli.Read(repo.RemotePath!, "rev-list", "--count", "main")).Should().Be(afterFirst);
    }

    [Fact]
    public async Task A_heartbeat_commits_the_manifest_alone_when_no_content_changed()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");
        await publisher.PublishAsync(PayloadOn("feature/humanize-design"));
        var afterFirst = int.Parse(GitCli.Read(repo.RemotePath!, "rev-list", "--count", "main"));

        var heartbeat = PayloadOn("feature/humanize-design") with
        {
            Manifest = PayloadOn("feature/humanize-design").Manifest with
            {
                PublishedAt = Now.AddHours(6),
                Sources =
                [
                    new BundleSource(
                        "registry", "hub/registry.md", "registry.md",
                        Now, Now.AddHours(6), null, true),
                ],
            },
            PublishEvenIfUnchanged = true,
        };
        var result = await publisher.PublishAsync(heartbeat);

        result.Should().BeOfType<BundlePublishResult.Success>().Which.FileCount.Should().BeGreaterThan(0);
        int.Parse(GitCli.Read(repo.RemotePath!, "rev-list", "--count", "main"))
            .Should().Be(afterFirst + 1);

        var changed = GitCli.Read(repo.RemotePath!, "diff-tree", "--no-commit-id", "--name-only", "-r", "main");
        changed.Should().Be("hub/bundle/manifest.json");
    }

    [Fact]
    public async Task A_heartbeat_records_the_advanced_verified_timestamp_without_moving_modified()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");
        await publisher.PublishAsync(PayloadOn("feature/humanize-design"));

        await publisher.PublishAsync(PayloadOn("feature/humanize-design") with
        {
            Manifest = PayloadOn("feature/humanize-design").Manifest with
            {
                PublishedAt = Now.AddHours(6),
                Sources =
                [
                    new BundleSource(
                        "registry", "hub/registry.md", "registry.md",
                        Now, Now.AddHours(6), null, true),
                ],
            },
            PublishEvenIfUnchanged = true,
        });

        var json = GitCli.Read(repo.RemotePath!, "show", "main:hub/bundle/manifest.json");
        var manifest = JsonSerializer.Deserialize<BundleManifest>(json)!;
        var registry = manifest.Sources.Single(s => s.Id == "registry");
        registry.Modified.Should().Be(Now);
        registry.Verified.Should().Be(Now.AddHours(6));
    }

    [Fact]
    public async Task A_republish_that_changes_only_one_snapshot_still_commits()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");
        await publisher.PublishAsync(PayloadOn("feature/humanize-design"));
        var afterFirst = int.Parse(GitCli.Read(repo.RemotePath!, "rev-list", "--count", "main"));

        var changed = PayloadOn("feature/humanize-design") with
        {
            Files =
            [
                new BundleFile("registry.md", "# Registry"),
                new BundleFile("cca/HomeBase.STATE.md", "# HomeBase state"),
                new BundleFile("weekly/2026-W37.md", "# Week 37"),
                new BundleFile("tasks/yne.json", "{\"items\":[{\"title\":\"New task\"}]}"),
            ],
            Manifest = PayloadOn("feature/humanize-design").Manifest with { PublishedAt = Now.AddHours(1) },
        };
        await publisher.PublishAsync(changed);

        int.Parse(GitCli.Read(repo.RemotePath!, "rev-list", "--count", "main"))
            .Should().Be(afterFirst + 1);
        GitCli.Read(repo.RemotePath!, "show", "main:hub/bundle/tasks/yne.json")
            .Should().Contain("New task");
    }

    [Fact]
    public async Task A_remote_that_has_gone_away_never_reports_success()
    {
        using var repo = new TempGitRepo();
        repo.SetUpBareRemoteAndPush();
        repo.Run("checkout", "-b", "feature/humanize-design");
        using var home = new TempDirectory();
        var publisher = new GitWorktreeBundlePublisher(
            repo.Path, Path.Combine(home.Path, "bundle-worktree"), "main");
        await publisher.PublishAsync(PayloadOn("feature/humanize-design"));
        // Plain Directory.Delete throws here on Windows: git marks bare-repo loose
        // objects read-only, and Windows (unlike POSIX) refuses to delete a read-only
        // file regardless of directory permissions. TempDirectory.ForceDelete clears
        // the attribute first; see its comment for the full explanation.
        TempDirectory.ForceDelete(repo.RemotePath!);

        var result = await publisher.PublishAsync(PayloadOn("feature/humanize-design") with
        {
            Files = [new BundleFile("registry.md", "# Later")],
            Manifest = PayloadOn("feature/humanize-design").Manifest with { PublishedAt = Now.AddHours(1) },
        });

        result.Should().NotBeOfType<BundlePublishResult.Success>();
    }
}
