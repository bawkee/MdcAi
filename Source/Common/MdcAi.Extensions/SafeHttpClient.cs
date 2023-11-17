namespace MdcAi.Extensions;

/// <summary>
/// This class conceals the OperationCanceledException thrown by the HttpClient base class. This is because
/// some frameworks and UI components ignore or truncate these exceptions. One of these is System.Reactive.
/// This implementation will throw a special HttpOperationCancelledException instead, because it's sometimes
/// very important to catch or at least log these as they could indicate network problems.
/// </summary>
/// <remarks>
/// When there are issues with the network (ethernet cables, routers, switches, WiFi, 3G, 4G etc.) the
/// HttpClient handles this by cancelling the request via its own cancellation tokens, resulting in this
/// exception. Thus, the only indicator of a faulty network is an otherwise benign OperationCanceledException.
/// </remarks>
public class SafeHttpClient : HttpClient
{
    public SafeHttpClient() { }

    public SafeHttpClient(HttpMessageHandler handler)
        : base(handler) { }

    public SafeHttpClient(HttpMessageHandler handler, bool disposeHandler)
        : base(handler, disposeHandler) { }

    public override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return base.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException oce)
        {
            throw new HttpOperationCancelledException(oce);
        }
    }
}

public class HttpOperationCancelledException : Exception
{
    public HttpOperationCancelledException(OperationCanceledException innerEx)
        : base("A http operation was aborted by the client. " +
               "This could be due to network issues.",
               innerEx)
    { }
}