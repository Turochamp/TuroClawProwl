using System.Text.Json;
using TuroClawProwl.Application;
using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Infrastructure.Configuration;

public sealed class JsonConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _configPath;

    public JsonConfigStore(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        _configPath = configPath;
    }

    public static string DefaultConfigPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TuroClawProwl",
            "config.json");

    public async Task<TuroClawProwlConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_configPath))
            return new TuroClawProwlConfig();

        string json;
        try
        {
            json = await File.ReadAllTextAsync(_configPath, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return new TuroClawProwlConfig();
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<TuroClawProwlConfig>(json, SerializerOptions);
            return loaded ?? new TuroClawProwlConfig();
        }
        catch (JsonException)
        {
            QuarantineCorruptFile();
            return new TuroClawProwlConfig();
        }
    }

    public async Task SaveAsync(TuroClawProwlConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(config, SerializerOptions);
        var tmpPath = _configPath + ".tmp";

        await File.WriteAllTextAsync(tmpPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tmpPath, _configPath, overwrite: true);
    }

    private void QuarantineCorruptFile()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var corrupt = _configPath + ".corrupt." + timestamp;
        try
        {
            File.Move(_configPath, corrupt, overwrite: false);
        }
        catch (IOException)
        {
            // best-effort; if rename fails we still return defaults
        }
    }
}
