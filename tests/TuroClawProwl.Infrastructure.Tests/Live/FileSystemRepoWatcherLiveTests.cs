using FluentAssertions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.FileSystem;
using TuroClawProwl.Infrastructure.Tests.TestSupport;

namespace TuroClawProwl.Infrastructure.Tests.Live;

[Trait("Category", "Live")]
public class FileSystemRepoWatcherLiveTests
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task File_creation_inside_watched_root_raises_Changed()
    {
        using var tmp = new TempDirectory();
        using var watcher = new FileSystemRepoWatcher();
        using var changed = new ManualResetEventSlim();
        var lastReason = string.Empty;
        watcher.Changed += (_, args) => { lastReason = args.Reason; changed.Set(); };

        watcher.Start(tmp.Path);
        await Task.Delay(150);

        File.WriteAllText(Path.Combine(tmp.Path, "trigger.txt"), "x");

        changed.Wait(EventTimeout).Should().BeTrue(
            $"FileSystemWatcher should report the new file within {EventTimeout.TotalSeconds}s");
        lastReason.Should().StartWith("fs:");
    }

    [Fact]
    public async Task File_written_inside_dot_git_objects_does_not_raise_Changed()
    {
        using var tmp = new TempDirectory();
        var gitObjectsAb = Path.Combine(tmp.Path, "repo", ".git", "objects", "ab");
        Directory.CreateDirectory(gitObjectsAb);

        using var watcher = new FileSystemRepoWatcher();
        using var changed = new ManualResetEventSlim();
        watcher.Changed += (_, _) => changed.Set();

        watcher.Start(tmp.Path);
        await Task.Delay(150);

        File.WriteAllText(Path.Combine(gitObjectsAb, "abcdef"), "pack-data-payload");

        var fired = changed.Wait(TimeSpan.FromSeconds(1));
        fired.Should().BeFalse("changes inside .git/objects are not relevant per DD-3 filtering");
    }

    [Fact]
    public async Task Dot_git_HEAD_write_is_considered_relevant()
    {
        using var tmp = new TempDirectory();
        using var watcher = new FileSystemRepoWatcher();
        using var changed = new ManualResetEventSlim();
        watcher.Changed += (_, _) => changed.Set();

        var repo = Path.Combine(tmp.Path, "repo");
        var gitDir = Path.Combine(repo, ".git");
        Directory.CreateDirectory(gitDir);
        watcher.Start(tmp.Path);
        await Task.Delay(150);

        File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main\n");

        changed.Wait(EventTimeout).Should().BeTrue(
            "writing .git/HEAD is a commit/branch-change signal and must be surfaced");
    }
}
