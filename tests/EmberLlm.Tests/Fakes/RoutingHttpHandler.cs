using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace EmberLlm.Tests.Fakes;

/// <summary>
/// A fake network of servers that answers by URL, for code that sends requests in parallel (discovery).
/// A host:port with no server behaves like a closed port (connection refused).
/// </summary>
internal sealed class RoutingHttpHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Func<string, HttpResponseMessage>> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _hanging = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<Uri> Requests { get; } = new();

    /// <summary>A server at <paramref name="authority"/> (host:port) that answers unrouted paths with <paramref name="fallback"/>.</summary>
    public RoutingHttpHandler Server(string authority, Func<string, HttpResponseMessage>? fallback = null)
    {
        _servers[authority] = fallback ?? (_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("404 page not found") });
        return this;
    }

    /// <summary>A server that never answers anything (like a hung process, or a closed port on Windows).</summary>
    public RoutingHttpHandler Hanging(string authority)
    {
        _hanging[authority] = true;
        return this;
    }

    public RoutingHttpHandler Route(string url, string body, string mediaType = "application/json", HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[url] = () => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
        return this;
    }

    /// <summary>LM Studio's real behavior for endpoints it doesn't have: HTTP 200 with a JSON error body.</summary>
    public static HttpResponseMessage LmStudioUnknownEndpoint(string path) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($$"""{"error":"Unexpected endpoint or method. (GET {{path}})"}""", Encoding.UTF8, "application/json"),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Requests.Enqueue(uri);

        if (_hanging.ContainsKey(uri.Authority))
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (_routes.TryGetValue(uri.AbsoluteUri, out var route))
        {
            return route();
        }

        if (_servers.TryGetValue(uri.Authority, out var fallback))
        {
            return fallback(uri.AbsolutePath);
        }

        throw new HttpRequestException($"No connection could be made because the target machine actively refused it. ({uri.Authority})");
    }
}
