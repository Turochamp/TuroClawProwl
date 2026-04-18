namespace TuroClawProwl.Domain;

public sealed record PushPlan(IReadOnlyList<string> Repos)
{
    public bool IsEmpty => Repos.Count == 0;
}
