using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.UseCases;

public sealed class PushUnpushedReposUseCase
{
    private readonly IGitRunner _git;
    private readonly IToastService _toasts;
    private readonly ILogger<PushUnpushedReposUseCase> _logger;

    public PushUnpushedReposUseCase(
        IGitRunner git,
        IToastService toasts,
        ILogger<PushUnpushedReposUseCase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(toasts);

        _git = git;
        _toasts = toasts;
        _logger = logger ?? NullLogger<PushUnpushedReposUseCase>.Instance;
    }

    public async Task<PushSummary> ExecuteAsync(
        IReadOnlyDictionary<string, RepoState> states,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(states);

        var plan = PushPlanner.Plan(states);
        if (plan.IsEmpty)
            return new PushSummary(Array.Empty<PushOutcome>());

        var outcomes = new List<PushOutcome>(plan.Repos.Count);
        foreach (var repoKey in plan.Repos)
        {
            var result = await _git.PushAsync(repoKey, cancellationToken).ConfigureAwait(false);
            outcomes.Add(result switch
            {
                GitPushResult.Success => PushOutcome.Success(repoKey),
                GitPushResult.Failure f => PushOutcome.Failure(repoKey, f.Error),
                _ => throw new ArgumentException(
                    $"Unknown git push result variant: {result.GetType().Name}",
                    nameof(result)),
            });
        }

        var summary = new PushSummary(outcomes);
        _logger.LogInformation(
            "Push summary: {SuccessCount} succeeded, {FailureCount} failed",
            summary.SuccessCount, summary.FailureCount);
        await _toasts.NotifyPushSummaryAsync(summary, cancellationToken).ConfigureAwait(false);
        return summary;
    }
}
