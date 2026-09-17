using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Infrastructure.FileSystem;

public sealed class JsonFileSnapshotStore : ISnapshotStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _directory;

    public JsonFileSnapshotStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TuroClawProwl",
            "snapshots");

    public async Task<SnapshotReadResult> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var path = PathFor(id);
        if (!File.Exists(path)) return new SnapshotReadResult.NotFound();

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var record = JsonSerializer.Deserialize<SnapshotFile>(json, SerializerOptions);
            if (record is null || record.Content is null)
                return new SnapshotReadResult.Unreadable($"snapshot file at {path} did not deserialize as expected");

            return new SnapshotReadResult.Found(
                new StoredSnapshot(id, record.Content, record.ReadAt, record.VerifiedAt));
        }
        catch (JsonException ex)
        {
            return new SnapshotReadResult.Unreadable(ex.Message);
        }
        catch (IOException ex)
        {
            return new SnapshotReadResult.Unreadable(ex.Message);
        }
    }

    public async Task SaveAsync(StoredSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.Id);

        Directory.CreateDirectory(_directory);

        var path = PathFor(snapshot.Id);
        var json = JsonSerializer.Serialize(
            new SnapshotFile(snapshot.Id, snapshot.ContentReadAt, snapshot.VerifiedAt, snapshot.Content),
            SerializerOptions);

        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, json, cancellationToken).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
    }

    // Ids carry slashes ("tasks/yne"), so the file name is a hash rather than
    // the id itself: no subdirectories, no traversal, no separator collisions.
    private string PathFor(string id)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(id));
        return Path.Combine(_directory, Convert.ToHexString(digest)[..32] + ".json");
    }

    private sealed record SnapshotFile(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("read_at")] DateTimeOffset ReadAt,
        [property: JsonPropertyName("verified_at")] DateTimeOffset VerifiedAt,
        [property: JsonPropertyName("content")] string? Content);
}
