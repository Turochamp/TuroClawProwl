using FluentAssertions;
using Microsoft.Win32;
using TuroClawProwl.Infrastructure.Autostart;

namespace TuroClawProwl.Infrastructure.Tests.Autostart;

public class RegistryAutostartManagerTests : IDisposable
{
    private readonly string _isolatedValueName =
        "TuroClawProwl.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void Is_enabled_returns_false_when_value_is_absent()
    {
        var manager = new RegistryAutostartManager(_isolatedValueName);

        manager.IsEnabled().Should().BeFalse();
    }

    [Fact]
    public void Enable_writes_run_key_entry_and_is_enabled_returns_true()
    {
        var manager = new RegistryAutostartManager(_isolatedValueName);

        manager.Enable(@"C:\Program Files\TuroClawProwl\TuroClawProwl.exe");

        manager.IsEnabled().Should().BeTrue();
    }

    [Fact]
    public void Enable_quotes_paths_that_contain_spaces()
    {
        var manager = new RegistryAutostartManager(_isolatedValueName);
        manager.Enable(@"C:\Program Files\TuroClawProwl\TuroClawProwl.exe");

        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var value = key!.GetValue(_isolatedValueName) as string;
        value.Should().StartWith("\"").And.EndWith("\"");
    }

    [Fact]
    public void Disable_removes_the_run_key_entry()
    {
        var manager = new RegistryAutostartManager(_isolatedValueName);
        manager.Enable(@"C:\x.exe");

        manager.Disable();

        manager.IsEnabled().Should().BeFalse();
    }

    [Fact]
    public void Disable_when_nothing_is_written_is_a_no_op()
    {
        var manager = new RegistryAutostartManager(_isolatedValueName);
        Action act = () => manager.Disable();
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_rejects_empty_value_name()
    {
        Action act = () => new RegistryAutostartManager("");
        act.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        key?.DeleteValue(_isolatedValueName, throwOnMissingValue: false);
    }
}
