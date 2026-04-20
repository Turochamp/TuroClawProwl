namespace TuroClawProwl.Application.Ports;

public interface IRepoWatcher : IDisposable
{
    event EventHandler<RepoWatcherEventArgs>? Changed;

    void Start(string rootPath);

    void Stop();
}

public sealed class RepoWatcherEventArgs : EventArgs
{
    public required string Reason { get; init; }
}
