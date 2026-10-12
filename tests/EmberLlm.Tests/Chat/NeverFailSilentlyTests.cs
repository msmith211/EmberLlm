using System.Reflection;
using System.Text.Json;
using EmberLlm.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace EmberLlm.Tests.Chat;

/// <summary>
/// Decision 0006: everything a caller asks for is sent or rejected, and nothing the server sends is discarded.
/// </summary>
public class NeverFailSilentlyTests
{
    // Outgoing: message fields

    [Fact]
    public async Task AuthorName_IsSentAsName()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion());
        using var client = TestClients.Create(handler);
        var message = new ChatMessage(ChatRole.User, "Hi") { AuthorName = "alice" };

        await client.ChatAsync(new ChatRequest { Model = "m", Messages = [message] }, TestContext.Current.CancellationToken);

        Assert.Equal("alice", SentMessages(handler)[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task NoAuthorName_OmitsName()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion());
        using var client = TestClients.Create(handler);

        await client.ChatAsync(new ChatRequest { Model = "m", Messages = [new(ChatRole.User, "Hi")] }, TestContext.Current.CancellationToken);

        Assert.False(SentMessages(handler)[0].TryGetProperty("name", out _));
    }

    [Fact]
    public async Task MessageAdditionalProperties_ArePassedThroughAsFields()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion());
        using var client = TestClients.Create(handler);
        var message = new ChatMessage(ChatRole.User, "Hi")
        {
            AdditionalProperties = new()
            {
                ["text_field"] = "value",
                ["number_field"] = 42,
                ["bool_field"] = true,
                ["object_field"] = new Dictionary<string, int> { ["nested"] = 1 },
                ["json_field"] = JsonDocument.Parse("""{"raw":[1,2]}""").RootElement,
                ["null_field"] = null,
            },
        };

        await client.ChatAsync(new ChatRequest { Model = "m", Messages = [message] }, TestContext.Current.CancellationToken);

        var sent = SentMessages(handler)[0];
        Assert.Equal("value", sent.GetProperty("text_field").GetString());
        Assert.Equal(42, sent.GetProperty("number_field").GetInt32());
        Assert.True(sent.GetProperty("bool_field").GetBoolean());
        Assert.Equal(1, sent.GetProperty("object_field").GetProperty("nested").GetInt32());
        Assert.Equal(2, sent.GetProperty("json_field").GetProperty("raw").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, sent.GetProperty("null_field").ValueKind);
        Assert.Equal("Hi", sent.GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("role")]
    [InlineData("content")]
    [InlineData("name")]
    public async Task MessageAdditionalProperties_OverridingMappedField_Throws(string field)
    {
        var handler = new FakeHttpHandler();
        using var client = TestClients.Create(handler);
        var message = new ChatMessage(ChatRole.User, "Hi") { AdditionalProperties = new() { [field] = "x" } };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ChatAsync(new ChatRequest { Model = "m", Messages = [message] }, TestContext.Current.CancellationToken));

        Assert.Contains(field, ex.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    // Incoming: multiple choices

    [Fact]
    public async Task Response_WithMultipleChoices_Throws()
    {
        const string json = """
            { "id": "c", "model": "m", "choices": [
                { "index": 0, "message": { "role": "assistant", "content": "A" }, "finish_reason": "stop" },
                { "index": 1, "message": { "role": "assistant", "content": "B" }, "finish_reason": "stop" } ] }
            """;
        var handler = new FakeHttpHandler().RespondJson(json);
        using var client = TestClients.Create(handler, o => o.DefaultModel = "m");

        var ex = await Assert.ThrowsAsync<EmberLlmException>(() => client.CompleteAsync("Hi", TestContext.Current.CancellationToken));

        Assert.Contains("more than one choice", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Response_WithOnlyANonFirstChoice_Throws()
    {
        const string json = """{ "choices": [ { "index": 1, "message": { "role": "assistant", "content": "B" } } ] }""";
        var handler = new FakeHttpHandler().RespondJson(json);
        using var client = TestClients.Create(handler, o => o.DefaultModel = "m");

        await Assert.ThrowsAsync<EmberLlmException>(() => client.CompleteAsync("Hi", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Stream_ChunkForSecondChoice_Throws()
    {
        var sse = SampleStreams.Sse(
            SampleStreams.ContentChunk("A"),
            """{ "choices": [ { "index": 1, "delta": { "content": "B" } } ] }""");
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "m");
        var received = new List<string>();

        await Assert.ThrowsAsync<EmberLlmException>(async () =>
        {
            await foreach (var token in client.StreamAsync("Hi", TestContext.Current.CancellationToken))
            {
                received.Add(token);
            }
        });

        Assert.Equal(["A"], received);
    }

    // Incoming: fields EmberLlm doesn't model are kept

    [Fact]
    public async Task Response_UnmodeledMessageFields_LandInMessageAdditionalProperties()
    {
        const string json = """
            { "choices": [ { "index": 0, "finish_reason": "stop",
                "message": { "role": "assistant", "content": "Hi", "name": "bot", "refusal": null, "custom_note": "kept" } } ] }
            """;
        var handler = new FakeHttpHandler().RespondJson(json);
        using var client = TestClients.Create(handler, o => o.DefaultModel = "m");

        var response = await client.ChatAsync(new ChatRequest { Messages = [new(ChatRole.User, "Hi")] }, TestContext.Current.CancellationToken);

        var message = Assert.Single(response.Messages);
        Assert.Equal("bot", message.AuthorName);
        Assert.NotNull(message.AdditionalProperties);
        Assert.Equal("kept", Assert.IsType<JsonElement>(message.AdditionalProperties["custom_note"]).GetString());
        Assert.True(message.AdditionalProperties.ContainsKey("refusal"));
    }

    [Fact]
    public async Task Response_UnmodeledChoiceFields_LandInResponseAdditionalProperties()
    {
        const string json = """
            { "logprobs_summary": "top-level",
              "choices": [ { "index": 0, "finish_reason": "stop", "logprobs": { "content": [] }, "logprobs_summary": "choice-level",
                "message": { "role": "assistant", "content": "Hi" } } ] }
            """;
        var handler = new FakeHttpHandler().RespondJson(json);
        using var client = TestClients.Create(handler, o => o.DefaultModel = "m");

        var response = await client.ChatAsync(new ChatRequest { Messages = [new(ChatRole.User, "Hi")] }, TestContext.Current.CancellationToken);

        var properties = response.AdditionalProperties!;
        Assert.True(properties.ContainsKey("logprobs"));

        // Same key at two levels: both values survive
        Assert.Equal("top-level", Assert.IsType<JsonElement>(properties["logprobs_summary"]).GetString());
        Assert.Equal("choice-level", Assert.IsType<JsonElement>(properties["choice.logprobs_summary"]).GetString());
    }

    [Fact]
    public async Task Stream_UnmodeledDeltaAndChoiceFields_LandInUpdateAdditionalProperties()
    {
        var sse = SampleStreams.Sse("""{ "choices": [ { "index": 0, "logprobs": null, "delta": { "content": "Hi", "custom_note": "kept" } } ] }""");
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "m");

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.StreamUpdatesAsync(new ChatRequest { Messages = [new(ChatRole.User, "Hi")] }, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        var properties = Assert.Single(updates).AdditionalProperties!;
        Assert.True(properties.ContainsKey("logprobs"));
        Assert.Equal("kept", Assert.IsType<JsonElement>(properties["custom_note"]).GetString());
    }

    [Fact]
    public async Task Response_UnmodeledUsageFields_LandInResponseAdditionalProperties()
    {
        const string json = """
            { "choices": [ { "index": 0, "finish_reason": "stop", "message": { "role": "assistant", "content": "Hi" } } ],
              "usage": { "prompt_tokens": 10, "completion_tokens": 2, "total_tokens": 12,
                         "prompt_tokens_details": { "cached_tokens": 8 } } }
            """;
        var handler = new FakeHttpHandler().RespondJson(json);
        using var client = TestClients.Create(handler, o => o.DefaultModel = "m");

        var response = await client.ChatAsync(new ChatRequest { Messages = [new(ChatRole.User, "Hi")] }, TestContext.Current.CancellationToken);

        Assert.Equal(10, response.Usage?.InputTokenCount);
        var details = Assert.IsType<JsonElement>(response.AdditionalProperties!["usage.prompt_tokens_details"]);
        Assert.Equal(8, details.GetProperty("cached_tokens").GetInt32());
    }

    [Fact]
    public async Task Stream_UnmodeledUsageFields_LandInUpdateAdditionalProperties()
    {
        var sse = SampleStreams.Sse("""{ "choices": [], "usage": { "prompt_tokens": 10, "completion_tokens": 2, "completion_tokens_details": { "reasoning_tokens": 1 } } }""");
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "m");

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.StreamUpdatesAsync(new ChatRequest { Messages = [new(ChatRole.User, "Hi")] }, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        Assert.True(Assert.Single(updates).AdditionalProperties!.ContainsKey("usage.completion_tokens_details"));
    }

    // Incoming: SSE event names

    [Fact]
    public async Task Stream_ErrorNamedEvent_Throws_EvenWithoutErrorField()
    {
        var sse = $"data: {SampleStreams.ContentChunk("Heap")}\n\nevent: error\ndata: {{\"message\":\"model unloaded\"}}\n\ndata: [DONE]\n\n";
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "m");
        var received = new List<string>();

        var ex = await Assert.ThrowsAsync<EmberLlmException>(async () =>
        {
            await foreach (var token in client.StreamAsync("Hi", TestContext.Current.CancellationToken))
            {
                received.Add(token);
            }
        });

        Assert.Equal(["Heap"], received);
        Assert.Contains("model unloaded", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stream_CustomEventName_IsKeptInAdditionalProperties()
    {
        var sse = $"event: completion\ndata: {SampleStreams.ContentChunk("Heap")}\n\nevent: message\ndata: {SampleStreams.ContentChunk(" fills")}\n\ndata: [DONE]\n\n";
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(sse));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "m");

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.StreamUpdatesAsync(new ChatRequest { Messages = [new(ChatRole.User, "Hi")] }, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        Assert.Equal("completion", updates[0].AdditionalProperties?["sse.event"]);

        // "message" is the SSE default name, so it isn't recorded
        Assert.Null(updates[1].AdditionalProperties);
    }

    // Guard: every ChatMessage property is classified

    private static readonly HashSet<string> MappedMessageProperties = ["Role", "Contents", "AuthorName", "AdditionalProperties"];

    // Bookkeeping the caller attaches to a message; no server field exists for these and they never affect the reply
    private static readonly HashSet<string> IgnoredMetadataProperties = ["MessageId", "CreatedAt", "RawRepresentation"];

    [Fact]
    public void EveryChatMessageProperty_IsMappedOrDocumentedAsMetadata()
    {
        // A Microsoft.Extensions.AI upgrade that adds a ChatMessage property fails this test until
        // someone decides whether EmberLlm sends it, rejects it, or ignores it as metadata.
        var settable = typeof(ChatMessage)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod?.IsPublic == true)
            .Select(p => p.Name)
            .ToHashSet();

        var unclassified = settable.Except(MappedMessageProperties).Except(IgnoredMetadataProperties).Order().ToList();
        var stale = MappedMessageProperties.Union(IgnoredMetadataProperties).Except(settable).Order().ToList();

        Assert.True(unclassified.Count == 0, $"Unclassified ChatMessage properties (see decision 0006): {string.Join(", ", unclassified)}");
        Assert.True(stale.Count == 0, $"Classified properties that no longer exist on ChatMessage: {string.Join(", ", stale)}");
    }

    private static List<JsonElement> SentMessages(FakeHttpHandler handler)
    {
        var body = JsonDocument.Parse(handler.Requests[^1].Body!);
        return [.. body.RootElement.GetProperty("messages").EnumerateArray()];
    }
}
