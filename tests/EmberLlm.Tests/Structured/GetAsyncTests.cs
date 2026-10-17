using System.Net;
using System.Text.Json;
using EmberLlm.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace EmberLlm.Tests.Structured;

public class GetAsyncTests
{
    public sealed record Person(string Name, int Age);

    public sealed record Tagged(string Title, string[] Tags);

    private const string ValidPerson = """{"name":"Ada","age":36}""";

    // SampleResponses.Completion inserts content verbatim, so it must already be JSON-escaped
    private static string Reply(string text) => SampleResponses.Completion(JsonEncodedText.Encode(text).ToString());

    private static EmberLlmClient CreateClient(FakeHttpHandler handler, StructuredOutputMode mode = StructuredOutputMode.JsonSchema, int? maxRetries = null) =>
        TestClients.Create(handler, o =>
        {
            o.DefaultModel = "qwen3-14b";
            o.StructuredOutputMode = mode;
            if (maxRetries is { } retries)
            {
                o.MaxStructuredRetries = retries;
            }
        });

    private static JsonElement Body(FakeHttpHandler handler, int index) => JsonDocument.Parse(handler.Requests[index].Body!).RootElement;

    private static List<JsonElement> Messages(FakeHttpHandler handler, int index) =>
        [.. Body(handler, index).GetProperty("messages").EnumerateArray()];

