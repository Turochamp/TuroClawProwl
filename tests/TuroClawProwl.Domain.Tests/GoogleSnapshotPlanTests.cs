using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class GoogleSnapshotPlanTests
{
    [Fact]
    public void Exactly_two_task_lists_are_snapshotted_with_their_corrected_branches()
    {
        var lists = GoogleSnapshotPlan.TaskLists();

        lists.Should().BeEquivalentTo(new[]
        {
            new SnapshotTaskList("tasks/yne", "QTA2NmhzdHRMbFBla2ZaRQ", "professional", "tasks/yne.json"),
            new SnapshotTaskList(
                "tasks/my-tasks", "MTIzNjE2OTI4NjM5NTQ3NDc3NzM6MDow", "personal", "tasks/my-tasks.json"),
        }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void The_reclaim_sink_list_is_never_snapshotted()
    {
        GoogleSnapshotPlan.ReclaimListId.Should().Be("LU0ySV9fOXlYNjJmZm1wWA");
        GoogleSnapshotPlan.IsExcludedList("LU0ySV9fOXlYNjJmZm1wWA").Should().BeTrue();
        GoogleSnapshotPlan.TaskLists().Should().NotContain(l => l.ListId == GoogleSnapshotPlan.ReclaimListId);
    }

    [Fact]
    public void The_yne_test_list_is_never_snapshotted()
    {
        GoogleSnapshotPlan.YneTestListId.Should().Be("ckdXRUdBQ1gwX1ZPaElhYg");
        GoogleSnapshotPlan.IsExcludedList("ckdXRUdBQ1gwX1ZPaElhYg").Should().BeTrue();
        GoogleSnapshotPlan.TaskLists().Should().NotContain(l => l.ListId == GoogleSnapshotPlan.YneTestListId);
    }

    [Fact]
    public void A_snapshotted_list_is_not_reported_as_excluded()
    {
        GoogleSnapshotPlan.IsExcludedList("QTA2NmhzdHRMbFBla2ZaRQ").Should().BeFalse();
        GoogleSnapshotPlan.IsExcludedList("MTIzNjE2OTI4NjM5NTQ3NDc3NzM6MDow").Should().BeFalse();
    }

    [Fact]
    public void A_task_list_api_path_is_an_api_identity_not_a_repo_path()
    {
        var path = GoogleSnapshotPlan.TaskListApiPath("QTA2NmhzdHRMbFBla2ZaRQ");

        path.Should().Be("googleapis://tasks/QTA2NmhzdHRMbFBla2ZaRQ");
        path.Should().StartWith("googleapis://");
    }

    [Fact]
    public void The_calendar_window_api_path_is_an_api_identity_not_a_repo_path()
    {
        GoogleSnapshotPlan.CalendarWindowApiPath.Should().Be("googleapis://calendar/window");
        GoogleSnapshotPlan.CalendarWindowSourceId.Should().Be("calendar/window");
        GoogleSnapshotPlan.CalendarWindowBundlePath.Should().Be("calendar/window.json");
    }

    [Fact]
    public void No_snapshot_api_path_could_be_mistaken_for_a_repo_relative_path()
    {
        var paths = GoogleSnapshotPlan.TaskLists()
            .Select(l => GoogleSnapshotPlan.TaskListApiPath(l.ListId))
            .Append(GoogleSnapshotPlan.CalendarWindowApiPath);

        paths.Should().AllSatisfy(p =>
        {
            p.Should().StartWith("googleapis://");
            p.Should().NotEndWith(".md");
            p.Should().NotEndWith(".json");
        });
    }

    [Fact]
    public void The_window_covers_today_plus_two_days()
    {
        var today = new DateTimeOffset(2026, 9, 12, 8, 30, 0, TimeSpan.FromHours(2));

        var window = GoogleSnapshotPlan.WindowFor(today);

        window.TimeMin.Should().Be("2026-09-12T00:00:00Z");
        window.TimeMax.Should().Be("2026-09-14T23:59:59Z");
    }

    [Fact]
    public void The_window_rolls_over_a_month_boundary_correctly()
    {
        var today = new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.FromHours(2));

        var window = GoogleSnapshotPlan.WindowFor(today);

        window.TimeMin.Should().Be("2026-09-30T00:00:00Z");
        window.TimeMax.Should().Be("2026-10-02T23:59:59Z");
    }

    [Fact]
    public void Empty_list_id_is_rejected()
    {
        Action act = () => GoogleSnapshotPlan.TaskListApiPath("  ");

        act.Should().Throw<ArgumentException>();
    }
}
