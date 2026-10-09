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
}
