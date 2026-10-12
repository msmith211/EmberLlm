using EmberLlm.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace EmberLlm.Tests;

/// <summary>
/// EmberLlmClient as a Microsoft.Extensions.AI IChatClient (decision 0005).
/// </summary>
public class ChatClientTests
{
    [Fact]
    public async Task WorksThroughTheIChatClientInterface()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion("Hello there!"));
        using var client = TestClients.Create(handler, o => o.DefaultModel = "m");
        IChatClient chat = client;

        // GetResponseAsync(string) is a Microsoft.Extensions.AI extension method, available to every IChatClient
        var response = await chat.GetResponseAsync("Hi", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Hello there!", response.Text);
    }

    [Fact]
    public void GetService_ReturnsMetadata()
    {
        using var client = new EmberLlmClient(new EmberLlmOptions { BaseUrl = "http://localhost:1234", DefaultModel = "qwen3-14b" });

        var metadata = client.GetService<ChatClientMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal("EmberLlm", metadata.ProviderName);
        Assert.Equal(new Uri("http://localhost:1234/v1/"), metadata.ProviderUri);
        Assert.Equal("qwen3-14b", metadata.DefaultModelId);
    }

    [Fact]
    public void GetService_ReturnsClientForItsOwnTypes()
    {
        using var client = new EmberLlmClient(TestClients.BaseUrl);

        Assert.Same(client, client.GetService<EmberLlmClient>());
        Assert.Same(client, client.GetService<IChatClient>());
    }

    [Fact]
    public void GetService_UnknownTypeOrKey_ReturnsNull()
    {
        using var client = new EmberLlmClient(TestClients.BaseUrl);

        Assert.Null(client.GetService<HttpClient>());
        Assert.Null(client.GetService(typeof(ChatClientMetadata), serviceKey: "some-key"));
    }

    [Fact]
    public async Task ConcurrentFirstCalls_ListModelsOnlyOnce()
    {
        // Call A asks for the model list and is held "in flight". Call B starts meanwhile and must wait
        // for A's answer instead of sending a second /v1/models request (the lock in ResolveModelAsync)
        var listArrived = new TaskCompletionSource();
        var releaseList = new TaskCompletionSource();
        var handler = new FakeHttpHandler()
            .RespondJsonWhenReleased(SampleResponses.Models("first-model"), listArrived, releaseList.Task)
            .RespondJson(SampleResponses.Completion())
            .RespondJson(SampleResponses.Completion());
        using var client = TestClients.Create(handler);
        var ct = TestContext.Current.CancellationToken;

        var callA = client.CompleteAsync("A", ct);
        await listArrived.Task.WaitAsync(ct);
        var callB = client.CompleteAsync("B", ct);

        // Give call B time to reach the lock; without the lock it would send its own /v1/models request now
        await Task.Delay(100, ct);
        releaseList.SetResult();
        await Task.WhenAll(callA, callB);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Get);
    }
}
