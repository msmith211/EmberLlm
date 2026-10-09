using Microsoft.Extensions.AI;

namespace EmberLlm;

/// <summary>
/// A chat completion request.
/// </summary>
public sealed class ChatRequest
{
    /// <summary>
    /// The model to use. When <see langword="null"/>, the client falls back to
    /// <see cref="EmberLlmOptions.DefaultModel"/>, then to the first model the server lists.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// The conversation so far. Only text content is supported in v0.1.
    /// </summary>
    public IList<ChatMessage> Messages { get; set; } = [];

    /// <summary>
    /// Sampling temperature. <see langword="null"/> uses the server's default.
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>
    /// The maximum number of tokens to generate. <see langword="null"/> uses the server's default.
    /// </summary>
    public int? MaxTokens { get; set; }
}
