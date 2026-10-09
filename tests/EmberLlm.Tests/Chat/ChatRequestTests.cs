using System.Text.Json;
using EmberLlm.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace EmberLlm.Tests.Chat;

public class ChatRequestTests
{
    [Fact]
    public async Task ChatAsync_PostsOpenAiShapedRequest()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion());
        using var client = TestClients.Create(handler);

        await client.ChatAsync(new ChatRequest
        {
            Model = "qwen3-14b",
            Messages = [new(ChatRole.System, "You are terse."), new(ChatRole.User, "Hi")],
            Temperature = 0.2f,
            MaxTokens = 256,
        }, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("http://localhost:1234/v1/chat/completions"), request.Uri);

        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        Assert.Equal("qwen3-14b", root.GetProperty("model").GetString());
        Assert.Equal(0.2f, root.GetProperty("temperature").GetSingle());
        Assert.Equal(256, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.GetProperty("stream").GetBoolean());

        var messages = root.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are terse.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("Hi", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ChatAsync_OmitsUnsetOptionalFields()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion());
        using var client = TestClients.Create(handler);

        await client.ChatAsync(new ChatRequest { Model = "m", Messages = [new(ChatRole.User, "Hi")] }, TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.False(body.RootElement.TryGetProperty("temperature", out _));
        Assert.False(body.RootElement.TryGetProperty("max_tokens", out _));
    }

    [Fact]
    public async Task CompleteAsync_SendsSingleUserMessageAndReturnsText()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion("Span<T> is a view over memory."));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "qwen3-14b");

        var answer = await client.CompleteAsync("Explain Span<T>", TestContext.Current.CancellationToken);

        Assert.Equal("Span<T> is a view over memory.", answer);
        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        var message = Assert.Single(body.RootElement.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("Explain Span<T>", message.GetProperty("content").GetString());
    }

    [Fact]
    public async Task ChatAsync_DoesNotSendReasoningFromEarlierTurns()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion());
        using var client = TestClients.Create(handler);
        var assistantTurn = new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("thinking..."), new TextContent("42")]);

        await client.ChatAsync(new ChatRequest
        {
            Model = "m",
            Messages = [new(ChatRole.User, "What is the answer?"), assistantTurn, new(ChatRole.User, "Why?")],
        }, TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests[0].Body!);
        var messages = body.RootElement.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal("42", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task ChatAsync_NonTextContent_ThrowsNotSupported()
    {
        using var client = TestClients.Create(new FakeHttpHandler());
        var message = new ChatMessage(ChatRole.User, [new UriContent(new Uri("https://example.com/cat.png"), "image/png")]);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            client.ChatAsync(new ChatRequest { Model = "m", Messages = [message] }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChatAsync_NoMessages_ThrowsArgumentException()
    {
        using var client = TestClients.Create(new FakeHttpHandler());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.ChatAsync(new ChatRequest { Model = "m" }, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://localhost:1234")]
    [InlineData("http://localhost:1234/")]
    [InlineData("http://localhost:1234/v1")]
    [InlineData("http://localhost:1234/v1/")]
    public async Task BaseUrl_WithOrWithoutV1_TargetsSameEndpoint(string baseUrl)
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion());
        using var client = new EmberLlmClient(new HttpClient(handler), new EmberLlmOptions { BaseUrl = baseUrl });

        await client.ChatAsync(new ChatRequest { Model = "m", Messages = [new(ChatRole.User, "Hi")] }, TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("http://localhost:1234/v1/chat/completions"), handler.Requests[0].Uri);
    }

    [Fact]
    public async Task BaseUrl_WithPathPrefix_IsPreserved()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion());
        using var client = new EmberLlmClient(new HttpClient(handler), new EmberLlmOptions { BaseUrl = "http://gpu-box:8080/llm" });

        await client.ChatAsync(new ChatRequest { Model = "m", Messages = [new(ChatRole.User, "Hi")] }, TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("http://gpu-box:8080/llm/v1/chat/completions"), handler.Requests[0].Uri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("localhost:1234")]
    [InlineData("ftp://localhost:1234")]
    public void BaseUrl_Invalid_ThrowsArgumentException(string? baseUrl)
    {
        Assert.Throws<ArgumentException>(() => new EmberLlmClient(new EmberLlmOptions { BaseUrl = baseUrl }));
    }
}
