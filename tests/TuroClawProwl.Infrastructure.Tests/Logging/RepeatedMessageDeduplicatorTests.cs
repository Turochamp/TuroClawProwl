using FluentAssertions;
using Serilog.Events;
using Serilog.Parsing;
using TuroClawProwl.Infrastructure.Logging;

namespace TuroClawProwl.Infrastructure.Tests.Logging;

public class RepeatedMessageDeduplicatorTests
{
    private static readonly MessageTemplateParser TemplateParser = new();

    [Fact]
    public void First_event_for_template_passes()
    {
        var dedup = new RepeatedMessageDeduplicator(TimeSpan.FromMinutes(5));
        var ev = MakeEvent(DateTimeOffset.UtcNow, LogEventLevel.Warning, "ignored {Path}");

        dedup.IsEnabled(ev).Should().BeTrue();
    }

    [Fact]
    public void Duplicate_inside_window_is_dropped()
    {
        var dedup = new RepeatedMessageDeduplicator(TimeSpan.FromMinutes(5));
        var t0 = DateTimeOffset.UtcNow;

        dedup.IsEnabled(MakeEvent(t0, LogEventLevel.Warning, "ignored {Path}")).Should().BeTrue();
        dedup.IsEnabled(MakeEvent(t0.AddSeconds(30), LogEventLevel.Warning, "ignored {Path}"))
            .Should().BeFalse();
    }

    [Fact]
    public void Duplicate_after_window_passes_again()
    {
        var dedup = new RepeatedMessageDeduplicator(TimeSpan.FromMinutes(5));
        var t0 = DateTimeOffset.UtcNow;

        dedup.IsEnabled(MakeEvent(t0, LogEventLevel.Warning, "ignored {Path}")).Should().BeTrue();
        dedup.IsEnabled(MakeEvent(t0.AddMinutes(6), LogEventLevel.Warning, "ignored {Path}"))
            .Should().BeTrue();
    }

    [Fact]
    public void Errors_are_never_deduped()
    {
        var dedup = new RepeatedMessageDeduplicator(TimeSpan.FromMinutes(5));
        var t0 = DateTimeOffset.UtcNow;

        dedup.IsEnabled(MakeEvent(t0, LogEventLevel.Error, "boom")).Should().BeTrue();
        dedup.IsEnabled(MakeEvent(t0.AddSeconds(1), LogEventLevel.Error, "boom")).Should().BeTrue();
    }

    [Fact]
    public void Different_levels_are_not_collapsed()
    {
        var dedup = new RepeatedMessageDeduplicator(TimeSpan.FromMinutes(5));
        var t0 = DateTimeOffset.UtcNow;

        dedup.IsEnabled(MakeEvent(t0, LogEventLevel.Warning, "thing {X}")).Should().BeTrue();
        dedup.IsEnabled(MakeEvent(t0, LogEventLevel.Information, "thing {X}")).Should().BeTrue();
    }

    [Fact]
    public void Different_source_contexts_are_not_collapsed()
    {
        var dedup = new RepeatedMessageDeduplicator(TimeSpan.FromMinutes(5));
        var t0 = DateTimeOffset.UtcNow;

        dedup.IsEnabled(MakeEvent(t0, LogEventLevel.Warning, "ignored {Path}", sourceContext: "A"))
            .Should().BeTrue();
        dedup.IsEnabled(MakeEvent(t0, LogEventLevel.Warning, "ignored {Path}", sourceContext: "B"))
            .Should().BeTrue();
    }

    private static LogEvent MakeEvent(
        DateTimeOffset timestamp,
        LogEventLevel level,
        string template,
        string? sourceContext = null)
    {
        var parsed = TemplateParser.Parse(template);
        var properties = new List<LogEventProperty>();
        if (sourceContext is not null)
            properties.Add(new LogEventProperty("SourceContext", new ScalarValue(sourceContext)));

        return new LogEvent(timestamp, level, exception: null, parsed, properties);
    }
}
