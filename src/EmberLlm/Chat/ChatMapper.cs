using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace EmberLlm.Chat;

/// <summary>
/// Converts between Microsoft.Extensions.AI types and the OpenAI-compatible wire format.
/// </summary>
/// <remarks>
/// Follows decision 0006, "never fail silently": everything a caller asks for is either sent or
/// rejected with an exception, and nothing the server sends is discarded.
/// </remarks>
internal static class ChatMapper
{
    // Message fields EmberLlm sets itself; AdditionalProperties may not override them
    private static readonly HashSet<string> MappedMessageFields = ["role", "content", "name"];

    public static WireMessage ToWireMessage(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new WireMessage
        {
            Role = message.Role.Value,
            Content = GetText(message),
            Name = message.AuthorName,
            ExtensionData = ToExtensionData(message),
        };
    }

    public static ChatResponseUpdate ToUpdate(WireChatResponse chunk)
    {
        var choice = GetOnlyChoice(chunk.Choices, allowNone: true);
        var delta = choice?.Delta;

        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(delta?.ReasoningContent))
        {
            contents.Add(new TextReasoningContent(delta.ReasoningContent));
        }

        if (!string.IsNullOrEmpty(delta?.Content))
        {
            contents.Add(new TextContent(delta.Content));
        }

        // Usage arrives in a final chunk (often with no choices) when stream_options.include_usage is set
        if (ToUsage(chunk.Usage) is { } usage)
        {
            contents.Add(new UsageContent(usage));
        }

        var role = string.IsNullOrEmpty(delta?.Role) ? (ChatRole?)null : new ChatRole(delta.Role);

        // An update has a single AdditionalProperties, so unmodeled chunk, choice and delta fields share it
        var extras = ToAdditionalProperties(chunk.ExtensionData);
        extras = MergeInto(extras, choice?.ExtensionData, "choice.");
        extras = MergeInto(extras, delta?.ExtensionData, "delta.");
        extras = AddPrefixed(extras, chunk.Usage?.ExtensionData, "usage.");

