namespace EmberLlm.Tests.Fakes;

/// <summary>
/// Fingerprint responses for backends that haven't been recorded from a real server yet. Hand-written from
/// each project's documentation; replace with recordings under tests/fixtures/&lt;backend&gt;/ when available.
/// LM Studio is not here: its responses are real recordings in tests/fixtures/lmstudio/.
/// </summary>
internal static class SampleBackends
{
    public const string OllamaRoot = "Ollama is running";

    public const string OllamaVersion = """{"version":"0.12.3"}""";

    public const string OllamaModels = """
        {"object":"list","data":[{"id":"llama3.2:latest","object":"model","created":1760000000,"owned_by":"library"}]}
        """;

    public const string LlamaCppModels = """
        {"object":"list","data":[{"id":"qwen3-8b-q4_k_m.gguf","object":"model","created":1760000000,"owned_by":"llamacpp","meta":{"n_params":8190735360}}]}
        """;

    public const string LlamaCppProps = """
        {"default_generation_settings":{"n_ctx":8192,"params":{"temperature":0.8}},"total_slots":1,"model_path":"qwen3-8b-q4_k_m.gguf","build_info":"b6500-abc1234"}
        """;

    public const string VllmModels = """
        {"object":"list","data":[{"id":"Qwen/Qwen3-8B","object":"model","created":1760000000,"owned_by":"vllm","root":"Qwen/Qwen3-8B","parent":null,"max_model_len":32768,"permission":[]}]}
        """;

    public const string VllmVersion = """{"version":"0.11.0"}""";

    public const string GenericModels = """{"object":"list","data":[{"id":"some-model","object":"model"}]}""";

    public const string HtmlNotFound = "<html><body><h1>Not Found</h1></body></html>";
}
