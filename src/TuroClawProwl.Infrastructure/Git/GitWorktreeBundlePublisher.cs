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
        var head = await RunAsync(_sourceRepoPath, cancellationToken, "rev-parse", "--abbrev-ref", "HEAD")
            .ConfigureAwait(false);
        return head.ExitCode == 0 ? head.StdOut.Trim() : "unknown";
    }

    public async Task<BundlePublishResult> PublishAsync(
        BundlePayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

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
        var commit = await RunAsync(_worktreePath, cancellationToken, "commit", "-m", "auto: state bundle " + stamp)
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

    private async Task<BundlePublishResult?> EnsureWorktreeAsync(CancellationToken cancellationToken)
    {
        if (Directory.Exists(_worktreePath))
        {
            var inside = await RunAsync(_worktreePath, cancellationToken, "rev-parse", "--is-inside-work-tree")
                .ConfigureAwait(false);
            if (inside.ExitCode == 0 && inside.StdOut.Trim() == "true")
                return null;

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
