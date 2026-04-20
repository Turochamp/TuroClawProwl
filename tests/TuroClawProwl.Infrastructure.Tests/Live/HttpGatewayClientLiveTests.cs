using FluentAssertions;
using TuroClawProwl.Application.Ports;
using TuroClawProwl.Infrastructure.Gateway;
using TuroClawProwl.Infrastructure.Tests.TestSupport;

namespace TuroClawProwl.Infrastructure.Tests.Live;

[Trait("Category", "Live")]
public class HttpGatewayClientLiveTests
{
    [SkippableFact]
    public async Task Real_gateway_health_endpoint_responds_with_success_poll_result()
    {
        Skip.If(string.IsNullOrWhiteSpace(LiveEnv.GatewayUrl), "TURO_LIVE_GATEWAY_URL not set");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var tokenStore = new InMemoryTokenStore(LiveEnv.GatewayToken);
        var client = new HttpGatewayClient(http, tokenStore, new Uri(LiveEnv.GatewayUrl!));

        var result = await client.GetHealthAsync();

        result.Should().BeOfType<GatewayPollResult.Success>(
            "the configured gateway must respond to GET /health on this machine");
    }
}
