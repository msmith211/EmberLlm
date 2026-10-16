using System.Net;
using System.Net.Sockets;

namespace EmberLlm.Http;

/// <summary>
/// Creates the <see cref="HttpClient"/>s EmberLlm owns (decision 0010), and the primary handler <c>AddEmberLlm()</c>
/// registers (decision 0011). An <see cref="HttpClient"/> the caller passes in is never changed.
/// </summary>
internal static class OwnedHttpClient
{
    /// <summary>
    /// An <see cref="HttpClient"/> with no built-in timeout (EmberLlm applies its own) that connects to every
    /// address a host name resolves to at the same time and keeps the first connection that succeeds.
    /// </summary>
    public static HttpClient Create() => new(CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>
    /// A handler that connects to every address a host name resolves to at the same time and keeps the first
    /// connection that succeeds.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() =>
        new() { ConnectCallback = (context, cancellationToken) => ConnectAsync(context.DnsEndPoint, cancellationToken) };

    /// <summary>
    /// Connects to <paramref name="endpoint"/>, trying all of its addresses in parallel.
    /// </summary>
    /// <remarks>
    /// .NET otherwise tries addresses one after another. <c>localhost</c> resolves to <c>::1</c> first, and on
    /// Windows a refused connection takes about 2 seconds to fail, so reaching a server that listens only on
    /// IPv4 (such as LM Studio) costs ~2 s on every new connection. Racing the addresses removes that delay.
    /// </remarks>
    public static async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(endpoint.Host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);

        if (addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        using var raceSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var attempts = addresses.Select(address => ConnectToAsync(address, endpoint.Port, raceSource.Token)).ToList();
        Exception? firstFailure = null;

        while (attempts.Count > 0)
        {
            var finished = await Task.WhenAny(attempts).ConfigureAwait(false);
            attempts.Remove(finished);

            if (finished.IsCompletedSuccessfully)
            {
                // Stop the other attempts; any that still manage to connect are closed straight away
                await raceSource.CancelAsync().ConfigureAwait(false);
                foreach (var loser in attempts)
                {
                    _ = loser.ContinueWith(
                        static t => t.Result.Dispose(),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion,
                        TaskScheduler.Default);
                }

                return new NetworkStream(finished.Result, ownsSocket: true);
            }

            firstFailure ??= finished.Exception?.InnerException;
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw firstFailure ?? new SocketException((int)SocketError.ConnectionRefused);
    }

    private static async Task<Socket> ConnectToAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
