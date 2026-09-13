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

    [Fact]
    public void The_publish_branch_defaults_to_main_and_the_paths_default_to_empty()
    {
        var config = new TuroClawProwlConfig();

        config.BundlePublishBranch.Should().Be("main");
        config.HubRepoPath.Should().BeEmpty();
        config.BundleWorktreePath.Should().BeEmpty();
    }

    [Fact]
    public void The_intervals_default_to_one_hour_and_six_and_gws_to_its_known_path()
    {
        var config = new TuroClawProwlConfig();

        config.SnapshotInterval.Should().Be(TimeSpan.FromHours(1));
        config.HeartbeatInterval.Should().Be(TimeSpan.FromHours(6));
        config.GwsExecutablePath.Should().Be("C:/Users/micha/bin/gws.cmd");
    }

    [Fact]
    public void The_git_executable_defaults_to_the_bare_command()
    {
        var config = new TuroClawProwlConfig();

        config.GitExecutablePath.Should().Be("git");
    }

    [Fact]
    public void An_interval_below_its_floor_is_raised_to_it()
    {
        var config = new TuroClawProwlConfig
        {
            SnapshotInterval = TimeSpan.FromSeconds(30),
            HeartbeatInterval = TimeSpan.FromSeconds(30),
        };

        ConfigIntervals.EffectiveSnapshotInterval(config)
            .Should().Be(ConfigIntervals.MinimumSnapshotInterval);
        ConfigIntervals.EffectiveHeartbeatInterval(config)
            .Should().Be(ConfigIntervals.MinimumHeartbeatInterval);
    }

    [Fact]
    public void A_heartbeat_shorter_than_the_snapshot_interval_is_raised_to_it()
    {
        var config = new TuroClawProwlConfig
        {
            SnapshotInterval = TimeSpan.FromHours(4),
            HeartbeatInterval = TimeSpan.FromHours(1),
        };

        ConfigIntervals.EffectiveHeartbeatInterval(config)
            .Should().Be(TimeSpan.FromHours(4),
                "a heartbeat that outruns the refresh would republish the same timestamps");
    }

    [Fact]
    public void The_default_pair_needs_no_adjustment()
    {
        var config = new TuroClawProwlConfig();

        ConfigIntervals.EffectiveSnapshotInterval(config).Should().Be(TimeSpan.FromHours(1));
        ConfigIntervals.EffectiveHeartbeatInterval(config).Should().Be(TimeSpan.FromHours(6));
    }

    [Fact]
    public async Task The_bundle_publisher_fields_round_trip()
    {
        using var tmp = new TempDirectory();
        var store = new JsonConfigStore(Path.Combine(tmp.Path, "config.json"));
        var original = new TuroClawProwlConfig
        {
            GatewayUrl = "http://192.168.1.121:18789/",
            HubRepoPath = @"C:\Git\ClaudeCodeAssistants",
            BundleWorktreePath = @"C:\Users\micha\AppData\Local\TuroClawProwl\bundle-worktree",
            BundlePublishBranch = "main",
            SnapshotInterval = TimeSpan.FromMinutes(30),
            HeartbeatInterval = TimeSpan.FromHours(6),
            GwsExecutablePath = "C:/Users/micha/bin/gws.cmd",
            GitExecutablePath = @"C:\Program Files\Git\bin\git.exe",
        };

        await store.SaveAsync(original);
        var loaded = await store.LoadAsync();

        loaded.HubRepoPath.Should().Be(@"C:\Git\ClaudeCodeAssistants");
        loaded.BundleWorktreePath.Should().Be(@"C:\Users\micha\AppData\Local\TuroClawProwl\bundle-worktree");
        loaded.BundlePublishBranch.Should().Be("main");
        loaded.SnapshotInterval.Should().Be(TimeSpan.FromMinutes(30));
        loaded.HeartbeatInterval.Should().Be(TimeSpan.FromHours(6));
        loaded.GwsExecutablePath.Should().Be("C:/Users/micha/bin/gws.cmd");
        loaded.GitExecutablePath.Should().Be(@"C:\Program Files\Git\bin\git.exe");
        loaded.Should().Be(original);
    }

    [Fact]
    public async Task The_bundle_publisher_fields_persist_under_the_setting_names_reports_quote()
    {
        using var tmp = new TempDirectory();
        var configPath = Path.Combine(tmp.Path, "config.json");
        var store = new JsonConfigStore(configPath);

        await store.SaveAsync(new TuroClawProwlConfig
        {
            GatewayUrl = "http://192.168.1.121:18789/",
            HubRepoPath = @"C:\Git\ClaudeCodeAssistants",
            BundleWorktreePath = @"C:\Temp\wt",
            BundlePublishBranch = "main",
        });

        var json = await File.ReadAllTextAsync(configPath);
        json.Should().Contain("\"hubRepoPath\"");
        json.Should().Contain("\"bundleWorktreePath\"");
        json.Should().Contain("\"bundlePublishBranch\"");
        json.Should().Contain("\"snapshotInterval\"");
        json.Should().Contain("\"heartbeatInterval\"");
        json.Should().Contain("\"gwsExecutablePath\"");
        json.Should().Contain("\"gitExecutablePath\"");
    }

    [Fact]
    public async Task An_existing_config_without_the_new_fields_loads_with_their_defaults()
    {
        using var tmp = new TempDirectory();
        var configPath = Path.Combine(tmp.Path, "config.json");
        await File.WriteAllTextAsync(configPath, """
            {
              "gatewayUrl": "http://192.168.1.121:18789/",
              "sshHost": "fox.local",
              "sshUser": "turo",
              "pollInterval": "00:00:15",
              "autostartEnabled": true,
              "todaySkillPath": "C:\\Git\\ClaudeCodeAssistants\\.claude\\skills\\today\\SKILL.md",
              "todayCcaRoot": "C:\\Git\\ClaudeCodeAssistants",
              "todayCrmIndexPath": "C:\\Git\\ClaudeCodeAssistants\\crm\\data\\contacts\\_index.md"
            }
            """);
        var store = new JsonConfigStore(configPath);

        var loaded = await store.LoadAsync();

        loaded.HubRepoPath.Should().BeEmpty();
        loaded.BundleWorktreePath.Should().BeEmpty();
        loaded.BundlePublishBranch.Should().Be("main");
        loaded.SnapshotInterval.Should().Be(TimeSpan.FromHours(1));
        loaded.HeartbeatInterval.Should().Be(TimeSpan.FromHours(6));
        loaded.GwsExecutablePath.Should().Be("C:/Users/micha/bin/gws.cmd");
        loaded.GitExecutablePath.Should().Be("git");
        loaded.TodayCcaRoot.Should().Be(@"C:\Git\ClaudeCodeAssistants");
    }
}
