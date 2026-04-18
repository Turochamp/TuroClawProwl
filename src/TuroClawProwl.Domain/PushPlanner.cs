namespace TuroClawProwl.Domain;

public static class PushPlanner
{
    public static PushPlan Plan(IReadOnlyDictionary<string, RepoState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        var selected = states
            .Where(kv => kv.Value.UnpushedCount > 0)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        return new PushPlan(selected);
    }
}
