using FluentAssertions;
using Moq;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.Tests.TestSupport;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Tests.UseCases;

public class PublishAndReportStateBundleUseCaseTests
{
    private const string HubRoot = @"C:\Git\ClaudeCodeAssistants";

    private static readonly DateTimeOffset Now = new(2026, 9, 12, 6, 30, 0, TimeSpan.Zero);

    private const string Registry = """
        ## Active set

        | CCA | Branch | Cadence | Status | State file |
        |-----|--------|---------|--------|-----------|
        | YNE | professional | weekly | active | CCA-YNE/STATE.md |
        """;

    private readonly Mock<ISourceFileReader> _files = new(MockBehavior.Strict);
    private readonly Mock<IGitFileFactsReader> _gitFacts = new(MockBehavior.Strict);
    private readonly Mock<IGoogleWorkspaceReader> _google = new(MockBehavior.Strict);
    private readonly Mock<ISnapshotStore> _snapshots = new(MockBehavior.Strict);
    private readonly Mock<IBundlePublisher> _publisher = new(MockBehavior.Strict);
    private readonly Mock<ITrayView> _tray = new(MockBehavior.Strict);
    private readonly Mock<IToastService> _toasts = new(MockBehavior.Strict);
    private readonly FixedClock _clock = new(Now);

    private readonly List<PublishHealth> _trayStates = [];
    private readonly List<PublishHealth.Failed> _toasted = [];

    private readonly ReportBundlePublishUseCase _report;

    public PublishAndReportStateBundleUseCaseTests()
    {
        _tray.Setup(t => t.SetPublishHealthAsync(It.IsAny<PublishHealth>(), It.IsAny<CancellationToken>()))
            .Callback<PublishHealth, CancellationToken>((h, _) => _trayStates.Add(h))
            .Returns(Task.CompletedTask);
        _toasts.Setup(t => t.NotifyBundlePublishFailureAsync(
                It.IsAny<PublishHealth.Failed>(), It.IsAny<CancellationToken>()))
            .Callback<PublishHealth.Failed, CancellationToken>((f, _) => _toasted.Add(f))
            .Returns(Task.CompletedTask);

        _publisher.Setup(p => p.GetSourceBranchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("feature/humanize-design");
        _gitFacts.Setup(g => g.GetFileFactsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitFileFacts.Untracked());

        // This class is about surfacing the publish outcome, not about snapshots,
        // so the Google reads fail and retain nothing.
        _google.Setup(g => g.ReadTaskListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Failure("snapshots are out of scope in this test"));
        _google.Setup(g => g.ReadCalendarWindowAsync(
                It.IsAny<IReadOnlyList<RegistryCalendar>>(), It.IsAny<CalendarWindow>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleReadResult.Failure("snapshots are out of scope in this test"));
        _snapshots.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StoredSnapshot?)null);

        SetUpFile(BundleLayout.RegistryRelativePath, Registry);
        SetUpFile(BundleLayout.CrmIndexRelativePath, "# Contacts");
        SetUpFile(BundleLayout.WeeklyRelativePath(Now.ToLocalTime()), "# Week");
        SetUpFile("CCA-YNE/STATE.md", "# YNE state");

        _report = new ReportBundlePublishUseCase(_tray.Object, _toasts.Object, _clock);
    }

