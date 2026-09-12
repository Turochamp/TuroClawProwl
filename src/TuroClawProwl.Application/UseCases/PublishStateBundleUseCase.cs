using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.UseCases;

public sealed class PublishStateBundleUseCase
{
    private readonly ISourceFileReader _files;
    private readonly IGitFileFactsReader _gitFacts;
    private readonly IGoogleWorkspaceReader _google;
    private readonly ISnapshotStore _snapshots;
    private readonly IBundlePublisher _publisher;
    private readonly IClock _clock;
    private readonly ILogger<PublishStateBundleUseCase> _logger;

    private DateTimeOffset? _lastCommittedPublishAt;

    public PublishStateBundleUseCase(
        ISourceFileReader files,
        IGitFileFactsReader gitFacts,
        IGoogleWorkspaceReader google,
        ISnapshotStore snapshots,
        IBundlePublisher publisher,
        IClock clock,
        ILogger<PublishStateBundleUseCase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(gitFacts);
        ArgumentNullException.ThrowIfNull(google);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(clock);

        _files = files;
        _gitFacts = gitFacts;
        _google = google;
        _snapshots = snapshots;
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

        var calendars = registry is null
            ? Array.Empty<RegistryCalendar>()
            : RegistryParser.ParseCalendars(registry);

        foreach (var list in GoogleSnapshotPlan.TaskLists())
        {
            var read = await _google.ReadTaskListAsync(list.ListId, cancellationToken)
                .ConfigureAwait(false);
            await CollectSnapshotAsync(
                list.Id, GoogleSnapshotPlan.TaskListApiPath(list.ListId), list.BundlePath,
                read, now, collected, cancellationToken).ConfigureAwait(false);
        }

        var window = GoogleSnapshotPlan.WindowFor(localNow);
        var calendarRead = await _google
            .ReadCalendarWindowAsync(calendars, window, cancellationToken).ConfigureAwait(false);
        await CollectSnapshotAsync(
            GoogleSnapshotPlan.CalendarWindowSourceId,
            GoogleSnapshotPlan.CalendarWindowApiPath,
            GoogleSnapshotPlan.CalendarWindowBundlePath,
            calendarRead, now, collected, cancellationToken).ConfigureAwait(false);

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

        // No previous committed publish means the verified timestamps are not on
        // record at all yet, so the first publish of a session is a heartbeat.
        var heartbeatDue =
            _lastCommittedPublishAt is not { } last ||
            now - last >= request.HeartbeatInterval;

        var published = await _publisher
            .PublishAsync(
                new BundlePayload(collected.Files, manifest, heartbeatDue), cancellationToken)
            .ConfigureAwait(false);

        // Only an actual commit puts the timestamps on record, so only that
        // resets the window. A no-op or a failure leaves the heartbeat due.
        if (published is BundlePublishResult.Success { FileCount: > 0 })
            _lastCommittedPublishAt = now;

        // A snapshot misconfiguration must not abort the bundle, but Michael
        // still has to be told which setting is wrong, so it is reported once
        // the reachable sources are safely published.
        if (published is BundlePublishResult.Success &&
            collected.SnapshotMisconfiguration is { } snapshotFault)
        {
            return new BundlePublishResult.Misconfigured(
                snapshotFault.SettingName, snapshotFault.Detail);
        }

        return published;
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

    private async Task CollectSnapshotAsync(
        string id,
        string apiPath,
        string bundlePath,
        GoogleReadResult read,
        DateTimeOffset now,
        Collected collected,
        CancellationToken cancellationToken)
    {
        if (read is GoogleReadResult.Success success)
        {
            var previous = await _snapshots.GetAsync(id, cancellationToken).ConfigureAwait(false);

            // A byte-identical re-read keeps the content timestamp and advances
            // only the verification timestamp: the data is the same age, but it
            // has just been confirmed current.
            var unchanged = previous is not null &&
                string.Equals(previous.Content, success.Json, StringComparison.Ordinal);
            var contentReadAt = unchanged ? previous!.ContentReadAt : now;

            await _snapshots
                .SaveAsync(new StoredSnapshot(id, success.Json, contentReadAt, now), cancellationToken)
                .ConfigureAwait(false);

            collected.Files.Add(new BundleFile(bundlePath, success.Json));
            collected.Sources.Add(new BundleSource(
                Id: id,
                Path: apiPath,
                BundlePath: bundlePath,
                Modified: contentReadAt,
                Verified: now,
                Committed: null,
                Dirty: false));
            return;
        }

        await RetainSnapshotAsync(id, apiPath, bundlePath, read, collected, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RetainSnapshotAsync(
        string id,
        string apiPath,
        string bundlePath,
        GoogleReadResult read,
        Collected collected,
        CancellationToken cancellationToken)
    {
        var reason = ReasonFor(read);
        collected.Failures.Add(new BundleFailure(id, reason));

        if (read is GoogleReadResult.Misconfigured misconfigured)
            collected.SnapshotMisconfiguration ??= misconfigured;

        var previous = await _snapshots.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (previous is null)
        {
            _logger.LogWarning(
                "Snapshot {Id} failed with no previous snapshot to retain: {Reason}", id, reason);
            return;
        }

        // The retained snapshot keeps BOTH original timestamps, and the store is
        // not rewritten. Advancing either would let a failed read look like a
        // successful one, which is the failure this pipeline exists to remove.
        collected.Files.Add(new BundleFile(bundlePath, previous.Content));
        collected.Sources.Add(new BundleSource(
            Id: id,
            Path: apiPath,
            BundlePath: bundlePath,
            Modified: previous.ContentReadAt,
            Verified: previous.VerifiedAt,
            Committed: null,
            Dirty: false));

        _logger.LogWarning(
            "Snapshot {Id} failed; retaining content from {ContentReadAt} last verified {VerifiedAt}: {Reason}",
            id, previous.ContentReadAt, previous.VerifiedAt, reason);
    }

    private static string ReasonFor(GoogleReadResult read) => read switch
    {
        GoogleReadResult.NotAuthenticated n => "not authenticated: " + n.Detail,
        GoogleReadResult.Misconfigured m => $"misconfigured ({m.SettingName}): {m.Detail}",
        GoogleReadResult.Failure f => f.Detail,
        GoogleReadResult.Success => throw new ArgumentException(
            "A successful read is not a retention case", nameof(read)),
        _ => throw new ArgumentException(
            $"Unknown Google read result variant: {read.GetType().Name}", nameof(read)),
    };

    private sealed class Collected
    {
        public List<BundleFile> Files { get; } = [];
        public List<BundleSource> Sources { get; } = [];
        public List<BundleFailure> Failures { get; } = [];
        public GoogleReadResult.Misconfigured? SnapshotMisconfiguration { get; set; }
    }
}
