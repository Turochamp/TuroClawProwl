namespace TuroClawProwl.Infrastructure.Tests.TestSupport;

public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TuroClawProwl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string CreateSubDirectory(string name)
    {
        var full = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(full);
        return full;
    }

    public string CreateRepoFolder(string name)
    {
        var repo = CreateSubDirectory(name);
        Directory.CreateDirectory(System.IO.Path.Combine(repo, ".git"));
        return repo;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                ForceDelete(Path);
        }
        catch (Exception)
        {
            // best-effort cleanup; tmp dirs are self-healing
        }
    }

    // Public so tests can force-delete a directory that isn't this TempDirectory's
    // own root -- e.g. a TempGitRepo's bare remote, whose loose objects git marks
    // read-only. On Windows, plain Directory.Delete throws UnauthorizedAccessException
    // on a read-only file regardless of directory permissions (unlike POSIX, where
    // deleting only needs write access to the containing directory), so the read-only
    // attribute must be cleared first.
    public static void ForceDelete(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
        }
        Directory.Delete(path, recursive: true);
    }
}
