using FluentAssertions;
using Moq;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.Tests.TestSupport;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Tests.UseCases;

public class BundleHeartbeatTests
{
    private const string HubRoot = @"C:\Git\ClaudeCodeAssistants";

    private static readonly DateTimeOffset Now = new(2026, 9, 12, 6, 30, 0, TimeSpan.Zero);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromHours(6);

    private const string Registry = """
        ## Active set

        | CCA | Branch | Cadence | Status | State file |
        |-----|--------|---------|--------|-----------|
        | YNE | professional | weekly | active | CCA-YNE/STATE.md |

        ### Calendars

        | Calendar | Branch | ID | Use |
        |----------|--------|----|----|
        | Primary | professional | `michael.ahs@gmail.com` | Work + general |
        """;

    private readonly Mock<ISourceFileReader> _files = new(MockBehavior.Strict);
    private readonly Mock<IGoogleWorkspaceReader> _google = new(MockBehavior.Strict);
    private readonly Mock<ISnapshotStore> _snapshots = new(MockBehavior.Strict);
    private readonly Mock<IBundlePublisher> _publisher = new(MockBehavior.Strict);
    private readonly FixedClock _clock = new(Now);

    private readonly List<BundlePayload> _payloads = [];

    private int _committedFileCount = 8;

    public BundleHeartbeatTests()
    {
        _publisher.Setup(p => p.GetSourceBranchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("feature/humanize-design");
        _publisher.Setup(p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()))
            .Callback<BundlePayload, CancellationToken>((p, _) => _payloads.Add(p))
            .ReturnsAsync(() => new BundlePublishResult.Success("abc1234", _committedFileCount));

        _snapshots.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SnapshotReadResult.NotFound());
        _snapshots.Setup(s => s.SaveAsync(It.IsAny<StoredSnapshot>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        SetUpFile(BundleLayout.RegistryRelativePath, Registry);

        _google.Setup(g => g.ReadTaskListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Success("{\"items\":[]}"));
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
        new(_files.Object, _google.Object, _snapshots.Object,
            _publisher.Object, _clock);

    private static StateBundleRequest Request() =>
        new(HubRoot, "TuroClawProwl/0.3.0", Heartbeat);

    [Fact]
    public async Task The_first_publish_of_a_session_is_always_a_heartbeat()
    {
        await CreateUseCase().ExecuteAsync(Request());

        _payloads.Should().ContainSingle().Which.PublishEvenIfUnchanged.Should().BeTrue();
    }

    [Fact]
    public async Task A_publish_inside_the_heartbeat_window_does_not_force_a_commit()
    {
        var useCase = CreateUseCase();
        await useCase.ExecuteAsync(Request());

        _clock.Advance(TimeSpan.FromHours(1));
        await useCase.ExecuteAsync(Request());

        _payloads.Should().HaveCount(2);
        _payloads[1].PublishEvenIfUnchanged.Should().BeFalse();
    }

    [Fact]
    public async Task A_publish_once_the_heartbeat_interval_has_elapsed_forces_a_commit()
    {
        var useCase = CreateUseCase();
        await useCase.ExecuteAsync(Request());

        _clock.Advance(Heartbeat);
        await useCase.ExecuteAsync(Request());

        _payloads[1].PublishEvenIfUnchanged.Should().BeTrue();
    }

    [Fact]
    public async Task A_committed_publish_resets_the_heartbeat_window()
    {
        var useCase = CreateUseCase();
        await useCase.ExecuteAsync(Request());

        _clock.Advance(Heartbeat);
        await useCase.ExecuteAsync(Request());
        _clock.Advance(TimeSpan.FromHours(1));
        await useCase.ExecuteAsync(Request());

        _payloads.Should().HaveCount(3);
        _payloads[2].PublishEvenIfUnchanged.Should().BeFalse();
    }

    [Fact]
    public async Task A_publish_the_publisher_found_nothing_to_commit_does_not_reset_the_window()
    {
        var useCase = CreateUseCase();
        await useCase.ExecuteAsync(Request());
        _committedFileCount = 0;

        _clock.Advance(Heartbeat);
        await useCase.ExecuteAsync(Request());
        _clock.Advance(TimeSpan.FromMinutes(1));
        await useCase.ExecuteAsync(Request());

        _payloads[1].PublishEvenIfUnchanged.Should().BeTrue();
        _payloads[2].PublishEvenIfUnchanged.Should()
            .BeTrue("nothing was committed, so the verified timestamps are still not on record");
    }

    [Fact]
    public async Task A_failed_publish_does_not_reset_the_heartbeat_window()
    {
        var useCase = CreateUseCase();
        await useCase.ExecuteAsync(Request());
        _publisher.Setup(p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()))
            .Callback<BundlePayload, CancellationToken>((p, _) => _payloads.Add(p))
            .ReturnsAsync(new BundlePublishResult.Transient("Connection timed out"));

        _clock.Advance(Heartbeat);
        await useCase.ExecuteAsync(Request());
        _clock.Advance(TimeSpan.FromMinutes(1));
        await useCase.ExecuteAsync(Request());

        _payloads[2].PublishEvenIfUnchanged.Should().BeTrue();
    }

    [Fact]
    public async Task The_heartbeat_flag_never_changes_what_the_bundle_contains()
    {
        var useCase = CreateUseCase();
        await useCase.ExecuteAsync(Request());

        _clock.Advance(TimeSpan.FromHours(1));
        await useCase.ExecuteAsync(Request());

        _payloads[1].Files.Select(f => f.RelativePath)
            .Should().BeEquivalentTo(_payloads[0].Files.Select(f => f.RelativePath));
    }
}
