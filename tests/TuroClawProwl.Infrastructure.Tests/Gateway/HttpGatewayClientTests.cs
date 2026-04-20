using System.Net;
using FluentAssertions;
using Moq;
using Polly;
using Polly.Retry;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.Gateway;
using TuroClawProwl.Infrastructure.Tests.TestSupport;

namespace TuroClawProwl.Infrastructure.Tests.Gateway;

public class HttpGatewayClientTests
{
    private static readonly Uri BaseAddress = new("http://macmini.lan:8080/");

    private readonly Mock<ITokenStore> _tokenStore = new(MockBehavior.Loose);

    public HttpGatewayClientTests()
    {
        _tokenStore.Setup(t => t.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    private HttpGatewayClient CreateClient(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler), _tokenStore.Object, BaseAddress, ResiliencePipeline.Empty);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Success_body_with_uptime_seconds_is_parsed_into_timespan()
    {
        var handler = FakeHttpMessageHandler.Responding(_ => Json(HttpStatusCode.OK, """{"status":"healthy","uptime_seconds":900}"""));
        var client = CreateClient(handler);

        var result = await client.GetHealthAsync();

        result.Should().BeOfType<GatewayPollResult.Success>()
            .Which.Uptime.Should().Be(TimeSpan.FromSeconds(900));
    }

    [Fact]
    public async Task Success_body_without_uptime_yields_success_with_null_uptime()
    {
        var handler = FakeHttpMessageHandler.Responding(_ => Json(HttpStatusCode.OK, """{"status":"healthy"}"""));
        var client = CreateClient(handler);

        var result = await client.GetHealthAsync();

        result.Should().BeOfType<GatewayPollResult.Success>()
            .Which.Uptime.Should().BeNull();
    }

    [Fact]
    public async Task Unparseable_body_still_yields_success_with_null_uptime()
    {
        var handler = FakeHttpMessageHandler.Responding(_ => Json(HttpStatusCode.OK, "not json at all"));
        var client = CreateClient(handler);

        var result = await client.GetHealthAsync();

        result.Should().BeOfType<GatewayPollResult.Success>()
            .Which.Uptime.Should().BeNull();
    }

    [Fact]
    public async Task Non_2xx_response_is_treated_as_failure_with_status_code()
    {
        var handler = FakeHttpMessageHandler.Responding(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var client = CreateClient(handler);

        var result = await client.GetHealthAsync();

        result.Should().BeOfType<GatewayPollResult.Failure>()
            .Which.Reason.Should().Contain("503");
    }

    [Fact]
    public async Task Network_error_is_treated_as_failure()
    {
        var handler = FakeHttpMessageHandler.Throwing(new HttpRequestException("connection refused"));
        var client = CreateClient(handler);

        var result = await client.GetHealthAsync();

        result.Should().BeOfType<GatewayPollResult.Failure>()
            .Which.Reason.Should().Contain("connection refused");
    }

    [Fact]
    public async Task Bearer_token_is_sent_when_token_store_returns_a_value()
    {
        _tokenStore.Setup(t => t.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("secret-token");
        var handler = FakeHttpMessageHandler.Responding(_ => Json(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler);

        await client.GetHealthAsync();

        var request = handler.Received.Single();
        request.Headers.Authorization.Should().NotBeNull();
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Authorization.Parameter.Should().Be("secret-token");
    }

    [Fact]
    public async Task No_authorization_header_is_sent_when_token_is_null()
    {
        var handler = FakeHttpMessageHandler.Responding(_ => Json(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler);

        await client.GetHealthAsync();

        handler.Received.Single().Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task Request_targets_base_address_plus_health_path()
    {
        var handler = FakeHttpMessageHandler.Responding(_ => Json(HttpStatusCode.OK, "{}"));
        var client = CreateClient(handler);

        await client.GetHealthAsync();

        handler.Received.Single().RequestUri.Should().Be(new Uri("http://macmini.lan:8080/health"));
    }

    [Fact]
    public void Constructor_rejects_null_http()
    {
        Action act = () => new HttpGatewayClient(null!, _tokenStore.Object, BaseAddress);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_token_store()
    {
        using var handler = FakeHttpMessageHandler.Responding(_ => new HttpResponseMessage(HttpStatusCode.OK));
        Action act = () => new HttpGatewayClient(new HttpClient(handler), null!, BaseAddress);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_rejects_null_base_address()
    {
        using var handler = FakeHttpMessageHandler.Responding(_ => new HttpResponseMessage(HttpStatusCode.OK));
        Action act = () => new HttpGatewayClient(new HttpClient(handler), _tokenStore.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static ResiliencePipeline BuildFastRetryPipeline(int maxAttempts = 3) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxAttempts,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant,
                UseJitter = false,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
            })
            .Build();

    [Fact]
    public async Task Transient_network_failure_is_retried_and_recovers()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            callCount++;
            if (callCount < 3)
                throw new HttpRequestException("transient");
            return Task.FromResult(Json(HttpStatusCode.OK, """{"uptime_seconds":42}"""));
        });
        var client = new HttpGatewayClient(
            new HttpClient(handler), _tokenStore.Object, BaseAddress, BuildFastRetryPipeline());

        var result = await client.GetHealthAsync();

        callCount.Should().Be(3);
        result.Should().BeOfType<GatewayPollResult.Success>()
            .Which.Uptime.Should().Be(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public async Task All_attempts_failing_yields_failure_with_last_error_message()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            callCount++;
            throw new HttpRequestException($"attempt {callCount}");
        });
        var client = new HttpGatewayClient(
            new HttpClient(handler), _tokenStore.Object, BaseAddress, BuildFastRetryPipeline(maxAttempts: 3));

        var result = await client.GetHealthAsync();

        callCount.Should().Be(4); // initial + 3 retries
        result.Should().BeOfType<GatewayPollResult.Failure>()
            .Which.Reason.Should().Contain("attempt 4");
    }

    [Fact]
    public async Task Non_2xx_response_is_not_retried()
    {
        var callCount = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });
        var client = new HttpGatewayClient(
            new HttpClient(handler), _tokenStore.Object, BaseAddress, BuildFastRetryPipeline());

        var result = await client.GetHealthAsync();

        callCount.Should().Be(1);
        result.Should().BeOfType<GatewayPollResult.Failure>()
            .Which.Reason.Should().Contain("401");
    }

    [Fact]
    public async Task Cancellation_stops_retry_loop_immediately()
    {
        using var cts = new CancellationTokenSource();
        var callCount = 0;
        var handler = new FakeHttpMessageHandler((_, ct) =>
        {
            callCount++;
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            throw new HttpRequestException("should not retry after cancel");
        });
        var client = new HttpGatewayClient(
            new HttpClient(handler), _tokenStore.Object, BaseAddress, BuildFastRetryPipeline());

        Func<Task> act = () => client.GetHealthAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        callCount.Should().Be(1);
    }
}
