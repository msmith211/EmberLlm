namespace EmberLlm.Tests.Fakes;

/// <summary>
/// Hand-written server-sent event streams in the OpenAI-compatible chunk shape.
/// </summary>
internal static class SampleStreams
{
    public static string RoleChunk() => Chunk("""{ "role": "assistant", "content": "" }""");

    public static string ContentChunk(string content) => Chunk($$"""{ "content": "{{content}}" }""");

    public static string ReasoningChunk(string reasoning) => Chunk($$"""{ "reasoning_content": "{{reasoning}}" }""");

    public static string FinishChunk(string finishReason = "stop") => Chunk("{}", finishReason);

    public static string UsageChunk(int prompt = 12, int completion = 3) => $$"""
        { "id": "chatcmpl-1", "model": "qwen3-14b", "created": 1760000000, "choices": [],
          "usage": { "prompt_tokens": {{prompt}}, "completion_tokens": {{completion}}, "total_tokens": {{prompt + completion}} } }
        """.ReplaceLineEndings(" ");

    /// <summary>Formats payloads as SSE events and appends the [DONE] marker.</summary>
    public static string Sse(params string[] payloads) =>
        string.Concat(payloads.Select(p => $"data: {p}\n\n")) + "data: [DONE]\n\n";

    /// <summary>A typical complete stream: role, three tokens, finish reason, usage.</summary>
    public static string Haiku() => Sse(
        RoleChunk(),
        ContentChunk("Heap"),
        ContentChunk(" fills"),
        ContentChunk(" up"),
        FinishChunk(),
        UsageChunk());

    private static string Chunk(string delta, string? finishReason = null)
    {
        var finish = finishReason is null ? "null" : $"\"{finishReason}\"";
        return $$"""{ "id": "chatcmpl-1", "model": "qwen3-14b", "created": 1760000000, "choices": [ { "index": 0, "delta": {{delta}}, "finish_reason": {{finish}} } ] }""";
    }
}
