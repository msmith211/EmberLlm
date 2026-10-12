using System.Net;
using EmberLlm.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace EmberLlm.Tests.Discovery;

public class DiscoveryTests
{
    // Identification (LM Studio from real recordings; the others from documentation, see SampleBackends)

    [Fact]
    public async Task LmStudio_IsIdentified_AndNotMistakenForLlamaCppOrVllm()
    {
        // LM Studio answers /props and /version with HTTP 200 (and an error body), so status codes alone would mislead
        var network = new RoutingHttpHandler()
            .Server("127.0.0.1:1234", RoutingHttpHandler.LmStudioUnknownEndpoint)
            .Route("http://127.0.0.1:1234/v1/models", Fixtures.Read("lmstudio/models.json"))
            .Route("http://127.0.0.1:1234/api/v0/models", Fixtures.Read("lmstudio/api-v0-models.json"))
            .Route("http://127.0.0.1:1234/api/v1/models", Fixtures.Read("lmstudio/api-v1-models.json"));

        var server = Assert.Single(await FindAsync(network, "http://127.0.0.1:1234"));

        Assert.Equal(BackendType.LmStudio, server.Backend);
        Assert.Equal(new Uri("http://127.0.0.1:1234/"), server.BaseUrl);
        Assert.Equal(2, server.Models.Count);
        Assert.Null(server.Version);
    }

    [Fact]
    public async Task LmStudio_RecordedUnknownEndpointResponse_MatchesTheFake()
    {
        // Keeps RoutingHttpHandler.LmStudioUnknownEndpoint honest against the real recording
        var recorded = Fixtures.Read("lmstudio/unknown-endpoint-health.json");
        var faked = await RoutingHttpHandler.LmStudioUnknownEndpoint("/health").Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(recorded, faked);
    }

    [Fact]
    public async Task Ollama_IsIdentified_WithVersion()
    {
        var network = new RoutingHttpHandler()
            .Server("127.0.0.1:11434")
            .Route("http://127.0.0.1:11434/", SampleBackends.OllamaRoot, "text/plain")
            .Route("http://127.0.0.1:11434/api/version", SampleBackends.OllamaVersion)
            .Route("http://127.0.0.1:11434/v1/models", SampleBackends.OllamaModels);

        var server = Assert.Single(await FindAsync(network, "http://127.0.0.1:11434"));

        Assert.Equal(BackendType.Ollama, server.Backend);
        Assert.Equal("0.12.3", server.Version);
    }

    [Fact]
    public async Task LlamaCpp_IsIdentified_WithBuild()
    {
        var network = new RoutingHttpHandler()
            .Server("127.0.0.1:8080")
            .Route("http://127.0.0.1:8080/v1/models", SampleBackends.LlamaCppModels)
            .Route("http://127.0.0.1:8080/props", SampleBackends.LlamaCppProps);

        var server = Assert.Single(await FindAsync(network, "http://127.0.0.1:8080"));

        Assert.Equal(BackendType.LlamaCpp, server.Backend);
        Assert.Equal("b6500-abc1234", server.Version);
    }

    [Fact]
    public async Task Vllm_IsIdentified_WithVersion()
    {
        var network = new RoutingHttpHandler()
            .Server("127.0.0.1:8000")
            .Route("http://127.0.0.1:8000/v1/models", SampleBackends.VllmModels)
            .Route("http://127.0.0.1:8000/version", SampleBackends.VllmVersion);

        var server = Assert.Single(await FindAsync(network, "http://127.0.0.1:8000"));

        Assert.Equal(BackendType.Vllm, server.Backend);
        Assert.Equal("0.11.0", server.Version);
    }

    [Fact]
    public async Task UnknownOpenAiCompatibleServer_IsGeneric()
    {
        var network = new RoutingHttpHandler()
            .Server("127.0.0.1:9000")
            .Route("http://127.0.0.1:9000/v1/models", SampleBackends.GenericModels);

        var server = Assert.Single(await FindAsync(network, "http://127.0.0.1:9000"));

        Assert.Equal(BackendType.Generic, server.Backend);
        Assert.Null(server.Version);
    }

    [Fact]
    public async Task BackendIsIdentifiedByContent_NotByPort()
    {
        // An Ollama server on llama.cpp's default port is still Ollama
        var network = new RoutingHttpHandler()
            .Server("127.0.0.1:8080")
            .Route("http://127.0.0.1:8080/", SampleBackends.OllamaRoot, "text/plain")
            .Route("http://127.0.0.1:8080/v1/models", SampleBackends.OllamaModels);

        Assert.Equal(BackendType.Ollama, Assert.Single(await FindAsync(network, "http://127.0.0.1:8080")).Backend);
    }

    // What isn't a server

