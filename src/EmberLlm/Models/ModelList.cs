using System.Collections;
using Microsoft.Extensions.AI;

namespace EmberLlm;

/// <summary>
/// The models a server lists at <c>/v1/models</c>, in the server's order. Usable as any
/// <see cref="IReadOnlyList{T}"/> of <see cref="ModelInfo"/>.
/// </summary>
public sealed class ModelList : IReadOnlyList<ModelInfo>
{
    private readonly IReadOnlyList<ModelInfo> _models;

    /// <summary>Creates a list of models.</summary>
    /// <param name="models">The models, in the server's order.</param>
    public ModelList(IEnumerable<ModelInfo> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        _models = [.. models];
    }

    /// <summary>
    /// Top-level fields the server sent besides the model list itself (for example <c>"object": "list"</c>),
    /// as <see cref="System.Text.Json.JsonElement"/> values.
    /// </summary>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; init; }

    /// <inheritdoc />
    public int Count => _models.Count;

    /// <inheritdoc />
    public ModelInfo this[int index] => _models[index];

    /// <inheritdoc />
    public IEnumerator<ModelInfo> GetEnumerator() => _models.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
