using System.Diagnostics;
using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Infrastructure.Browser;

public sealed class DefaultBrowserLauncher : IBrowserLauncher
{
    public Task OpenAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        var psi = BuildStartInfo(url);
        System.Diagnostics.Process.Start(psi);
        return Task.CompletedTask;
    }

    internal static ProcessStartInfo BuildStartInfo(Uri url) =>
        new(url.AbsoluteUri) { UseShellExecute = true };
}
