using System.Text.Json;

namespace EmberLlm.Structured;

/// <summary>
/// Recovers the JSON value from a reply that isn't valid JSON as it stands: small models wrap it in reasoning
/// blocks, code fences or explanations, especially when the server doesn't enforce a schema.
/// </summary>
internal static class StructuredOutputCleaner
{
    private const string ThinkOpen = "<think>";
    private const string ThinkClose = "</think>";
    private const string Fence = "```";

    /// <summary>Whether <paramref name="text"/> is a single valid JSON value. Valid replies are never cleaned.</summary>
    public static bool IsJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes a leading reasoning block, a surrounding code fence and any text outside the outermost
    /// <c>{…}</c> or <c>[…]</c>. Returns what remains, which may still not be valid JSON.
    /// </summary>
    public static string Clean(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = StripReasoning(text.Trim()).Trim();
        result = StripFence(result).Trim();
        return TakeOutermostJsonValue(result);
    }

    // A block that opens the reply, possibly unterminated (the model ran out of tokens while thinking, so no
    // JSON follows). Some chat templates put the opening tag in the prompt, so the reply has only the closing one.
    private static string StripReasoning(string text)
    {
        if (text.StartsWith(ThinkOpen, StringComparison.OrdinalIgnoreCase))
        {
            var close = text.IndexOf(ThinkClose, StringComparison.OrdinalIgnoreCase);
            return close < 0 ? string.Empty : text[(close + ThinkClose.Length)..];
        }

        var orphanClose = text.IndexOf(ThinkClose, StringComparison.OrdinalIgnoreCase);
        return orphanClose < 0 ? text : text[(orphanClose + ThinkClose.Length)..];
    }

    // ```json … ``` or bare ``` … ```
    private static string StripFence(string text)
    {
        if (!text.StartsWith(Fence, StringComparison.Ordinal))
        {
            return text;
        }

        var firstLineEnd = text.IndexOf('\n', StringComparison.Ordinal);
        if (firstLineEnd < 0)
        {
            return text;
        }

        var body = text[(firstLineEnd + 1)..].TrimEnd();
        return body.EndsWith(Fence, StringComparison.Ordinal) ? body[..^Fence.Length] : body;
    }

    private static string TakeOutermostJsonValue(string text)
    {
        var start = text.AsSpan().IndexOfAny('{', '[');
        if (start < 0)
        {
            return text;
        }

        var end = text.LastIndexOf(text[start] == '{' ? '}' : ']');
        return end > start ? text[start..(end + 1)] : text[start..];
    }
}
