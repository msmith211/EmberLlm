using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using EmberLlm.Chat;
using Microsoft.Extensions.AI;

namespace EmberLlm;

/// <summary>
/// A client for a local LLM server that exposes an OpenAI-compatible API
/// (LM Studio, Ollama, llama.cpp server, vLLM and others).
/// </summary>
public sealed class EmberLlmClient : IDisposable
{
    private const int MaxBodyLengthInMessage = 500;

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly Uri _chatCompletionsUri;
    private readonly Uri _modelsUri;
    private readonly string? _defaultModel;
    private readonly TimeSpan _requestTimeout;
    private readonly SemaphoreSlim _modelLock = new(1, 1);
    private volatile string? _firstListedModel;
    private bool _disposed;

    /// <summary>
    /// Creates a client for the server at <paramref name="baseUrl"/>, with default options.
    /// The client creates and owns its own <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="baseUrl">The server's base URL, for example <c>http://localhost:1234</c>.</param>
    public EmberLlmClient(string baseUrl)
        : this(new EmberLlmOptions { BaseUrl = baseUrl })
    {
    }

    /// <summary>
    /// Creates a client from <paramref name="options"/>. The client creates and owns its own <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="options">The client configuration. <see cref="EmberLlmOptions.BaseUrl"/> is required.</param>
    public EmberLlmClient(EmberLlmOptions options)
        : this(options, httpClient: null)
    {
    }

    /// <summary>
    /// Creates a client that sends requests through <paramref name="httpClient"/>. The caller keeps
    /// ownership of the <see cref="HttpClient"/>; disposing this client does not dispose it.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use, for example one from <c>IHttpClientFactory</c>.</param>
    /// <param name="options">The client configuration. <see cref="EmberLlmOptions.BaseUrl"/> is required.</param>
    public EmberLlmClient(HttpClient httpClient, EmberLlmOptions options)
        : this(options, httpClient ?? throw new ArgumentNullException(nameof(httpClient)))
    {
    }

    private EmberLlmClient(EmberLlmOptions options, HttpClient? httpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.RequestTimeout <= TimeSpan.Zero && options.RequestTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.RequestTimeout, "RequestTimeout must be positive or Timeout.InfiniteTimeSpan.");
        }

        // Options are copied so later changes to the options object do not affect this client
        var apiBase = GetApiBase(options.BaseUrl);
        _chatCompletionsUri = new Uri(apiBase, "chat/completions");
        _modelsUri = new Uri(apiBase, "models");
        _defaultModel = string.IsNullOrWhiteSpace(options.DefaultModel) ? null : options.DefaultModel;
        _requestTimeout = options.RequestTimeout;

        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// Sends a single user prompt and returns the reply text.
    /// </summary>
    /// <param name="prompt">The user message.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The assistant's reply text.</returns>
    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var response = await ChatAsync(new ChatRequest { Messages = [new(ChatRole.User, prompt)] }, cancellationToken).ConfigureAwait(false);
        return response.Text;
    }

    /// <summary>
    /// Sends a chat completion request and returns the full response, including token usage and finish reason.
    /// </summary>
    /// <param name="request">The request. Must contain at least one message.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The server's response.</returns>
    /// <exception cref="EmberLlmHttpException">The server returned a non-success status code.</exception>
    /// <exception cref="EmberLlmConnectionException">The server could not be reached.</exception>
    /// <exception cref="EmberLlmTimeoutException">The request exceeded <see cref="EmberLlmOptions.RequestTimeout"/>.</exception>
    /// <exception cref="NoModelAvailableException">No model was specified and the server lists none.</exception>
    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (request.Messages is null || request.Messages.Count == 0)
        {
            throw new ArgumentException("A chat request needs at least one message.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var model = await ResolveModelAsync(request.Model, cancellationToken).ConfigureAwait(false);
        var body = JsonSerializer.Serialize(ChatMapper.ToWire(request, model, stream: false), WireJsonContext.Default.WireChatRequest);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _chatCompletionsUri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        var wire = await SendAsync(httpRequest, WireJsonContext.Default.WireChatResponse, cancellationToken).ConfigureAwait(false);
        return ChatMapper.ToChatResponse(wire);
    }

    /// <summary>
    /// Disposes the underlying <see cref="HttpClient"/> if this client created it.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _modelLock.Dispose();
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>
    /// Picks the model for a request: the request's model, then the configured default, then the
    /// first model the server lists (fetched once and cached for the lifetime of the client).
    /// </summary>
    private async ValueTask<string> ResolveModelAsync(string? requestedModel, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel))
        {
            return requestedModel;
        }

        if (_defaultModel is not null)
        {
            return _defaultModel;
        }

        if (_firstListedModel is { } cached)
        {
            return cached;
        }

        await _modelLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_firstListedModel is { } fetchedMeanwhile)
            {
                return fetchedMeanwhile;
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, _modelsUri);
            var models = await SendAsync(httpRequest, WireJsonContext.Default.WireModelList, cancellationToken).ConfigureAwait(false);

            _firstListedModel = models.Data?.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m.Id))?.Id
                ?? throw new NoModelAvailableException(
                    $"No model was specified and the server at {_modelsUri} lists no models. Set ChatRequest.Model or EmberLlmOptions.DefaultModel, or load a model on the server.");

            return _firstListedModel;
        }
        finally
        {
            _modelLock.Release();
        }
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> responseType, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_requestTimeout != Timeout.InfiniteTimeSpan)
        {
            timeoutSource.CancelAfter(_requestTimeout);
        }

        string body;
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutSource.Token).ConfigureAwait(false);
            body = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateHttpException(request, response.StatusCode, body);
            }
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            // Report the caller's own token, not the internal linked one
            throw new OperationCanceledException(ex.Message, ex, cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            // Not the caller's token, so either RequestTimeout or the HttpClient's own Timeout fired
            throw new EmberLlmTimeoutException($"{request.Method} {request.RequestUri} did not complete within the request timeout.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new EmberLlmConnectionException($"Could not reach the server at {request.RequestUri}: {ex.Message}", ex);
        }

        try
        {
            return JsonSerializer.Deserialize(body, responseType)
                ?? throw new EmberLlmException($"{request.Method} {request.RequestUri} returned an empty response.");
        }
        catch (JsonException ex)
        {
            throw new EmberLlmException($"{request.Method} {request.RequestUri} returned a response that is not valid JSON: {Truncate(body)}", ex);
        }
    }

    private static EmberLlmHttpException CreateHttpException(HttpRequestMessage request, System.Net.HttpStatusCode statusCode, string body)
    {
        var serverMessage = ChatMapper.TryGetServerMessage(body);
        var detail = serverMessage ?? (string.IsNullOrWhiteSpace(body) ? "(empty response body)" : Truncate(body));
        return new EmberLlmHttpException(
            $"{request.Method} {request.RequestUri} failed with {(int)statusCode} {statusCode}: {detail}",
            statusCode,
            serverMessage,
            body);
    }

    private static Uri GetApiBase(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"BaseUrl must be an absolute http or https URL, but was '{baseUrl}'.", nameof(baseUrl));
        }

        // Accept both http://host:port and http://host:port/v1, with or without a trailing slash
        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            path += "/v1";
        }

        return new UriBuilder(uri) { Path = path + "/", Query = string.Empty, Fragment = string.Empty }.Uri;
    }

    private static string Truncate(string text) =>
        text.Length <= MaxBodyLengthInMessage ? text : string.Concat(text.AsSpan(0, MaxBodyLengthInMessage), "…");
}
