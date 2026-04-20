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

    private static void ForceDelete(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
        }
        Directory.Delete(path, recursive: true);
    }
}
