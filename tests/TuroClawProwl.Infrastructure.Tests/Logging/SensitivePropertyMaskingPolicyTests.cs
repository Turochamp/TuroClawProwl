using FluentAssertions;
using Serilog;
using Serilog.Events;
using TuroClawProwl.Infrastructure.Logging;
using TuroClawProwl.Infrastructure.Tests.TestSupport;

namespace TuroClawProwl.Infrastructure.Tests.Logging;

// TODO(QR-O1): static-analysis test over all ILogger call sites that
// rejects interpolated strings as the first argument ("user {$}" ...)
// so the masking policy can never be bypassed silently. Requires a
// Roslyn source scan (Microsoft.CodeAnalysis.CSharp). Deferred from
// the pilot; current defence is the call-site discipline documented in
// Raw_interpolated_strings_are_left_untouched_by_the_policy below.
public class SensitivePropertyMaskingPolicyTests
{
    private const string TokenSeed = "QR-S2-SENTINEL-6f7a9c1d3b8e";
    private const string PasswordSeed = "QR-S2-PW-a1b2c3d4e5f6";

    [Fact]
    public async Task Log_file_never_contains_the_raw_token_when_logged_via_destructured_object()
    {
        using var tmp = new TempDirectory();
        var logPath = Path.Combine(tmp.Path, "prowl-.log");

        using var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Destructure.With(new SensitivePropertyMaskingPolicy())
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        logger.Information("connecting with {@Creds}", new Credentials(TokenSeed, PasswordSeed));
        logger.Information("profile {@Profile}", new { ApiKey = TokenSeed, DisplayName = "Fox" });
        logger.Information("nested {@Outer}", new { Config = new { Token = TokenSeed } });

        await Task.Delay(50);
        logger.Dispose();

        var contents = await ReadAllLogsAsync(tmp.Path);
        contents.Should().NotContain(TokenSeed, "QR-S2: raw token must never appear in the log file");
        contents.Should().NotContain(PasswordSeed, "QR-S2: raw password must never appear in the log file");
        contents.Should().Contain("***", "the masking policy must actually emit the mask value");
    }

    [Fact]
    public async Task Non_sensitive_properties_are_preserved_verbatim()
    {
        using var tmp = new TempDirectory();
        var logPath = Path.Combine(tmp.Path, "prowl-.log");

        using var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Destructure.With(new SensitivePropertyMaskingPolicy())
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        logger.Information("profile {@Profile}", new { DisplayName = "Fox", Host = "Mac.local" });

        await Task.Delay(50);
        logger.Dispose();

        var contents = await ReadAllLogsAsync(tmp.Path);
        contents.Should().Contain("Fox");
        contents.Should().Contain("Mac.local");
    }

    [Fact]
    public async Task Raw_interpolated_strings_are_left_untouched_by_the_policy()
    {
        // This test documents a KNOWN limitation: interpolated strings bypass
        // the destructuring policy entirely. The mechanical QR-S2 guarantee
        // only holds when tokens are logged via {@Destructured} properties.
        using var tmp = new TempDirectory();
        var logPath = Path.Combine(tmp.Path, "prowl-.log");

        using var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Destructure.With(new SensitivePropertyMaskingPolicy())
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        // Deliberately anti-pattern: interpolated sensitive value, no property name.
        var rawLeak = "LEAK-VALUE-zzz-never-do-this";
        logger.Information($"raw {rawLeak}");

        await Task.Delay(50);
        logger.Dispose();

        var contents = await ReadAllLogsAsync(tmp.Path);
        contents.Should().Contain(rawLeak,
            "interpolation bypasses destructuring; discipline at call sites is required");
    }

    private static async Task<string> ReadAllLogsAsync(string directory)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var file in Directory.EnumerateFiles(directory, "*.log"))
        {
            sb.Append(await File.ReadAllTextAsync(file));
        }
        return sb.ToString();
    }

    private sealed record Credentials(string Token, string Password);
}
