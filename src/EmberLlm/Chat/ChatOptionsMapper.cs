using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace EmberLlm.Chat;

/// <summary>
/// Builds the OpenAI-compatible request body from messages and <see cref="ChatOptions"/>.
/// </summary>
/// <remarks>
/// Applies the design rules in SPEC.md: every OpenAI-compatible option is mapped (decision 0005), and
/// anything EmberLlm can't honor throws before any request is sent, never silently ignored (decision 0006).
/// The mapping of <c>TopK</c>, <c>Reasoning</c> and <c>ResponseFormat</c> is proposed in decision 0007.
/// </remarks>
internal static class ChatOptionsMapper
{
    /// <summary>
    /// Validates and converts a request into its JSON body. The <c>model</c> field is left out: the caller
    /// adds it after model selection, which may itself send a request, so validation has to come first.
    /// </summary>
    public static JsonObject ToRequestBody(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
    {
        RejectUnsupported(options);

        var wireMessages = new List<WireMessage>();

        // Instructions has no chat-completions field; a leading system message is what it means
        if (!string.IsNullOrEmpty(options?.Instructions))
        {
            wireMessages.Add(new WireMessage { Role = ChatRole.System.Value, Content = options.Instructions });
        }

        foreach (var message in messages)
        {
            wireMessages.Add(ChatMapper.ToWireMessage(message));
        }

        if (wireMessages.Count == 0)
        {
            throw new ArgumentException("A chat request needs at least one message, or ChatOptions.Instructions.", nameof(messages));
        }

        var wire = new WireChatRequest
        {
            Messages = wireMessages,
            Temperature = options?.Temperature,
            MaxTokens = options?.MaxOutputTokens,
            TopP = options?.TopP,
            TopK = options?.TopK,
            Stop = options?.StopSequences is { Count: > 0 } stop ? [.. stop] : null,
            Seed = options?.Seed,
            FrequencyPenalty = options?.FrequencyPenalty,
            PresencePenalty = options?.PresencePenalty,
            ReasoningEffort = ToReasoningEffort(options?.Reasoning),
            ResponseFormat = ToResponseFormat(options?.ResponseFormat),
            Stream = stream,
            StreamOptions = stream ? new WireStreamOptions { IncludeUsage = true } : null,
        };

        var body = JsonSerializer.SerializeToNode(wire, WireJsonContext.Default.WireChatRequest)!.AsObject();
        AddPassThroughFields(body, options?.AdditionalProperties);
        return body;
    }

    private static void RejectUnsupported(ChatOptions? options)
    {
        if (options is null)
        {
            return;
        }

        // Only settings that actually ask for something are rejected: an empty tool list, or tool mode
        // "auto"/"none" with no tools, asks for nothing EmberLlm can't do
        if (options.Tools is { Count: > 0 })
        {
            throw Unsupported(nameof(ChatOptions.Tools), "Tool calling is planned for v0.2.");
        }

        if (options.ToolMode is RequiredChatToolMode)
        {
            throw Unsupported(nameof(ChatOptions.ToolMode), "Tool calling is planned for v0.2.");
        }

        if (options.AllowMultipleToolCalls == true)
        {
            throw Unsupported(nameof(ChatOptions.AllowMultipleToolCalls), "Tool calling is planned for v0.2.");
        }

        if (options.ConversationId is not null)
        {
            throw Unsupported(nameof(ChatOptions.ConversationId), "OpenAI-compatible chat completions have no server-side conversations; send the full message history instead.");
        }

        if (options.AllowBackgroundResponses == true)
        {
            throw Unsupported(nameof(ChatOptions.AllowBackgroundResponses), "Background responses are not part of the chat completions API.");
        }

        if (options.ContinuationToken is not null)
        {
            throw Unsupported(nameof(ChatOptions.ContinuationToken), "Background responses are not part of the chat completions API.");
        }

        if (options.RawRepresentationFactory is not null)
        {
            throw Unsupported(nameof(ChatOptions.RawRepresentationFactory), "EmberLlm has no native SDK request object; use AdditionalProperties to send extra fields.");
        }
    }

    private static string? ToReasoningEffort(ReasoningOptions? reasoning)
    {
        if (reasoning is null)
        {
            return null;
        }

        if (reasoning.Output is not null)
        {
            throw Unsupported($"{nameof(ChatOptions.Reasoning)}.{nameof(ReasoningOptions.Output)}", "OpenAI-compatible servers decide on their own whether to return reasoning.");
        }

        return reasoning.Effort switch
        {
            null => null,
            ReasoningEffort.None => "none",
            ReasoningEffort.Low => "low",
            ReasoningEffort.Medium => "medium",
            ReasoningEffort.High => "high",
            _ => throw Unsupported(
                $"{nameof(ChatOptions.Reasoning)}.{nameof(ReasoningOptions.Effort)} = {reasoning.Effort}",
                "OpenAI-compatible reasoning_effort goes up to \"high\"."),
        };
    }

    private static WireResponseFormat? ToResponseFormat(ChatResponseFormat? format) => format switch
    {
        null => null,
        ChatResponseFormatText => new WireResponseFormat { Type = "text" },
        ChatResponseFormatJson { Schema: { } schema } json => new WireResponseFormat
        {
            Type = "json_schema",
            JsonSchema = new WireJsonSchema
            {
                // OpenAI requires a schema name; "response" is used when the caller gives none
                Name = string.IsNullOrWhiteSpace(json.SchemaName) ? "response" : json.SchemaName,
                Description = json.SchemaDescription,
                Schema = schema,
            },
        },
        ChatResponseFormatJson => new WireResponseFormat { Type = "json_object" },
        _ => throw Unsupported(nameof(ChatOptions.ResponseFormat), $"Unknown response format {format.GetType().Name}."),
    };

    /// <summary>
    /// Adds <see cref="ChatOptions.AdditionalProperties"/> as extra top-level fields (decision 0005). A key
    /// that EmberLlm already writes for this request throws, so neither value is silently overwritten.
    /// </summary>
    private static void AddPassThroughFields(JsonObject body, AdditionalPropertiesDictionary? properties)
    {
        if (properties is null)
        {
            return;
        }

        foreach (var (key, value) in properties)
        {
            if (body.ContainsKey(key) || key == "model")
            {
                throw new ArgumentException(
                    $"ChatOptions.AdditionalProperties contains \"{key}\", which EmberLlm already sets for this request. Use the matching ChatOptions property instead.",
                    nameof(properties));
            }

            body[key] = value switch
            {
                null => null,
                JsonElement element => JsonSerializer.SerializeToNode(element, WireJsonContext.Default.JsonElement),
                _ => JsonSerializer.SerializeToNode(value, AIJsonUtilities.DefaultOptions.GetTypeInfo(value.GetType())),
            };
        }
    }

    private static NotSupportedException Unsupported(string option, string reason) =>
        new($"ChatOptions.{option} is not supported by EmberLlm. {reason}");
}
