using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;
using TuroClawProwl.Application.Ports;

namespace TuroClawProwl.Infrastructure.Gateway;

public sealed class HttpGatewayClient : IGatewayClient
{
    private readonly HttpClient _http;
    private readonly ITokenStore _tokenStore;
    private readonly ResiliencePipeline _pipeline;
    private Uri _healthEndpoint;

    public HttpGatewayClient(
        HttpClient http,
        ITokenStore tokenStore,
        Uri baseAddress,
        ResiliencePipeline? pipeline = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(tokenStore);
        ArgumentNullException.ThrowIfNull(baseAddress);

        _http = http;
        _tokenStore = tokenStore;
        _healthEndpoint = new Uri(baseAddress, "health");
        _pipeline = pipeline ?? ResiliencePipeline.Empty;
    }

    // Atomically swap the gateway base URL. Used by LiveConfigApplier so a
    // settings save takes effect on the next poll without rebuilding the
    // HttpClient or the Polly pipeline. Reads of _healthEndpoint inside
    // SendOnceAsync are a single reference-typed field load and are safe
    // without an explicit lock.
    public void SetBaseAddress(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        Volatile.Write(ref _healthEndpoint, new Uri(baseAddress, "health"));
    }

    public static ResiliencePipeline BuildDefaultRetryPipeline(ILogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
                OnRetry = args =>
                {
                    log.LogDebug(
                        "Health poll transient failure; retry {Attempt} in {Delay}: {Reason}",
                        args.AttemptNumber + 1,
                        args.RetryDelay,
                        args.Outcome.Exception?.Message);
                    return default;
                },
            })
            .Build();
    }

    public async Task<GatewayPollResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pipeline.ExecuteAsync(
                async ct => await SendOnceAsync(ct).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
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

    private async Task<GatewayPollResult> SendOnceAsync(CancellationToken cancellationToken)
    {
        var endpoint = Volatile.Read(ref _healthEndpoint);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        var token = await _tokenStore.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return new GatewayPollResult.Failure($"HTTP {(int)response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new GatewayPollResult.Success(TryParseUptime(body));
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
