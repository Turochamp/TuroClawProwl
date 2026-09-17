using FluentAssertions;
using Moq;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.Tests.TestSupport;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Tests.UseCases;

public class PublishStateBundleSnapshotTests
{
    private const string HubRoot = @"C:\Git\ClaudeCodeAssistants";

    private static readonly DateTimeOffset Now = new(2026, 9, 12, 6, 30, 0, TimeSpan.Zero);

    private const string Registry = """
        ## Active set

        | CCA | Branch | Cadence | Status | State file |
        |-----|--------|---------|--------|-----------|
        | YNE | professional | weekly | active | CCA-YNE/STATE.md |

        ## Calendar & task-list config

        ### Calendars

        | Calendar | Branch | ID | Use |
        |----------|--------|----|----|
        | Primary | professional | `michael.ahs@gmail.com` | Work + general |
        | Holidays in Norway | both | `en.norwegian#holiday@group.v.calendar.google.com` | Context only — not actions |

        ### Task lists

        | List | Branch | ID |
        |------|--------|----|
        | 🗓️ Reclaim | professional | `LU0ySV9fOXlYNjJmZm1wWA` |
        """;

    private readonly Mock<ISourceFileReader> _files = new(MockBehavior.Strict);
    private readonly Mock<IGitFileFactsReader> _gitFacts = new(MockBehavior.Strict);
    private readonly Mock<IGoogleWorkspaceReader> _google = new(MockBehavior.Strict);
    private readonly Mock<ISnapshotStore> _snapshots = new(MockBehavior.Strict);
    private readonly Mock<IBundlePublisher> _publisher = new(MockBehavior.Strict);
    private readonly FixedClock _clock = new(Now);

    private readonly List<StoredSnapshot> _saved = [];

    private BundlePayload? _captured;

    public PublishStateBundleSnapshotTests()
    {
        _publisher.Setup(p => p.GetSourceBranchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("feature/humanize-design");
        _publisher.Setup(p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()))
            .Callback<BundlePayload, CancellationToken>((p, _) => _captured = p)
            .ReturnsAsync(new BundlePublishResult.Success("abc1234", 8));

        _gitFacts.Setup(g => g.GetFileFactsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitFileFacts.Untracked());

        _snapshots.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SnapshotReadResult.NotFound());
        _snapshots.Setup(s => s.SaveAsync(It.IsAny<StoredSnapshot>(), It.IsAny<CancellationToken>()))
            .Callback<StoredSnapshot, CancellationToken>((s, _) => _saved.Add(s))
            .Returns(Task.CompletedTask);

        SetUpFile(BundleLayout.RegistryRelativePath, Registry);
        SetUpFile(BundleLayout.CrmIndexRelativePath, "# Contacts");
        SetUpFile(BundleLayout.WeeklyRelativePath(Now.ToLocalTime()), "# Week");
        SetUpFile("CCA-YNE/STATE.md", "# YNE state");

