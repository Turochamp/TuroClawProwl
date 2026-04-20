using TuroClawProwl.Application.Ports;
using TimerCallback = System.Threading.TimerCallback;
using SystemTimer = System.Threading.Timer;

namespace TuroClawProwl.Infrastructure.FileSystem;

public sealed class FileSystemRepoWatcher : IRepoWatcher
{
    private static readonly TimeSpan PeriodicRescanInterval = TimeSpan.FromSeconds(60);

    private FileSystemWatcher? _watcher;
    private SystemTimer? _timer;
    private bool _disposed;

    public event EventHandler<RepoWatcherEventArgs>? Changed;

    public void Start(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        Stop();

        _watcher = new FileSystemWatcher(rootPath)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 65536,
            EnableRaisingEvents = false,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size,
        };

        _watcher.Changed += OnFileSystemChanged;
        _watcher.Created += OnFileSystemChanged;
        _watcher.Deleted += OnFileSystemChanged;
        _watcher.Renamed += OnFileSystemChanged;
        _watcher.Error += OnWatcherError;

        _watcher.EnableRaisingEvents = true;

        _timer = new SystemTimer(OnPeriodicTick, state: null,
            dueTime: PeriodicRescanInterval,
            period: PeriodicRescanInterval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileSystemChanged;
            _watcher.Created -= OnFileSystemChanged;
            _watcher.Deleted -= OnFileSystemChanged;
            _watcher.Renamed -= OnFileSystemChanged;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsRelevant(e.FullPath)) return;
        Raise($"fs:{e.ChangeType}:{e.Name}");
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Raise($"watcher-error:{e.GetException().GetType().Name}");
    }

    private void OnPeriodicTick(object? state)
    {
        Raise("periodic-rescan");
    }

    private void Raise(string reason)
    {
        Changed?.Invoke(this, new RepoWatcherEventArgs { Reason = reason });
    }

    private static bool IsRelevant(string fullPath)
    {
        var idx = fullPath.IndexOf(".git", StringComparison.Ordinal);
        if (idx < 0) return true;

        var afterGit = fullPath.AsSpan(idx + 4);
        if (afterGit.IsEmpty || afterGit[0] != Path.DirectorySeparatorChar && afterGit[0] != '/')
            return true;

        var remainder = afterGit[1..];
        return remainder.StartsWith("HEAD", StringComparison.Ordinal)
            || remainder.StartsWith("refs", StringComparison.Ordinal)
            || remainder.StartsWith("index", StringComparison.Ordinal);
    }
}
