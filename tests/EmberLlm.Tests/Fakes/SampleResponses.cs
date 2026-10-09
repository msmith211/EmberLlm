namespace EmberLlm.Tests.Fakes;

/// <summary>
/// Hand-written responses in the OpenAI-compatible shape, for testing request and response handling.
/// Backend-specific quirks are tested from responses recorded from real servers under tests/fixtures.
/// </summary>
internal static class SampleResponses
{
    public static string Completion(string content = "Hello there!", string model = "qwen3-14b", string finishReason = "stop") => $$"""
        {
          "id": "chatcmpl-123",
          "object": "chat.completion",
          "created": 1760000000,
          "model": "{{model}}",
          "choices": [
            {
              "index": 0,
              "message": { "role": "assistant", "content": "{{content}}" },
              "finish_reason": "{{finishReason}}"
            }
          ],
          "usage": { "prompt_tokens": 12, "completion_tokens": 3, "total_tokens": 15 }
        }
        """;

    public const string CompletionWithReasoning = """
        {
          "id": "chatcmpl-456",
          "model": "qwen3-14b",
          "choices": [
            {
              "index": 0,
              "message": { "role": "assistant", "content": "42", "reasoning_content": "The user wants the answer." },
              "finish_reason": "stop"
            }
          ]
        }
        """;

    public const string CompletionWithTimings = """
        {
          "id": "chatcmpl-789",
          "model": "qwen3-14b",
          "choices": [ { "index": 0, "message": { "role": "assistant", "content": "Hi" }, "finish_reason": "stop" } ],
          "timings": { "prompt_n": 12, "prompt_per_second": 850.5, "predicted_n": 3, "predicted_per_second": 42.1 }
        }
        """;

    public const string CompletionWithNoChoices = """{ "id": "chatcmpl-0", "model": "qwen3-14b", "choices": [] }""";

    public static string Models(params string[] ids) =>
        $$"""{ "object": "list", "data": [{{string.Join(", ", ids.Select(id => $$"""{ "id": "{{id}}", "object": "model" }"""))}}] }""";

    public const string OpenAiError = """{ "error": { "message": "max_tokens must be positive", "type": "invalid_request_error" } }""";

    public const string OllamaError = """{ "error": "model 'missing-model' not found" }""";

    public const string HtmlErrorPage = "<html><body><h1>500 Internal Server Error</h1></body></html>";
}
