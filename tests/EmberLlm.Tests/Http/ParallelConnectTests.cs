using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using EmberLlm.Http;
using EmberLlm.Tests.Fakes;

namespace EmberLlm.Tests.Http;

/// <summary>
/// Decision 0010: HttpClients EmberLlm creates connect to all of a host's addresses at once. These tests use a
/// real loopback server that listens only on IPv4, like LM Studio.
/// </summary>
public class ParallelConnectTests
{
    // Sequential connection to "localhost" costs ~2 s on Windows (IPv6 refused first); parallel takes milliseconds
    private static readonly TimeSpan FastEnough = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task Localhost_ReachesIpv4OnlyServer_Quickly()
    {
        using var listener = LoopbackServer.StartIpv4(out var port);
        var accept = listener.AcceptSocketAsync(TestContext.Current.CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        await using var stream = await OwnedHttpClient.ConnectAsync(new DnsEndPoint("localhost", port), TestContext.Current.CancellationToken);
        stopwatch.Stop();

        using var accepted = await accept;
        Assert.True(stopwatch.Elapsed < FastEnough, $"Connecting took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task OwnedHttpClient_GetsAResponse_ViaLocalhost()
    {
        using var listener = LoopbackServer.StartIpv4(out var port);
        var server = LoopbackServer.ServeOneResponseAsync(listener, "{\"ok\":true}", TestContext.Current.CancellationToken);
        using var http = OwnedHttpClient.Create();

        var stopwatch = Stopwatch.StartNew();
        var body = await http.GetStringAsync(new Uri($"http://localhost:{port}/v1/models"), TestContext.Current.CancellationToken);
        stopwatch.Stop();

        await server;
        Assert.Equal("{\"ok\":true}", body);
        Assert.True(stopwatch.Elapsed < FastEnough, $"Request took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task IpLiteral_ConnectsDirectly()
    {
        using var listener = LoopbackServer.StartIpv4(out var port);
        var accept = listener.AcceptSocketAsync(TestContext.Current.CancellationToken);

        await using var stream = await OwnedHttpClient.ConnectAsync(new DnsEndPoint("127.0.0.1", port), TestContext.Current.CancellationToken);

        using var accepted = await accept;
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task NothingListening_ThrowsSocketException()
    {
        // Find a free port, then close it so nothing listens there
        int port;
        using (var probe = LoopbackServer.StartIpv4(out port))
        {
        }

        await Assert.ThrowsAsync<SocketException>(async () =>
            await OwnedHttpClient.ConnectAsync(new DnsEndPoint("127.0.0.1", port), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CallerCancellation_Throws()
    {
        using var listener = LoopbackServer.StartIpv4(out var port);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await OwnedHttpClient.ConnectAsync(new DnsEndPoint("localhost", port), new CancellationToken(canceled: true)));
    }
}
