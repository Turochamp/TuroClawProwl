namespace TuroClawProwl.Application.Ports;

public interface IConfigStore
{
    Task<TuroClawProwlConfig> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(TuroClawProwlConfig config, CancellationToken cancellationToken = default);

    // Raised after SaveAsync succeeds. Subscribers run synchronously on
    // whatever thread called SaveAsync; keep handlers fast or marshal as
    // needed. Used by LiveConfigApplier to re-apply settings without an
    // app restart.
    event EventHandler<TuroClawProwlConfig>? ConfigSaved;
}
