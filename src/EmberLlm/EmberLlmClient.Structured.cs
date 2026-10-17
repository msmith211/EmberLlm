using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using EmberLlm.Structured;
using Microsoft.Extensions.AI;

namespace EmberLlm;

public sealed partial class EmberLlmClient
{
    // Strict, so a reply missing a required member or holding null where T doesn't allow it fails and is retried
    private static readonly JsonSerializerOptions StrictSerializerOptions = CreateStrictSerializerOptions();

    /// <summary>The mode this client uses, for tests of how it was chosen.</summary>
    internal StructuredOutputMode StructuredOutputMode => _structuredOutputMode;

    /// <summary>
    /// Sends a single user prompt and returns the reply as a <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type to return. Its JSON schema is sent to the server.</typeparam>
    /// <param name="prompt">The user message.</param>
    /// <param name="cancellationToken">Cancels the request, including any retries.</param>
    /// <returns>The reply, deserialized.</returns>
    /// <inheritdoc cref="GetAsync{T}(IEnumerable{ChatMessage}, JsonSerializerOptions, ChatOptions?, CancellationToken)" path="/remarks"/>
    /// <exception cref="StructuredOutputException">No reply could be read as <typeparamref name="T"/> after every attempt.</exception>
    public Task<T> GetAsync<T>(string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        return GetAsync<T>([new(ChatRole.User, prompt)], StrictSerializerOptions, options: null, cancellationToken);
    }

    /// <summary>
    /// Sends a chat request and returns the reply as a <typeparamref name="T"/>, deserialized strictly: a missing
    /// required member, or <see langword="null"/> where <typeparamref name="T"/> doesn't allow it, counts as a failed attempt.
    /// </summary>
    /// <typeparam name="T">The type to return. Its JSON schema is sent to the server.</typeparam>
    /// <param name="messages">The conversation so far. Only text content is supported.</param>
    /// <param name="options">Request options, as for <see cref="GetResponseAsync"/>. <see cref="ChatOptions.ResponseFormat"/> must be unset.</param>
    /// <param name="cancellationToken">Cancels the request, including any retries.</param>
    /// <returns>The reply, deserialized.</returns>
    /// <inheritdoc cref="GetAsync{T}(IEnumerable{ChatMessage}, JsonSerializerOptions, ChatOptions?, CancellationToken)" path="/remarks"/>
    /// <exception cref="StructuredOutputException">No reply could be read as <typeparamref name="T"/> after every attempt.</exception>
    /// <exception cref="ArgumentException"><see cref="ChatOptions.ResponseFormat"/> is set.</exception>
    public Task<T> GetAsync<T>(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        GetAsync<T>(messages, StrictSerializerOptions, options, cancellationToken);

    /// <summary>
    /// Sends a chat request and returns the reply as a <typeparamref name="T"/>, using <paramref name="serializerOptions"/>
    /// for both the schema and deserialization. Use this for source-generated JSON metadata (trimmed or AOT apps) or
    /// custom naming. The options are used as given, so they decide how strict deserialization is.
    /// </summary>
    /// <typeparam name="T">The type to return. Its JSON schema is sent to the server.</typeparam>
    /// <param name="messages">The conversation so far. Only text content is supported.</param>
    /// <param name="serializerOptions">The options for schema generation and deserialization.</param>
    /// <param name="options">Request options, as for <see cref="GetResponseAsync"/>. <see cref="ChatOptions.ResponseFormat"/> must be unset.</param>
    /// <param name="cancellationToken">Cancels the request, including any retries.</param>
    /// <returns>The reply, deserialized.</returns>
    /// <remarks>
    /// Builds on Microsoft.Extensions.AI's <c>GetResponseAsync&lt;T&gt;</c>, which generates the schema and deserializes the reply.
    /// <see cref="EmberLlmOptions.StructuredOutputMode"/> decides whether the schema goes in <c>response_format</c> or in
    /// the prompt. A reply that isn't valid JSON is cleaned first (leading reasoning block, code fence, surrounding text).
    /// When a reply still can't be read, it goes back to the model with the error and the request is repeated, up to
    /// <see cref="EmberLlmOptions.MaxStructuredRetries"/> times. Server errors, timeouts and cancellation are not retried.
    /// </remarks>
    /// <exception cref="StructuredOutputException">No reply could be read as <typeparamref name="T"/> after every attempt.</exception>
    /// <exception cref="ArgumentException"><see cref="ChatOptions.ResponseFormat"/> is set.</exception>
    public async Task<T> GetAsync<T>(
        IEnumerable<ChatMessage> messages,
        JsonSerializerOptions serializerOptions,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(serializerOptions);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (options?.ResponseFormat is not null)
        {
            throw new ArgumentException(
                "ChatOptions.ResponseFormat must be null for GetAsync<T>, which sets the response format from T. " +
                "Use GetResponseAsync to send a response format of your own.",
                nameof(options));
        }

        var conversation = messages.ToList();
        var client = new StructuredOutputChatClient(this, _structuredOutputMode);
        var useJsonSchema = _structuredOutputMode == StructuredOutputMode.JsonSchema;
        var attempts = new List<StructuredOutputAttempt>();
        Exception lastError = null!;

        for (var attempt = 0; attempt <= _maxStructuredRetries; attempt++)
        {
            var response = await client.GetResponseAsync<T>(conversation, serializerOptions, options, useJsonSchema, cancellationToken)
                .ConfigureAwait(false);

            if (TryGetResult(response, out var result, out var error, out var exception))
            {
                return result;
            }

            attempts.Add(new StructuredOutputAttempt(client.LastRawText, error));
            lastError = exception;
            conversation.Add(new ChatMessage(ChatRole.Assistant, client.LastRawText));
            conversation.Add(new ChatMessage(
                ChatRole.User,
                $"That reply could not be used: {error} Reply again with only the JSON value, following the schema exactly."));
        }

        throw new StructuredOutputException(
            $"No reply could be read as {typeof(T).Name} after {attempts.Count} attempt{(attempts.Count == 1 ? "" : "s")}. " +
            $"Last error: {attempts[^1].Error} See Attempts for each reply and error.",
            attempts,
            lastError);
    }

    // Result throws for invalid JSON, a failed deserialization (including the strict checks) and a JSON null
    private static bool TryGetResult<T>(ChatResponse<T> response, out T result, out string error, [NotNullWhen(false)] out Exception? exception)
    {
        try
        {
            result = response.Result;
            (error, exception) = (string.Empty, null);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            (result, error, exception) = (default!, ex.Message, ex);
            return false;
        }
    }

    private static JsonSerializerOptions CreateStrictSerializerOptions()
    {
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions)
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };
        options.MakeReadOnly();
        return options;
    }
}
