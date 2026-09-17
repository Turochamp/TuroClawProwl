using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class PublishFailureToastTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 7, 15, 0, TimeSpan.Zero);

    [Fact]
    public void A_misconfiguration_is_labelled_as_one_and_names_the_setting()
    {
        var failure = new PublishHealth.Failed(
            Now, IsMisconfiguration: true, "bundleWorktreePath",
            @"C:\Temp\stale exists but is not a git worktree");

        var lines = PublishFailureToast.Compose(failure);

        lines[0].Should().Contain("misconfigured");
        lines.Should().Contain(l => l.Contains("bundleWorktreePath", StringComparison.Ordinal));
    }

    [Fact]
    public void A_transient_failure_is_labelled_as_transient_and_names_no_setting()
    {
        var failure = new PublishHealth.Failed(
            Now, IsMisconfiguration: false, string.Empty, "could not resolve host github.com");

        var lines = PublishFailureToast.Compose(failure);

        lines[0].Should().Contain("publish failed");
        lines[0].Should().NotContain("misconfigured");
        lines.Should().Contain(l => l.Contains("Transient", StringComparison.Ordinal));
    }

    [Fact]
    public void The_detail_is_always_carried()
    {
        var failure = new PublishHealth.Failed(
            Now, IsMisconfiguration: false, string.Empty, "could not resolve host github.com");

        var lines = PublishFailureToast.Compose(failure);

        lines.Should().Contain(l => l.Contains("could not resolve host github.com", StringComparison.Ordinal));
    }

    [Fact]
    public void Never_more_than_three_lines_because_the_toast_api_caps_at_four()
    {
        var failure = new PublishHealth.Failed(
            Now, IsMisconfiguration: true, "hubRepoPath", new string('x', 4000));

        var lines = PublishFailureToast.Compose(failure);

        lines.Should().HaveCountLessThanOrEqualTo(PublishFailureToast.MaxLines);
        PublishFailureToast.MaxLines.Should().Be(3);
    }

    [Fact]
    public void A_long_detail_is_clipped_rather_than_wrapped_into_extra_lines()
    {
        var failure = new PublishHealth.Failed(
            Now, IsMisconfiguration: false, string.Empty, new string('x', 4000));

        var lines = PublishFailureToast.Compose(failure);

        lines.Should().AllSatisfy(l => l.Length.Should().BeLessThanOrEqualTo(130));
    }

    [Fact]
    public void Null_failure_is_rejected()
    {
        Action act = () => PublishFailureToast.Compose(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
