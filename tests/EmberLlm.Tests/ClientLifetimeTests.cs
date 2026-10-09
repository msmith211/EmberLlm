using EmberLlm.Tests.Fakes;

namespace EmberLlm.Tests;

public class ClientLifetimeTests
{
    [Fact]
    public async Task Dispose_DoesNotDisposeInjectedHttpClient()
    {
        var handler = new FakeHttpHandler().Respond(System.Net.HttpStatusCode.OK, "still alive");
        using var http = new HttpClient(handler);
        var client = new EmberLlmClient(http, new EmberLlmOptions { BaseUrl = TestClients.BaseUrl });

        client.Dispose();

        var body = await http.GetStringAsync(new Uri("http://localhost:1234/anything"), TestContext.Current.CancellationToken);
        Assert.Equal("still alive", body);
    }

    [Fact]
    public async Task DisposedClient_ThrowsObjectDisposed()
    {
        var client = TestClients.Create(new FakeHttpHandler());
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.CompleteAsync("Hi", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow()
    {
        var client = new EmberLlmClient(TestClients.BaseUrl);

        client.Dispose();
        client.Dispose();
    }
}
