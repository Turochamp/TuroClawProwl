using System.Diagnostics;
using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Infrastructure.Terminal;

public sealed class WindowsTerminalLauncher : ITerminalLauncher
{
    private readonly string? _windowsTerminalPath;
    private readonly string _sshExecutable;

    public WindowsTerminalLauncher(string? windowsTerminalPath = null, string sshExecutable = "ssh")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sshExecutable);
        _windowsTerminalPath = windowsTerminalPath ?? ResolveWindowsTerminal();
        _sshExecutable = sshExecutable;
    }

    public Task LaunchSshInteractiveAsync(
        SshTarget target,
        string remoteCommand,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteCommand);

        var psi = BuildStartInfo(_windowsTerminalPath, _sshExecutable, target, remoteCommand);
        System.Diagnostics.Process.Start(psi);
        return Task.CompletedTask;
    }

    internal static ProcessStartInfo BuildStartInfo(
        string? windowsTerminalPath,
        string sshExecutable,
        SshTarget target,
        string remoteCommand)
    {
        if (!string.IsNullOrEmpty(windowsTerminalPath))
        {
            var psi = new ProcessStartInfo(windowsTerminalPath) { UseShellExecute = true };
            psi.ArgumentList.Add(sshExecutable);
            psi.ArgumentList.Add($"{target.User}@{target.Host}");
            psi.ArgumentList.Add(remoteCommand);
            return psi;
        }
        else
        {
            var psi = new ProcessStartInfo(sshExecutable) { UseShellExecute = true };
            psi.ArgumentList.Add($"{target.User}@{target.Host}");
            psi.ArgumentList.Add(remoteCommand);
            return psi;
        }
    }

    private static string? ResolveWindowsTerminal()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidate = Path.Combine(localAppData, "Microsoft", "WindowsApps", "wt.exe");
        return File.Exists(candidate) ? candidate : null;
    }
}
