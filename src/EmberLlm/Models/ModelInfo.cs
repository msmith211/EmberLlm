using Microsoft.Extensions.AI;

namespace EmberLlm;

/// <summary>
/// One model a server lists at <c>/v1/models</c>.
/// </summary>
public sealed class ModelInfo
{
    /// <summary>Creates model info with the given identifier.</summary>
    /// <param name="id">The model identifier to use as <see cref="ChatOptions.ModelId"/>.</param>
    public ModelInfo(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    /// <summary>
    /// The model identifier, for example <c>qwen/qwen3-4b-thinking-2507</c>. Pass it as
    /// <see cref="ChatOptions.ModelId"/> to use this model.
    /// </summary>
    public string Id { get; }

    /// <summary>The owner the server reports (<c>owned_by</c>), if any.</summary>
    public string? OwnedBy { get; init; }

    /// <summary>When the model was created (<c>created</c>), if the server reports it.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Fields the server sent that EmberLlm doesn't model, such as llama.cpp's <c>meta</c> or vLLM's
    /// <c>max_model_len</c>, as <see cref="System.Text.Json.JsonElement"/> values.
    /// </summary>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; init; }

    /// <summary>Returns <see cref="Id"/>.</summary>
    /// <returns>The model identifier.</returns>
    public override string ToString() => Id;
}
