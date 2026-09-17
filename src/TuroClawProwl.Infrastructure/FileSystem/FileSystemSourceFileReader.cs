using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Infrastructure.FileSystem;

public sealed class FileSystemSourceFileReader : ISourceFileReader
{
    public async Task<SourceReadResult> ReadAsync(
        string absolutePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        if (!File.Exists(absolutePath))
            return new SourceReadResult.Missing();

        try
        {
            var content = await File.ReadAllTextAsync(absolutePath, cancellationToken).ConfigureAwait(false);
            var modified = new DateTimeOffset(File.GetLastWriteTimeUtc(absolutePath), TimeSpan.Zero);
            return new SourceReadResult.Found(content, modified);
        }
        catch (IOException ex)
        {
            return new SourceReadResult.Unreadable(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new SourceReadResult.Unreadable(ex.Message);
        }
    }
}
