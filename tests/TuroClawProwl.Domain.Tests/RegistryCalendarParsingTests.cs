using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class RegistryCalendarParsingTests
{
    private const string LiveRegistry = """
        ## Active set

        | CCA | Branch | Cadence | Status | State file |
        |-----|--------|---------|--------|-----------|
        | YNE | professional | weekly | active | CCA-YNE/STATE.md |

        ## Calendar & task-list config

        ### Calendars

        | Calendar | Branch | ID | Use |
        |----------|--------|----|----|
        | Primary | professional | `michael.ahs@gmail.com` | Work + general |
        | Family (Cozi) | personal | `q1eegih8ilpseia21bs43ep7f2st2t89@import.calendar.google.com` | Family events |
        | Michael (Cozi Feed) | personal | `family17973008126090803343@group.calendar.google.com` | Personal/family feed |
        | Holidays in Norway | both | `en.norwegian#holiday@group.v.calendar.google.com` | Context only — not actions |

        Skip for planning: **Week Numbers**, **Facebook Events**.

        ### Task lists

        | List | Branch | ID |
        |------|--------|----|
        | My Tasks | professional (default) | `MTIzNjE2OTI4NjM5NTQ3NDc3NzM6MDow` |
        """;

    [Fact]
    public void Planning_calendars_are_returned_with_their_branch_and_id()
    {
        var calendars = RegistryParser.ParseCalendars(LiveRegistry);

        calendars.Should().BeEquivalentTo(new[]
        {
            new RegistryCalendar("Primary", "professional", "michael.ahs@gmail.com"),
            new RegistryCalendar(
                "Family (Cozi)", "personal",
                "q1eegih8ilpseia21bs43ep7f2st2t89@import.calendar.google.com"),
            new RegistryCalendar(
                "Michael (Cozi Feed)", "personal",
                "family17973008126090803343@group.calendar.google.com"),
        }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void A_calendar_marked_context_only_is_excluded()
    {
        var calendars = RegistryParser.ParseCalendars(LiveRegistry);

        calendars.Should().NotContain(c => c.Name == "Holidays in Norway");
        calendars.Should().NotContain(c => c.CalendarId.Contains("holiday", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_task_list_table_is_not_mistaken_for_a_calendar()
    {
        var calendars = RegistryParser.ParseCalendars(LiveRegistry);

        calendars.Should().NotContain(c => c.Name == "My Tasks");
        calendars.Should().HaveCount(3);
    }

    [Fact]
    public void The_active_set_table_is_not_mistaken_for_a_calendar()
    {
        var calendars = RegistryParser.ParseCalendars(LiveRegistry);

        calendars.Should().NotContain(c => c.Name == "YNE");
    }

    [Fact]
    public void Backticks_are_stripped_from_the_calendar_id()
    {
        var calendars = RegistryParser.ParseCalendars(LiveRegistry);

        calendars.Should().AllSatisfy(c => c.CalendarId.Should().NotContain("`"));
    }

    [Fact]
    public void Every_parsed_calendar_carries_a_branch_the_renderer_can_lane()
    {
        var calendars = RegistryParser.ParseCalendars(LiveRegistry);

        calendars.Should().AllSatisfy(c =>
            c.Branch.Should().BeOneOf("professional", "personal", "both"));
    }

    [Fact]
    public void A_registry_without_a_calendar_section_yields_no_calendars()
    {
        var calendars = RegistryParser.ParseCalendars("## Active set\n\n| a | b | c | d | e |\n");

        calendars.Should().BeEmpty();
    }

    [Fact]
    public void Null_markdown_is_rejected()
    {
        Action act = () => RegistryParser.ParseCalendars(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
