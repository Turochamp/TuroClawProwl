using FluentAssertions;
using Moq;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Application.Tests.TestSupport;
using TuroClawProwl.Application.UseCases;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Tests.UseCases;

public class ReportBundlePublishUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 6, 30, 0, TimeSpan.Zero);

    private readonly Mock<ITrayView> _tray = new(MockBehavior.Strict);
    private readonly Mock<IToastService> _toasts = new(MockBehavior.Strict);
    private readonly FixedClock _clock = new(Now);

    private readonly List<PublishHealth> _trayStates = [];
    private readonly List<PublishHealth.Failed> _toasted = [];

    public ReportBundlePublishUseCaseTests()
    {
        _tray.Setup(t => t.SetPublishHealthAsync(It.IsAny<PublishHealth>(), It.IsAny<CancellationToken>()))
            .Callback<PublishHealth, CancellationToken>((h, _) => _trayStates.Add(h))
            .Returns(Task.CompletedTask);
        _toasts.Setup(t => t.NotifyBundlePublishFailureAsync(
                It.IsAny<PublishHealth.Failed>(), It.IsAny<CancellationToken>()))
            .Callback<PublishHealth.Failed, CancellationToken>((f, _) => _toasted.Add(f))
            .Returns(Task.CompletedTask);
    }

    private ReportBundlePublishUseCase CreateUseCase() =>
        new(_tray.Object, _toasts.Object, _clock);

    [Fact]
    public async Task A_successful_publish_reports_healthy_and_raises_no_toast()
    {
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new BundlePublishResult.Success("abc1234", 6));

        useCase.Health.Should().BeOfType<PublishHealth.Healthy>()
            .Which.LastPublishedAt.Should().Be(Now);
        _trayStates.Should().ContainSingle().Which.Should().BeOfType<PublishHealth.Healthy>();
        _toasted.Should().BeEmpty();
    }

    [Fact]
    public async Task A_transient_failure_changes_the_tray_state_and_raises_a_toast()
    {
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new BundlePublishResult.Transient("could not resolve host github.com"));

        var failed = useCase.Health.Should().BeOfType<PublishHealth.Failed>().Subject;
        failed.IsMisconfiguration.Should().BeFalse();
        failed.SettingName.Should().BeEmpty();
        failed.Detail.Should().Be("could not resolve host github.com");
        _trayStates.Should().ContainSingle().Which.Should().BeOfType<PublishHealth.Failed>();
        _toasted.Should().ContainSingle();
    }

    [Fact]
    public async Task A_misconfiguration_is_reported_as_one_and_names_the_setting_at_fault()
    {
        var useCase = CreateUseCase();

        await useCase.ExecuteAsync(new BundlePublishResult.Misconfigured(
            "bundlePublishBranch", "branch nope does not exist"));

        var failed = useCase.Health.Should().BeOfType<PublishHealth.Failed>().Subject;
        failed.IsMisconfiguration.Should().BeTrue();
        failed.SettingName.Should().Be("bundlePublishBranch");
        _toasted.Should().ContainSingle().Which.IsMisconfiguration.Should().BeTrue();
        _toasted[0].SettingName.Should().Be("bundlePublishBranch");
    }

    [Fact]
    public async Task A_success_after_a_failure_returns_the_indicator_to_healthy()
    {
        var useCase = CreateUseCase();
        await useCase.ExecuteAsync(new BundlePublishResult.Transient("network down"));

        _clock.Advance(TimeSpan.FromMinutes(2));
        await useCase.ExecuteAsync(new BundlePublishResult.Success("def5678", 6));

        useCase.Health.Should().BeOfType<PublishHealth.Healthy>();
        _trayStates.Should().HaveCount(2);
        _trayStates[0].Should().BeOfType<PublishHealth.Failed>();
        _trayStates[1].Should().BeOfType<PublishHealth.Healthy>();
    }

    [Fact]
    public async Task The_same_failure_repeating_inside_ten_minutes_is_not_toasted_again()
    {
        var useCase = CreateUseCase();
        await useCase.ExecuteAsync(new BundlePublishResult.Transient("network down"));

        _clock.Advance(TimeSpan.FromMinutes(3));
        await useCase.ExecuteAsync(new BundlePublishResult.Transient("network down"));

        _toasted.Should().ContainSingle();
        _trayStates.Should().HaveCount(2, "the tray is always refreshed even when the toast is suppressed");
    }

    [Fact]
    public async Task A_different_failure_is_toasted_even_inside_the_dedup_window()
    {
        var useCase = CreateUseCase();
        await useCase.ExecuteAsync(new BundlePublishResult.Transient("network down"));

        _clock.Advance(TimeSpan.FromMinutes(1));
        await useCase.ExecuteAsync(new BundlePublishResult.Misconfigured("hubRepoPath", "not a git repository"));

        _toasted.Should().HaveCount(2);
        _toasted[1].IsMisconfiguration.Should().BeTrue();
    }

    [Fact]
    public async Task A_failure_returning_after_a_success_is_toasted_again()
    {
        var useCase = CreateUseCase();
        await useCase.ExecuteAsync(new BundlePublishResult.Transient("network down"));
        _clock.Advance(TimeSpan.FromMinutes(1));
        await useCase.ExecuteAsync(new BundlePublishResult.Success("abc1234", 6));

        _clock.Advance(TimeSpan.FromMinutes(1));
        await useCase.ExecuteAsync(new BundlePublishResult.Transient("network down"));

        _toasted.Should().HaveCount(2);
    }

    [Fact]
    public async Task Before_any_publish_the_health_is_never_published()
    {
        CreateUseCase().Health.Should().BeOfType<PublishHealth.NeverPublished>();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Null_result_is_rejected()
    {
        var useCase = CreateUseCase();

        Func<Task> act = () => useCase.ExecuteAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
