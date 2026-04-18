using Microsoft.Win32;
using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Infrastructure.Autostart;

public sealed class RegistryAutostartManager : IAutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string DefaultValueName = "TuroClawProwl";

    private readonly string _valueName;

    public RegistryAutostartManager(string valueName = DefaultValueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        _valueName = valueName;
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(_valueName) is string s && !string.IsNullOrWhiteSpace(s);
    }

    public void Enable(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException($"Unable to open HKCU\\{RunKeyPath}.");
        key.SetValue(_valueName, QuoteIfNeeded(executablePath), RegistryValueKind.String);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    private static string QuoteIfNeeded(string path) =>
        path.Contains(' ') && !path.StartsWith('"') ? $"\"{path}\"" : path;
}
