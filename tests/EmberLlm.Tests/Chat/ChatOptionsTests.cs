using System.Reflection;
using System.Text.Json;
using EmberLlm.Tests.Fakes;
using Microsoft.Extensions.AI;

namespace EmberLlm.Tests.Chat;

/// <summary>
/// The ChatOptions design rules in SPEC.md: every OpenAI-compatible option is sent, and everything
/// else throws before any request goes out.
/// </summary>
public class ChatOptionsTests
{
    private static readonly ChatMessage[] Messages = [new(ChatRole.User, "Hi")];

    // Mapped options

    [Fact]
    public async Task EveryOpenAiCompatibleOption_IsSent()
    {
        var body = await SendAsync(new ChatOptions
        {
            ModelId = "qwen3-14b",
            Temperature = 0.3f,
            MaxOutputTokens = 128,
            TopP = 0.9f,
            TopK = 40,
            StopSequences = ["END", "STOP"],
            Seed = 42,
            FrequencyPenalty = 0.5f,
            PresencePenalty = 0.25f,
        });

        Assert.Equal("qwen3-14b", body.GetProperty("model").GetString());
        Assert.Equal(0.3f, body.GetProperty("temperature").GetSingle());
        Assert.Equal(128, body.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.9f, body.GetProperty("top_p").GetSingle());
        Assert.Equal(40, body.GetProperty("top_k").GetInt32());
        Assert.Equal(["END", "STOP"], body.GetProperty("stop").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(42, body.GetProperty("seed").GetInt64());
        Assert.Equal(0.5f, body.GetProperty("frequency_penalty").GetSingle());
        Assert.Equal(0.25f, body.GetProperty("presence_penalty").GetSingle());
    }

    [Fact]
    public async Task Instructions_BecomeLeadingSystemMessage()
    {
        var body = await SendAsync(new ChatOptions { ModelId = "m", Instructions = "Be terse." });

        var messages = body.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("Be terse.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
    }

    [Theory]
    [InlineData(ReasoningEffort.None, "none")]
    [InlineData(ReasoningEffort.Low, "low")]
    [InlineData(ReasoningEffort.Medium, "medium")]
    [InlineData(ReasoningEffort.High, "high")]
    public async Task ReasoningEffort_IsSent(ReasoningEffort effort, string expected)
    {
        var body = await SendAsync(new ChatOptions { ModelId = "m", Reasoning = new ReasoningOptions { Effort = effort } });

        Assert.Equal(expected, body.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task ResponseFormat_Text_IsSent()
    {
        var body = await SendAsync(new ChatOptions { ModelId = "m", ResponseFormat = ChatResponseFormat.Text });

        Assert.Equal("text", body.GetProperty("response_format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ResponseFormat_JsonWithoutSchema_IsJsonObject()
    {
        var body = await SendAsync(new ChatOptions { ModelId = "m", ResponseFormat = ChatResponseFormat.Json });

        var format = body.GetProperty("response_format");
        Assert.Equal("json_object", format.GetProperty("type").GetString());
        Assert.False(format.TryGetProperty("json_schema", out _));
    }

    [Fact]
    public async Task ResponseFormat_JsonWithSchema_IsJsonSchema()
    {
        using var schema = JsonDocument.Parse("""{ "type": "object", "properties": { "answer": { "type": "integer" } } }""");
        var format = ChatResponseFormat.ForJsonSchema(schema.RootElement, schemaName: "answer", schemaDescription: "The answer");

        var body = await SendAsync(new ChatOptions { ModelId = "m", ResponseFormat = format });

        var sent = body.GetProperty("response_format");
        Assert.Equal("json_schema", sent.GetProperty("type").GetString());
        var jsonSchema = sent.GetProperty("json_schema");
        Assert.Equal("answer", jsonSchema.GetProperty("name").GetString());
        Assert.Equal("The answer", jsonSchema.GetProperty("description").GetString());
        Assert.Equal("integer", jsonSchema.GetProperty("schema").GetProperty("properties").GetProperty("answer").GetProperty("type").GetString());
    }

    [Fact]
    public async Task ResponseFormat_SchemaWithoutName_UsesDefaultName()
    {
        using var schema = JsonDocument.Parse("""{ "type": "object" }""");

        var body = await SendAsync(new ChatOptions { ModelId = "m", ResponseFormat = ChatResponseFormat.ForJsonSchema(schema.RootElement) });

        Assert.Equal("response", body.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString());
    }

    // Settings that ask for nothing EmberLlm can't do are accepted

    [Fact]
    public async Task HarmlessToolAndBackgroundSettings_AreAccepted()
    {
        var body = await SendAsync(new ChatOptions
        {
            ModelId = "m",
            Tools = [],
            ToolMode = ChatToolMode.Auto,
            AllowMultipleToolCalls = false,
            AllowBackgroundResponses = false,
        });

        Assert.False(body.TryGetProperty("tools", out _));
        Assert.False(body.TryGetProperty("tool_choice", out _));
    }

    // AdditionalProperties pass through

    [Fact]
    public async Task AdditionalProperties_ArePassedThroughAsTopLevelFields()
    {
        var body = await SendAsync(new ChatOptions
        {
            ModelId = "m",
            AdditionalProperties = new()
            {
                ["min_p"] = 0.05,
                ["keep_alive"] = "5m",
                ["options"] = new Dictionary<string, int> { ["num_ctx"] = 8192 },
            },
        });

        Assert.Equal(0.05, body.GetProperty("min_p").GetDouble());
        Assert.Equal("5m", body.GetProperty("keep_alive").GetString());
        Assert.Equal(8192, body.GetProperty("options").GetProperty("num_ctx").GetInt32());
    }

    [Fact]
    public async Task AdditionalProperties_ForAFieldEmberLlmDoesNotSet_IsAllowed()
    {
        // top_k via AdditionalProperties is fine as long as ChatOptions.TopK isn't also set
        var body = await SendAsync(new ChatOptions { ModelId = "m", AdditionalProperties = new() { ["top_k"] = 20 } });

        Assert.Equal(20, body.GetProperty("top_k").GetInt32());
    }

    [Theory]
    [InlineData("model")]
    [InlineData("messages")]
    [InlineData("stream")]
    [InlineData("temperature")]
    public async Task AdditionalProperties_OverridingAFieldEmberLlmSets_Throws(string key)
    {
        var handler = new FakeHttpHandler();
        using var client = TestClients.Create(handler);
        var options = new ChatOptions { ModelId = "m", Temperature = 0.5f, AdditionalProperties = new() { [key] = "x" } };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken));

        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    // Rejected options

    public static TheoryData<string, ChatOptions> UnsupportedOptions => new()
    {
        { "Tools", new ChatOptions { Tools = [AIFunctionFactory.Create(() => 42, "answer")] } },
        { "ToolMode", new ChatOptions { ToolMode = ChatToolMode.RequireAny } },
        { "AllowMultipleToolCalls", new ChatOptions { AllowMultipleToolCalls = true } },
        { "ConversationId", new ChatOptions { ConversationId = "conversation-1" } },
        { "AllowBackgroundResponses", new ChatOptions { AllowBackgroundResponses = true } },
        { "ContinuationToken", new ChatOptions { ContinuationToken = ResponseContinuationToken.FromBytes(new byte[] { 1 }) } },
        { "RawRepresentationFactory", new ChatOptions { RawRepresentationFactory = _ => new object() } },
        { "Reasoning.Effort", new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.ExtraHigh } } },
        { "Reasoning.Output", new ChatOptions { Reasoning = new ReasoningOptions { Output = ReasoningOutput.Full } } },
    };

    [Theory]
    [MemberData(nameof(UnsupportedOptions))]
    public async Task UnsupportedOption_Throws_BeforeAnyRequest(string option, ChatOptions options)
    {
        // No model is configured, so a request would go out for the model list if validation came too late
        var handler = new FakeHttpHandler();
        using var client = TestClients.Create(handler);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken));

        Assert.Contains($"ChatOptions.{option}", ex.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [MemberData(nameof(UnsupportedOptions))]
    public void UnsupportedOption_ThrowsAtCallSite_WhenStreaming(string option, ChatOptions options)
    {
        using var client = TestClients.Create(new FakeHttpHandler());

        var ex = Assert.Throws<NotSupportedException>(() => client.GetStreamingResponseAsync(Messages, options, TestContext.Current.CancellationToken));

        Assert.Contains($"ChatOptions.{option}", ex.Message, StringComparison.Ordinal);
    }

    // Guard: every ChatOptions property is classified

    private static readonly HashSet<string> MappedOptions =
    [
        "ModelId", "Temperature", "MaxOutputTokens", "TopP", "TopK", "StopSequences", "Seed",
        "FrequencyPenalty", "PresencePenalty", "Instructions", "Reasoning", "ResponseFormat", "AdditionalProperties",
    ];

    private static readonly HashSet<string> RejectedOptions =
    [
        "Tools", "ToolMode", "AllowMultipleToolCalls", "ConversationId",
        "AllowBackgroundResponses", "ContinuationToken", "RawRepresentationFactory",
    ];

    [Fact]
    public void EveryChatOptionsProperty_IsMappedOrRejected()
    {
        // A Microsoft.Extensions.AI upgrade that adds a ChatOptions property fails this test until
        // someone decides whether EmberLlm sends it or rejects it (SPEC.md, Design rules)
        var settable = typeof(ChatOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod?.IsPublic == true)
            .Select(p => p.Name)
            .ToHashSet();

        var unclassified = settable.Except(MappedOptions).Except(RejectedOptions).Order().ToList();
        var stale = MappedOptions.Union(RejectedOptions).Except(settable).Order().ToList();

        Assert.True(unclassified.Count == 0, $"Unclassified ChatOptions properties: {string.Join(", ", unclassified)}");
        Assert.True(stale.Count == 0, $"Classified properties that no longer exist on ChatOptions: {string.Join(", ", stale)}");
    }

    private static async Task<JsonElement> SendAsync(ChatOptions options)
    {
        var handler = new FakeHttpHandler().RespondJson(SampleResponses.Completion());
        using var client = TestClients.Create(handler);

        await client.GetResponseAsync(Messages, options, TestContext.Current.CancellationToken);

        return JsonDocument.Parse(handler.Requests[^1].Body!).RootElement.Clone();
    }
}
