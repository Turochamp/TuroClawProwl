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
        // Oslo (+02:00) local midnight on 2026-09-12 is 2026-09-11T22:00:00Z, and
        // local 23:59:59 on 2026-09-14 is 2026-09-14T21:59:59Z: the window bounds
        // are true UTC instants of the local day, not the local date with "Z" glued on.
        var today = new DateTimeOffset(2026, 9, 12, 8, 30, 0, TimeSpan.FromHours(2));

        var window = GoogleSnapshotPlan.WindowFor(today);

        window.TimeMin.Should().Be("2026-09-11T22:00:00Z");
        window.TimeMax.Should().Be("2026-09-14T21:59:59Z");
    }

    [Fact]
    public void The_window_rolls_over_a_month_boundary_correctly()
    {
        // FirstDay local midnight 2026-09-30T00:00:00+02:00 -> 2026-09-29T22:00:00Z.
        // LastDay (FirstDay + 2 days) local end-of-day 2026-10-02T23:59:59+02:00
        // -> 2026-10-02T21:59:59Z.
        var today = new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.FromHours(2));

        var window = GoogleSnapshotPlan.WindowFor(today);

        window.TimeMin.Should().Be("2026-09-29T22:00:00Z");
        window.TimeMax.Should().Be("2026-10-02T21:59:59Z");
    }

    [Fact]
    public void A_zero_offset_input_is_unchanged_by_the_utc_conversion()
    {
        // Proves the fix converts using each value's own offset rather than applying
        // a blanket shift: at +00:00 the local day and the UTC day coincide exactly.
        var today = new DateTimeOffset(2026, 9, 12, 8, 30, 0, TimeSpan.Zero);

        var window = GoogleSnapshotPlan.WindowFor(today);

        window.TimeMin.Should().Be("2026-09-12T00:00:00Z");
        window.TimeMax.Should().Be("2026-09-14T23:59:59Z");
    }

    [Fact]
    public void Empty_list_id_is_rejected()
    {
        Action act = () => GoogleSnapshotPlan.TaskListApiPath("  ");

        act.Should().Throw<ArgumentException>();
    }
}
