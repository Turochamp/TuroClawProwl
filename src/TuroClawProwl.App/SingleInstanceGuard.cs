using System.IO.Pipes;
using System.Text;

namespace TuroClawProwl.App;

internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Global\TuroClawProwl.SingleInstance.v1";
    private const string PipeName = "TuroClawProwl.SingleInstance.v1";
    private const string ShowSettingsSignal = "show-settings";

    private readonly Mutex _mutex;
    private CancellationTokenSource? _serverCts;
    private Task? _serverTask;

    public bool IsPrimary { get; }

    public event EventHandler? ShowSettingsRequested;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, name: MutexName, createdNew: out var createdNew);
        IsPrimary = createdNew;
    }

    public void StartListening()
    {
        if (!IsPrimary) return;

        _serverCts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ServerLoopAsync(_serverCts.Token));
    }

    public static void SignalExistingInstanceToShowSettings()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 2000);
            var payload = Encoding.UTF8.GetBytes(ShowSettingsSignal);
            client.Write(payload, 0, payload.Length);
        }
        catch (TimeoutException) { }
        catch (IOException) { }
    }

    private async Task ServerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, maxNumberOfServerInstances: 1);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                var buffer = new byte[64];
                var read = await server.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                var message = Encoding.UTF8.GetString(buffer, 0, read);
                if (message == ShowSettingsSignal)
                    ShowSettingsRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { }
        }
    }

    public void Dispose()
    {
        _serverCts?.Cancel();
        try { _serverTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _serverCts?.Dispose();

        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }
}
