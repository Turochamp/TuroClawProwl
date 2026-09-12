using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class RegistryParserTests
{
    private const string LiveRegistry = """
        # Registry — the hub's source of truth

        ## Active set

        | CCA | Branch | Cadence | Status | State file |
        |-----|--------|---------|--------|-----------|
        | YNE | professional | weekly | active | CCA-YNE/STATE.md |
        | SmoEms | professional | weekly | flagged | CCA-SmoEms/STATE.md |
        | HomeBase | personal | weekly | active | CCA-HomeBase/STATE.md |

        **Flag note — SmoEms:** engagement winding down. Kept in the weekly roll-up only
        until April + May invoices are paid, then move the folder to `archive/`.

        ## Quiet (kept on disk, excluded from all roll-ups)

        | CCA | Reason |
        |-----|--------|
        | Richards-Finn-Butikk | Built but never activated |

        ## Archived (in `archive/`, out of context)

        | CCA | Reason | Date |
        |-----|--------|------|
        | InspiritNegotiation | Closed exit negotiation | 2026-06-25 |
        """;

    [Fact]
    public void Active_and_flagged_rows_are_returned_with_name_status_and_state_file()
    {
        var entries = RegistryParser.ParseActiveSet(LiveRegistry);

        entries.Should().BeEquivalentTo(new[]
        {
            new RegistryEntry("YNE", "active", "CCA-YNE/STATE.md"),
            new RegistryEntry("SmoEms", "flagged", "CCA-SmoEms/STATE.md"),
            new RegistryEntry("HomeBase", "active", "CCA-HomeBase/STATE.md"),
        }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Quiet_and_archived_tables_are_excluded()
    {
        var entries = RegistryParser.ParseActiveSet(LiveRegistry);

        entries.Should().NotContain(e => e.Name == "Richards-Finn-Butikk");
        entries.Should().NotContain(e => e.Name == "InspiritNegotiation");
    }

    [Fact]
    public void Header_and_separator_rows_are_not_entries()
    {
        var entries = RegistryParser.ParseActiveSet(LiveRegistry);

        entries.Should().HaveCount(3);
        entries.Should().NotContain(e => e.Name == "CCA");
    }

    [Fact]
    public void Statuses_outside_active_and_flagged_are_excluded()
    {
        var md = """
            ## Active set

            | CCA | Branch | Cadence | Status | State file |
            |-----|--------|---------|--------|-----------|
            | Live | personal | weekly | active | CCA-Live/STATE.md |
            | Dead | personal | weekly | archived | CCA-Dead/STATE.md |
            | Paused | personal | weekly | quiet | CCA-Paused/STATE.md |
            """;

        var entries = RegistryParser.ParseActiveSet(md);

        entries.Should().ContainSingle().Which.Name.Should().Be("Live");
    }

    [Fact]
    public void Backticks_and_backslashes_in_the_state_file_cell_are_normalised()
    {
        var md = """
            ## Active set

            | CCA | Branch | Cadence | Status | State file |
            |-----|--------|---------|--------|-----------|
            | Win | personal | weekly | active | `CCA-Win\STATE.md` |
            """;

        var entries = RegistryParser.ParseActiveSet(md);

        entries.Should().ContainSingle().Which.StateFileRelativePath.Should().Be("CCA-Win/STATE.md");
    }

    [Fact]
    public void Null_markdown_is_rejected()
    {
        Action act = () => RegistryParser.ParseActiveSet(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
