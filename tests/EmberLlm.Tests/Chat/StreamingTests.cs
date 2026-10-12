using System.Net;
using System.Text.Json;
using EmberLlm.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace EmberLlm.Tests.Chat;

public class StreamingTests
{
    private static readonly ChatMessage[] Messages = [new(ChatRole.User, "Write a haiku")];

    private static readonly ChatOptions Options = new() { ModelId = "qwen3-14b" };

    private const string Prompt = "Write a haiku";

    [Fact]
    public async Task StreamAsync_SendsStreamingRequestWithUsage()
    {
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(SampleStreams.Haiku()));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        await DrainAsync(client.StreamAsync("Write a haiku", TestContext.Current.CancellationToken));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri("http://localhost:1234/v1/chat/completions"), request.Uri);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.True(body.RootElement.GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
        Assert.Equal("qwen3-14b", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task NonStreamingRequest_DoesNotSendStreamOptions()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion());
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        await client.GetResponseAsync(Messages, Options, TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.False(body.RootElement.TryGetProperty("stream_options", out _));
    }

    [Fact]
    public async Task StreamAsync_YieldsTextTokensInOrder()
    {
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(SampleStreams.Haiku()));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        var tokens = await DrainAsync(client.StreamAsync(Prompt, TestContext.Current.CancellationToken));

        // The role-only, finish and usage chunks carry no text and are skipped
        Assert.Equal(["Heap", " fills", " up"], tokens);
    }

    [Fact]
    public async Task StreamAsync_SkipsReasoning()
    {
        var sse = SampleStreams.Sse(
            SampleStreams.ReasoningChunk("Think about memory."),
            SampleStreams.ContentChunk("Heap"),
            SampleStreams.FinishChunk());
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        var tokens = await DrainAsync(client.StreamAsync(Prompt, TestContext.Current.CancellationToken));

        Assert.Equal(["Heap"], tokens);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_FinalUpdatesCarryFinishReasonAndUsage()
    {
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(SampleStreams.Haiku()));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        var updates = await DrainAsync(client.GetStreamingResponseAsync(Messages, Options, TestContext.Current.CancellationToken));

        Assert.Equal(6, updates.Count);
        Assert.Equal(ChatRole.Assistant, updates[0].Role);
        Assert.Equal(ChatFinishReason.Stop, updates[4].FinishReason);
        var usage = Assert.Single(updates[5].Contents.OfType<UsageContent>()).Details;
        Assert.Equal(12, usage.InputTokenCount);
        Assert.Equal(3, usage.OutputTokenCount);
        Assert.All(updates, u => Assert.Equal("qwen3-14b", u.ModelId));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_CombinesIntoChatResponse()
    {
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(SampleStreams.Haiku()));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        var updates = await DrainAsync(client.GetStreamingResponseAsync(Messages, Options, TestContext.Current.CancellationToken));
        var response = updates.ToChatResponse();

        Assert.Equal("Heap fills up", response.Text);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Equal(15, response.Usage?.TotalTokenCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ReasoningIsTextReasoningContent()
    {
        var sse = SampleStreams.Sse(SampleStreams.ReasoningChunk("Think."), SampleStreams.ContentChunk("Done"));
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        var updates = await DrainAsync(client.GetStreamingResponseAsync(Messages, Options, TestContext.Current.CancellationToken));

        Assert.Equal("Think.", Assert.IsType<TextReasoningContent>(Assert.Single(updates[0].Contents)).Text);
        Assert.Equal(string.Empty, updates[0].Text);
        Assert.Equal("Done", updates[1].Text);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(64)]
    public async Task SplitNetworkChunks_ProduceSameTokens(int chunkSize)
    {
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(SampleStreams.Haiku(), chunkSize));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        var tokens = await DrainAsync(client.StreamAsync(Prompt, TestContext.Current.CancellationToken));

        Assert.Equal(["Heap", " fills", " up"], tokens);
    }

    [Fact]
    public async Task KeepAliveComments_BetweenChunks_AreIgnored()
    {
        var sse = ": keep-alive\n\n"
            + $"data: {SampleStreams.ContentChunk("Heap")}\n\n"
            + ": keep-alive\n\n: keep-alive\n\n"
            + $"data: {SampleStreams.ContentChunk(" fills")}\n\n"
            + "data: [DONE]\n\n";
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        var tokens = await DrainAsync(client.StreamAsync(Prompt, TestContext.Current.CancellationToken));

        Assert.Equal(["Heap", " fills"], tokens);
    }

    [Fact]
    public async Task DoneMarker_EndsStream_EvenIfServerKeepsConnectionOpen()
    {
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(SampleStreams.Haiku(), end: StreamEnd.Hang));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        var tokens = await DrainAsync(client.StreamAsync(Prompt, TestContext.Current.CancellationToken));

        Assert.Equal(3, tokens.Count);
    }

    [Fact]
    public async Task StreamWithoutDoneMarker_EndsWhenConnectionCloses()
    {
        var sse = $"data: {SampleStreams.ContentChunk("Heap")}\n\ndata: {SampleStreams.FinishChunk()}\n\n";
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        var tokens = await DrainAsync(client.StreamAsync(Prompt, TestContext.Current.CancellationToken));

        Assert.Equal(["Heap"], tokens);
    }

    [Fact]
    public async Task CallerCancellation_MidStream_ThrowsOperationCanceledWithCallerToken()
    {
        var sse = $"data: {SampleStreams.ContentChunk("Heap")}\n\n";
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse, end: StreamEnd.Hang));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var received = new List<string>();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var token in client.StreamAsync(Prompt, cancellation.Token))
            {
                received.Add(token);
                cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
            }
        });

        Assert.Equal(["Heap"], received);
        Assert.Equal(cancellation.Token, ex.CancellationToken);
    }

    [Fact]
    public async Task WithCancellation_OnEnumerable_IsHonored()
    {
        var sse = $"data: {SampleStreams.ContentChunk("Heap")}\n\n";
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse, end: StreamEnd.Hang));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            // The token deliberately goes through WithCancellation instead of the method argument
#pragma warning disable xUnit1051
            await foreach (var _ in client.StreamAsync(Prompt).WithCancellation(cancellation.Token))
