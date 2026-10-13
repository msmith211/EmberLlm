namespace EmberLlm;

/// <summary>
/// Normalizes server URLs. Callers may give either <c>http://host:port</c> or <c>http://host:port/v1</c>,
/// with or without a trailing slash, and a path prefix (for a server behind a proxy) is kept.
/// </summary>
internal static class ServerUrls
{
    /// <summary>The server root, for example <c>http://localhost:1234/</c>.</summary>
    public static Uri GetRoot(string? baseUrl, string parameterName)
    {
        var uri = Parse(baseUrl, parameterName);
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^"/v1".Length];
        }

        return new UriBuilder(uri) { Path = path + "/", Query = string.Empty, Fragment = string.Empty }.Uri;
    }

    /// <summary>The OpenAI-compatible API base, for example <c>http://localhost:1234/v1/</c>.</summary>
    public static Uri GetApiBase(string? baseUrl, string parameterName) => new(GetRoot(baseUrl, parameterName), "v1/");

    private static Uri Parse(string? baseUrl, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"BaseUrl must be an absolute http or https URL, but was '{baseUrl}'.", parameterName);
        }

        return uri;
    }
}
