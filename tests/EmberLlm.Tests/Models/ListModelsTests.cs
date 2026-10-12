using System.Net;
using System.Text.Json;
using EmberLlm.Tests.Fakes;

namespace EmberLlm.Tests.Models;

public class ListModelsTests
{
    // Recorded fixtures

    [Fact]
    public async Task LmStudioFixture_Parses()
    {
        var handler = new FakeHttpHandler().RespondJson(Fixtures.Read("lmstudio/models.json"));
        using var client = TestClients.Create(handler);

        var models = await client.ListModelsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["google/gemma-4-12b", "qwen/qwen3-4b-thinking-2507"], models.Select(m => m.Id));
        Assert.All(models, m => Assert.Equal("organization_owner", m.OwnedBy));

        // LM Studio sends no "created" field
        Assert.All(models, m => Assert.Null(m.CreatedAt));

        // "object" discriminators are kept, not dropped (decision 0006)
        Assert.Equal("model", Assert.IsType<JsonElement>(models[0].AdditionalProperties!["object"]).GetString());
        Assert.Equal("list", Assert.IsType<JsonElement>(models.AdditionalProperties!["object"]).GetString());

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("http://localhost:1234/v1/models"), request.Uri);
    }

    [Fact]
    public async Task OllamaFixture_Parses()
    {
        Assert.SkipUnless(Fixtures.Exists("ollama/models.json"), "No Ollama fixture recorded yet: Ollama isn't installed on the development machine. Record tests/fixtures/ollama/models.json from a real Ollama server to enable this test.");

        var handler = new FakeHttpHandler().RespondJson(Fixtures.Read("ollama/models.json"));
        using var client = TestClients.Create(handler);

        var models = await client.ListModelsAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(models);
        Assert.All(models, m => Assert.False(string.IsNullOrWhiteSpace(m.Id)));
    }

    [Fact]
    public async Task LmStudioFixture_FirstModelIsUsedWhenNoneConfigured()
    {
        var handler = new FakeHttpHandler()
            .RespondJson(Fixtures.Read("lmstudio/models.json"))
            .RespondJson(SampleResponses.Completion());
        using var client = TestClients.Create(handler);

        await client.CompleteAsync("Hi", TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Equal("google/gemma-4-12b", body.RootElement.GetProperty("model").GetString());
    }

    // Shapes from other servers (hand-written)

    [Fact]
    public async Task CreatedAndExtraFields_AreMapped()
    {
        const string json = """
            { "object": "list", "data": [
                { "id": "Qwen/Qwen3-8B", "object": "model", "created": 1760000000, "owned_by": "vllm", "max_model_len": 32768, "root": "Qwen/Qwen3-8B" } ] }
            """;
        var handler = new FakeHttpHandler().RespondJson(json);
        using var client = TestClients.Create(handler);

        var model = Assert.Single(await client.ListModelsAsync(TestContext.Current.CancellationToken));

        Assert.Equal("Qwen/Qwen3-8B", model.Id);
        Assert.Equal("vllm", model.OwnedBy);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1760000000), model.CreatedAt);
        Assert.Equal(32768, Assert.IsType<JsonElement>(model.AdditionalProperties!["max_model_len"]).GetInt32());
        Assert.Equal("Qwen/Qwen3-8B", model.ToString());
    }

    [Fact]
    public async Task EmptyList_IsEmpty()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Models());
        using var client = TestClients.Create(handler);

        Assert.Empty(await client.ListModelsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResponseWithoutData_Throws()
    {
        var handler = new FakeHttpHandler().RespondJson("""{ "models": [] }""");
        using var client = TestClients.Create(handler);

        var ex = await Assert.ThrowsAsync<EmberLlmException>(() => client.ListModelsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("\"data\"", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModelWithoutId_Throws_RatherThanBeingSkipped()
    {
        var handler = new FakeHttpHandler().RespondJson("""{ "data": [ { "id": "good" }, { "object": "model" } ] }""");
        using var client = TestClients.Create(handler);

        var ex = await Assert.ThrowsAsync<EmberLlmException>(() => client.ListModelsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("entry 1", ex.Message, StringComparison.Ordinal);
    }

    // Behavior

    [Fact]
    public async Task EachCall_AsksTheServerAgain()
    {
        var handler = new FakeHttpHandler()
            .RespondJson(SampleResponses.Models("a"))
            .RespondJson(SampleResponses.Models("a", "b"));
        using var client = TestClients.Create(handler);

        await client.ListModelsAsync(TestContext.Current.CancellationToken);
        var second = await client.ListModelsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, second.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ServerError_ThrowsHttpException()
    {
        var handler = new FakeHttpHandler().Respond(HttpStatusCode.InternalServerError, "boom");
        using var client = TestClients.Create(handler);

        await Assert.ThrowsAsync<EmberLlmHttpException>(() => client.ListModelsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposedClient_Throws()
    {
        var client = TestClients.Create(new FakeHttpHandler());
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ListModelsAsync(TestContext.Current.CancellationToken));
    }
}
