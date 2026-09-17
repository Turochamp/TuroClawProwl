namespace TuroClawProwl.Domain;

public static class RegistryParser
{
    private const string ActiveSetHeading = "## Active set";
    private const int ExpectedColumnCount = 5;

    private static readonly string[] PublishedStatuses = ["active", "flagged"];

    public static IReadOnlyList<RegistryEntry> ParseActiveSet(string registryMarkdown)
    {
        ArgumentNullException.ThrowIfNull(registryMarkdown);

        var entries = new List<RegistryEntry>();
        var insideActiveSet = false;

        foreach (var rawLine in registryMarkdown.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("##", StringComparison.Ordinal))
            {
                insideActiveSet = line.StartsWith(ActiveSetHeading, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!insideActiveSet || !line.StartsWith('|')) continue;

            var cells = line.Trim('|').Split('|');
            if (cells.Length != ExpectedColumnCount) continue;

            var name = cells[0].Trim().Trim('`');
            var status = cells[3].Trim();
            var stateFile = cells[4].Trim().Trim('`').Replace('\\', '/');

            if (name.Length == 0 || stateFile.Length == 0) continue;
            if (!PublishedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)) continue;

            entries.Add(new RegistryEntry(name, status.ToLowerInvariant(), stateFile));
        }

        return entries;
    }

    private const string CalendarsHeading = "### Calendars";
    private const int CalendarColumnCount = 4;
    private const string ContextOnlyMarker = "context only";
    // A calendar the registry lists before it is readable would otherwise
    // surface as a read failure in every brief until access is granted.
    private const string PendingMarker = "pending";

    public static IReadOnlyList<RegistryCalendar> ParseCalendars(string registryMarkdown)
    {
        ArgumentNullException.ThrowIfNull(registryMarkdown);

        var calendars = new List<RegistryCalendar>();
        var insideCalendars = false;

        foreach (var rawLine in registryMarkdown.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                insideCalendars = line.StartsWith(CalendarsHeading, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!insideCalendars || !line.StartsWith('|')) continue;

            var cells = line.Trim('|').Split('|');
            if (cells.Length != CalendarColumnCount) continue;

            var name = cells[0].Trim().Trim('`');
            var branch = cells[1].Trim().ToLowerInvariant();
            var calendarId = cells[2].Trim().Trim('`');
            var use = cells[3].Trim();

            if (name.Length == 0 || calendarId.Length == 0) continue;
            if (!calendarId.Contains('@', StringComparison.Ordinal)) continue;
            if (use.Contains(ContextOnlyMarker, StringComparison.OrdinalIgnoreCase)) continue;
            if (use.Contains(PendingMarker, StringComparison.OrdinalIgnoreCase)) continue;

            calendars.Add(new RegistryCalendar(name, branch, calendarId));
        }

        return calendars;
    }
}
