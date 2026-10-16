using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EmberLlm.Tests.Fakes;

/// <summary>
/// A real loopback TCP server that listens only on IPv4, like LM Studio.
/// </summary>
internal static class LoopbackServer
{
    public static DisposableListener StartIpv4(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return new DisposableListener(listener);
    }

    public static async Task ServeOneResponseAsync(DisposableListener listener, string json, CancellationToken cancellationToken)
    {
        using var socket = await listener.AcceptSocketAsync(cancellationToken);
        await using var stream = new NetworkStream(socket);
        var buffer = new byte[4096];
        _ = await stream.ReadAsync(buffer, cancellationToken);

        var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n{json}";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), cancellationToken);
    }

    public sealed class DisposableListener(TcpListener listener) : IDisposable
    {
        public ValueTask<Socket> AcceptSocketAsync(CancellationToken cancellationToken) => listener.AcceptSocketAsync(cancellationToken);

        public void Dispose() => listener.Stop();
    }
}
