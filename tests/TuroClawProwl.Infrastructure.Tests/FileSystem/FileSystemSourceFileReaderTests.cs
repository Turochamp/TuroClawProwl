using FluentAssertions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.FileSystem;
using TuroClawProwl.Infrastructure.Tests.TestSupport;

namespace TuroClawProwl.Infrastructure.Tests.FileSystem;

public class FileSystemSourceFileReaderTests
{
    [Fact]
    public async Task An_existing_file_is_read_with_its_last_write_time()
    {
        using var tmp = new TempDirectory();
        var path = Path.Combine(tmp.Path, "STATE.md");
        await File.WriteAllTextAsync(path, "# state");
        var stamp = new DateTime(2026, 9, 11, 20, 15, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);

        var result = await new FileSystemSourceFileReader().ReadAsync(path);

        var found = result.Should().BeOfType<SourceReadResult.Found>().Subject;
        found.Content.Should().Be("# state");
        found.LastModified.Should().Be(new DateTimeOffset(stamp, TimeSpan.Zero));
    }

    [Fact]
    public async Task An_edit_that_has_not_been_committed_is_read_as_its_current_content()
    {
        using var repo = new TempGitRepo();
        repo.Commit("add state", "STATE.md", "# committed content");
        repo.WriteFile("STATE.md", "# uncommitted content");

        var result = await new FileSystemSourceFileReader()
            .ReadAsync(Path.Combine(repo.Path, "STATE.md"));

        result.Should().BeOfType<SourceReadResult.Found>()
            .Which.Content.Should().Be("# uncommitted content");
    }

    [Fact]
    public async Task A_file_that_does_not_exist_reads_as_missing()
    {
        using var tmp = new TempDirectory();

        var result = await new FileSystemSourceFileReader()
            .ReadAsync(Path.Combine(tmp.Path, "nope.md"));

        result.Should().BeOfType<SourceReadResult.Missing>();
    }

    [Fact]
    public async Task A_file_locked_for_exclusive_access_reads_as_unreadable_rather_than_throwing()
    {
        using var tmp = new TempDirectory();
        var path = Path.Combine(tmp.Path, "locked.md");
        await File.WriteAllTextAsync(path, "# state");

        using var hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = await new FileSystemSourceFileReader().ReadAsync(path);

        result.Should().BeOfType<SourceReadResult.Unreadable>()
            .Which.Error.Should().NotBeEmpty();
    }

    [Fact]
    public async Task An_empty_path_is_rejected()
    {
        var reader = new FileSystemSourceFileReader();

        Func<Task> act = () => reader.ReadAsync("  ");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
