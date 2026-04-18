using System.Net.Http.Headers;
using System.Text.Json;
using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Infrastructure.Gateway;

public sealed class HttpGatewayClient : IGatewayClient
{
    private readonly HttpClient _http;
    private readonly ITokenStore _tokenStore;
    private readonly Uri _healthEndpoint;

    public HttpGatewayClient(HttpClient http, ITokenStore tokenStore, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(tokenStore);
        ArgumentNullException.ThrowIfNull(baseAddress);

        _http = http;
        _tokenStore = tokenStore;
        _healthEndpoint = new Uri(baseAddress, "health");
    }

    public async Task<GatewayPollResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _healthEndpoint);
            var token = await _tokenStore.GetTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new GatewayPollResult.Failure($"HTTP {(int)response.StatusCode}");

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new GatewayPollResult.Success(TryParseUptime(body));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new GatewayPollResult.Failure("timeout");
        }
        catch (HttpRequestException ex)
        {
            return new GatewayPollResult.Failure(ex.Message);
        }
    }

    private static TimeSpan? TryParseUptime(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (doc.RootElement.TryGetProperty("uptime_seconds", out var u) &&
                u.ValueKind == JsonValueKind.Number &&
                u.TryGetDouble(out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }
}
