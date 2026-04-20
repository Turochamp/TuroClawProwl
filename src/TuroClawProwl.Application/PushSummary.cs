namespace TuroClawProwl.Application;

public sealed record PushSummary(IReadOnlyList<PushOutcome> Outcomes)
{
    public int SuccessCount => Outcomes.Count(o => o.IsSuccess);

    public int FailureCount => Outcomes.Count(o => !o.IsSuccess);

    public bool IsEmpty => Outcomes.Count == 0;
}
