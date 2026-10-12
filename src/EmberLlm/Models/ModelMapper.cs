using EmberLlm.Chat;

namespace EmberLlm.Models;

/// <summary>
/// Converts a <c>/v1/models</c> response into a <see cref="ModelList"/>. Follows decision 0006: unmodeled
/// fields are kept in <c>AdditionalProperties</c>, and anything that can't be represented throws.
/// </summary>
internal static class ModelMapper
{
    public static ModelList ToModelList(WireModelList wire, Uri source)
    {
        if (wire.Data is null)
        {
            throw new EmberLlmException($"GET {source} returned a response without a \"data\" list, so it isn't an OpenAI-style model list.");
        }

        var models = new List<ModelInfo>(wire.Data.Count);
        for (var i = 0; i < wire.Data.Count; i++)
        {
            var model = wire.Data[i];
            if (string.IsNullOrWhiteSpace(model?.Id))
            {
                throw new EmberLlmException($"GET {source} listed a model without an \"id\" (entry {i}), which can't be used to select it.");
            }

            models.Add(new ModelInfo(model.Id)
            {
                OwnedBy = model.OwnedBy,
                CreatedAt = model.Created is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
                AdditionalProperties = ChatMapper.ToAdditionalProperties(model.ExtensionData),
            });
        }

        return new ModelList(models) { AdditionalProperties = ChatMapper.ToAdditionalProperties(wire.ExtensionData) };
    }
}