        return new ChatResponseUpdate(role, contents)
        {
            ResponseId = chunk.Id,
            ModelId = chunk.Model,
            CreatedAt = chunk.Created is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
            FinishReason = ToFinishReason(choice?.FinishReason),
            AdditionalProperties = extras,
            RawRepresentation = chunk,
        };
    }

    public static ChatResponse ToChatResponse(WireChatResponse wire)
    {
        var choice = GetOnlyChoice(wire.Choices, allowNone: false)!;

        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(choice.Message?.ReasoningContent))
        {
            contents.Add(new TextReasoningContent(choice.Message.ReasoningContent));
        }

        if (choice.Message?.Content is { } text)
        {
            contents.Add(new TextContent(text));
        }

        var role = string.IsNullOrEmpty(choice.Message?.Role) ? ChatRole.Assistant : new ChatRole(choice.Message.Role);
        var message = new ChatMessage(role, contents)
        {
            AuthorName = choice.Message?.Name,
            AdditionalProperties = ToAdditionalProperties(choice.Message?.ExtensionData),
        };

        return new ChatResponse(message)
        {
            ResponseId = wire.Id,
            ModelId = wire.Model,
            CreatedAt = wire.Created is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
            FinishReason = ToFinishReason(choice.FinishReason),
            Usage = ToUsage(wire.Usage),
            AdditionalProperties = AddPrefixed(
                MergeInto(ToAdditionalProperties(wire.ExtensionData), choice.ExtensionData, "choice."),
                wire.Usage?.ExtensionData,
                "usage."),
            RawRepresentation = wire,
        };
    }

    /// <summary>
    /// Returns the single choice in a response. EmberLlm requests one completion, and
    /// <see cref="ChatResponse"/> has no way to represent alternatives, so more than one is an error
    /// rather than something to silently truncate.
    /// </summary>
    private static WireChoice? GetOnlyChoice(IReadOnlyList<WireChoice>? choices, bool allowNone)
    {
        if (choices is null || choices.Count == 0)
        {
            // Streaming: the final usage chunk legitimately has no choices
            return allowNone
                ? null
                : throw new EmberLlmException("The server returned a chat completion with no choices.");
        }

        if (choices.Count > 1 || choices[0].Index != 0)
        {
            throw new EmberLlmException(
                $"The server returned more than one choice (choices: {choices.Count}, first index: {choices[0].Index}). EmberLlm requests a single completion and cannot represent alternatives.");
        }

        return choices[0];
    }

    public static ChatFinishReason? ToFinishReason(string? value) => value switch
    {
        null or "" => null,
        "stop" => ChatFinishReason.Stop,
        "length" => ChatFinishReason.Length,
        "tool_calls" or "function_call" => ChatFinishReason.ToolCalls,
        "content_filter" => ChatFinishReason.ContentFilter,
        _ => new ChatFinishReason(value),
    };

    public static UsageDetails? ToUsage(WireUsage? usage) => usage is null
        ? null
        : new UsageDetails
        {
            InputTokenCount = usage.PromptTokens,
            OutputTokenCount = usage.CompletionTokens,
            TotalTokenCount = usage.TotalTokens,
        };

    /// <summary>
    /// Extracts a human-readable error message from an error response body. Recognizes the OpenAI
    /// shape (<c>{"error":{"message":...}}</c>), the Ollama shape (<c>{"error":"..."}</c>), and the
    /// top-level <c>message</c> or <c>detail</c> strings some servers use.
    /// </summary>
    public static string? TryGetServerMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString();
                }

                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var nested)
                    && nested.ValueKind == JsonValueKind.String)
                {
                    return nested.GetString();
                }
            }

            foreach (var name in (ReadOnlySpan<string>)["message", "detail"])
            {
                if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (for example an HTML error page); the caller falls back to the raw body
        }

        return null;
    }

    private static string GetText(ChatMessage message)
    {
        var builder = new StringBuilder();
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent text:
                    builder.Append(text.Text);
                    break;

                // Reasoning from an earlier assistant turn is not sent back, matching how
                // OpenAI-compatible servers expect thinking-model history to be replayed
                case TextReasoningContent:
                    break;

                default:
                    throw new NotSupportedException(
                        $"EmberLlm v0.1 supports only text message content, but a {message.Role} message contains {content.GetType().Name}.");
            }
        }

        return builder.ToString();
    }

    internal static AdditionalPropertiesDictionary? ToAdditionalProperties(Dictionary<string, JsonElement>? extensionData)
    {
        if (extensionData is null || extensionData.Count == 0)
        {
            return null;
        }

        var properties = new AdditionalPropertiesDictionary();
        foreach (var (key, value) in extensionData)
        {
            properties[key] = value;
        }

        return properties;
    }

    /// <summary>
    /// Converts a message's <see cref="ChatMessage.AdditionalProperties"/> into extra JSON fields on that
    /// message. Values are serialized with Microsoft.Extensions.AI's default JSON options; a value that
    /// can't be serialized throws instead of being dropped.
    /// </summary>
    private static Dictionary<string, JsonElement>? ToExtensionData(ChatMessage message)
    {
        if (message.AdditionalProperties is not { Count: > 0 } properties)
        {
            return null;
        }

        var extensionData = new Dictionary<string, JsonElement>(properties.Count, StringComparer.Ordinal);
        foreach (var (key, value) in properties)
        {
            if (MappedMessageFields.Contains(key))
            {
                throw new ArgumentException(
                    $"A {message.Role} message's AdditionalProperties contains \"{key}\", which EmberLlm already sets from the message itself. Use the matching ChatMessage property instead.",
                    nameof(message));
            }

            extensionData[key] = value switch
            {
                JsonElement element => element,
                null => JsonSerializer.SerializeToElement<object?>(null, AIJsonUtilities.DefaultOptions),
                _ => JsonSerializer.SerializeToElement(value, AIJsonUtilities.DefaultOptions.GetTypeInfo(value.GetType())),
            };
        }

        return extensionData;
    }

    /// <summary>
    /// Adds <paramref name="source"/> entries to <paramref name="target"/>. A key that already exists is
    /// stored with <paramref name="prefix"/> instead, so neither value is lost.
    /// </summary>
    private static AdditionalPropertiesDictionary? MergeInto(AdditionalPropertiesDictionary? target, Dictionary<string, JsonElement>? source, string prefix)
    {
        if (source is null || source.Count == 0)
        {
            return target;
        }

        target ??= [];
        foreach (var (key, value) in source)
        {
            target[target.ContainsKey(key) ? prefix + key : key] = value;
        }

        return target;
    }

    /// <summary>
    /// Adds <paramref name="source"/> entries to <paramref name="target"/>, always under
    /// <paramref name="prefix"/>, so their origin is clear (for example <c>usage.prompt_tokens_details</c>).
    /// </summary>
    private static AdditionalPropertiesDictionary? AddPrefixed(AdditionalPropertiesDictionary? target, Dictionary<string, JsonElement>? source, string prefix)
    {
        if (source is null || source.Count == 0)
        {
            return target;
        }

        target ??= [];
        foreach (var (key, value) in source)
        {
            target[prefix + key] = value;
        }

        return target;
    }
}
