namespace TuroClawProwl.Application.Ports;

public interface IGitFileFactsReader
{
    Task<GitFileFacts> GetFileFactsAsync(string absolutePath, CancellationToken cancellationToken = default);
}
