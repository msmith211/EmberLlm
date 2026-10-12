using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmberLlm.Chat;

// OpenAI-compatible wire format. Only the fields v0.1 uses are modeled; unknown top-level
// response fields (for example llama.cpp's "timings") are kept in ExtensionData.

internal sealed class WireChatRequest
{
    // Set after option validation, once model selection has run (validation must come first; see decision 0006)
    public string? Model { get; set; }

    public required IReadOnlyList<WireMessage> Messages { get; init; }

    public float? Temperature { get; init; }

    public int? MaxTokens { get; init; }

    public float? TopP { get; init; }

    // Not in OpenAI's own API, but accepted by llama.cpp, vLLM and LM Studio
    public int? TopK { get; init; }

    public IReadOnlyList<string>? Stop { get; init; }

    public long? Seed { get; init; }

    public float? FrequencyPenalty { get; init; }

    public float? PresencePenalty { get; init; }

    public string? ReasoningEffort { get; init; }

    public WireResponseFormat? ResponseFormat { get; init; }

    public bool Stream { get; init; }

    public WireStreamOptions? StreamOptions { get; init; }
}

internal sealed class WireResponseFormat
{
    // "text", "json_object" or "json_schema"
    public required string Type { get; init; }

    public WireJsonSchema? JsonSchema { get; init; }
}

internal sealed class WireJsonSchema
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public JsonElement Schema { get; init; }
}

internal sealed class WireStreamOptions
{
    // Asks the server to send token usage in a final chunk
    public bool IncludeUsage { get; init; }
}

internal sealed class WireMessage
{
    public string? Role { get; init; }

    public string? Content { get; init; }

    public string? Name { get; init; }

    // DeepSeek-style reasoning field, sent by some backends for thinking models
    public string? ReasoningContent { get; init; }

    // Outgoing: ChatMessage.AdditionalProperties, passed through as extra fields.
    // Incoming: any message fields EmberLlm doesn't model.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

// Used for both full responses and streaming chunks; chunks carry Delta instead of Message
internal sealed class WireChatResponse
{
    public string? Id { get; init; }

    public string? Model { get; init; }

    public long? Created { get; init; }

    public IReadOnlyList<WireChoice>? Choices { get; init; }

    public WireUsage? Usage { get; init; }

    // Must be { get; set; }: the source generator binds init-only properties like constructor
    // parameters, and extension data cannot bind that way
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

internal sealed class WireChoice
{
    public int Index { get; init; }

    public WireMessage? Message { get; init; }

    public WireMessage? Delta { get; init; }

    public string? FinishReason { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

internal sealed class WireUsage
{
    public long? PromptTokens { get; init; }

    public long? CompletionTokens { get; init; }

    public long? TotalTokens { get; init; }

    // For example prompt_tokens_details or completion_tokens_details
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

internal sealed class WireModelList
{
    public IReadOnlyList<WireModel>? Data { get; init; }
}

internal sealed class WireModel
{
    public string? Id { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WireChatRequest))]
[JsonSerializable(typeof(WireChatResponse))]
[JsonSerializable(typeof(WireModelList))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class WireJsonContext : JsonSerializerContext;
