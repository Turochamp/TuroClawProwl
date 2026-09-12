using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.UseCases;

public sealed class PublishStateBundleUseCase
{
    private readonly ISourceFileReader _files;
    private readonly IGitFileFactsReader _gitFacts;
    private readonly IBundlePublisher _publisher;
    private readonly IClock _clock;
    private readonly ILogger<PublishStateBundleUseCase> _logger;

    public PublishStateBundleUseCase(
        ISourceFileReader files,
        IGitFileFactsReader gitFacts,
        IBundlePublisher publisher,
        IClock clock,
        ILogger<PublishStateBundleUseCase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(gitFacts);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(clock);

        _files = files;
        _gitFacts = gitFacts;
        _publisher = publisher;
        _clock = clock;
        _logger = logger ?? NullLogger<PublishStateBundleUseCase>.Instance;
    }

    public async Task<BundlePublishResult> ExecuteAsync(
        StateBundleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.UtcNow;
        // The hub names week files by local ISO week, so resolve the week locally.
        var localNow = now.ToLocalTime();

        var sourceBranch = await _publisher.GetSourceBranchAsync(cancellationToken).ConfigureAwait(false);

        var collected = new Collected();

        var registry = await CollectAsync(
            request.HubRepoPath,
            BundleLayout.RegistrySourceId,
            BundleLayout.RegistryRelativePath,
            BundleLayout.RegistryBundlePath,
            collected,
            cancellationToken).ConfigureAwait(false);

        await CollectAsync(
            request.HubRepoPath,
            BundleLayout.CrmIndexSourceId,
            BundleLayout.CrmIndexRelativePath,
            BundleLayout.CrmIndexBundlePath,
            collected,
            cancellationToken).ConfigureAwait(false);

        await CollectAsync(
            request.HubRepoPath,
            BundleLayout.WeeklySourceId(localNow),
            BundleLayout.WeeklyRelativePath(localNow),
            BundleLayout.WeeklyBundlePath(localNow),
            collected,
            cancellationToken).ConfigureAwait(false);

        if (registry is not null)
        {
            foreach (var entry in RegistryParser.ParseActiveSet(registry))
            {
                await CollectAsync(
                    request.HubRepoPath,
                    BundleLayout.CcaSourceId(entry.Name),
                    entry.StateFileRelativePath,
                    BundleLayout.CcaBundlePath(entry.Name),
                    collected,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var manifest = new BundleManifest(
            SchemaVersion: BundleManifest.CurrentSchemaVersion,
            PublishedAt: now,
            Publisher: request.PublisherVersion,
            SourceBranch: sourceBranch,
            Sources: collected.Sources,
            Failures: collected.Failures);

        _logger.LogInformation(
            "State bundle collected {SourceCount} sources and {FailureCount} failures from branch {Branch}",
            collected.Sources.Count, collected.Failures.Count, sourceBranch);

        return await _publisher
            .PublishAsync(new BundlePayload(collected.Files, manifest), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string?> CollectAsync(
        string hubRepoPath,
        string id,
        string sourceRelativePath,
        string bundleRelativePath,
        Collected collected,
        CancellationToken cancellationToken)
    {
        var absolute = Path.Combine(
            hubRepoPath,
            sourceRelativePath.Replace('/', Path.DirectorySeparatorChar));

        var read = await _files.ReadAsync(absolute, cancellationToken).ConfigureAwait(false);
        if (read is not SourceReadResult.Found found)
        {
            var reason = read switch
            {
                SourceReadResult.Missing => "missing",
                SourceReadResult.Unreadable u => u.Error,
                _ => throw new ArgumentException(
                    $"Unknown source read result variant: {read.GetType().Name}", nameof(id)),
            };

            collected.Failures.Add(new BundleFailure(id, reason));
            _logger.LogWarning(
                "State bundle source {Id} unavailable at {Path}: {Reason}", id, absolute, reason);
            return null;
        }

        var facts = await _gitFacts.GetFileFactsAsync(absolute, cancellationToken).ConfigureAwait(false);

        collected.Files.Add(new BundleFile(bundleRelativePath, found.Content));
        collected.Sources.Add(new BundleSource(
            Id: id,
            Path: sourceRelativePath,
            BundlePath: bundleRelativePath,
            Modified: found.LastModified,
            // A file source is confirmed current by the act of reading it, so the
            // two timestamps are the same value and the renderer needs no branch.
            Verified: found.LastModified,
            Committed: facts.LastCommitAuthorDate,
            Dirty: facts.Dirty));

        return found.Content;
    }

    private sealed class Collected
    {
        public List<BundleFile> Files { get; } = [];
        public List<BundleSource> Sources { get; } = [];
        public List<BundleFailure> Failures { get; } = [];
    }
}
