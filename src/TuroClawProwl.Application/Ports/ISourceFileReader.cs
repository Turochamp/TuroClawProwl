namespace TuroClawProwl.Application.Ports;

public interface ISourceFileReader
{
    Task<SourceReadResult> ReadAsync(string absolutePath, CancellationToken cancellationToken = default);
}
