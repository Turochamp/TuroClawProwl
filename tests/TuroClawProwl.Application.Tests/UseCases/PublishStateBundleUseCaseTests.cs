using FluentAssertions;
using Moq;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.Tests.TestSupport;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Tests.UseCases;

public class PublishStateBundleUseCaseTests
{
    private const string HubRoot = @"C:\Git\ClaudeCodeAssistants";

    private static readonly DateTimeOffset Now = new(2026, 9, 12, 6, 30, 0, TimeSpan.Zero);

    private static readonly string[] PublishedSourceIds = ["tasks/yne", "tasks/my-tasks", "calendar/window"];

    private const string Registry = """
        ## Active set

        | CCA | Branch | Cadence | Status | State file |
        |-----|--------|---------|--------|-----------|
        | YNE | professional | weekly | active | CCA-YNE/STATE.md |
        | SmoEms | professional | weekly | flagged | CCA-SmoEms/STATE.md |

        ### Calendars

        | Calendar | Branch | ID | Use |
        |----------|--------|----|----|
        | Primary | professional | `michael.ahs@gmail.com` | Work + general |
        | Yne | professional | `michael@yne.no` | **The Head of AI work calendar.** Added 2026-09-16 · **Pending** — not yet readable (see note below) |
        | Holidays in Norway | both | `en.norwegian#holiday@group.v.calendar.google.com` | Context only — not actions |
        """;

    private readonly Mock<ISourceFileReader> _files = new(MockBehavior.Strict);
    private readonly Mock<IGoogleWorkspaceReader> _google = new(MockBehavior.Strict);
    private readonly Mock<ISnapshotStore> _snapshots = new(MockBehavior.Strict);
    private readonly Mock<IBundlePublisher> _publisher = new(MockBehavior.Strict);
    private readonly FixedClock _clock = new(Now);

    private BundlePayload? _captured;
    private IReadOnlyList<RegistryCalendar>? _calendarsRead;

