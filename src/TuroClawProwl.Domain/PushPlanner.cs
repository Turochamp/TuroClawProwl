namespace TuroClawProwl.Domain;

public static class PushPlanner
{
    public static PushPlan Plan(IReadOnlyCollection<TodayFileStatus> todayFiles)
    {
        ArgumentNullException.ThrowIfNull(todayFiles);

        var selected = todayFiles
            .Where(f => f.IsUnpushed)
            .Select(f => f.RepoPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        return new PushPlan(selected);
    }
}
