using System.Diagnostics;
using System.Text;

namespace TuroClawProwl.Infrastructure.Process;

internal static class ProcessRunner
{
    // Without this, a redirected stream is decoded with Console.OutputEncoding -- the
    // machine's console code page, cp1252 here. gws prints UTF-8, so every non-ASCII
    // character was decoded as the wrong characters and then written back out as UTF-8:
    // "Kurt pa fredag?" reached the bundle as "Kurt pA¥ fredag?", "o" as "A¸", "'" as
    // "a€™". Every Norwegian and Swedish word in tasks and calendar was affected from
    // the first publish. No BOM: these are pipes, not files, and a BOM would land in the
    // first line of stdout and break every parser downstream.
    private static readonly UTF8Encoding PipeEncoding = new(encoderShouldEmitUTF8Identifier: false);

    internal sealed record Result(int ExitCode, string StdOut, string StdErr);

    internal static async Task<Result> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        var psi = BuildStartInfo(fileName, args, workingDirectory);
        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        proc.Start();

        var stdOutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // stdOutTask/stdErrTask were started with the same token, so they are
            // about to fault or cancel too, and this rethrow means neither is
            // awaited below. Observe them here so that fault never surfaces later
            // as an unobserved task exception, then let the cancellation propagate.
            await ObserveAsync(stdOutTask).ConfigureAwait(false);
            await ObserveAsync(stdErrTask).ConfigureAwait(false);
            throw;
        }

        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);

        return new Result(proc.ExitCode, stdOut, stdErr);
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // The cancellation this task carries is reported by the rethrow above;
            // this exists solely to observe the fault so it is never unobserved.
        }
    }

    internal static ProcessStartInfo BuildStartInfo(
        string fileName,
        IReadOnlyList<string> args,
        string? workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = PipeEncoding,
            StandardErrorEncoding = PipeEncoding,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? string.Empty,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        // Every caller in this solution parses git's stdout/stderr text (e.g. matching
        // "not a git repository" to classify a failure). Git's messages are localized,
        // and gettext's precedence is LANGUAGE > LC_ALL > LC_MESSAGES > LANG, so LC_ALL
        // alone is not enough -- LANGUAGE must also be cleared, or a translated
        // environment silently breaks every substring match in the solution.
        psi.Environment["LC_ALL"] = "C";
        psi.Environment.Remove("LANGUAGE");

        return psi;
    }
}
