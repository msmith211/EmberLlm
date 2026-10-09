using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmberLlm.Chat;

// OpenAI-compatible wire format. Only the fields v0.1 uses are modeled; unknown top-level
// response fields (for example llama.cpp's "timings") are kept in ExtensionData.

internal sealed class WireChatRequest
{
    public required string Model { get; init; }

    public required IReadOnlyList<WireMessage> Messages { get; init; }

    public float? Temperature { get; init; }

    public int? MaxTokens { get; init; }

    public bool Stream { get; init; }
}

internal sealed class WireMessage
{
    public string? Role { get; init; }

    public string? Content { get; init; }

    // DeepSeek-style reasoning field, sent by some backends for thinking models
    public string? ReasoningContent { get; init; }
}

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

    public string? FinishReason { get; init; }
}

internal sealed class WireUsage
{
    public long? PromptTokens { get; init; }

    public long? CompletionTokens { get; init; }

    public long? TotalTokens { get; init; }
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
