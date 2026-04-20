using System.Diagnostics;

namespace TuroClawProwl.Infrastructure.Tests.TestSupport;

public sealed class TempGitRepo : IDisposable
{
    private readonly TempDirectory _tmp;

    public string Path { get; }
    public string? RemotePath { get; private set; }

    public TempGitRepo()
    {
        _tmp = new TempDirectory();
        Path = _tmp.CreateSubDirectory("repo");
        Run("init", "--initial-branch=main");
        Run("config", "user.email", "test@example.com");
        Run("config", "user.name", "Test User");
        Run("commit", "--allow-empty", "-m", "initial");
    }

    public void SetUpBareRemoteAndPush()
    {
        RemotePath = _tmp.CreateSubDirectory("remote.git");
        Run(RemotePath, new[] { "init", "--bare", "--initial-branch=main" });
        Run("remote", "add", "origin", RemotePath);
        Run("push", "-u", "origin", "main");
    }

    public void WriteFile(string relativePath, string content) =>
        File.WriteAllText(System.IO.Path.Combine(Path, relativePath), content);

    public void Commit(string message, string file, string content)
    {
        WriteFile(file, content);
        Run("add", file);
        Run("commit", "-m", message);
    }

    public void Run(params string[] args) => Run(Path, args);

    private static void Run(string workingDir, string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDir,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git.");
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            var stderr = p.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({p.ExitCode}): {stderr}");
        }
    }

    public void Dispose() => _tmp.Dispose();
}
