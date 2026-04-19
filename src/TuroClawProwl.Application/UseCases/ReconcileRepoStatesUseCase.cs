using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.UseCases;

public sealed class ReconcileRepoStatesUseCase
{
    private readonly IRepoDiscovery _discovery;
    private readonly IGitRunner _git;
    private readonly ITrayView _tray;
    private readonly ILogger<ReconcileRepoStatesUseCase> _logger;

    public ReconcileRepoStatesUseCase(
        IRepoDiscovery discovery,
        IGitRunner git,
        ITrayView tray,
        ILogger<ReconcileRepoStatesUseCase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(tray);

        _discovery = discovery;
        _git = git;
        _tray = tray;
        _logger = logger ?? NullLogger<ReconcileRepoStatesUseCase>.Instance;
    }

    public async Task<IReadOnlyDictionary<string, RepoState>> ExecuteAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var repos = await _discovery.DiscoverAsync(rootPath, cancellationToken).ConfigureAwait(false);
        var states = new Dictionary<string, RepoState>(repos.Count, StringComparer.Ordinal);

        foreach (var repoPath in repos)
        {
            var info = await _git.GetRepoInfoAsync(repoPath, cancellationToken).ConfigureAwait(false);
            var summary = PorcelainParser.Parse(info.PorcelainOutput);
            var state = RepoStateClassifier.Classify(summary, info.UnpushedCount, info.HasUpstream);
            states[repoPath] = state;
        }

        await _tray.SetRepoStatesAsync(states, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Reconciled {RepoCount} repos under {RootPath}", states.Count, rootPath);
        return states;
    }
}
