using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace TuroClawProwl.Infrastructure.Logging;

// Drops log events whose (level, message template, SourceContext) tuple
// has been emitted more than once inside the dedup window. The first
// occurrence in a window always passes; suppressed events are silently
// dropped (no "N suppressed" summary — keeps the file sink simple).
//
// Targets the historical pattern in prowl-*.log where a single repeated
// warning could account for 100+ lines per day (e.g. the gitignore
// warning before TodaySyncer learned to suppress those paths). Healthy
// signal-to-noise matters more than perfect fidelity for repeated
// failures we already know about.
public sealed class RepeatedMessageDeduplicator : ILogEventFilter
{
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastEmittedAt = new();

    public RepeatedMessageDeduplicator(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window), "Window must be positive.");
        _window = window;
    }

    public bool IsEnabled(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        // Don't dedup errors or fatals — those usually warrant individual
        // attention even when repeated. Information / Debug / Warning are
        // the noisy levels in this app.
        if (logEvent.Level >= LogEventLevel.Error) return true;

        var sourceContext = logEvent.Properties.TryGetValue("SourceContext", out var ctx)
            ? ctx.ToString()
            : "<none>";
        var key = $"{logEvent.Level}|{sourceContext}|{logEvent.MessageTemplate.Text}";

        var now = logEvent.Timestamp;
        var last = _lastEmittedAt.AddOrUpdate(
            key,
            _ => now,
            (_, existing) => now - existing >= _window ? now : existing);

        // If the AddOrUpdate left the existing timestamp in place, this is
        // a duplicate inside the window and should be dropped.
        return last == now;
    }
}
