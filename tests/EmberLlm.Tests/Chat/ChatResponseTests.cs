using System.Text.Json;
using EmberLlm.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace EmberLlm.Tests.Chat;

public class ChatResponseTests
{
    private static readonly ChatRequest Request = new() { Model = "qwen3-14b", Messages = [new(ChatRole.User, "Hi")] };

    [Fact]
    public async Task ChatAsync_MapsTextModelUsageAndFinishReason()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion("Hello there!"));
        using var client = TestClients.Create(handler);

        var response = await client.ChatAsync(Request, TestContext.Current.CancellationToken);

        Assert.Equal("Hello there!", response.Text);
        Assert.Equal(ChatRole.Assistant, Assert.Single(response.Messages).Role);
        Assert.Equal("chatcmpl-123", response.ResponseId);
        Assert.Equal("qwen3-14b", response.ModelId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1760000000), response.CreatedAt);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.NotNull(response.Usage);
        Assert.Equal(12, response.Usage.InputTokenCount);
        Assert.Equal(3, response.Usage.OutputTokenCount);
        Assert.Equal(15, response.Usage.TotalTokenCount);
    }

    [Theory]
    [InlineData("length", "length")]
    [InlineData("tool_calls", "tool_calls")]
    [InlineData("content_filter", "content_filter")]
    [InlineData("eos", "eos")]
    public async Task ChatAsync_MapsFinishReason(string wireValue, string expected)
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion(finishReason: wireValue));
        using var client = TestClients.Create(handler);

        var response = await client.ChatAsync(Request, TestContext.Current.CancellationToken);

        Assert.Equal(new ChatFinishReason(expected), response.FinishReason);
    }

    [Fact]
    public async Task ChatAsync_ReasoningContent_IsSeparateFromText()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.CompletionWithReasoning);
        using var client = TestClients.Create(handler);

        var response = await client.ChatAsync(Request, TestContext.Current.CancellationToken);

        Assert.Equal("42", response.Text);
        var reasoning = Assert.Single(response.Messages[0].Contents.OfType<TextReasoningContent>());
        Assert.Equal("The user wants the answer.", reasoning.Text);
    }

    [Fact]
    public async Task ChatAsync_UnknownFields_LandInAdditionalProperties()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.CompletionWithTimings);
        using var client = TestClients.Create(handler);

        var response = await client.ChatAsync(Request, TestContext.Current.CancellationToken);

        Assert.NotNull(response.AdditionalProperties);
        var timings = Assert.IsType<JsonElement>(response.AdditionalProperties["timings"]);
        Assert.Equal(42.1, timings.GetProperty("predicted_per_second").GetDouble());
        Assert.Null(response.Usage);
    }

    [Fact]
    public async Task ChatAsync_NoChoices_ThrowsEmberLlmException()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.CompletionWithNoChoices);
        using var client = TestClients.Create(handler);

        var ex = await Assert.ThrowsAsync<EmberLlmException>(() => client.ChatAsync(Request, TestContext.Current.CancellationToken));
        Assert.Contains("no choices", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatAsync_InvalidJson_ThrowsEmberLlmException()
    {
        var handler = new FakeHttpHandler().Respond(System.Net.HttpStatusCode.OK, "not json");
        using var client = TestClients.Create(handler);

        var ex = await Assert.ThrowsAsync<EmberLlmException>(() => client.ChatAsync(Request, TestContext.Current.CancellationToken));
        Assert.IsType<JsonException>(ex.InnerException, exactMatch: false);
    }
}
