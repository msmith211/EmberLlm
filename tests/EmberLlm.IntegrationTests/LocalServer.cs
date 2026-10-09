namespace EmberLlm.IntegrationTests;

/// <summary>
/// Locates the local server integration tests run against. Tests are skipped unless
/// the EMBERLLM_TEST_URL environment variable is set (for example http://localhost:1234).
/// </summary>
internal static class LocalServer
{
    public const string UrlVariable = "EMBERLLM_TEST_URL";

    public static Uri RequireUrl()
    {
        var value = Environment.GetEnvironmentVariable(UrlVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(value), $"Set {UrlVariable} to a local server URL to run integration tests.");

        return new Uri(value!);
    }
}
