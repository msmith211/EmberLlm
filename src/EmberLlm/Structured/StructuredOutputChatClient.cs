using Microsoft.Extensions.AI;

namespace EmberLlm.Structured;

/// <summary>
/// The <see cref="IChatClient"/> that <see cref="EmberLlmClient.GetAsync{T}(string, CancellationToken)"/> hands to
/// Microsoft.Extensions.AI's <c>GetResponseAsync&lt;T&gt;</c>, which generates the schema, adds it to the request and
/// deserializes the reply. This sits in between to adjust both sides:
/// <list type="bullet">
/// <item>In <see cref="StructuredOutputMode.Prompt"/>, <c>GetResponseAsync&lt;T&gt;</c> puts the schema in a message
/// and also asks for <c>response_format: json_object</c>, which LM Studio rejects. That format is removed here; the
/// schema message stays.</item>
/// <item>A reply that isn't valid JSON is cleaned (<see cref="StructuredOutputCleaner"/>) before it's deserialized.
/// Valid replies pass through untouched.</item>
/// </list>
/// One instance serves one <c>GetAsync&lt;T&gt;</c> call. It doesn't own the inner client.
/// </summary>
internal sealed class StructuredOutputChatClient(IChatClient inner, StructuredOutputMode mode) : IChatClient
{
    /// <summary>The last reply's text as the server sent it, before cleanup.</summary>
    public string LastRawText { get; private set; } = string.Empty;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (mode == StructuredOutputMode.Prompt && options?.ResponseFormat is ChatResponseFormatJson { Schema: null })
        {
            options = options.Clone();
            options.ResponseFormat = null;
        }

        var response = await inner.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        LastRawText = response.Text;
        if (StructuredOutputCleaner.IsJson(LastRawText))
        {
            return response;
        }

        // ChatResponse<T> deserializes from Text, so the cleaned text replaces the reply's messages
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, StructuredOutputCleaner.Clean(LastRawText)))
        {
            ResponseId = response.ResponseId,
            ModelId = response.ModelId,
            CreatedAt = response.CreatedAt,
            FinishReason = response.FinishReason,
            Usage = response.Usage,
            AdditionalProperties = response.AdditionalProperties,
            RawRepresentation = response.RawRepresentation,
        };
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Structured output uses non-streaming requests only.");

    public object? GetService(Type serviceType, object? serviceKey = null) => inner.GetService(serviceType, serviceKey);

    public void Dispose()
    {
        // The inner client belongs to the caller
    }
}
