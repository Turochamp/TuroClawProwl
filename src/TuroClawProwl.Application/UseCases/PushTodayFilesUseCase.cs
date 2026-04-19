using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.UseCases;

public sealed class PushTodayFilesUseCase
{
    private readonly IGitRunner _git;
    private readonly IToastService _toasts;
    private readonly ILogger<PushTodayFilesUseCase> _logger;

    public PushTodayFilesUseCase(
        IGitRunner git,
        IToastService toasts,
        ILogger<PushTodayFilesUseCase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(toasts);

        _git = git;
        _toasts = toasts;
        _logger = logger ?? NullLogger<PushTodayFilesUseCase>.Instance;
    }

    public async Task<PushSummary> ExecuteAsync(
        IReadOnlyCollection<TodayFileStatus> statuses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statuses);

        var plan = PushPlanner.Plan(statuses);
        if (plan.IsEmpty)
            return new PushSummary(Array.Empty<PushOutcome>());

        var outcomes = new List<PushOutcome>(plan.Repos.Count);
        foreach (var repoPath in plan.Repos)
        {
            var result = await _git.PushAsync(repoPath, cancellationToken).ConfigureAwait(false);
            outcomes.Add(result switch
            {
                GitPushResult.Success => PushOutcome.Success(repoPath),
                GitPushResult.Failure f => PushOutcome.Failure(repoPath, f.Error),
                _ => throw new ArgumentException(
                    $"Unknown git push result variant: {result.GetType().Name}",
                    nameof(result)),
            });
        }

        var summary = new PushSummary(outcomes);
        _logger.LogInformation(
            "Push Today files summary: {SuccessCount} succeeded, {FailureCount} failed",
            summary.SuccessCount, summary.FailureCount);
        await _toasts.NotifyPushSummaryAsync(summary, cancellationToken).ConfigureAwait(false);
        return summary;
    }
}
