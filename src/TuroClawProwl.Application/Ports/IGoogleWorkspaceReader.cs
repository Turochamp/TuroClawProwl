using TuroClawProwl.Domain;

namespace TuroClawProwl.Application.Ports;

public interface IGoogleWorkspaceReader
{
    Task<GoogleReadResult> ReadTaskListAsync(
        string listId,
        CancellationToken cancellationToken = default);

    Task<GoogleReadResult> ReadCalendarWindowAsync(
        IReadOnlyList<RegistryCalendar> calendars,
        CalendarWindow window,
        CancellationToken cancellationToken = default);
}
