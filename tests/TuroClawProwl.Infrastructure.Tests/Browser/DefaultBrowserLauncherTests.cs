using FluentAssertions;
using TuroClawProwl.Infrastructure.Browser;

namespace TuroClawProwl.Infrastructure.Tests.Browser;

public class DefaultBrowserLauncherTests
{
    [Fact]
    public void Build_start_info_uses_shell_execute_so_os_can_resolve_default_browser()
    {
        var psi = DefaultBrowserLauncher.BuildStartInfo(
            new Uri("http://localhost:18790/#token=abc"));

        psi.UseShellExecute.Should().BeTrue();
        psi.FileName.Should().Be("http://localhost:18790/#token=abc");
    }

    [Fact]
    public async Task Open_async_rejects_null_url()
    {
        var launcher = new DefaultBrowserLauncher();
        Func<Task> act = () => launcher.OpenAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
