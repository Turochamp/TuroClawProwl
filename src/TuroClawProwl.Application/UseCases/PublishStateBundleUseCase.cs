using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.UseCases;

public sealed class PublishStateBundleUseCase
{
    private readonly ISourceFileReader _files;
    private readonly IGoogleWorkspaceReader _google;
    private readonly ISnapshotStore _snapshots;
    private readonly IBundlePublisher _publisher;
    private readonly IClock _clock;
    private readonly ILogger<PublishStateBundleUseCase> _logger;

    private DateTimeOffset? _lastCommittedPublishAt;

    public PublishStateBundleUseCase(
        ISourceFileReader files,
        IGoogleWorkspaceReader google,
        ISnapshotStore snapshots,
        IBundlePublisher publisher,
        IClock clock,
        ILogger<PublishStateBundleUseCase>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(google);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(clock);

        _files = files;
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
        // The calendar window is a local-day window, so resolve the day locally.
        var localNow = now.ToLocalTime();

        var sourceBranch = await _publisher.GetSourceBranchAsync(cancellationToken).ConfigureAwait(false);

        var collected = new Collected();

        foreach (var list in GoogleSnapshotPlan.TaskLists())
        {
            var read = await _google.ReadTaskListAsync(list.ListId, cancellationToken)
                .ConfigureAwait(false);
            await CollectSnapshotAsync(
                list.Id, GoogleSnapshotPlan.TaskListApiPath(list.ListId), list.BundlePath,
                read, now, collected, cancellationToken).ConfigureAwait(false);
        }

        var window = GoogleSnapshotPlan.WindowFor(localNow);
        var calendarRead = await ReadCalendarWindowAsync(request.HubRepoPath, window, cancellationToken)
            .ConfigureAwait(false);
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

    // The registry is not published; it only names the calendars to read. Without
    // it the calendar set is unknown, and reading none would publish an empty
    // window stamped as freshly verified, so the window fails and is retained.
    private async Task<GoogleReadResult> ReadCalendarWindowAsync(
        string hubRepoPath,
        CalendarWindow window,
        CancellationToken cancellationToken)
    {
        var registryPath = Path.Combine(
            hubRepoPath,
            BundleLayout.RegistryRelativePath.Replace('/', Path.DirectorySeparatorChar));

        var read = await _files.ReadAsync(registryPath, cancellationToken).ConfigureAwait(false);
        if (read is SourceReadResult.Found found)
        {
            var calendars = RegistryParser.ParseCalendars(found.Content);
            return await _google.ReadCalendarWindowAsync(calendars, window, cancellationToken)
                .ConfigureAwait(false);
        }

        var reason = read switch
        {
            SourceReadResult.Missing => "missing",
            SourceReadResult.Unreadable u => u.Error,
            _ => throw new ArgumentException(
                $"Unknown source read result variant: {read.GetType().Name}", nameof(hubRepoPath)),
        };

        _logger.LogWarning(
            "Hub registry unavailable at {Path}; calendar window not read: {Reason}", registryPath, reason);
        return new GoogleReadResult.Failure("hub registry unavailable: " + reason);
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
            var previousRead = await _snapshots.GetAsync(id, cancellationToken).ConfigureAwait(false);

            // An unreadable snapshot must never be treated as "no previous
            // snapshot": that would let the content timestamp reset to `now`
            // and claim a byte-identical re-read as newly changed content, the
            // one place `modified` and `verified` could otherwise collapse.
            // There is nothing safe to compare against or retain here, so this
            // read is dropped for the cycle rather than guessed at.
            if (previousRead is SnapshotReadResult.Unreadable unreadable)
            {
                collected.Failures.Add(new BundleFailure(id, $"snapshot store unreadable: {unreadable.Reason}"));
                _logger.LogWarning(
                    "Snapshot {Id} previous content could not be read; skipping this cycle rather than " +
                    "claiming the new read is freshly changed content: {Reason}", id, unreadable.Reason);
                return;
            }

            var previous = previousRead is SnapshotReadResult.Found found ? found.Snapshot : null;

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

        var previousRead = await _snapshots.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (previousRead is not SnapshotReadResult.Found found)
        {
            var storeDetail = previousRead is SnapshotReadResult.Unreadable unreadable
                ? $"; previous snapshot unreadable: {unreadable.Reason}"
                : string.Empty;
            _logger.LogWarning(
                "Snapshot {Id} failed with no previous snapshot to retain: {Reason}{StoreDetail}",
                id, reason, storeDetail);
            return;
        }

        var previous = found.Snapshot;

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