    [Fact]
    public async Task JsonSchemaMode_SendsTheSchemaAsResponseFormat()
    {
        var handler = new FakeHttpHandler().RespondJson(Reply(ValidPerson));
        using var client = CreateClient(handler);

        var person = await client.GetAsync<Person>("Describe Ada Lovelace.", TestContext.Current.CancellationToken);

        Assert.Equal(new Person("Ada", 36), person);
        var format = Body(handler, 0).GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        var schema = format.GetProperty("json_schema").GetProperty("schema");
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("name").GetProperty("type").GetString());
        Assert.Equal("integer", schema.GetProperty("properties").GetProperty("age").GetProperty("type").GetString());
    }

    [Fact]
    public async Task PromptMode_PutsTheSchemaInTheConversationAndSendsNoResponseFormat()
    {
        var handler = new FakeHttpHandler().RespondJson(Reply(ValidPerson));
        using var client = CreateClient(handler, StructuredOutputMode.Prompt);

        var person = await client.GetAsync<Person>("Describe Ada Lovelace.", TestContext.Current.CancellationToken);

        Assert.Equal(new Person("Ada", 36), person);
        var body = Body(handler, 0);
        Assert.False(body.TryGetProperty("response_format", out _), "Prompt mode must never send response_format (LM Studio rejects json_object).");
        var messages = Messages(handler, 0);
        Assert.Equal("Describe Ada Lovelace.", messages[0].GetProperty("content").GetString());
        var schemaMessage = messages[^1].GetProperty("content").GetString()!;
        Assert.Contains("\"name\"", schemaMessage, StringComparison.Ordinal);
        Assert.Contains("\"age\"", schemaMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(StructuredOutputMode.JsonSchema)]
    [InlineData(StructuredOutputMode.Prompt)]
    public async Task ThinkPrefixedReply_ParsesOnTheFirstAttempt(StructuredOutputMode mode)
    {
        var handler = new FakeHttpHandler().RespondJson(Reply("<think>Ada was born in 1815.</think>\n" + ValidPerson));
        using var client = CreateClient(handler, mode);

        var person = await client.GetAsync<Person>("Describe Ada Lovelace.", TestContext.Current.CancellationToken);

        Assert.Equal(new Person("Ada", 36), person);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(StructuredOutputMode.JsonSchema)]
    [InlineData(StructuredOutputMode.Prompt)]
    public async Task FencedReply_ParsesOnTheFirstAttempt(StructuredOutputMode mode)
    {
        var handler = new FakeHttpHandler().RespondJson(Reply("Here you go:\n```json\n" + ValidPerson + "\n```"));
        using var client = CreateClient(handler, mode);

        var person = await client.GetAsync<Person>("Describe Ada Lovelace.", TestContext.Current.CancellationToken);

        Assert.Equal(new Person("Ada", 36), person);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ValidJson_IsNotCleaned()
    {
        // Cleanup would cut this string at </think>; valid replies must pass through untouched
        var handler = new FakeHttpHandler().RespondJson(Reply("""{"title":"Tags like </think> are reasoning","tags":["a"]}"""));
        using var client = CreateClient(handler);

        var tagged = await client.GetAsync<Tagged>("Go.", TestContext.Current.CancellationToken);

        Assert.Equal("Tags like </think> are reasoning", tagged.Title);
    }

    [Fact]
    public async Task MalformedFirstReply_TriggersExactlyOneCorrectiveRetry()
    {
        var handler = new FakeHttpHandler()
            .RespondJson(Reply("""{"name": "Ada", "age": """))
            .RespondJson(Reply(ValidPerson));
        using var client = CreateClient(handler);

        var person = await client.GetAsync<Person>("Describe Ada Lovelace.", TestContext.Current.CancellationToken);

        Assert.Equal(new Person("Ada", 36), person);
        Assert.Equal(2, handler.Requests.Count);

        // The retry sends the original conversation, the bad reply, and the error
        var retry = Messages(handler, 1);
        Assert.Equal(3, retry.Count);
        Assert.Equal("Describe Ada Lovelace.", retry[0].GetProperty("content").GetString());
        Assert.Equal("assistant", retry[1].GetProperty("role").GetString());
        Assert.Equal("""{"name": "Ada", "age": """, retry[1].GetProperty("content").GetString());
        Assert.Equal("user", retry[2].GetProperty("role").GetString());
        Assert.StartsWith("That reply could not be used: ", retry[2].GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.Equal("json_schema", Body(handler, 1).GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ExhaustingRetries_ThrowsWithEveryAttempt()
    {
        var handler = new FakeHttpHandler()
            .RespondJson(Reply("not json"))
            .RespondJson(Reply("""{"name":"Ada"}"""))
            .RespondJson(Reply("""{"name":null,"age":36}"""));
        using var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<StructuredOutputException>(
            () => client.GetAsync<Person>("Describe Ada Lovelace.", TestContext.Current.CancellationToken));

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(3, ex.Attempts.Count);
        Assert.Equal(["not json", """{"name":"Ada"}""", """{"name":null,"age":36}"""], ex.Attempts.Select(a => a.RawText));
        Assert.All(ex.Attempts, a => Assert.False(string.IsNullOrWhiteSpace(a.Error)));
        Assert.Equal("""{"name":null,"age":36}""", ex.RawText);
        Assert.Contains("3 attempts", ex.Message, StringComparison.Ordinal);
        Assert.IsType<JsonException>(ex.InnerException, exactMatch: false);
    }

    [Fact]
    public async Task MissingRequiredMember_IsAFailedAttempt()
    {
        var handler = new FakeHttpHandler()
            .RespondJson(Reply("""{"name":"Ada"}"""))
            .RespondJson(Reply(ValidPerson));
        using var client = CreateClient(handler);

        await client.GetAsync<Person>("Go.", TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task NullForANonNullableMember_IsAFailedAttempt()
    {
        var handler = new FakeHttpHandler()
            .RespondJson(Reply("""{"name":null,"age":36}"""))
            .RespondJson(Reply(ValidPerson));
        using var client = CreateClient(handler);

        await client.GetAsync<Person>("Go.", TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task JsonNull_IsAFailedAttempt()
    {
        var handler = new FakeHttpHandler().RespondJson(Reply("null"));
        using var client = CreateClient(handler, maxRetries: 0);

        var ex = await Assert.ThrowsAsync<StructuredOutputException>(
            () => client.GetAsync<Person>("Go.", TestContext.Current.CancellationToken));

        Assert.Equal("null", ex.RawText);
    }

    [Fact]
    public async Task MaxStructuredRetriesZero_MakesOneAttempt()
    {
        var handler = new FakeHttpHandler().RespondJson(Reply("not json"));
        using var client = CreateClient(handler, maxRetries: 0);

        var ex = await Assert.ThrowsAsync<StructuredOutputException>(
            () => client.GetAsync<Person>("Go.", TestContext.Current.CancellationToken));

        Assert.Single(handler.Requests);
        Assert.Single(ex.Attempts);
        Assert.Contains("1 attempt.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerError_IsNotRetried()
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.OpenAiError, HttpStatusCode.BadRequest);
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<EmberLlmHttpException>(
            () => client.GetAsync<Person>("Go.", TestContext.Current.CancellationToken));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ResponseFormatSet_ThrowsBeforeSending()
    {
        var handler = new FakeHttpHandler();
        using var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.GetAsync<Person>(
            [new(ChatRole.User, "Go.")], new ChatOptions { ResponseFormat = ChatResponseFormat.Json }, TestContext.Current.CancellationToken));

        Assert.Equal("options", ex.ParamName);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ChatOptions_AreSentOnEveryAttempt()
    {
        var handler = new FakeHttpHandler()
            .RespondJson(Reply("not json"))
            .RespondJson(Reply(ValidPerson));
        using var client = CreateClient(handler);

        await client.GetAsync<Person>(
            [new(ChatRole.User, "Go.")],
            new ChatOptions { ModelId = "gemma-3-12b", Temperature = 0.1f, Instructions = "Be accurate." },
            TestContext.Current.CancellationToken);

        for (var i = 0; i < 2; i++)
        {
            var body = Body(handler, i);
            Assert.Equal("gemma-3-12b", body.GetProperty("model").GetString());
            Assert.Equal(0.1f, body.GetProperty("temperature").GetSingle());
            Assert.Equal("Be accurate.", Messages(handler, i)[0].GetProperty("content").GetString());
        }
    }

    [Fact]
    public async Task CallerSerializerOptions_AreUsedForSchemaAndParsing()
    {
        var snakeCase = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var handler = new FakeHttpHandler().RespondJson(Reply("""{"title":"T","tags":["x","y"]}"""));
        using var client = CreateClient(handler);

        var tagged = await client.GetAsync<Tagged>([new(ChatRole.User, "Go.")], snakeCase, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["x", "y"], tagged.Tags);
        var properties = Body(handler, 0).GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").GetProperty("properties");
        Assert.True(properties.TryGetProperty("tags", out _));
    }

    [Fact]
    public async Task NonObjectType_RoundTrips()
    {
        // GetResponseAsync<T> wraps non-object schemas in {"data": ...}
        var handler = new FakeHttpHandler().RespondJson(Reply("""{"data":[3,1,2]}"""));
        using var client = CreateClient(handler);

        var numbers = await client.GetAsync<int[]>("Three numbers.", TestContext.Current.CancellationToken);

        Assert.Equal([3, 1, 2], numbers);
    }

    [Fact]
    public async Task NoModelNamed_ResolvesOneFirst()
    {
        var handler = new FakeHttpHandler()
            .RespondJson(SampleResponses.Models("qwen3-14b"))
            .RespondJson(Reply(ValidPerson));
        using var client = TestClients.Create(handler);

        await client.GetAsync<Person>("Go.", TestContext.Current.CancellationToken);

        Assert.Equal("qwen3-14b", Body(handler, 1).GetProperty("model").GetString());
    }

    [Theory]
    [InlineData(BackendType.Generic, StructuredOutputMode.Prompt)]
    [InlineData(BackendType.LmStudio, StructuredOutputMode.JsonSchema)]
    [InlineData(BackendType.Ollama, StructuredOutputMode.JsonSchema)]
    [InlineData(BackendType.LlamaCpp, StructuredOutputMode.JsonSchema)]
    [InlineData(BackendType.Vllm, StructuredOutputMode.JsonSchema)]
    public void DiscoveredServer_PicksTheModeFromTheBackend(BackendType backend, StructuredOutputMode expected)
    {
        var server = new DiscoveredServer(new Uri("http://127.0.0.1:1234/"), backend, new([]));
        using var client = new EmberLlmClient(server);

        Assert.Equal(expected, client.StructuredOutputMode);
    }

    [Fact]
    public void Options_DefaultToJsonSchemaAndTwoRetries()
    {
        var options = new EmberLlmOptions();

        Assert.Equal(StructuredOutputMode.JsonSchema, options.StructuredOutputMode);
        Assert.Equal(2, options.MaxStructuredRetries);
    }

    [Fact]
    public void Options_NegativeRetriesAreRejected()
    {
        var options = new EmberLlmOptions { BaseUrl = TestClients.BaseUrl, MaxStructuredRetries = -1 };

        Assert.Throws<ArgumentOutOfRangeException>(() => new EmberLlmClient(options));
    }

    [Fact]
    public void Options_UndefinedModeIsRejected()
    {
        var options = new EmberLlmOptions { BaseUrl = TestClients.BaseUrl, StructuredOutputMode = (StructuredOutputMode)42 };

        Assert.Throws<ArgumentOutOfRangeException>(() => new EmberLlmClient(options));
    }
}
