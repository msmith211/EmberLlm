namespace EmberLlm.Tests.Fakes;

internal static class TestClients
{
    public const string BaseUrl = "http://localhost:1234";

    public static EmberLlmClient Create(FakeHttpHandler handler, Action<EmberLlmOptions>? configure = null)
    {
        var options = new EmberLlmOptions { BaseUrl = BaseUrl };
        configure?.Invoke(options);
        return new EmberLlmClient(new HttpClient(handler), options);
    }
}
