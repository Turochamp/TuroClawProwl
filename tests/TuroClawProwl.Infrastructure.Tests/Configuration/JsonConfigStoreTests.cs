using FluentAssertions;
using TuroClawProwl.Application;
using TuroClawProwl.Infrastructure.Configuration;
using TuroClawProwl.Infrastructure.Tests.TestSupport;

namespace TuroClawProwl.Infrastructure.Tests.Configuration;

public class JsonConfigStoreTests
{
    [Fact]
    public async Task Loading_when_file_does_not_exist_returns_defaults()
    {
        using var tmp = new TempDirectory();
        var store = new JsonConfigStore(Path.Combine(tmp.Path, "config.json"));

        var config = await store.LoadAsync();

        config.Should().Be(new TuroClawProwlConfig());
    }

    [Fact]
    public async Task Saved_config_round_trips_without_loss()
    {
        using var tmp = new TempDirectory();
        var store = new JsonConfigStore(Path.Combine(tmp.Path, "config.json"));
        var original = new TuroClawProwlConfig
        {
            GatewayUrl = "http://fox.local:8080/",
            SshHost = "fox.local",
            SshUser = "turo",
            ReposRoot = @"C:\Git\Other",
            PollInterval = TimeSpan.FromSeconds(30),
            AutostartEnabled = false,
        };

        await store.SaveAsync(original);
        var loaded = await store.LoadAsync();

        loaded.Should().Be(original);
    }

    [Fact]
    public async Task Corrupt_file_is_quarantined_with_timestamp_and_defaults_are_returned()
    {
        using var tmp = new TempDirectory();
        var configPath = Path.Combine(tmp.Path, "config.json");
        await File.WriteAllTextAsync(configPath, "{ this is not valid json");
        var store = new JsonConfigStore(configPath);

        var config = await store.LoadAsync();

        config.Should().Be(new TuroClawProwlConfig());
        File.Exists(configPath).Should().BeFalse("the corrupt original must be moved out of the way");
        Directory.EnumerateFiles(tmp.Path, "config.json.corrupt.*")
            .Should().ContainSingle("the corrupt file must be quarantined with a timestamped suffix");
    }

    [Fact]
    public async Task Save_creates_missing_parent_directory()
    {
        using var tmp = new TempDirectory();
        var nestedPath = Path.Combine(tmp.Path, "sub", "folder", "config.json");
        var store = new JsonConfigStore(nestedPath);

        await store.SaveAsync(new TuroClawProwlConfig());

        File.Exists(nestedPath).Should().BeTrue();
    }

    [Fact]
    public async Task Save_does_not_leave_a_tmp_file_behind()
    {
        using var tmp = new TempDirectory();
        var store = new JsonConfigStore(Path.Combine(tmp.Path, "config.json"));

        await store.SaveAsync(new TuroClawProwlConfig());

        Directory.EnumerateFiles(tmp.Path, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_json_file_recovers_by_returning_defaults()
    {
        using var tmp = new TempDirectory();
        var configPath = Path.Combine(tmp.Path, "config.json");
        await File.WriteAllTextAsync(configPath, "");
        var store = new JsonConfigStore(configPath);

        var config = await store.LoadAsync();

        config.Should().Be(new TuroClawProwlConfig());
    }

    [Fact]
    public void Constructor_rejects_empty_path()
    {
        Action act = () => new JsonConfigStore("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Save_rejects_null_config()
    {
        using var tmp = new TempDirectory();
        var store = new JsonConfigStore(Path.Combine(tmp.Path, "config.json"));

        Func<Task> act = () => store.SaveAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
