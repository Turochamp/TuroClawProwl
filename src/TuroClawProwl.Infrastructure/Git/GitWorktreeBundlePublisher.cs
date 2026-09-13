using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Domain;
using TuroClawProwl.Infrastructure.Process;

namespace TuroClawProwl.Infrastructure.Git;

// Publishing goes through a dedicated DETACHED worktree pinned to the publish
// branch's remote tip, and pushes with an explicit refspec. Detaching is what
// lets this work while the publish branch is also checked out in the main tree,
// and the explicit refspec is what makes the user's own upstream irrelevant.
public sealed class GitWorktreeBundlePublisher : IBundlePublisher
{
    public const string SourceRepoSetting = "hubRepoPath";
    public const string WorktreeSetting = "bundleWorktreePath";
    public const string BranchSetting = "bundlePublishBranch";
    public const string GitExecutableSetting = "gitExecutablePath";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    private readonly string _sourceRepoPath;
    private readonly string _worktreePath;
    private readonly string _publishBranch;
    private readonly string _gitExecutable;
    private readonly ILogger<GitWorktreeBundlePublisher> _logger;

    public GitWorktreeBundlePublisher(
        string sourceRepoPath,
        string worktreePath,
        string publishBranch,
        string gitExecutable = "git",
        ILogger<GitWorktreeBundlePublisher>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRepoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(publishBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitExecutable);

        _sourceRepoPath = sourceRepoPath;
        _worktreePath = worktreePath;
        _publishBranch = publishBranch;
        _gitExecutable = gitExecutable;
        _logger = logger ?? NullLogger<GitWorktreeBundlePublisher>.Instance;
    }

    public async Task<string> GetSourceBranchAsync(CancellationToken cancellationToken = default)
    {
        ProcessRunner.Result head;
        try
        {
            head = await RunAsync(_sourceRepoPath, cancellationToken, "rev-parse", "--abbrev-ref", "HEAD")
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Win32Exception or System.IO.FileNotFoundException)
        {
            // Swallowed the same way a non-zero exit already is: the source branch
            // is cosmetic manifest content, not the outcome of the publish. The
            // authoritative classification of a missing git executable happens in
            // PublishAsync, which is what runs next and can actually report
            // Misconfigured.
            return "unknown";
        }

        return head.ExitCode == 0 ? head.StdOut.Trim() : "unknown";
    }

    public async Task<BundlePublishResult> PublishAsync(
        BundlePayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            var prepared = await EnsureWorktreeAsync(cancellationToken).ConfigureAwait(false);
            if (prepared is not null) return prepared;

            var pinned = await PinToPublishTipAsync(cancellationToken).ConfigureAwait(false);
            if (pinned is not null) return pinned;

            // The source files are written and compared BEFORE the manifest, because
            // published_at and the verified timestamps move on every run: comparing
            // with the manifest in place would make an hourly snapshot refresh commit
            // even when nothing changed. A manifest-only commit is allowed, but only
            // when the caller asks for one via PublishEvenIfUnchanged.
            await WriteBundleSourcesAsync(payload, cancellationToken).ConfigureAwait(false);

            var add = await RunAsync(_worktreePath, cancellationToken,
                "add", "--all", "--", BundleLayout.BundleDirectory).ConfigureAwait(false);
            if (add.ExitCode != 0)
                return new BundlePublishResult.Transient(Error(add));

            var staged = await RunAsync(_worktreePath, cancellationToken, "diff", "--cached", "--quiet")
                .ConfigureAwait(false);
            var sourcesUnchanged = staged.ExitCode == 0;

            if (sourcesUnchanged && !payload.PublishEvenIfUnchanged)
            {
                var unchangedHead = await RunAsync(_worktreePath, cancellationToken, "rev-parse", "HEAD")
                    .ConfigureAwait(false);
                _logger.LogInformation("State bundle unchanged; nothing to publish");
                return new BundlePublishResult.Success(unchangedHead.StdOut.Trim(), 0);
            }

            if (sourcesUnchanged)
            {
                _logger.LogInformation(
                    "State bundle content unchanged; publishing a heartbeat so the verified timestamps stay on record");
            }

            await WriteManifestAsync(payload, cancellationToken).ConfigureAwait(false);

            var addManifest = await RunAsync(_worktreePath, cancellationToken,
                "add", "--all", "--", BundleLayout.BundleDirectory).ConfigureAwait(false);
            if (addManifest.ExitCode != 0)
                return new BundlePublishResult.Transient(Error(addManifest));

            var stamp = payload.Manifest.PublishedAt.ToString(
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            var commit = await RunAsync(
                    _worktreePath, cancellationToken, "commit", "-m", "auto: state bundle " + stamp)
                .ConfigureAwait(false);
            if (commit.ExitCode != 0)
                return new BundlePublishResult.Transient(Error(commit));

            var sha = await RunAsync(_worktreePath, cancellationToken, "rev-parse", "HEAD").ConfigureAwait(false);

            var push = await RunAsync(_worktreePath, cancellationToken,
                "push", "origin", $"HEAD:refs/heads/{_publishBranch}").ConfigureAwait(false);
            if (push.ExitCode != 0)
            {
                var error = Error(push);
                return LooksLikeMissingRemote(error)
                    ? new BundlePublishResult.Misconfigured(SourceRepoSetting, error)
                    : new BundlePublishResult.Transient(error);
            }

            var fileCount = payload.Files.Count + 1;
            _logger.LogInformation(
                "State bundle published to {Branch} as {Sha} with {FileCount} files",
                _publishBranch, sha.StdOut.Trim(), fileCount);

            return new BundlePublishResult.Success(sha.StdOut.Trim(), fileCount);
        }
        catch (Exception ex) when (ex is Win32Exception or System.IO.FileNotFoundException)
        {
            return new BundlePublishResult.Misconfigured(
                GitExecutableSetting, $"{_gitExecutable} could not be started: {ex.Message}");
        }
    }

