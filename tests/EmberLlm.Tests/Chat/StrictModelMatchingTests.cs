using EmberLlm.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace EmberLlm.Tests.Chat;

/// <summary>
/// Decision 0008: model substitution by the server is reported in ModelId by default, and throws when
/// EmberLlmOptions.StrictModelMatching is on.
/// </summary>
public class StrictModelMatchingTests
{
    private static readonly ChatMessage[] Messages = [new(ChatRole.User, "Hi")];

    [Fact]
    public async Task Default_SubstitutedModel_IsReportedInModelId()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion(model: "loaded-model"));
        using var client = TestClients.Create(handler);

        var response = await client.GetResponseAsync(Messages, new ChatOptions { ModelId = "requested-model" }, TestContext.Current.CancellationToken);

        Assert.Equal("loaded-model", response.ModelId);
    }

    [Fact]
    public async Task Strict_SubstitutedModel_Throws()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion(model: "loaded-model"));
        using var client = TestClients.Create(handler, o => o.StrictModelMatching = true);

        var ex = await Assert.ThrowsAsync<ModelMismatchException>(() =>
            client.GetResponseAsync(Messages, new ChatOptions { ModelId = "requested-model" }, TestContext.Current.CancellationToken));

        Assert.Equal("requested-model", ex.RequestedModel);
        Assert.Equal("loaded-model", ex.ReportedModel);
    }

    [Fact]
    public async Task Strict_SameModelDifferentCase_Passes()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion(model: "Google/Gemma-4-12B"));
        using var client = TestClients.Create(handler, o => o.StrictModelMatching = true);

        var response = await client.GetResponseAsync(Messages, new ChatOptions { ModelId = "google/gemma-4-12b" }, TestContext.Current.CancellationToken);

        Assert.Equal("Google/Gemma-4-12B", response.ModelId);
    }

    [Fact]
    public async Task Strict_AppliesToDefaultModelToo()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion(model: "loaded-model"));
        using var client = TestClients.Create(handler, o => { o.DefaultModel = "default-model"; o.StrictModelMatching = true; });

        await Assert.ThrowsAsync<ModelMismatchException>(() => client.CompleteAsync("Hi", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Strict_ReplyWithoutModel_Throws()
    {
        const string json = """{ "choices": [ { "index": 0, "message": { "role": "assistant", "content": "Hi" }, "finish_reason": "stop" } ] }""";
        var handler = new FakeHttpHandler().RespondJson(json);
        using var client = TestClients.Create(handler, o => o.StrictModelMatching = true);

        var ex = await Assert.ThrowsAsync<ModelMismatchException>(() =>
            client.GetResponseAsync(Messages, new ChatOptions { ModelId = "m" }, TestContext.Current.CancellationToken));

        Assert.Null(ex.ReportedModel);
    }

    [Fact]
    public async Task Strict_Streaming_SubstitutedModel_ThrowsBeforeAnyUpdate()
    {
        // SampleStreams chunks report model "qwen3-14b"
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(SampleStreams.Haiku()));
        using var client = TestClients.Create(handler, o => o.StrictModelMatching = true);
        var received = new List<ChatResponseUpdate>();

        await Assert.ThrowsAsync<ModelMismatchException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync(Messages, new ChatOptions { ModelId = "requested-model" }, TestContext.Current.CancellationToken))
            {
                received.Add(update);
            }
        });

        Assert.Empty(received);
    }

    [Fact]
    public async Task Strict_Streaming_MatchingModel_Passes()
    {
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(SampleStreams.Haiku()));
        using var client = TestClients.Create(handler, o => o.StrictModelMatching = true);
        var received = new List<ChatResponseUpdate>();

        await foreach (var update in client.GetStreamingResponseAsync(Messages, new ChatOptions { ModelId = "qwen3-14b" }, TestContext.Current.CancellationToken))
        {
            received.Add(update);
        }

        Assert.Equal(6, received.Count);
    }

    [Fact]
    public async Task Strict_Streaming_NoChunkReportsModel_ThrowsAtEnd()
    {
        var sse = SampleStreams.Sse("""{ "choices": [ { "index": 0, "delta": { "content": "Hi" } } ] }""");
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse));
        using var client = TestClients.Create(handler, o => o.StrictModelMatching = true);

        await Assert.ThrowsAsync<ModelMismatchException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync(Messages, new ChatOptions { ModelId = "m" }, TestContext.Current.CancellationToken))
            {
            }
        });
    }
}
