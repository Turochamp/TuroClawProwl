using System.Security.Cryptography;
using System.Text;
using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Infrastructure.Security;

public sealed class DpapiTokenStore : ITokenStore
{
    private readonly string _filePath;

    public DpapiTokenStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath)) return null;

        var cipher = await File.ReadAllBytesAsync(_filePath, cancellationToken).ConfigureAwait(false);
        var plain = ProtectedData.Unprotect(cipher, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plain);
    }

    public async Task SetTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        var plain = Encoding.UTF8.GetBytes(token);
        var cipher = ProtectedData.Protect(plain, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllBytesAsync(_filePath, cipher, cancellationToken).ConfigureAwait(false);
    }
}
