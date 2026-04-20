using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Infrastructure.Tests.TestSupport;

public sealed class InMemoryTokenStore : ITokenStore
{
    private string? _token;

    public InMemoryTokenStore(string? initial = null) => _token = initial;

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_token);

    public Task SetTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        _token = token;
        return Task.CompletedTask;
    }
}
