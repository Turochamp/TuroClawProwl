using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class GwsOutputParserTests
{
    [Fact]
    public void The_keyring_preamble_is_stripped_from_an_object_payload()
    {
        var stdout = "Using keyring backend: keyring\n{\"items\":[]}\n";

        GwsOutputParser.ExtractJson(stdout).Should().Be("{\"items\":[]}");
    }

    [Fact]
    public void The_keyring_preamble_is_stripped_from_an_array_payload()
    {
        var stdout = "Using keyring backend: keyring\n[{\"id\":\"1\"}]\n";

        GwsOutputParser.ExtractJson(stdout).Should().Be("[{\"id\":\"1\"}]");
    }

    [Fact]
    public void Several_preamble_lines_are_stripped()
    {
        var stdout = "Using keyring backend: keyring\nwarning: token refreshed\n{\"items\":[]}";

        GwsOutputParser.ExtractJson(stdout).Should().Be("{\"items\":[]}");
    }

    [Fact]
    public void Clean_json_with_no_preamble_is_returned_unchanged()
    {
        GwsOutputParser.ExtractJson("{\"items\":[]}").Should().Be("{\"items\":[]}");
    }

    [Fact]
    public void A_brace_inside_a_preamble_string_does_not_start_the_payload()
    {
        var stdout = "Using keyring backend: keyring\n{\"items\":[{\"title\":\"pay {rent}\"}]}";

        GwsOutputParser.ExtractJson(stdout)
            .Should().Be("{\"items\":[{\"title\":\"pay {rent}\"}]}");
    }

    [Fact]
    public void Trailing_output_after_the_payload_is_dropped()
    {
        var stdout = "{\"items\":[]}\nDone in 412ms\n";

        GwsOutputParser.ExtractJson(stdout).Should().Be("{\"items\":[]}");
    }

    [Fact]
    public void Output_with_no_json_at_all_yields_null()
    {
        GwsOutputParser.ExtractJson("Not authenticated. Run gws auth login.").Should().BeNull();
    }

    [Fact]
    public void Empty_output_yields_null()
    {
        GwsOutputParser.ExtractJson("").Should().BeNull();
        GwsOutputParser.ExtractJson("   \n  ").Should().BeNull();
    }

    [Fact]
    public void Truncated_json_yields_null_rather_than_a_half_document()
    {
        GwsOutputParser.ExtractJson("Using keyring backend: keyring\n{\"items\":[").Should().BeNull();
    }

    [Fact]
    public void Null_stdout_is_rejected()
    {
        Action act = () => GwsOutputParser.ExtractJson(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
