using FluentAssertions;
using TuroClawProwl.Domain;

namespace TuroClawProwl.Domain.Tests;

public class PorcelainParserTests
{
    [Fact]
    public void Empty_output_means_no_uncommitted_changes()
    {
        PorcelainParser.Parse("").HasUncommitted.Should().BeFalse();
    }

    [Fact]
    public void Null_output_means_no_uncommitted_changes()
    {
        PorcelainParser.Parse(null).HasUncommitted.Should().BeFalse();
    }

    [Fact]
    public void Whitespace_only_output_means_no_uncommitted_changes()
    {
        PorcelainParser.Parse("  \n  \t \n").HasUncommitted.Should().BeFalse();
    }

    [Theory]
    [InlineData(" M modified.txt", "unstaged modification")]
    [InlineData("M  staged.txt", "staged modification")]
    [InlineData("A  added.txt", "staged addition")]
    [InlineData("D  deleted.txt", "staged deletion")]
    [InlineData("?? untracked.txt", "untracked file")]
    [InlineData("R  old.txt -> new.txt", "rename")]
    [InlineData("UU conflicted.txt", "merge conflict")]
    public void Any_single_nonempty_porcelain_line_marks_uncommitted(string line, string description)
    {
        PorcelainParser.Parse(line).HasUncommitted.Should().BeTrue(description);
    }

    [Fact]
    public void Multiple_nonempty_lines_mark_uncommitted()
    {
        var output = " M a.txt\n?? b.txt\nA  c.txt";
        PorcelainParser.Parse(output).HasUncommitted.Should().BeTrue();
    }

    [Fact]
    public void Trailing_newline_on_otherwise_empty_output_stays_clean()
    {
        PorcelainParser.Parse("\n").HasUncommitted.Should().BeFalse();
    }
}