        _google.Setup(g => g.ReadTaskListAsync(GoogleSnapshotPlan.YneListId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Success("{\"items\":[{\"title\":\"Ring Eirik\"}]}"));
        _google.Setup(g => g.ReadTaskListAsync(GoogleSnapshotPlan.MyTasksListId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Success("{\"items\":[{\"title\":\"Bokhandel\"}]}"));
        _google.Setup(g => g.ReadCalendarWindowAsync(
                It.IsAny<IReadOnlyList<RegistryCalendar>>(), It.IsAny<CalendarWindow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Success("{\"calendars\":[]}"));
    }

    private static string Abs(string relative) =>
        Path.Combine(HubRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private void SetUpFile(string relative, string content) =>
        _files.Setup(f => f.ReadAsync(Abs(relative), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SourceReadResult.Found(content, Now.AddMinutes(-5)));

    private PublishStateBundleUseCase CreateUseCase() =>
        new(_files.Object, _gitFacts.Object, _google.Object, _snapshots.Object,
            _publisher.Object, _clock);

    private static StateBundleRequest Request() =>
        new(HubRoot, "TuroClawProwl/0.3.0", TimeSpan.FromHours(6));

    [Fact]
    public async Task Both_task_lists_and_the_calendar_window_are_written_into_the_bundle()
    {
        await CreateUseCase().ExecuteAsync(Request());

        _captured!.Files.Select(f => f.RelativePath).Should().Contain(new[]
        {
            "tasks/yne.json",
            "tasks/my-tasks.json",
            "calendar/window.json",
        });
    }

    [Fact]
    public async Task The_snapshot_content_is_what_the_reader_returned()
    {
        await CreateUseCase().ExecuteAsync(Request());

        _captured!.Files.Single(f => f.RelativePath == "tasks/yne.json").Content
            .Should().Be("{\"items\":[{\"title\":\"Ring Eirik\"}]}");
        _captured!.Files.Single(f => f.RelativePath == "calendar/window.json").Content
            .Should().Be("{\"calendars\":[]}");
    }

    [Fact]
    public async Task A_snapshot_source_carries_an_api_identity_as_its_path_not_a_repo_path()
    {
        await CreateUseCase().ExecuteAsync(Request());

        _captured!.Manifest.Sources.Single(s => s.Id == "tasks/yne").Path
            .Should().Be("googleapis://tasks/QTA2NmhzdHRMbFBla2ZaRQ");
        _captured!.Manifest.Sources.Single(s => s.Id == "tasks/my-tasks").Path
            .Should().Be("googleapis://tasks/MTIzNjE2OTI4NjM5NTQ3NDc3NzM6MDow");
        _captured!.Manifest.Sources.Single(s => s.Id == "calendar/window").Path
            .Should().Be("googleapis://calendar/window");
    }

    [Fact]
    public async Task A_snapshot_source_is_never_committed_and_never_dirty()
    {
        await CreateUseCase().ExecuteAsync(Request());

        var snapshots = _captured!.Manifest.Sources
            .Where(s => s.Path.StartsWith("googleapis://", StringComparison.Ordinal))
            .ToArray();

        snapshots.Should().HaveCount(3);
        snapshots.Should().AllSatisfy(s =>
        {
            s.Committed.Should().BeNull();
            s.Dirty.Should().BeFalse();
        });
    }

    [Fact]
    public async Task A_first_read_sets_both_modified_and_verified_to_the_read_time()
    {
        await CreateUseCase().ExecuteAsync(Request());

        var yne = _captured!.Manifest.Sources.Single(s => s.Id == "tasks/yne");
        yne.Modified.Should().Be(Now);
        yne.Verified.Should().Be(Now);

        var window = _captured!.Manifest.Sources.Single(s => s.Id == "calendar/window");
        window.Modified.Should().Be(Now);
        window.Verified.Should().Be(Now);
    }

    [Fact]
    public async Task A_successful_read_is_stored_with_both_timestamps()
    {
        await CreateUseCase().ExecuteAsync(Request());

        _saved.Select(s => s.Id).Should().BeEquivalentTo(new[]
        {
            "tasks/yne", "tasks/my-tasks", "calendar/window",
        });
        _saved.Should().AllSatisfy(s =>
        {
            s.ContentReadAt.Should().Be(Now);
            s.VerifiedAt.Should().Be(Now);
        });
    }

    [Fact]
    public async Task The_reclaim_sink_list_is_never_read()
    {
        await CreateUseCase().ExecuteAsync(Request());

        _google.Verify(
            g => g.ReadTaskListAsync(GoogleSnapshotPlan.ReclaimListId, It.IsAny<CancellationToken>()),
            Times.Never);
        _captured!.Files.Should().NotContain(f => f.RelativePath.Contains("reclaim", StringComparison.OrdinalIgnoreCase));
        _captured!.Manifest.Sources.Should().NotContain(s =>
            s.Path.Contains(GoogleSnapshotPlan.ReclaimListId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_yne_test_list_is_never_read()
    {
        await CreateUseCase().ExecuteAsync(Request());

        _google.Verify(
            g => g.ReadTaskListAsync(GoogleSnapshotPlan.YneTestListId, It.IsAny<CancellationToken>()),
            Times.Never);
        _captured!.Manifest.Sources.Should().NotContain(s =>
            s.Path.Contains(GoogleSnapshotPlan.YneTestListId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_the_registrys_planning_calendars_are_passed_to_the_reader()
    {
        IReadOnlyList<RegistryCalendar>? passed = null;
        _google.Setup(g => g.ReadCalendarWindowAsync(
                It.IsAny<IReadOnlyList<RegistryCalendar>>(), It.IsAny<CalendarWindow>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<RegistryCalendar>, CalendarWindow, CancellationToken>(
                (c, _, _) => passed = c)
            .ReturnsAsync(new GoogleReadResult.Success("{\"calendars\":[]}"));

        await CreateUseCase().ExecuteAsync(Request());

        passed.Should().ContainSingle().Which.CalendarId.Should().Be("michael.ahs@gmail.com");
        passed.Should().NotContain(c => c.Name == "Holidays in Norway");
    }

    [Fact]
    public async Task The_calendar_window_covers_today_plus_two_days()
    {
        CalendarWindow? passed = null;
        _google.Setup(g => g.ReadCalendarWindowAsync(
                It.IsAny<IReadOnlyList<RegistryCalendar>>(), It.IsAny<CalendarWindow>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<RegistryCalendar>, CalendarWindow, CancellationToken>(
                (_, w, _) => passed = w)
            .ReturnsAsync(new GoogleReadResult.Success("{\"calendars\":[]}"));

        await CreateUseCase().ExecuteAsync(Request());

        var expected = GoogleSnapshotPlan.WindowFor(Now.ToLocalTime());
        passed!.TimeMin.Should().Be(expected.TimeMin);
        passed!.TimeMax.Should().Be(expected.TimeMax);
    }

    [Fact]
    public async Task A_snapshot_bundle_path_matches_a_file_actually_in_the_payload()
    {
        await CreateUseCase().ExecuteAsync(Request());

        var payloadPaths = _captured!.Files.Select(f => f.RelativePath).ToArray();
        _captured!.Manifest.Sources.Select(s => s.BundlePath).Should().BeEquivalentTo(payloadPaths);
    }

    private void StoreExistingYneSnapshot(
        string content, DateTimeOffset contentReadAt, DateTimeOffset verifiedAt) =>
        _snapshots.Setup(s => s.GetAsync("tasks/yne", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SnapshotReadResult.Found(
                new StoredSnapshot("tasks/yne", content, contentReadAt, verifiedAt)));

    private void MakeYneSnapshotStoreUnreadable(string reason) =>
        _snapshots.Setup(s => s.GetAsync("tasks/yne", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SnapshotReadResult.Unreadable(reason));

    private void FailYneRead() =>
        _google.Setup(g => g.ReadTaskListAsync(GoogleSnapshotPlan.YneListId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.NotAuthenticated("exit code 2"));

    [Fact]
    public async Task A_failed_read_retains_the_previous_snapshot_content()
    {
        StoreExistingYneSnapshot(
            "{\"items\":[{\"title\":\"Yesterday\"}]}", Now.AddHours(-9), Now.AddHours(-1));
        FailYneRead();

        await CreateUseCase().ExecuteAsync(Request());

        _captured!.Files.Single(f => f.RelativePath == "tasks/yne.json").Content
            .Should().Be("{\"items\":[{\"title\":\"Yesterday\"}]}");
    }

    [Fact]
    public async Task A_failed_read_advances_neither_modified_nor_verified()
    {
        var contentReadAt = Now.AddHours(-9);
        var verifiedAt = Now.AddHours(-1);
        StoreExistingYneSnapshot("{\"items\":[]}", contentReadAt, verifiedAt);
        FailYneRead();

        await CreateUseCase().ExecuteAsync(Request());

        var yne = _captured!.Manifest.Sources.Single(s => s.Id == "tasks/yne");
        yne.Modified.Should().Be(contentReadAt, "restamping would make stale data look fresh");
        yne.Verified.Should().Be(
            verifiedAt, "a failed read must not be able to look like a successful one");
        yne.Modified.Should().NotBe(Now);
        yne.Verified.Should().NotBe(Now);
    }

    [Fact]
    public async Task A_failed_read_records_the_failure_and_keeps_the_retained_source_listed()
    {
        StoreExistingYneSnapshot("{\"items\":[]}", Now.AddHours(-9), Now.AddHours(-1));
        FailYneRead();

        await CreateUseCase().ExecuteAsync(Request());

        _captured!.Manifest.Failures.Should().Contain(f => f.Id == "tasks/yne");
        _captured!.Manifest.Sources.Should().Contain(s => s.Id == "tasks/yne");
        _captured!.Manifest.Failures.Single(f => f.Id == "tasks/yne").Reason
            .Should().Contain("not authenticated");
    }

    [Fact]
    public async Task A_failed_read_does_not_overwrite_the_stored_snapshot()
    {
        StoreExistingYneSnapshot("{\"items\":[]}", Now.AddHours(-9), Now.AddHours(-1));
        FailYneRead();

        await CreateUseCase().ExecuteAsync(Request());

        _saved.Should().NotContain(s => s.Id == "tasks/yne");
    }

    [Fact]
    public async Task A_failed_read_with_no_previous_snapshot_records_only_a_failure()
    {
        _google.Setup(g => g.ReadTaskListAsync(GoogleSnapshotPlan.YneListId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Failure("connection reset"));

        await CreateUseCase().ExecuteAsync(Request());

        _captured!.Manifest.Failures.Should().Contain(f => f.Id == "tasks/yne" && f.Reason == "connection reset");
        _captured!.Manifest.Sources.Should().NotContain(s => s.Id == "tasks/yne");
        _captured!.Files.Should().NotContain(f => f.RelativePath == "tasks/yne.json");
    }

    [Fact]
    public async Task One_failed_snapshot_does_not_stop_the_others_from_publishing()
    {
        _google.Setup(g => g.ReadTaskListAsync(GoogleSnapshotPlan.YneListId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Failure("connection reset"));

        var result = await CreateUseCase().ExecuteAsync(Request());

        result.Should().BeOfType<BundlePublishResult.Success>();
        _captured!.Files.Select(f => f.RelativePath).Should().Contain(new[]
        {
            "tasks/my-tasks.json", "calendar/window.json", "registry.md",
        });
    }

    [Fact]
    public async Task A_byte_identical_refresh_keeps_modified_and_advances_verified()
    {
        var contentReadAt = Now.AddHours(-3);
        StoreExistingYneSnapshot(
            "{\"items\":[{\"title\":\"Ring Eirik\"}]}", contentReadAt, Now.AddHours(-1));

        await CreateUseCase().ExecuteAsync(Request());

        var yne = _captured!.Manifest.Sources.Single(s => s.Id == "tasks/yne");
        yne.Modified.Should().Be(contentReadAt, "the data is the same age as before");
        yne.Verified.Should().Be(Now, "but it has just been confirmed current");
    }

    [Fact]
    public async Task A_byte_identical_refresh_stores_the_advanced_verification_time()
    {
        var contentReadAt = Now.AddHours(-3);
        StoreExistingYneSnapshot(
            "{\"items\":[{\"title\":\"Ring Eirik\"}]}", contentReadAt, Now.AddHours(-1));

        await CreateUseCase().ExecuteAsync(Request());

        var stored = _saved.Single(s => s.Id == "tasks/yne");
        stored.ContentReadAt.Should().Be(contentReadAt);
        stored.VerifiedAt.Should().Be(Now);
    }

    [Fact]
    public async Task An_unreadable_snapshot_store_does_not_let_a_re_read_claim_fresh_content()
    {
        // Content identical to what was actually last stored -- but the store
        // cannot be read, so there is nothing to compare against. The bug this
        // guards: previously, an unreadable snapshot was treated exactly like
        // "no previous snapshot", which reset Modified to `now` and claimed a
        // byte-identical read as freshly changed content.
        MakeYneSnapshotStoreUnreadable("the snapshot file is corrupt");

        await CreateUseCase().ExecuteAsync(Request());

        _captured!.Manifest.Sources.Should().NotContain(s => s.Id == "tasks/yne");
        _captured!.Files.Should().NotContain(f => f.RelativePath == "tasks/yne.json");
        _captured!.Manifest.Failures.Should().Contain(f =>
            f.Id == "tasks/yne" && f.Reason.Contains("the snapshot file is corrupt", StringComparison.Ordinal));
        _saved.Should().NotContain(s => s.Id == "tasks/yne");
    }

    [Fact]
    public async Task A_changed_refresh_advances_both_timestamps()
    {
        StoreExistingYneSnapshot("{\"items\":[]}", Now.AddHours(-3), Now.AddHours(-1));

        await CreateUseCase().ExecuteAsync(Request());

        var yne = _captured!.Manifest.Sources.Single(s => s.Id == "tasks/yne");
        yne.Modified.Should().Be(Now);
        yne.Verified.Should().Be(Now);

        var stored = _saved.Single(s => s.Id == "tasks/yne");
        stored.ContentReadAt.Should().Be(Now);
        stored.VerifiedAt.Should().Be(Now);
    }

    [Fact]
    public async Task A_missing_gws_binary_is_reported_as_a_misconfiguration_naming_the_setting()
    {
        _google.Setup(g => g.ReadTaskListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Misconfigured(
                "gwsExecutablePath", @"C:/Users/micha/bin/gws.cmd was not found"));
        _google.Setup(g => g.ReadCalendarWindowAsync(
                It.IsAny<IReadOnlyList<RegistryCalendar>>(), It.IsAny<CalendarWindow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Misconfigured(
                "gwsExecutablePath", @"C:/Users/micha/bin/gws.cmd was not found"));

        var result = await CreateUseCase().ExecuteAsync(Request());

        var misconfigured = result.Should().BeOfType<BundlePublishResult.Misconfigured>().Subject;
        misconfigured.SettingName.Should().Be("gwsExecutablePath");
        misconfigured.Detail.Should().Contain("gws.cmd");
    }

    [Fact]
    public async Task A_misconfigured_reader_still_publishes_every_reachable_source()
    {
        _google.Setup(g => g.ReadTaskListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Misconfigured("gwsExecutablePath", "not found"));
        _google.Setup(g => g.ReadCalendarWindowAsync(
                It.IsAny<IReadOnlyList<RegistryCalendar>>(), It.IsAny<CalendarWindow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Misconfigured("gwsExecutablePath", "not found"));

        await CreateUseCase().ExecuteAsync(Request());

        _publisher.Verify(
            p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()), Times.Once);
        _captured!.Files.Select(f => f.RelativePath).Should().Contain(new[]
        {
            "registry.md", "crm-index.md", "cca/YNE.STATE.md",
        });
        _captured!.Manifest.Failures.Select(f => f.Id).Should().Contain(new[]
        {
            "tasks/yne", "tasks/my-tasks", "calendar/window",
        });
    }

    [Fact]
    public async Task Not_authenticated_is_a_failed_read_not_a_misconfiguration()
    {
        _google.Setup(g => g.ReadTaskListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.NotAuthenticated("exit code 2"));

        var result = await CreateUseCase().ExecuteAsync(Request());

        result.Should().BeOfType<BundlePublishResult.Success>();
    }

    [Fact]
    public async Task A_git_publish_failure_outranks_a_snapshot_misconfiguration()
    {
        _google.Setup(g => g.ReadTaskListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Misconfigured("gwsExecutablePath", "not found"));
        _google.Setup(g => g.ReadCalendarWindowAsync(
                It.IsAny<IReadOnlyList<RegistryCalendar>>(), It.IsAny<CalendarWindow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Misconfigured("gwsExecutablePath", "not found"));
        _publisher.Setup(p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()))
            .Callback<BundlePayload, CancellationToken>((p, _) => _captured = p)
            .ReturnsAsync(new BundlePublishResult.Transient("Connection timed out"));

        var result = await CreateUseCase().ExecuteAsync(Request());

        result.Should().BeOfType<BundlePublishResult.Transient>();
    }
}
