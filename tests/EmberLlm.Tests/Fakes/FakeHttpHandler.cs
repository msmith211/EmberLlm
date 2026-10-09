using System.Net;
using System.Text;

namespace EmberLlm.Tests.Fakes;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that replays queued responses in order and records every request.
/// </summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public FakeHttpHandler RespondJson(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        Respond(status, json, "application/json");

    public FakeHttpHandler Respond(HttpStatusCode status, string body, string mediaType = "text/plain")
    {
        _responses.Enqueue(_ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        }));
        return this;
    }

    /// <summary>Answers 200 with an event stream body read from <paramref name="stream"/>.</summary>
    public FakeHttpHandler RespondStream(Stream stream)
    {
        _responses.Enqueue(_ =>
        {
            var content = new StreamContent(stream);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        return this;
    }

    /// <summary>Never answers; completes only when the request is cancelled.</summary>
    public FakeHttpHandler Hang()
    {
        _responses.Enqueue(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        });
        return this;
    }

    public FakeHttpHandler Throw(Exception exception)
    {
        _responses.Enqueue(_ => Task.FromException<HttpResponseMessage>(exception));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));

        if (!_responses.TryDequeue(out var respond))
        {
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        }

        return await respond(cancellationToken);
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body);
