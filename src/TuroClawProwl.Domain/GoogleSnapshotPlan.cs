using System.Globalization;

namespace TuroClawProwl.Domain;

public sealed record SnapshotTaskList(string Id, string ListId, string Branch, string BundlePath);

public sealed record CalendarWindow(DateTimeOffset FirstDay, DateTimeOffset LastDay)
{
    public string TimeMin =>
        FirstDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z";

    public string TimeMax =>
        LastDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59Z";
}

// The task lists and their branches are a fixed contract, not registry-derived:
// the registry's task-list table is stale (finding #10) and a wrong row there
// must not be able to redirect or widen what the publisher reads.
public static class GoogleSnapshotPlan
{
    public const string YneListId = "QTA2NmhzdHRMbFBla2ZaRQ";
    public const string MyTasksListId = "MTIzNjE2OTI4NjM5NTQ3NDc3NzM6MDow";

    // Reclaim's own sink is not a planning surface; YNE-test is excluded everywhere.
    public const string ReclaimListId = "LU0ySV9fOXlYNjJmZm1wWA";
    public const string YneTestListId = "ckdXRUdBQ1gwX1ZPaElhYg";

    public const string CalendarWindowSourceId = "calendar/window";
    public const string CalendarWindowBundlePath = "calendar/window.json";
    public const string CalendarWindowApiPath = "googleapis://calendar/window";

    public const int WindowDaysAhead = 2;

    private static readonly string[] ExcludedListIds = [ReclaimListId, YneTestListId];

    public static IReadOnlyList<SnapshotTaskList> TaskLists() =>
    [
        new SnapshotTaskList("tasks/yne", YneListId, "professional", "tasks/yne.json"),
        new SnapshotTaskList("tasks/my-tasks", MyTasksListId, "personal", "tasks/my-tasks.json"),
    ];

    public static string TaskListApiPath(string listId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listId);
        return "googleapis://tasks/" + listId;
    }

    public static bool IsExcludedList(string listId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(listId);
        return ExcludedListIds.Contains(listId, StringComparer.Ordinal);
    }

    public static CalendarWindow WindowFor(DateTimeOffset localToday) =>
        new(localToday, localToday.AddDays(WindowDaysAhead));
}