    [Fact]
    public async Task NonLlmWebServer_IsSkippedWithReason()
    {
        var network = new RoutingHttpHandler()
            .Server("127.0.0.1:8080")
            .Route("http://127.0.0.1:8080/v1/models", SampleBackends.HtmlNotFound, "text/html", HttpStatusCode.NotFound);

        var result = await FindAsync(network, "http://127.0.0.1:8080");

        Assert.Empty(result);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(new Uri("http://127.0.0.1:8080/"), skipped.Endpoint);
        Assert.Contains("404", skipped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonThatIsNotAModelList_IsSkippedWithReason()
    {
        // A server returning 200 with JSON that isn't a model list (as LM Studio does for unknown paths)
        var network = new RoutingHttpHandler().Server("127.0.0.1:8000", RoutingHttpHandler.LmStudioUnknownEndpoint);

        var result = await FindAsync(network, "http://127.0.0.1:8000");

        Assert.Empty(result);
        Assert.Contains("\"data\"", Assert.Single(result.Skipped).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingRunning_ReturnsEmpty_AndReportsNothing()
    {
        var result = await EmberLlmDiscovery.FindAsync(new HttpClient(new RoutingHttpHandler()), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public async Task HangingServer_IsTreatedAsAbsent_WithinTheTimeout()
    {
        var network = new RoutingHttpHandler().Hanging("127.0.0.1:1234");
        var options = Options("http://127.0.0.1:1234");
        options.ProbeTimeout = TimeSpan.FromMilliseconds(100);

        var started = DateTimeOffset.UtcNow;
        var result = await EmberLlmDiscovery.FindAsync(new HttpClient(network), options, TestContext.Current.CancellationToken);

        Assert.Empty(result);
        Assert.Empty(result.Skipped);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(2));
    }

    // Behavior

    [Fact]
    public async Task DefaultEndpoints_AreEachBackendsPortOn127001()
    {
        var network = new RoutingHttpHandler();

        await EmberLlmDiscovery.FindAsync(new HttpClient(network), cancellationToken: TestContext.Current.CancellationToken);

        var probed = network.Requests.Select(u => u.AbsoluteUri).Order().ToList();
        Assert.Equal(
            ["http://127.0.0.1:11434/v1/models", "http://127.0.0.1:1234/v1/models", "http://127.0.0.1:8000/v1/models", "http://127.0.0.1:8080/v1/models"],
            probed);
    }

    [Fact]
    public async Task SeveralServers_AreReturnedInEndpointOrder()
    {
        var network = new RoutingHttpHandler()
            .Server("127.0.0.1:11434")
            .Route("http://127.0.0.1:11434/", SampleBackends.OllamaRoot, "text/plain")
            .Route("http://127.0.0.1:11434/v1/models", SampleBackends.OllamaModels)
            .Server("gpu-box:8000")
            .Route("http://gpu-box:8000/v1/models", SampleBackends.VllmModels);

        var result = await FindAsync(network, "http://gpu-box:8000", "http://127.0.0.1:1234", "http://127.0.0.1:11434");

        Assert.Equal([BackendType.Vllm, BackendType.Ollama], result.Select(s => s.Backend));
    }

    [Fact]
    public async Task SameServerListedTwice_IsProbedOnce()
    {
        var network = new RoutingHttpHandler()
            .Server("127.0.0.1:9000")
            .Route("http://127.0.0.1:9000/v1/models", SampleBackends.GenericModels);

        var result = await FindAsync(network, "http://127.0.0.1:9000", "http://127.0.0.1:9000/v1/");

        Assert.Single(result);
        Assert.Single(network.Requests, u => u.AbsolutePath == "/v1/models");
    }

    [Fact]
    public async Task CallerCancellation_Throws()
    {
        var network = new RoutingHttpHandler().Hanging("127.0.0.1:1234");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
        var options = Options("http://127.0.0.1:1234");
        options.ProbeTimeout = TimeSpan.FromSeconds(30);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EmberLlmDiscovery.FindAsync(new HttpClient(network), options, cancellation.Token));
    }

    [Fact]
    public async Task InvalidProbeTimeout_Throws()
    {
        var options = new DiscoveryOptions { ProbeTimeout = TimeSpan.Zero };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => EmberLlmDiscovery.FindAsync(options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void DiscoveredServer_CanBeUsedToCreateAClient()
    {
        var models = new ModelList([new ModelInfo("m")]);
        var server = new DiscoveredServer(new Uri("http://127.0.0.1:1234/"), BackendType.LmStudio, models);

        // This constructor creates its own HttpClient, so check the URL the client was given
        using var client = new EmberLlmClient(server);

        Assert.Equal(new Uri("http://127.0.0.1:1234/v1/"), client.GetService<ChatClientMetadata>()!.ProviderUri);
        Assert.Equal("LmStudio at http://127.0.0.1:1234/ (1 model)", server.ToString());
    }

    private static DiscoveryOptions Options(params string[] endpoints)
    {
        var options = new DiscoveryOptions();
        options.Endpoints.Clear();
        foreach (var endpoint in endpoints)
        {
            options.Endpoints.Add(new Uri(endpoint));
        }

        return options;
    }

    private static Task<DiscoveryResult> FindAsync(RoutingHttpHandler network, params string[] endpoints) =>
        EmberLlmDiscovery.FindAsync(new HttpClient(network), Options(endpoints), TestContext.Current.CancellationToken);
}
