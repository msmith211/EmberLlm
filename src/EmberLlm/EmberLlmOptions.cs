namespace EmberLlm;

/// <summary>
/// Configuration for an <see cref="EmberLlmClient"/>.
/// </summary>
public sealed class EmberLlmOptions
{
    /// <summary>
    /// The default value of <see cref="RequestTimeout"/>: 10 minutes, because local generation can be slow.
    /// </summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The server's base URL, for example <c>http://localhost:1234</c>. A trailing <c>/v1</c> is optional.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The model to use when a request does not name one. When this is also unset, the client uses
    /// the first model the server lists. Set this for Ollama, which lists every installed model, not
    /// just loaded ones, so picking the first could trigger a model load.
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// The maximum time a single request may take. Exceeding it throws <see cref="EmberLlmTimeoutException"/>.
    /// Use <see cref="Timeout.InfiniteTimeSpan"/> to rely only on the caller's cancellation token.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = DefaultRequestTimeout;

    /// <summary>
    /// When <see langword="true"/>, a reply that reports a different model than the one EmberLlm requested
    /// throws <see cref="ModelMismatchException"/>, and so does a reply that reports no model at all, since it
    /// can't be checked. Off by default. Some servers (LM Studio, for one) answer a request for an unknown
    /// model with whichever model is loaded instead of returning an error; turn this on to catch that.
    /// The comparison ignores case but is otherwise exact, so a server that answers with an alias of the
    /// requested name (for example <c>llama3</c> answered as <c>llama3:latest</c>) also throws.
    /// </summary>
    public bool StrictModelMatching { get; set; }

    /// <summary>
    /// How <see cref="EmberLlmClient.GetAsync{T}(string, CancellationToken)"/> asks the server for JSON.
    /// Defaults to <see cref="StructuredOutputMode.JsonSchema"/>, which LM Studio, Ollama, llama.cpp and vLLM all support.
    /// A client created from a <see cref="DiscoveredServer"/> picks the mode from the backend instead.
    /// </summary>
    public StructuredOutputMode StructuredOutputMode { get; set; } = StructuredOutputMode.JsonSchema;

    /// <summary>
    /// How many times <see cref="EmberLlmClient.GetAsync{T}(string, CancellationToken)"/> asks again when a reply
    /// can't be read as the requested type, sending back the reply and the error. Defaults to 2, so 3 attempts in total.
    /// 0 disables retries.
    /// </summary>
    public int MaxStructuredRetries { get; set; } = 2;
}

/// <summary>How <see cref="EmberLlmClient.GetAsync{T}(string, CancellationToken)"/> asks the server for JSON.</summary>
public enum StructuredOutputMode
{
    /// <summary>
    /// Send the type's JSON schema as <c>response_format: json_schema</c>, so the server constrains the reply to it.
    /// Requires server support.
    /// </summary>
    JsonSchema,

    /// <summary>
    /// Put the type's JSON schema in the prompt and ask for JSON only, for servers without JSON schema support.
    /// The server doesn't enforce the schema, so replies are cleaned before parsing (reasoning blocks, code fences,
    /// surrounding text). EmberLlm never falls back to <c>response_format: json_object</c>, which LM Studio rejects.
    /// </summary>
    Prompt,
}
