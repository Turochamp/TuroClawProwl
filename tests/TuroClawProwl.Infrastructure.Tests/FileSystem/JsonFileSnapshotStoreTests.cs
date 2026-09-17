using FluentAssertions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.FileSystem;
using TuroClawProwl.Infrastructure.Tests.TestSupport;

namespace TuroClawProwl.Infrastructure.Tests.FileSystem;

public class JsonFileSnapshotStoreTests
{
    private static readonly DateTimeOffset ReadAt = new(2026, 9, 12, 5, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset VerifiedAt = new(2026, 9, 12, 6, 20, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_saved_snapshot_round_trips_with_its_content_and_both_timestamps()
    {
        using var tmp = new TempDirectory();
        var store = new JsonFileSnapshotStore(Path.Combine(tmp.Path, "snapshots"));

        await store.SaveAsync(new StoredSnapshot("tasks/yne", "{\"items\":[]}", ReadAt, VerifiedAt));
        var loaded = await store.GetAsync("tasks/yne");

        var found = loaded.Should().BeOfType<SnapshotReadResult.Found>().Subject;
        found.Snapshot.Id.Should().Be("tasks/yne");
        found.Snapshot.Content.Should().Be("{\"items\":[]}");
        found.Snapshot.ContentReadAt.Should().Be(ReadAt);
        found.Snapshot.VerifiedAt.Should().Be(VerifiedAt);
    }

    [Fact]
    public async Task The_two_timestamps_are_persisted_independently()
    {
        using var tmp = new TempDirectory();
        var directory = Path.Combine(tmp.Path, "snapshots");
        var store = new JsonFileSnapshotStore(directory);

        await store.SaveAsync(new StoredSnapshot("tasks/yne", "{\"items\":[]}", ReadAt, VerifiedAt));

        var json = await File.ReadAllTextAsync(Directory.GetFiles(directory).Single());
        json.Should().Contain("\"read_at\"");
        json.Should().Contain("\"verified_at\"");
        var loaded = ((SnapshotReadResult.Found)await store.GetAsync("tasks/yne")).Snapshot;
        loaded.VerifiedAt.Should().BeAfter(loaded.ContentReadAt);
    }

    [Fact]
    public async Task An_id_containing_a_slash_does_not_escape_the_store_directory()
    {
        using var tmp = new TempDirectory();
        var directory = Path.Combine(tmp.Path, "snapshots");
        var store = new JsonFileSnapshotStore(directory);

        await store.SaveAsync(new StoredSnapshot("calendar/window", "{}", ReadAt, VerifiedAt));

        Directory.GetFiles(directory).Should().ContainSingle();
        Directory.GetDirectories(directory).Should().BeEmpty();
        ((SnapshotReadResult.Found)await store.GetAsync("calendar/window")).Snapshot.Content.Should().Be("{}");
    }

    [Fact]
    public async Task Two_ids_that_differ_only_by_separator_do_not_collide()
    {
        using var tmp = new TempDirectory();
        var store = new JsonFileSnapshotStore(Path.Combine(tmp.Path, "snapshots"));

        await store.SaveAsync(new StoredSnapshot("tasks/yne", "{\"a\":1}", ReadAt, VerifiedAt));
        await store.SaveAsync(new StoredSnapshot("tasks/my-tasks", "{\"b\":2}", ReadAt, VerifiedAt));

        ((SnapshotReadResult.Found)await store.GetAsync("tasks/yne")).Snapshot.Content.Should().Be("{\"a\":1}");
        ((SnapshotReadResult.Found)await store.GetAsync("tasks/my-tasks")).Snapshot.Content.Should().Be("{\"b\":2}");
    }

    [Fact]
    public async Task An_unknown_id_yields_not_found_rather_than_throwing()
    {
        using var tmp = new TempDirectory();
        var store = new JsonFileSnapshotStore(Path.Combine(tmp.Path, "snapshots"));

        (await store.GetAsync("tasks/yne")).Should().BeOfType<SnapshotReadResult.NotFound>();
    }

    [Fact]
    public async Task Saving_twice_replaces_the_snapshot_and_its_read_time()
    {
        using var tmp = new TempDirectory();
        var store = new JsonFileSnapshotStore(Path.Combine(tmp.Path, "snapshots"));
        await store.SaveAsync(new StoredSnapshot("tasks/yne", "{\"a\":1}", ReadAt, VerifiedAt));

        await store.SaveAsync(new StoredSnapshot("tasks/yne", "{\"a\":2}", ReadAt.AddHours(1), VerifiedAt.AddHours(1)));
        var loaded = ((SnapshotReadResult.Found)await store.GetAsync("tasks/yne")).Snapshot;

        loaded.Content.Should().Be("{\"a\":2}");
        loaded.ContentReadAt.Should().Be(ReadAt.AddHours(1));
        loaded.VerifiedAt.Should().Be(VerifiedAt.AddHours(1));
    }

    [Fact]
    public async Task A_corrupt_snapshot_file_reads_as_unreadable_rather_than_throwing_or_reading_as_absent()
    {
        using var tmp = new TempDirectory();
        var directory = Path.Combine(tmp.Path, "snapshots");
        var store = new JsonFileSnapshotStore(directory);
        await store.SaveAsync(new StoredSnapshot("tasks/yne", "{}", ReadAt, VerifiedAt));
        var file = Directory.GetFiles(directory).Single();
        await File.WriteAllTextAsync(file, "{ this is not json");

        // Unreadable, not NotFound: a snapshot DOES exist here, it just could not
        // be read back. Collapsing the two would let a caller treat a corrupt
        // file the same as "nothing has ever been stored" (see
        // PublishStateBundleUseCase.CollectSnapshotAsync).
        (await store.GetAsync("tasks/yne")).Should().BeOfType<SnapshotReadResult.Unreadable>();
    }

    [Fact]
    public async Task A_snapshot_survives_a_new_store_over_the_same_directory()
    {
        using var tmp = new TempDirectory();
        var directory = Path.Combine(tmp.Path, "snapshots");
        await new JsonFileSnapshotStore(directory)
            .SaveAsync(new StoredSnapshot("calendar/window", "{\"calendars\":[]}", ReadAt, VerifiedAt));

        var loaded = ((SnapshotReadResult.Found)await new JsonFileSnapshotStore(directory)
            .GetAsync("calendar/window")).Snapshot;

        loaded.VerifiedAt.Should().Be(VerifiedAt);
    }

    [Fact]
    public void The_default_directory_sits_under_the_app_data_folder()
    {
        JsonFileSnapshotStore.DefaultDirectory()
            .Should().EndWith(Path.Combine("TuroClawProwl", "snapshots"));
    }

    [Fact]
    public async Task An_empty_id_is_rejected()
    {
        using var tmp = new TempDirectory();
        var store = new JsonFileSnapshotStore(Path.Combine(tmp.Path, "snapshots"));

        Func<Task> act = () => store.GetAsync("  ");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
