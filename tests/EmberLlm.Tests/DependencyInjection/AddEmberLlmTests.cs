using System.Diagnostics;
using System.Text.Json.Nodes;
using EmberLlm.Tests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace EmberLlm.Tests.DependencyInjection;

public class AddEmberLlmTests
{
    [Fact]
    public void IChatClient_And_EmberLlmClient_AreTheSameSingleton()
    {
        var services = new ServiceCollection();
        services.AddEmberLlm(o => o.BaseUrl = TestClients.BaseUrl);
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<EmberLlmClient>();

        Assert.Same(client, provider.GetRequiredService<IChatClient>());
        Assert.Same(client, provider.GetRequiredService<EmberLlmClient>());
    }

    [Fact]
    public async Task Requests_GoThroughTheFactoryClient_WithConfiguredOptions()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion("Hi from DI"));
        var services = new ServiceCollection();
        services.AddEmberLlm(o =>
        {
            o.BaseUrl = TestClients.BaseUrl;
            o.DefaultModel = "qwen3-14b";
        }).ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var reply = await provider.GetRequiredService<IChatClient>().GetResponseAsync("Hello", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Hi from DI", reply.Text);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri("http://localhost:1234/v1/chat/completions"), request.Uri);
        Assert.Equal("qwen3-14b", JsonNode.Parse(request.Body!)!["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task ReturnedBuilder_AddsCallerHandlers()
    {
        var calls = 0;
        var services = new ServiceCollection();
        services.AddEmberLlm(o => o.BaseUrl = TestClients.BaseUrl)
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpHandler().RespondJson(SampleResponses.Completion()))
            .AddHttpMessageHandler(() => new CountingHandler(() => calls++));
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<EmberLlmClient>().GetResponseAsync("Hello", new ChatOptions { ModelId = "qwen3-14b" }, TestContext.Current.CancellationToken);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void HttpClient_HasNoTimeoutOfItsOwn()
    {
        var services = new ServiceCollection();
        services.AddEmberLlm(o => o.BaseUrl = TestClients.BaseUrl);
        using var provider = services.BuildServiceProvider();

        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(EmberLlmServiceCollectionExtensions.HttpClientName);

        Assert.Equal(Timeout.InfiniteTimeSpan, http.Timeout);
    }

    [Fact]
    public async Task DefaultHandler_ReachesIpv4OnlyServer_ViaLocalhost_Quickly()
    {
        using var listener = LoopbackServer.StartIpv4(out var port);
        var server = LoopbackServer.ServeOneResponseAsync(listener, SampleResponses.Models("qwen3-14b"), TestContext.Current.CancellationToken);
        var services = new ServiceCollection();
        services.AddEmberLlm(o => o.BaseUrl = $"http://localhost:{port}");
        using var provider = services.BuildServiceProvider();

        var stopwatch = Stopwatch.StartNew();
        var models = await provider.GetRequiredService<EmberLlmClient>().ListModelsAsync(TestContext.Current.CancellationToken);
        stopwatch.Stop();

        await server;
        Assert.Equal("qwen3-14b", Assert.Single(models).Id);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Request took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void MissingBaseUrl_ThrowsWhenResolved()
    {
        var services = new ServiceCollection();
        services.AddEmberLlm(o => o.DefaultModel = "qwen3-14b");
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<ArgumentException>(() => provider.GetRequiredService<IChatClient>());
        Assert.Contains("BaseUrl", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposingTheProvider_DisposesTheClient()
    {
        var services = new ServiceCollection();
        services.AddEmberLlm(o => o.BaseUrl = TestClients.BaseUrl);
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<EmberLlmClient>();

        await provider.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.CompleteAsync("Hi", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CalledTwice_RegistersOneClient_WithBothConfigureSteps()
    {
        var services = new ServiceCollection();
        services.AddEmberLlm(o => o.BaseUrl = TestClients.BaseUrl);
        services.AddEmberLlm(o => o.DefaultModel = "qwen3-14b");
        using var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IChatClient>());
        Assert.Single(provider.GetServices<EmberLlmClient>());
        var metadata = provider.GetRequiredService<IChatClient>().GetService<ChatClientMetadata>();
        Assert.Equal("qwen3-14b", metadata?.DefaultModelId);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddEmberLlm(_ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddEmberLlm(null!));
    }

    private sealed class CountingHandler(Action onSend) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onSend();
            return base.SendAsync(request, cancellationToken);
        }
    }
}
