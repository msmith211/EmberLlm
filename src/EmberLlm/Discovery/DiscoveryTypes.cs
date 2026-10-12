using System.Collections;

namespace EmberLlm;

/// <summary>The kind of server discovery identified.</summary>
public enum BackendType
{
    /// <summary>An OpenAI-compatible server that matched none of the known backends.</summary>
    Generic,

    /// <summary>LM Studio.</summary>
    LmStudio,

    /// <summary>Ollama.</summary>
    Ollama,

    /// <summary>llama.cpp's built-in server (<c>llama-server</c>).</summary>
    LlamaCpp,

    /// <summary>vLLM.</summary>
    Vllm,
}

/// <summary>A server that discovery found and confirmed with a valid <c>/v1/models</c> response.</summary>
public sealed class DiscoveredServer
{
    /// <summary>Creates a discovered server.</summary>
    /// <param name="baseUrl">The server root, for example <c>http://localhost:1234/</c>.</param>
    /// <param name="backend">The backend discovery identified.</param>
    /// <param name="models">The models the server listed.</param>
    public DiscoveredServer(Uri baseUrl, BackendType backend, ModelList models)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(models);
        BaseUrl = baseUrl;
        Backend = backend;
        Models = models;
    }

    /// <summary>The server root, for example <c>http://localhost:1234/</c>. Pass the server to <see cref="EmberLlmClient(DiscoveredServer)"/> to use it.</summary>
    public Uri BaseUrl { get; }

    /// <summary>The backend discovery identified, or <see cref="BackendType.Generic"/>.</summary>
    public BackendType Backend { get; }

    /// <summary>The server's version, when the backend reports one (Ollama, llama.cpp, vLLM).</summary>
    public string? Version { get; init; }

    /// <summary>The models the server listed during discovery.</summary>
    public ModelList Models { get; }

    /// <summary>Returns a short description, for example <c>LmStudio at http://localhost:1234/ (2 models)</c>.</summary>
    /// <returns>The description.</returns>
    public override string ToString() =>
        $"{Backend}{(Version is null ? "" : " " + Version)} at {BaseUrl} ({Models.Count} model{(Models.Count == 1 ? "" : "s")})";
}

/// <summary>
/// An endpoint that answered during discovery but isn't a usable OpenAI-compatible server, for example an
/// unrelated web server on port 8080. Endpoints where nothing answered are not listed.
/// </summary>
/// <param name="Endpoint">The endpoint that was probed.</param>
/// <param name="Reason">Why it was skipped.</param>
public sealed record SkippedEndpoint(Uri Endpoint, string Reason);

/// <summary>
/// The servers discovery found, in the order their endpoints were listed in <see cref="DiscoveryOptions.Endpoints"/>.
/// Usable as any <see cref="IReadOnlyList{T}"/> of <see cref="DiscoveredServer"/>.
/// </summary>
public sealed class DiscoveryResult : IReadOnlyList<DiscoveredServer>
{
    private readonly IReadOnlyList<DiscoveredServer> _servers;

    /// <summary>Creates a discovery result.</summary>
    /// <param name="servers">The servers found.</param>
    /// <param name="skipped">Endpoints that answered but weren't usable.</param>
    public DiscoveryResult(IEnumerable<DiscoveredServer> servers, IEnumerable<SkippedEndpoint> skipped)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(skipped);
        _servers = [.. servers];
        Skipped = [.. skipped];
    }

    /// <summary>
    /// Endpoints that answered but weren't usable, with the reason. Discovery never throws for a bad server,
    /// so this is where problems are reported instead of being dropped silently.
    /// </summary>
    public IReadOnlyList<SkippedEndpoint> Skipped { get; }

    /// <inheritdoc />
    public int Count => _servers.Count;

    /// <inheritdoc />
    public DiscoveredServer this[int index] => _servers[index];

    /// <inheritdoc />
    public IEnumerator<DiscoveredServer> GetEnumerator() => _servers.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Configuration for <see cref="EmberLlmDiscovery"/>.</summary>
public sealed class DiscoveryOptions
{
    /// <summary>The default probe timeout: 500 milliseconds.</summary>
    public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The endpoints to probe. Starts with each backend's default port on this machine (LM Studio 1234,
    /// Ollama 11434, llama.cpp 8080, vLLM 8000); add others, such as a GPU machine on your network, or clear it.
    /// The defaults use 127.0.0.1 rather than localhost: on Windows, .NET tries IPv6 (::1) first, and a server
    /// listening only on IPv4 then takes about 2 seconds to reach, longer than a probe is allowed.
    /// </summary>
    public IList<Uri> Endpoints { get; } =
    [
        new("http://127.0.0.1:1234/"),
        new("http://127.0.0.1:11434/"),
        new("http://127.0.0.1:8080/"),
        new("http://127.0.0.1:8000/"),
    ];

    /// <summary>
    /// How long each probe may take. A server that doesn't answer within this time is treated as absent.
    /// On Windows a closed port also takes this long to fail, so discovery takes roughly twice this value.
    /// </summary>
    public TimeSpan ProbeTimeout { get; set; } = DefaultProbeTimeout;
}
