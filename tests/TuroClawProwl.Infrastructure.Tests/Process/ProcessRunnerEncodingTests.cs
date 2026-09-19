using System.Text;
using FluentAssertions;
using TuroClawProwl.Infrastructure.Process;

namespace TuroClawProwl.Infrastructure.Tests.Process;

// Regression for the bundle's double-encoded UTF-8. A redirected stream defaults to
// Console.OutputEncoding -- cp1252 on this machine -- so gws's UTF-8 was decoded as the
// wrong characters and written back out as UTF-8: every Norwegian and Swedish word in
// tasks and calendar reached fox mojibaked, from the very first publish. The renderer on
// fox had been quietly repairing it, which is why nothing downstream ever matched the JSON.
public class ProcessRunnerEncodingTests
{
    private const string Norwegian = "Kurt på fredag? Besøk Norges nasjonalparker — it’s Mål 4";

    [Fact]
    public void Redirected_streams_are_decoded_as_UTF8_without_a_BOM()
    {
        var psi = ProcessRunner.BuildStartInfo("git", ["status"], workingDirectory: null);

        psi.StandardOutputEncoding.Should().BeOfType<UTF8Encoding>();
        psi.StandardErrorEncoding.Should().BeOfType<UTF8Encoding>();

        // A BOM here would be written into the first line of stdout, where every parser
        // downstream would meet it before the JSON's opening brace.
        psi.StandardOutputEncoding!.GetPreamble().Should().BeEmpty();
        psi.StandardErrorEncoding!.GetPreamble().Should().BeEmpty();
    }

    [Trait("Category", "Integration")]
    [Fact]
    public async Task UTF8_bytes_on_stdout_survive_the_round_trip()
    {
        // `type` copies the file's bytes to stdout untouched, so what RunAsync returns is
        // purely a question of how the pipe was decoded.
        var path = Path.Combine(Path.GetTempPath(), $"tcp-encoding-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, Norwegian, new UTF8Encoding(false));

        try
        {
            var result = await ProcessRunner.RunAsync(
                "cmd.exe", ["/c", "type", path], workingDirectory: null, CancellationToken.None);

            result.ExitCode.Should().Be(0);
            result.StdOut.Trim().Should().Be(Norwegian);
            result.StdOut.Should().NotContain("Ã", "that is what cp1252 decoding of UTF-8 looks like");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
