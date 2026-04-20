namespace TuroClawProwl.Application.Ports;

public interface ITokenStore
{
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);

    Task SetTokenAsync(string token, CancellationToken cancellationToken = default);
}
