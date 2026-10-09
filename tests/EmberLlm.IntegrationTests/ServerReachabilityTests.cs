namespace EmberLlm.IntegrationTests;

public class ServerReachabilityTests
{
    [Fact]
    public async Task ServerListsModels()
    {
        var baseUrl = LocalServer.RequireUrl();
        using var http = new HttpClient { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(10) };

        using var response = await http.GetAsync(new Uri("/v1/models", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"GET /v1/models returned {(int)response.StatusCode}");
    }
}