    private async Task<BundlePublishResult?> EnsureWorktreeAsync(CancellationToken cancellationToken)
    {
        if (Directory.Exists(_worktreePath))
        {
            var inside = await RunAsync(_worktreePath, cancellationToken, "rev-parse", "--is-inside-work-tree")
                .ConfigureAwait(false);
            if (inside.ExitCode == 0 && inside.StdOut.Trim() == "true")
            {
                // PinToPublishTipAsync fetches, hard-resets and cleans whatever is
                // checked out at _worktreePath, so adopting a working tree that isn't
                // this publisher's own would discard someone else's commits and
                // uncommitted files. Refuse without touching anything.
                var ownership = await DetermineWorktreeOwnershipAsync(cancellationToken).ConfigureAwait(false);
                switch (ownership)
                {
                    case WorktreeOwnership.Owned:
                        return null;

                    case WorktreeOwnership.MissingOrInvalidMarker:
                        // Structurally a real, registered linked worktree of this exact
                        // source repo -- but this publisher never marked it as its own
                        // (or the marker is empty/unreadable/for a different repo). That
                        // second case is exactly what this guard exists to catch: a
                        // worktree the USER created themselves, so leading with "delete
                        // this worktree" would be destructive advice most of the time it
                        // fires. Lead with the safe remedy -- point the setting elsewhere
                        // -- and mention deletion second, for the narrower case where this
                        // publisher made the worktree but its own marker was lost.
                        return new BundlePublishResult.Misconfigured(
                            WorktreeSetting,
                            $"{_worktreePath} is a linked worktree of {_sourceRepoPath}, but is not " +
                            "recognized as this publisher's own (its ownership marker is missing, " +
                            "unreadable, empty, or names a different repository); point " +
                            "bundleWorktreePath at a different, unused directory, or delete this " +
                            "worktree if this publisher created it");

                    default:
                        return new BundlePublishResult.Misconfigured(
                            WorktreeSetting,
                            $"{_worktreePath} is an existing git working tree that does not belong to this " +
                            "publisher (it is not a worktree of " + _sourceRepoPath +
                            "); point it at an unused directory");
                }
            }

            await RunAsync(_sourceRepoPath, cancellationToken, "worktree", "prune").ConfigureAwait(false);

            var leftovers = Directory
                .EnumerateFileSystemEntries(_worktreePath)
                .Where(e => !string.Equals(Path.GetFileName(e), ".git", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (leftovers.Length > 0)
            {
                return new BundlePublishResult.Misconfigured(
                    WorktreeSetting,
                    $"{_worktreePath} exists but is not a git worktree; point it at an unused directory");
            }

            Directory.Delete(_worktreePath, recursive: true);
        }

        var parent = Path.GetDirectoryName(_worktreePath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        var add = await RunAsync(_sourceRepoPath, cancellationToken,
            "worktree", "add", "--detach", _worktreePath, _publishBranch).ConfigureAwait(false);
        if (add.ExitCode == 0)
        {
            var markerFailure = await WriteOwnershipMarkerAsync(cancellationToken).ConfigureAwait(false);
            if (markerFailure is not null)
                return markerFailure;

            _logger.LogInformation(
                "Created the bundle worktree at {Worktree} detached at {Branch}",
                _worktreePath, _publishBranch);
            return null;
        }

        var error = Error(add);

        if (error.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
        {
            return new BundlePublishResult.Misconfigured(
                SourceRepoSetting, $"{_sourceRepoPath} is not a git repository: {error}");
        }

        if (error.Contains("invalid reference", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("not a valid object name", StringComparison.OrdinalIgnoreCase))
        {
            return new BundlePublishResult.Misconfigured(
                BranchSetting, $"branch {_publishBranch} does not exist in {_sourceRepoPath}: {error}");
        }

        return new BundlePublishResult.Transient(error);
    }

    // The file that marks a linked worktree as one THIS publisher created, living
    // inside the worktree's own private git-dir (.git/worktrees/<name>/ for a linked
    // worktree) rather than its working-tree content. That placement matters: content
    // placed inside the working tree itself would be wiped by PinToPublishTipAsync's
    // `git clean -fd` on every publish after the first, and git never runs `clean`
    // against its own admin directories.
    // internal (not private) purely so tests can locate and corrupt this file to
    // exercise the classification paths below, without hardcoding a duplicate literal.
    internal const string OwnershipMarkerFileName = "turoclawprowl-bundle-publisher";

    private enum WorktreeOwnership
    {
        // Fails a structural check (wrong path, main working tree, not a worktree of
        // this source repo at all, etc.) -- nothing to do with the marker.
        NotThisPublishers,

        // Passes every structural check -- a genuine, registered linked worktree of
        // this exact source repo -- but its ownership marker is missing, empty,
        // unreadable, or names a different repository.
        MissingOrInvalidMarker,

        Owned,
    }

    // A directory can pass `rev-parse --is-inside-work-tree` for ANY git repository,
    // not just one this publisher created -- and that includes the source repo's own
    // main working tree (its git-dir and git-common-dir are the same path, and its
    // common-dir trivially resolves "inside itself"), and any OTHER linked worktree of
    // the source repo, made the same way (`worktree add --detach <path> <branch>`) by
    // the user for their own purposes rather than by this publisher. Git's own
    // bookkeeping cannot tell those two cases apart -- a user-made worktree at the
    // configured path is, structurally, indistinguishable from one this publisher
    // made, down to appearing identically in `worktree list --porcelain` (verified
    // directly: both pass toplevel, git-dir-vs-common-dir and list-membership
    // identically). Adopting either lets PinToPublishTipAsync fetch/reset
    // --hard/clean a working tree that isn't this publisher's to touch. Adoption
    // therefore requires ALL of:
    //   1. _worktreePath is not the source repo root itself.
    //   2. Its own toplevel is exactly _worktreePath (not an ancestor or unrelated checkout).
    //   3. It is a LINKED worktree (git-dir != git-common-dir), not a main working tree.
    //   4. Its git-common-dir resolves inside _sourceRepoPath.
    //   5. git itself lists _worktreePath as one of _sourceRepoPath's worktrees.
    //   6. It carries this publisher's own ownership marker for this exact source repo
    //      -- the one structural fact git cannot supply on its own.
    private async Task<WorktreeOwnership> DetermineWorktreeOwnershipAsync(CancellationToken cancellationToken)
    {
        if (PathsEqual(_worktreePath, _sourceRepoPath))
            return WorktreeOwnership.NotThisPublishers;

        var toplevel = await RunAsync(_worktreePath, cancellationToken, "rev-parse", "--show-toplevel")
            .ConfigureAwait(false);
        if (toplevel.ExitCode != 0 || !PathsEqual(toplevel.StdOut.Trim(), _worktreePath))
            return WorktreeOwnership.NotThisPublishers;

        var gitDir = await RunAsync(_worktreePath, cancellationToken, "rev-parse", "--git-dir")
            .ConfigureAwait(false);
        var commonDir = await RunAsync(_worktreePath, cancellationToken, "rev-parse", "--git-common-dir")
            .ConfigureAwait(false);
        if (gitDir.ExitCode != 0 || commonDir.ExitCode != 0)
            return WorktreeOwnership.NotThisPublishers;

        var gitDirFull = ResolveAgainstWorktree(gitDir.StdOut.Trim());
        var commonDirFull = ResolveAgainstWorktree(commonDir.StdOut.Trim());

        // A main working tree (the source repo's own root, or any other repo's root)
        // has git-dir == git-common-dir; only a linked worktree differs.
        if (PathsEqual(gitDirFull, commonDirFull))
            return WorktreeOwnership.NotThisPublishers;

        if (!IsWithin(commonDirFull, _sourceRepoPath))
            return WorktreeOwnership.NotThisPublishers;

        var list = await RunAsync(_sourceRepoPath, cancellationToken, "worktree", "list", "--porcelain")
            .ConfigureAwait(false);
        if (list.ExitCode != 0)
            return WorktreeOwnership.NotThisPublishers;

        if (!ParseWorktreeListPaths(list.StdOut).Any(path => PathsEqual(path, _worktreePath)))
            return WorktreeOwnership.NotThisPublishers;

        // Every structural check passed: this IS a genuine, registered linked worktree
        // of the source repo. Only the ownership marker can now say whether this
        // publisher made it. Missing, empty or unreadable (IOException /
        // UnauthorizedAccessException) are all classified the same actionable way
        // without throwing. The one path that is NOT covered: PathsEqual below calls
        // Path.GetFullPath on the marker's content, which throws ArgumentException for
        // a syntactically invalid path. That is reachable only if something has
        // written junk into this publisher's own private git-dir -- not a case a
        // normal user or a corrupt-but-well-formed marker can hit -- which is why the
        // code is left as-is rather than adding a third catch for it.
        var markerPath = Path.Combine(gitDirFull, OwnershipMarkerFileName);
        string marker;
        try
        {
            if (!File.Exists(markerPath))
                return WorktreeOwnership.MissingOrInvalidMarker;

            marker = await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return WorktreeOwnership.MissingOrInvalidMarker;
        }

        if (string.IsNullOrWhiteSpace(marker))
            return WorktreeOwnership.MissingOrInvalidMarker;

        return PathsEqual(marker.Trim(), _sourceRepoPath)
            ? WorktreeOwnership.Owned
            : WorktreeOwnership.MissingOrInvalidMarker;
    }

    // A publisher-created worktree must always carry its ownership marker -- one
    // without it would be a permanent dead end, since EnsureWorktreeAsync never
    // recreates an existing, otherwise-valid worktree; only a human deleting the
    // directory by hand could recover it. If the marker cannot be written, remove the
    // worktree just created rather than leave that trap behind, and surface the real
    // cause as a Transient failure -- this is local I/O, not a bad setting.
    private async Task<BundlePublishResult?> WriteOwnershipMarkerAsync(CancellationToken cancellationToken)
    {
        var gitDir = await RunAsync(_worktreePath, cancellationToken, "rev-parse", "--git-dir")
            .ConfigureAwait(false);
        if (gitDir.ExitCode != 0)
            return await RemoveFreshWorktreeAndFailAsync(Error(gitDir), cancellationToken).ConfigureAwait(false);

        var gitDirFull = ResolveAgainstWorktree(gitDir.StdOut.Trim());
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(gitDirFull, OwnershipMarkerFileName), _sourceRepoPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return await RemoveFreshWorktreeAndFailAsync(ex.Message, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<BundlePublishResult> RemoveFreshWorktreeAndFailAsync(
        string detail, CancellationToken cancellationToken)
    {
        await RunAsync(_sourceRepoPath, cancellationToken, "worktree", "remove", "--force", _worktreePath)
            .ConfigureAwait(false);
        return new BundlePublishResult.Transient(
            $"could not write the bundle worktree ownership marker for {_worktreePath}: {detail}");
    }

    private string ResolveAgainstWorktree(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(_worktreePath, path));

    private static IEnumerable<string> ParseWorktreeListPaths(string porcelain) =>
        porcelain
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("worktree ", StringComparison.Ordinal))
            .Select(line => line["worktree ".Length..].Trim());

    // Normalizes away git's forward-slash output, trailing separators and short/long
    // form differences so path comparisons are not fooled by cosmetic differences.
    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool PathsEqual(string a, string b) =>
        string.Equals(NormalizeDirectory(a), NormalizeDirectory(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsWithin(string candidate, string root)
    {
        var normalizedCandidate = NormalizeDirectory(candidate);
        var normalizedRoot = NormalizeDirectory(root);
        return normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<BundlePublishResult?> PinToPublishTipAsync(CancellationToken cancellationToken)
    {
        var fetch = await RunAsync(_worktreePath, cancellationToken, "fetch", "origin", _publishBranch)
            .ConfigureAwait(false);

        var pinTarget = "FETCH_HEAD";
        if (fetch.ExitCode != 0)
        {
            var error = Error(fetch);
            if (LooksLikeMissingRemote(error))
            {
                return new BundlePublishResult.Misconfigured(
                    SourceRepoSetting, $"no usable 'origin' remote for {_publishBranch}: {error}");
            }

            // Offline is not a reason to skip publishing; commit locally and let the push decide.
            _logger.LogWarning(
                "Bundle worktree could not fetch {Branch}; committing on the local tip: {Error}",
                _publishBranch, error);
            pinTarget = _publishBranch;
        }

        var reset = await RunAsync(_worktreePath, cancellationToken, "reset", "--hard", pinTarget)
            .ConfigureAwait(false);
        if (reset.ExitCode != 0)
            return new BundlePublishResult.Transient(Error(reset));

        await RunAsync(_worktreePath, cancellationToken, "clean", "-fd").ConfigureAwait(false);
        return null;
    }

    private async Task WriteBundleSourcesAsync(BundlePayload payload, CancellationToken cancellationToken)
    {
        var bundleRoot = BundleRoot();
        var manifestPath = Path.Combine(bundleRoot, BundleLayout.ManifestFileName);

        // Rewriting from empty is what makes a retired CCA's state disappear from
        // the bundle rather than linger forever -- but manifest.json is spared here.
        // Sources are compared against HEAD (via the staged diff in PublishAsync)
        // BEFORE the manifest is rewritten, so leaving the on-disk manifest exactly
        // as last committed is what keeps that diff limited to source content;
        // deleting it here would stage a manifest deletion on every publish and
        // defeat the whole no-commit-when-unchanged check. WriteManifestAsync
        // rewrites it afterward regardless.
        if (Directory.Exists(bundleRoot))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(bundleRoot))
            {
                if (string.Equals(entry, manifestPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Directory.Exists(entry))
                    Directory.Delete(entry, recursive: true);
                else
                    File.Delete(entry);
            }
        }
        else
        {
            Directory.CreateDirectory(bundleRoot);
        }

        foreach (var file in payload.Files)
        {
            var target = Path.Combine(
                bundleRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(target, file.Content, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteManifestAsync(BundlePayload payload, CancellationToken cancellationToken)
    {
        var manifestJson = JsonSerializer.Serialize(payload.Manifest, ManifestJsonOptions);
        await File.WriteAllTextAsync(
            Path.Combine(BundleRoot(), BundleLayout.ManifestFileName), manifestJson, cancellationToken)
            .ConfigureAwait(false);
    }

    private string BundleRoot() =>
        Path.Combine(
            _worktreePath,
            BundleLayout.BundleDirectory.Replace('/', Path.DirectorySeparatorChar));

    private Task<ProcessRunner.Result> RunAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] args) =>
        ProcessRunner.RunAsync(_gitExecutable, args, workingDirectory, cancellationToken);

    private static string Error(ProcessRunner.Result result) =>
        (string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr).Trim();

    private static bool LooksLikeMissingRemote(string error) =>
        error.Contains("does not appear to be a git repository", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("No such remote", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("'origin' does not appear", StringComparison.OrdinalIgnoreCase);
}
