using System.Runtime.CompilerServices;
using System.Text;

namespace EmberLlm.Chat;

/// <summary>
/// Reads the data payloads of a server-sent events stream, as used by OpenAI-compatible streaming.
/// </summary>
/// <remarks>
/// Follows the SSE format: events are separated by blank lines, <c>data:</c> lines within one event are
/// joined with newlines, lines starting with <c>:</c> are comments (servers send them as keep-alives),
/// and other fields (<c>event:</c>, <c>id:</c>, <c>retry:</c>) are ignored. The OpenAI convention of a
/// final <c>data: [DONE]</c> event ends the stream. Network chunk boundaries don't matter because
/// input is read line by line.
/// </remarks>
internal static class SseReader
{
    public const string DoneMarker = "[DONE]";

    public static async IAsyncEnumerable<string> ReadDataAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var data = new StringBuilder();
        var hasData = false;

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                // Blank line: dispatch the event, if it had any data
                if (hasData)
                {
                    var payload = data.ToString();
                    if (payload == DoneMarker)
                    {
                        yield break;
                    }

                    yield return payload;
                    data.Clear();
                    hasData = false;
                }

                continue;
            }

            if (line[0] == ':')
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var field = colon < 0 ? line : line[..colon];
            if (field != "data")
            {
                continue;
            }

            // "data:value" and "data: value" are equivalent; only one leading space is stripped
            var value = colon < 0 ? string.Empty : line[(colon + 1)..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }

            if (hasData)
            {
                data.Append('\n');
            }

            data.Append(value);
            hasData = true;
        }

        // Stream ended without a trailing blank line
        if (hasData && data.ToString() != DoneMarker)
        {
            yield return data.ToString();
        }
    }
}
