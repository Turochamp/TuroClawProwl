namespace TuroClawProwl.App;

public interface IUiDispatcher
{
    Task PostAsync(Action work, CancellationToken cancellationToken = default);
}

public sealed class WinFormsUiDispatcher : IUiDispatcher
{
    private readonly SynchronizationContext _uiContext;

    public WinFormsUiDispatcher(SynchronizationContext uiContext)
    {
        ArgumentNullException.ThrowIfNull(uiContext);
        _uiContext = uiContext;
    }

    public Task PostAsync(Action work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiContext.Post(_ =>
        {
            try
            {
                work();
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, state: null);
        return tcs.Task.WaitAsync(cancellationToken);
    }
}
