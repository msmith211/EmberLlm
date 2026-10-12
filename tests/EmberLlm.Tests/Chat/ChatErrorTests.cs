using System.Net;
using EmberLlm.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace EmberLlm.Tests.Chat;

public class ChatErrorTests
{
    private static readonly ChatMessage[] Messages = [new(ChatRole.User, "Hi")];

    private static readonly ChatOptions Options = new() { ModelId = "qwen3-14b" };

    [Fact]
    public async Task ClientError_OpenAiShape_ThrowsHttpExceptionWithServerMessage()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.OpenAiError, HttpStatusCode.BadRequest);
        using var client = TestClients.Create(handler);

        var ex = await Assert.ThrowsAsync<EmberLlmHttpException>(() => client.GetResponseAsync(Messages, Options, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("max_tokens must be positive", ex.ServerMessage);
        Assert.False(ex.IsServerError);
        Assert.Contains("400", ex.Message, StringComparison.Ordinal);
        Assert.Contains("max_tokens must be positive", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientError_OllamaShape_ExtractsServerMessage()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.OllamaError, HttpStatusCode.NotFound);
        using var client = TestClients.Create(handler);

        var ex = await Assert.ThrowsAsync<EmberLlmHttpException>(() => client.GetResponseAsync(Messages, Options, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("model 'missing-model' not found", ex.ServerMessage);
    }

    [Fact]
    public async Task ServerError_NonJsonBody_KeepsRawBody()
    {
        var handler = new FakeHttpHandler().Respond(HttpStatusCode.InternalServerError, SampleResponses.HtmlErrorPage, "text/html");
        using var client = TestClients.Create(handler);

        var ex = await Assert.ThrowsAsync<EmberLlmHttpException>(() => client.GetResponseAsync(Messages, Options, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.True(ex.IsServerError);
        Assert.Null(ex.ServerMessage);
        Assert.Equal(SampleResponses.HtmlErrorPage, ex.ResponseBody);
        Assert.Contains("500 Internal Server Error", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerError_LongBody_IsTruncatedInMessageButNotInResponseBody()
    {
        var longBody = new string('x', 5_000);
        var handler = new FakeHttpHandler().Respond(HttpStatusCode.BadGateway, longBody);
        using var client = TestClients.Create(handler);

        var ex = await Assert.ThrowsAsync<EmberLlmHttpException>(() => client.GetResponseAsync(Messages, Options, TestContext.Current.CancellationToken));

        Assert.True(ex.Message.Length < 1_000);
        Assert.Equal(longBody, ex.ResponseBody);
    }

    [Fact]
    public async Task ServerNotRunning_ThrowsConnectionException()
    {
        var refused = new HttpRequestException("No connection could be made because the target machine actively refused it.");
        var handler = new FakeHttpHandler().Throw(refused);
        using var client = TestClients.Create(handler);

        var ex = await Assert.ThrowsAsync<EmberLlmConnectionException>(() => client.GetResponseAsync(Messages, Options, TestContext.Current.CancellationToken));

        Assert.Same(refused, ex.InnerException);
    }

    [Fact]
    public async Task SlowServer_ExceedingRequestTimeout_ThrowsTimeoutException()
    {
        var handler = new FakeHttpHandler().Hang();
        using var client = TestClients.Create(handler, o => o.RequestTimeout = TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<EmberLlmTimeoutException>(() => client.GetResponseAsync(Messages, Options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HttpClientTimeout_IsAlsoReportedAsTimeoutException()
    {
        // An injected HttpClient may have its own Timeout, which surfaces as TaskCanceledException
        var handler = new FakeHttpHandler().Hang();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        using var client = new EmberLlmClient(http, new EmberLlmOptions { BaseUrl = TestClients.BaseUrl });

        await Assert.ThrowsAsync<EmberLlmTimeoutException>(() => client.GetResponseAsync(Messages, Options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CallerCancellation_ThrowsOperationCanceled_NotTimeout()
    {
        var handler = new FakeHttpHandler().Hang();
        using var client = TestClients.Create(handler);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync(Messages, Options, cancellation.Token));

        Assert.Equal(cancellation.Token, ex.CancellationToken);
    }

    [Fact]
    public async Task AlreadyCancelledToken_ThrowsWithoutSendingRequest()
    {
        var handler = new FakeHttpHandler();
        using var client = TestClients.Create(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync(Messages, Options, new CancellationToken(canceled: true)));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void RequestTimeout_NotPositive_ThrowsArgumentOutOfRange(int milliseconds)
    {
        var options = new EmberLlmOptions { BaseUrl = TestClients.BaseUrl, RequestTimeout = TimeSpan.FromMilliseconds(milliseconds) };

        Assert.Throws<ArgumentOutOfRangeException>(() => new EmberLlmClient(options));
    }
}