    private static string Abs(string relative) =>
        Path.Combine(HubRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private void SetUpFile(string relative, string content) =>
        _files.Setup(f => f.ReadAsync(Abs(relative), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SourceReadResult.Found(content, Now.AddMinutes(-5)));

    private PublishAndReportStateBundleUseCase CreateUseCase() =>
        new(
            new PublishStateBundleUseCase(
                _files.Object, _gitFacts.Object, _google.Object, _snapshots.Object,
                _publisher.Object, _clock),
            _report);

    private static StateBundleRequest Request() => new(HubRepoPath: HubRoot, PublisherVersion: "TuroClawProwl/0.3.0", HeartbeatInterval: TimeSpan.FromHours(6));

    [Fact]
    public async Task A_publish_from_a_no_upstream_feature_branch_reports_healthy()
    {
        _publisher.Setup(p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BundlePublishResult.Success("abc1234", 5));

        var result = await CreateUseCase().ExecuteAsync(Request());

        result.Should().BeOfType<BundlePublishResult.Success>();
        _report.Health.Should().BeOfType<PublishHealth.Healthy>();
        _toasted.Should().BeEmpty();
    }

    [Fact]
    public async Task A_misconfigured_publish_names_the_setting_on_the_tray_and_in_the_toast()
    {
        _publisher.Setup(p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BundlePublishResult.Misconfigured("bundleWorktreePath", "occupied by other files"));

        await CreateUseCase().ExecuteAsync(Request());

        var failed = _trayStates.Should().ContainSingle().Subject
            .Should().BeOfType<PublishHealth.Failed>().Subject;
        failed.IsMisconfiguration.Should().BeTrue();
        failed.SettingName.Should().Be("bundleWorktreePath");
        _toasted.Should().ContainSingle().Which.SettingName.Should().Be("bundleWorktreePath");
    }

    [Fact]
    public async Task A_transient_publish_failure_is_not_reported_as_a_misconfiguration()
    {
        _publisher.Setup(p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BundlePublishResult.Transient("Connection timed out"));

        await CreateUseCase().ExecuteAsync(Request());

        _toasted.Should().ContainSingle().Which.IsMisconfiguration.Should().BeFalse();
        PublishFailureToast.Compose(_toasted[0])[0].Should().Contain("publish failed");
    }

    [Fact]
    public async Task A_publish_that_succeeds_after_failing_returns_the_tray_to_healthy()
    {
        var publishResults = new Queue<BundlePublishResult>(
        [
            new BundlePublishResult.Transient("Connection timed out"),
            new BundlePublishResult.Success("abc1234", 5),
        ]);
        _publisher.Setup(p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(publishResults.Dequeue);
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(Request());
        _clock.Advance(TimeSpan.FromMinutes(1));
        await useCase.ExecuteAsync(Request());

        _trayStates.Should().HaveCount(2);
        _trayStates[0].Should().BeOfType<PublishHealth.Failed>();
        _trayStates[1].Should().BeOfType<PublishHealth.Healthy>();
        _report.Health.Should().BeOfType<PublishHealth.Healthy>();
    }

    [Fact]
    public async Task A_missing_source_does_not_make_the_publish_unhealthy()
    {
        _files.Setup(f => f.ReadAsync(Abs("CCA-YNE/STATE.md"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SourceReadResult.Missing());
        _publisher.Setup(p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BundlePublishResult.Success("abc1234", 4));

        await CreateUseCase().ExecuteAsync(Request());

        _report.Health.Should().BeOfType<PublishHealth.Healthy>();
        _toasted.Should().BeEmpty();
    }

    [Fact]
    public async Task Null_request_is_rejected()
    {
        var useCase = CreateUseCase();

        Func<Task> act = () => useCase.ExecuteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task A_publish_that_throws_is_reported_as_a_transient_failure_instead_of_escaping()
    {
        // A genuinely unknown failure -- not one of the classified cases (a missing
        // git/gws executable is now classified as Misconfigured by the publisher
        // itself before it can reach here; see GitWorktreeBundlePublisherTests).
        _publisher.Setup(p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("an unexpected, unclassified bug"));

        var result = await CreateUseCase().ExecuteAsync(Request());

        result.Should().BeOfType<BundlePublishResult.Transient>()
            .Which.Detail.Should().Be("an unexpected, unclassified bug");
        _trayStates.Should().ContainSingle().Subject.Should().BeOfType<PublishHealth.Failed>();
        _toasted.Should().ContainSingle().Which.IsMisconfiguration.Should().BeFalse();
    }

    [Fact]
    public async Task A_canceled_publish_propagates_without_being_reported_as_a_failure()
    {
        _publisher.Setup(p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        Func<Task> act = () => CreateUseCase().ExecuteAsync(Request());

        await act.Should().ThrowAsync<OperationCanceledException>();
        _trayStates.Should().BeEmpty();
        _toasted.Should().BeEmpty();
    }

    [Fact]
    public async Task A_report_that_throws_does_not_escape_the_use_case()
    {
        _publisher.Setup(p => p.PublishAsync(It.IsAny<BundlePayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BundlePublishResult.Success("abc1234", 5));
        // Simulates the tray post throwing because the UI thread (and its
        // synchronization context) is already gone at shutdown.
        _tray.Setup(t => t.SetPublishHealthAsync(It.IsAny<PublishHealth>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("tray already disposed"));

        var result = await CreateUseCase().ExecuteAsync(Request());

        result.Should().BeOfType<BundlePublishResult.Success>();
    }
}
