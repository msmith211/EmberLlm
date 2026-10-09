using System.Text.Json;
using EmberLlm.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace EmberLlm.Tests.Chat;

public class ModelSelectionTests
{
    [Fact]
    public async Task RequestModel_IsUsedWithoutListingModels()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion());
        using var client = TestClients.Create(handler, o => o.DefaultModel = "default-model");

        await client.ChatAsync(new ChatRequest { Model = "request-model", Messages = [new(ChatRole.User, "Hi")] }, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("request-model", SentModel(request));
    }

    [Fact]
    public async Task DefaultModel_IsUsedWhenRequestHasNone()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion());
        using var client = TestClients.Create(handler, o => o.DefaultModel = "default-model");

        await client.CompleteAsync("Hi", TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("default-model", SentModel(request));
    }

    [Fact]
    public async Task NoModelConfigured_UsesFirstListedModel_AndCachesIt()
    {
        var handler = new FakeHttpHandler()
            .RespondJson(SampleResponses.Models("first-model", "second-model"))
            .RespondJson(SampleResponses.Completion())
            .RespondJson(SampleResponses.Completion());
        using var client = TestClients.Create(handler);

        await client.CompleteAsync("Hi", TestContext.Current.CancellationToken);
        await client.CompleteAsync("Again", TestContext.Current.CancellationToken);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(new Uri("http://localhost:1234/v1/models"), handler.Requests[0].Uri);
        Assert.Equal("first-model", SentModel(handler.Requests[1]));
        Assert.Equal("first-model", SentModel(handler.Requests[2]));
    }

    [Fact]
    public async Task NoModelConfigured_AndServerListsNone_ThrowsNoModelAvailable()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Models());
        using var client = TestClients.Create(handler);

        await Assert.ThrowsAsync<NoModelAvailableException>(() => client.CompleteAsync("Hi", TestContext.Current.CancellationToken));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FailedModelListing_IsNotCached()
    {
        var handler = new FakeHttpHandler()
            .RespondJson(SampleResponses.Models())
            .RespondJson(SampleResponses.Models("loaded-later"))
            .RespondJson(SampleResponses.Completion());
        using var client = TestClients.Create(handler);

        await Assert.ThrowsAsync<NoModelAvailableException>(() => client.CompleteAsync("Hi", TestContext.Current.CancellationToken));
        await client.CompleteAsync("Hi", TestContext.Current.CancellationToken);

        Assert.Equal("loaded-later", SentModel(handler.Requests[2]));
    }

    [Fact]
    public async Task OptionsChangedAfterConstruction_DoNotAffectClient()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion());
        var options = new EmberLlmOptions { BaseUrl = TestClients.BaseUrl, DefaultModel = "original" };
        using var client = new EmberLlmClient(new HttpClient(handler), options);

        options.DefaultModel = "changed";
        await client.CompleteAsync("Hi", TestContext.Current.CancellationToken);

        Assert.Equal("original", SentModel(handler.Requests[0]));
    }

    private static string? SentModel(RecordedRequest request)
    {
        using var body = JsonDocument.Parse(request.Body!);
        return body.RootElement.GetProperty("model").GetString();
    }
}
