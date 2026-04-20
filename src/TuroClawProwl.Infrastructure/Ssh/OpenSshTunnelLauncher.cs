using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Infrastructure.Ssh;

public sealed class OpenSshTunnelLauncher : ISshTunnelLauncher
{
    private static readonly TimeSpan DefaultReadyTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly string _sshExecutable;
    private readonly TimeSpan _readyTimeout;
    private readonly ILogger<OpenSshTunnelLauncher> _logger;

    public OpenSshTunnelLauncher(
        string sshExecutable = "ssh",
        TimeSpan? readyTimeout = null,
        ILogger<OpenSshTunnelLauncher>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sshExecutable);
        _sshExecutable = sshExecutable;
        _readyTimeout = readyTimeout ?? DefaultReadyTimeout;
        _logger = logger ?? NullLogger<OpenSshTunnelLauncher>.Instance;
    }

    public async Task<ISshTunnel> OpenAsync(
        SshTarget target,
        int localPort,
        string remoteBindHost,
        int remotePort,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteBindHost);
        if (localPort is <= 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(localPort));
        if (remotePort is <= 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(remotePort));

        var psi = BuildStartInfo(_sshExecutable, target, localPort, remoteBindHost, remotePort);

        var process = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Failed to start ssh.exe for tunnel");
        }

        _logger.LogInformation(
            "Started ssh tunnel (pid {Pid}) forwarding localhost:{LocalPort} -> {RemoteHost}:{RemotePort} via {User}@{Host}",
            process.Id, localPort, remoteBindHost, remotePort, target.User, target.Host);

        try
        {
            await WaitForListenerAsync(localPort, process, _readyTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            SafeKill(process);
            process.Dispose();
            throw;
        }

        return new SshTunnelHandle(process, localPort, _logger);
    }

    internal static ProcessStartInfo BuildStartInfo(
        string sshExecutable,
        SshTarget target,
        int localPort,
        string remoteBindHost,
        int remotePort)
    {
        var psi = new ProcessStartInfo(sshExecutable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add("-N");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("ExitOnForwardFailure=yes");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("ServerAliveInterval=30");
        psi.ArgumentList.Add("-L");
        psi.ArgumentList.Add($"{localPort}:{remoteBindHost}:{remotePort}");
        psi.ArgumentList.Add($"{target.User}@{target.Host}");

        return psi;
    }

    private static async Task WaitForListenerAsync(
        int port,
        System.Diagnostics.Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
                throw new InvalidOperationException($"ssh.exe exited before tunnel was ready (exit {process.ExitCode})");

            if (await TryConnectAsync(port, cancellationToken).ConfigureAwait(false))
                return;

            try
            {
                await Task.Delay(ReadyPollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                throw;
            }
        }

        throw new TimeoutException($"ssh tunnel on localhost:{port} did not become ready within {timeout}");
    }

    private static async Task<bool> TryConnectAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port, cancellationToken).ConfigureAwait(false);
            return client.Connected;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static void SafeKill(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (NotSupportedException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private sealed class SshTunnelHandle : ISshTunnel
    {
        private readonly System.Diagnostics.Process _process;
        private readonly ILogger _logger;
        private int _disposed;

        public SshTunnelHandle(System.Diagnostics.Process process, int localPort, ILogger logger)
        {
            _process = process;
            _logger = logger;
            LocalPort = localPort;
        }

        public int LocalPort { get; }

        public bool IsOpen => Volatile.Read(ref _disposed) == 0 && !_process.HasExited;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    try
                    {
                        await _process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { }
                }
            }
            catch (InvalidOperationException) { }
            catch (NotSupportedException) { }
            catch (System.ComponentModel.Win32Exception ex)
            {
                _logger.LogWarning(ex, "Failed to kill ssh tunnel process");
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
