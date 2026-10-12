using System.Net;
using System.Text.Json;
using EmberLlm.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace EmberLlm.Tests.Backends;

/// <summary>
/// Chat responses recorded from LM Studio with qwen/qwen3-4b-thinking-2507, a thinking model.
/// </summary>
public class LmStudioFixtureTests
{
    private static readonly ChatMessage[] Question = [new(ChatRole.User, "What is 17 + 25? Reply with just the number.")];

    private static readonly ChatOptions Options = new() { ModelId = "qwen/qwen3-4b-thinking-2507" };

    [Fact]
    public async Task ThinkingCompletion_SeparatesReasoningFromAnswer()
    {
        var handler = new FakeHttpHandler().RespondJson(Fixtures.Read("lmstudio/chat-completion-thinking.json"));
        using var client = TestClients.Create(handler);

        var response = await client.GetResponseAsync(Question, Options, TestContext.Current.CancellationToken);

        Assert.Equal("42", response.Text);
        var reasoning = Assert.Single(response.Messages[0].Contents.OfType<TextReasoningContent>());
        Assert.False(string.IsNullOrWhiteSpace(reasoning.Text));
        Assert.Equal("qwen/qwen3-4b-thinking-2507", response.ModelId);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.NotNull(response.Usage?.OutputTokenCount);

        // LM Studio's reasoning token count arrives in usage details, which EmberLlm keeps
        var details = Assert.IsType<JsonElement>(response.AdditionalProperties!["usage.completion_tokens_details"]);
        Assert.True(details.GetProperty("reasoning_tokens").GetInt32() > 0);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(7)]
    public async Task ThinkingStream_CombinesToSameAnswer(int chunkSize)
    {
        var stream = new ScriptedStream(Fixtures.ReadBytes("lmstudio/chat-stream-thinking.txt"), chunkSize);
        var handler = new FakeHttpHandler().RespondStream(stream);
        using var client = TestClients.Create(handler);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(Question, Options, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        var response = updates.ToChatResponse();
        Assert.Equal("42", response.Text);
        Assert.Contains(response.Messages[0].Contents, c => c is TextReasoningContent);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Equal(418, response.Usage?.OutputTokenCount);
    }

    [Fact]
    public async Task ThinkingStream_TextOnly_HasNoReasoning()
    {
        var handler = new FakeHttpHandler().RespondStream(new ScriptedStream(Fixtures.ReadBytes("lmstudio/chat-stream-thinking.txt")));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen/qwen3-4b-thinking-2507");

        var text = new List<string>();
        await foreach (var token in client.StreamAsync("What is 17 + 25?", TestContext.Current.CancellationToken))
        {
            text.Add(token);
        }

        Assert.Equal("42", string.Concat(text).Trim());
    }

    [Fact]
    public async Task UnknownModelError_ExposesServerMessage()
    {
        var handler = new FakeHttpHandler().RespondJson(Fixtures.Read("lmstudio/error-unknown-model.json"), HttpStatusCode.BadRequest);
        using var client = TestClients.Create(handler);

        var ex = await Assert.ThrowsAsync<EmberLlmHttpException>(() =>
            client.GetResponseAsync(Question, new ChatOptions { ModelId = "does-not-exist" }, TestContext.Current.CancellationToken));

        Assert.StartsWith("Invalid model identifier \"does-not-exist\"", ex.ServerMessage, StringComparison.Ordinal);
    }
}
