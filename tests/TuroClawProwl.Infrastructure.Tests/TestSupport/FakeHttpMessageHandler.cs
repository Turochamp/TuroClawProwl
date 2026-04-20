namespace TuroClawProwl.Infrastructure.Tests.TestSupport;

public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public List<HttpRequestMessage> Received { get; } = new();

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    public static FakeHttpMessageHandler Responding(Func<HttpRequestMessage, HttpResponseMessage> handler)
        => new((req, _) => Task.FromResult(handler(req)));

    public static FakeHttpMessageHandler Throwing(Exception exception)
        => new((_, _) => throw exception);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Received.Add(request);
        return _handler(request, cancellationToken);
    }
}