#pragma warning restore xUnit1051
            {
                cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
            }
        });
    }

    [Fact]
    public async Task StalledStream_ExceedingRequestTimeout_ThrowsTimeoutException()
    {
        var sse = $"data: {SampleStreams.ContentChunk("Heap")}\n\n";
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse, end: StreamEnd.Hang));
        using var client = TestClients.Create(handler, o => { o.DefaultModel = "qwen3-14b"; o.RequestTimeout = TimeSpan.FromMilliseconds(150); });

        await Assert.ThrowsAsync<EmberLlmTimeoutException>(() => DrainAsync(client.StreamAsync(Prompt, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task DroppedConnection_MidStream_ThrowsConnectionException()
    {
        var sse = $"data: {SampleStreams.ContentChunk("Heap")}\n\n";
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse, end: StreamEnd.Drop));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        var ex = await Assert.ThrowsAsync<EmberLlmConnectionException>(() => DrainAsync(client.StreamAsync(Prompt, TestContext.Current.CancellationToken)));

        Assert.IsType<IOException>(ex.InnerException);
    }

    [Fact]
    public async Task HttpError_BeforeStreaming_ThrowsHttpException()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.OllamaError, HttpStatusCode.NotFound);
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        var ex = await Assert.ThrowsAsync<EmberLlmHttpException>(() => DrainAsync(client.StreamAsync(Prompt, TestContext.Current.CancellationToken)));

        Assert.Equal("model 'missing-model' not found", ex.ServerMessage);
    }

    [Fact]
    public async Task ErrorEvent_MidStream_ThrowsWithServerMessage()
    {
        var sse = SampleStreams.Sse(
            SampleStreams.ContentChunk("Heap"),
            """{ "error": { "message": "CUDA out of memory", "type": "server_error" } }""");
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");
        var received = new List<string>();

        var ex = await Assert.ThrowsAsync<EmberLlmException>(async () =>
        {
            await foreach (var token in client.StreamAsync(Prompt, TestContext.Current.CancellationToken))
            {
                received.Add(token);
            }
        });

        Assert.Equal(["Heap"], received);
        Assert.Contains("CUDA out of memory", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedChunk_ThrowsEmberLlmException()
    {
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream("data: {not json\n\n"));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        var ex = await Assert.ThrowsAsync<EmberLlmException>(() => DrainAsync(client.StreamAsync(Prompt, TestContext.Current.CancellationToken)));

        Assert.IsType<JsonException>(ex.InnerException, exactMatch: false);
    }

    [Fact]
    public async Task ServerNotRunning_ThrowsConnectionException()
    {
        var handler = new FakeHttpHandler().Throw(new HttpRequestException("Connection refused"));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        await Assert.ThrowsAsync<EmberLlmConnectionException>(() => DrainAsync(client.StreamAsync(Prompt, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task StreamUsesModelSelection()
    {
        var handler = new FakeHttpHandler()
            .RespondJson(SampleResponses.Models("listed-model"))
            .RespondStream(new ScriptedStream(SampleStreams.Haiku()));
        using var client = TestClients.Create(handler);

        await DrainAsync(client.StreamAsync("Hi", TestContext.Current.CancellationToken));

        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal("listed-model", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public void InvalidArguments_ThrowAtCallSite_BeforeEnumeration()
    {
        using var client = TestClients.Create(new FakeHttpHandler());

        Assert.Throws<ArgumentException>(() => client.GetStreamingResponseAsync([], Options, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => client.StreamAsync(" ", TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentNullException>(() => client.GetStreamingResponseAsync(null!, Options, TestContext.Current.CancellationToken));

        // Unsupported options are rejected at the call site too, before anything is sent
        Assert.Throws<NotSupportedException>(() => client.GetStreamingResponseAsync(Messages, new ChatOptions { ConversationId = "c" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DisposedClient_ThrowsAtCallSite()
    {
        var client = TestClients.Create(new FakeHttpHandler());
        client.Dispose();

        Assert.Throws<ObjectDisposedException>(() => client.GetStreamingResponseAsync(Messages, Options, TestContext.Current.CancellationToken));
    }

    private static async Task<List<T>> DrainAsync<T>(IAsyncEnumerable<T> source)
    {
        var items = new List<T>();
        await foreach (var item in source)
        {
            items.Add(item);
        }

        return items;
    }
}