    public PublishStateBundleUseCaseTests()
    {
        _publisher.Setup(p => p.GetSourceBranchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("feature/humanize-design");
        _publisher.Setup(p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()))
            .Callback<BundlePayload, CancellationToken>((p, _) => _captured = p)
            .ReturnsAsync(new BundlePublishResult.Success("abc1234", 4));

        _snapshots.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SnapshotReadResult.NotFound());
        _snapshots.Setup(s => s.SaveAsync(It.IsAny<StoredSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _google.Setup(g => g.ReadTaskListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Success("{\"items\":[]}"));
        _google.Setup(g => g.ReadCalendarWindowAsync(
                It.IsAny<IReadOnlyList<RegistryCalendar>>(), It.IsAny<CalendarWindow>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<RegistryCalendar>, CalendarWindow, CancellationToken>(
                (c, _, _) => _calendarsRead = c)
            .ReturnsAsync(new GoogleReadResult.Success("{\"calendars\":[]}"));

        SetUpRegistry(new SourceReadResult.Found(Registry, Now.AddMinutes(-10)));
    }

    private PublishStateBundleUseCase CreateUseCase() =>
        new(_files.Object, _google.Object, _snapshots.Object, _publisher.Object, _clock);

    private static StateBundleRequest Request() =>
        new(HubRoot, "TuroClawProwl/0.3.0", TimeSpan.FromHours(6));

    private static string RegistryPath =>
        Path.Combine(HubRoot, BundleLayout.RegistryRelativePath.Replace('/', Path.DirectorySeparatorChar));

    private void SetUpRegistry(SourceReadResult read) =>
        _files.Setup(f => f.ReadAsync(RegistryPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(read);

    private void FailAllGoogleReads()
    {
        _google.Setup(g => g.ReadTaskListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Failure("offline"));
        _google.Setup(g => g.ReadCalendarWindowAsync(
                It.IsAny<IReadOnlyList<RegistryCalendar>>(), It.IsAny<CalendarWindow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Failure("offline"));
    }

    [Fact]
    public async Task The_manifest_sources_are_exactly_the_two_task_lists_and_the_calendar_window()
    {
        await CreateUseCase().ExecuteAsync(Request());

        _captured!.Manifest.Sources.Select(s => s.Id)
            .Should().BeEquivalentTo(PublishedSourceIds, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task No_hub_file_is_published_into_the_bundle()
    {
        await CreateUseCase().ExecuteAsync(Request());

        _captured!.Files.Select(f => f.RelativePath).Should().BeEquivalentTo(new[]
        {
            "tasks/yne.json", "tasks/my-tasks.json", "calendar/window.json",
        });
    }

    [Fact]
    public async Task Failures_never_name_a_file_source_even_when_every_read_fails()
    {
        FailAllGoogleReads();
        SetUpRegistry(new SourceReadResult.Missing());

        await CreateUseCase().ExecuteAsync(Request());

        _captured!.Manifest.Failures.Select(f => f.Id)
            .Should().OnlyContain(id => PublishedSourceIds.Contains(id));
        _captured!.Manifest.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task The_registry_is_read_only_to_choose_the_calendars()
    {
        await CreateUseCase().ExecuteAsync(Request());

        _files.Verify(f => f.ReadAsync(RegistryPath, It.IsAny<CancellationToken>()), Times.Once);
        _files.VerifyNoOtherCalls();
        _calendarsRead.Should().ContainSingle().Which.CalendarId.Should().Be("michael.ahs@gmail.com");
    }

    [Fact]
    public async Task A_missing_registry_fails_the_calendar_window_instead_of_publishing_an_empty_one()
    {
        SetUpRegistry(new SourceReadResult.Missing());

        var result = await CreateUseCase().ExecuteAsync(Request());

        result.Should().BeOfType<BundlePublishResult.Success>();
        _google.Verify(g => g.ReadCalendarWindowAsync(
                It.IsAny<IReadOnlyList<RegistryCalendar>>(), It.IsAny<CalendarWindow>(), It.IsAny<CancellationToken>()),
            Times.Never);
        var failure = _captured!.Manifest.Failures.Should().ContainSingle().Subject;
        failure.Id.Should().Be("calendar/window");
        failure.Reason.Should().Contain("registry").And.Contain("missing");
        _captured!.Manifest.Sources.Select(s => s.Id).Should().BeEquivalentTo("tasks/yne", "tasks/my-tasks");
    }

    [Fact]
    public async Task An_unreadable_registry_retains_the_previous_calendar_window_with_its_timestamps()
    {
        SetUpRegistry(new SourceReadResult.Unreadable("The process cannot access the file"));
        _snapshots.Setup(s => s.GetAsync("calendar/window", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SnapshotReadResult.Found(new StoredSnapshot(
                "calendar/window", "{\"calendars\":[1]}", Now.AddHours(-9), Now.AddHours(-1))));

        await CreateUseCase().ExecuteAsync(Request());

        _captured!.Manifest.Failures.Should().ContainSingle(f => f.Id == "calendar/window")
            .Which.Reason.Should().Contain("The process cannot access the file");
        var window = _captured!.Manifest.Sources.Single(s => s.Id == "calendar/window");
        window.Modified.Should().Be(Now.AddHours(-9));
        window.Verified.Should().Be(Now.AddHours(-1));
        _captured!.Files.Single(f => f.RelativePath == "calendar/window.json").Content
            .Should().Be("{\"calendars\":[1]}");
    }

    [Fact]
    public async Task The_manifest_records_the_schema_version_published_at_publisher_and_source_branch()
    {
        await CreateUseCase().ExecuteAsync(Request());

        _captured!.Manifest.SchemaVersion.Should().Be(BundleManifest.CurrentSchemaVersion);
        _captured!.Manifest.PublishedAt.Should().Be(Now);
        _captured!.Manifest.Publisher.Should().Be("TuroClawProwl/0.3.0");
        _captured!.Manifest.SourceBranch.Should().Be("feature/humanize-design");
    }

    [Fact]
    public async Task Every_manifest_source_bundle_path_matches_a_file_actually_in_the_payload()
    {
        await CreateUseCase().ExecuteAsync(Request());

        var payloadPaths = _captured!.Files.Select(f => f.RelativePath).ToArray();
        _captured!.Manifest.Sources.Select(s => s.BundlePath).Should().BeEquivalentTo(payloadPaths);
    }

    [Fact]
    public async Task The_publisher_result_is_returned_unchanged()
    {
        var result = await CreateUseCase().ExecuteAsync(Request());

        result.Should().BeOfType<BundlePublishResult.Success>()
            .Which.CommitSha.Should().Be("abc1234");
    }

    [Fact]
    public async Task Null_request_is_rejected()
    {
        var useCase = CreateUseCase();

        Func<Task> act = () => useCase.ExecuteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
