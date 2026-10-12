using System.Net;
using System.Text.Json;
using EmberLlm.Chat;
using EmberLlm.Models;

namespace EmberLlm;

/// <summary>
/// Finds OpenAI-compatible LLM servers and identifies which backend each one is.
/// </summary>
/// <remarks>
/// <para>
/// Every endpoint is probed in parallel. A server is reported only if <c>GET /v1/models</c> returns a valid
/// OpenAI-style model list, so an unrelated web server on a common port is never mistaken for one.
/// </para>
/// <para>
/// The backend is then identified by the <em>content</em> of a few fingerprint requests, never by status code
/// alone: LM Studio, for example, answers unknown endpoints with HTTP 200 and an error body.
/// </para>
/// <para>
/// Discovery never throws because of a server; problems are reported in <see cref="DiscoveryResult.Skipped"/>.
/// It does throw <see cref="OperationCanceledException"/> when the caller cancels.
/// </para>
/// </remarks>
public static class EmberLlmDiscovery
{
    /// <summary>Probes the endpoints in <paramref name="options"/> (by default, each backend's default port on localhost).</summary>
    /// <param name="options">Endpoints and timeout; <see langword="null"/> uses the defaults.</param>
    /// <param name="cancellationToken">Cancels discovery.</param>
    /// <returns>The servers found, plus endpoints that answered but weren't usable.</returns>
    public static async Task<DiscoveryResult> FindAsync(DiscoveryOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        return await FindAsync(http, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Probes the endpoints in <paramref name="options"/> using <paramref name="httpClient"/>, which is not disposed.</summary>
    /// <param name="httpClient">The HTTP client to send probes with.</param>
    /// <param name="options">Endpoints and timeout; <see langword="null"/> uses the defaults.</param>
    /// <param name="cancellationToken">Cancels discovery.</param>
    /// <returns>The servers found, plus endpoints that answered but weren't usable.</returns>
    public static async Task<DiscoveryResult> FindAsync(HttpClient httpClient, DiscoveryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        options ??= new DiscoveryOptions();
        if (options.ProbeTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.ProbeTimeout, "ProbeTimeout must be positive.");
        }

        // The same server listed twice (for example "/v1" and without) is probed once
        var roots = options.Endpoints
            .Select(endpoint => ServerUrls.GetRoot(endpoint?.ToString(), nameof(options)))
            .Distinct()
            .ToList();

        var probes = roots.Select(root => ProbeServerAsync(httpClient, root, options.ProbeTimeout, cancellationToken)).ToList();
        var outcomes = await Task.WhenAll(probes).ConfigureAwait(false);

        return new DiscoveryResult(
            outcomes.Select(o => o.Server).OfType<DiscoveredServer>(),
            outcomes.Select(o => o.Skipped).OfType<SkippedEndpoint>());
    }

    private static async Task<(DiscoveredServer? Server, SkippedEndpoint? Skipped)> ProbeServerAsync(
        HttpClient http, Uri root, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            var modelsUri = new Uri(root, "v1/models");
            var modelsProbe = await GetAsync(http, modelsUri, timeout, cancellationToken).ConfigureAwait(false);
            if (modelsProbe is null)
            {
                // Nothing answered: not a server, and not worth reporting (closed ports look the same on Windows)
                return default;
            }

            if (!modelsProbe.IsSuccess)
            {
                return (null, new SkippedEndpoint(root, $"GET {modelsUri} returned {(int)modelsProbe.Status} {modelsProbe.Status}."));
            }

            ModelList models;
            try
            {
                var wire = JsonSerializer.Deserialize(modelsProbe.Body, WireJsonContext.Default.WireModelList)
                    ?? throw new EmberLlmException($"GET {modelsUri} returned an empty response.");
                models = ModelMapper.ToModelList(wire, modelsUri);
            }
            catch (Exception ex) when (ex is JsonException or EmberLlmException)
            {
                return (null, new SkippedEndpoint(root, $"Not an OpenAI-compatible server: {ex.Message}"));
            }

            var (backend, version) = await IdentifyAsync(http, root, models, timeout, cancellationToken).ConfigureAwait(false);
            return (new DiscoveredServer(root, backend, models) { Version = version }, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Discovery never throws because of a server; anything unexpected is reported instead of dropped
            return (null, new SkippedEndpoint(root, $"Unexpected error while probing: {ex.Message}"));
        }
    }

    /// <summary>
    /// Identifies the backend from the content of fingerprint requests, checked in a fixed order.
    /// </summary>
    private static async Task<(BackendType Backend, string? Version)> IdentifyAsync(
        HttpClient http, Uri root, ModelList models, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var rootTask = GetAsync(http, root, timeout, cancellationToken);
        var lmStudioV0Task = GetAsync(http, new Uri(root, "api/v0/models"), timeout, cancellationToken);
        var lmStudioV1Task = GetAsync(http, new Uri(root, "api/v1/models"), timeout, cancellationToken);
        var propsTask = GetAsync(http, new Uri(root, "props"), timeout, cancellationToken);
        var versionTask = GetAsync(http, new Uri(root, "version"), timeout, cancellationToken);
        await Task.WhenAll(rootTask, lmStudioV0Task, lmStudioV1Task, propsTask, versionTask).ConfigureAwait(false);

        // Ollama: its root page says so, in plain text
        if (rootTask.Result is { IsSuccess: true } rootProbe && rootProbe.Body.Contains("Ollama is running", StringComparison.Ordinal))
        {
            var ollamaVersion = await GetAsync(http, new Uri(root, "api/version"), timeout, cancellationToken).ConfigureAwait(false);
            return (BackendType.Ollama, ReadString(ollamaVersion, "version"));
        }

        // LM Studio: its native model API describes models with fields only it uses
        if (HasArrayItemWith(lmStudioV0Task.Result, "data", "publisher", "arch", "state")
            || HasArrayItemWith(lmStudioV1Task.Result, "models", "key"))
        {
            return (BackendType.LmStudio, null);
        }

        // llama.cpp: /props describes the server's generation settings
        if (ReadObject(propsTask.Result) is { } props && props.TryGetProperty("default_generation_settings", out _))
        {
            return (BackendType.LlamaCpp, ReadString(propsTask.Result, "build_info"));
        }

        // vLLM: its model entries carry vLLM-specific fields
        if (models.Any(m => string.Equals(m.OwnedBy, "vllm", StringComparison.OrdinalIgnoreCase)
            || m.AdditionalProperties?.ContainsKey("max_model_len") == true))
        {
            return (BackendType.Vllm, ReadString(versionTask.Result, "version"));
        }

        return (BackendType.Generic, null);
    }

    private static async Task<Probe?> GetAsync(HttpClient http, Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseContentRead, timeoutSource.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
            return new Probe(response.StatusCode, body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static JsonElement? ReadObject(Probe? probe)
    {
        if (probe is not { IsSuccess: true })
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(probe.Body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(Probe? probe, string property) =>
        ReadObject(probe) is { } root && root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool HasArrayItemWith(Probe? probe, string arrayProperty, params string[] itemProperties)
    {
        if (ReadObject(probe) is not { } root
            || !root.TryGetProperty(arrayProperty, out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return array.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.Object && itemProperties.Any(p => item.TryGetProperty(p, out _)));
    }

    private sealed record Probe(HttpStatusCode Status, string Body)
    {
        public bool IsSuccess => (int)Status is >= 200 and < 300;
    }
}
