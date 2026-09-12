using System.Diagnostics;

namespace TuroClawProwl.Infrastructure.Tests.TestSupport;

public static class GitCli
{
    public static ProcessStartInfo StartInfo(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    public static string Read(string workingDirectory, params string[] args)
    {
        // Fully qualified: within TuroClawProwl.Infrastructure.Tests.TestSupport, an
        // unqualified `Process` resolves to the sibling namespace
        // TuroClawProwl.Infrastructure.Process (which holds ProcessRunner) rather than
        // System.Diagnostics.Process, because enclosing-namespace member lookup beats a
        // `using` directive.
        using var p = System.Diagnostics.Process.Start(StartInfo(workingDirectory, args))
            ?? throw new InvalidOperationException("Failed to start git.");
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return stdout.Trim();
    }
}
