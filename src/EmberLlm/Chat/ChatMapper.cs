using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace EmberLlm.Chat;

/// <summary>
/// Converts between Microsoft.Extensions.AI types and the OpenAI-compatible wire format.
/// </summary>
internal static class ChatMapper
{
    public static WireChatRequest ToWire(ChatRequest request, string model, bool stream)
    {
        var messages = new List<WireMessage>(request.Messages.Count);
        foreach (var message in request.Messages)
        {
            messages.Add(new WireMessage { Role = message.Role.Value, Content = GetText(message) });
        }

        return new WireChatRequest
        {
            Model = model,
            Messages = messages,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens,
            Stream = stream,
            StreamOptions = stream ? new WireStreamOptions { IncludeUsage = true } : null,
        };
    }

    public static ChatResponseUpdate ToUpdate(WireChatResponse chunk)
    {
        var choice = chunk.Choices?.FirstOrDefault();
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

        return new ChatResponseUpdate(role, contents)
        {
            ResponseId = chunk.Id,
            ModelId = chunk.Model,
            CreatedAt = chunk.Created is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
            FinishReason = ToFinishReason(choice?.FinishReason),
            AdditionalProperties = ToAdditionalProperties(chunk.ExtensionData),
            RawRepresentation = chunk,
        };
    }

    public static ChatResponse ToChatResponse(WireChatResponse wire)
    {
        var choice = wire.Choices?.FirstOrDefault()
            ?? throw new EmberLlmException("The server returned a chat completion with no choices.");

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

        return new ChatResponse(new ChatMessage(role, contents))
        {
            ResponseId = wire.Id,
            ModelId = wire.Model,
            CreatedAt = wire.Created is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
            FinishReason = ToFinishReason(choice.FinishReason),
            Usage = ToUsage(wire.Usage),
            AdditionalProperties = ToAdditionalProperties(wire.ExtensionData),
            RawRepresentation = wire,
        };
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

    private static AdditionalPropertiesDictionary? ToAdditionalProperties(Dictionary<string, JsonElement>? extensionData)
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
}
