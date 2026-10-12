using System.Runtime.CompilerServices;
using System.Text;

namespace EmberLlm.Chat;

/// <summary>One server-sent event: its optional name (from <c>event:</c>) and its data payload.</summary>
/// <param name="EventType">The <c>event:</c> field, or <see langword="null"/> when the server sent none.</param>
/// <param name="Data">The <c>data:</c> lines, joined with newlines.</param>
internal readonly record struct SseEvent(string? EventType, string Data);

/// <summary>
/// Reads the events of a server-sent events stream, as used by OpenAI-compatible streaming.
/// </summary>
/// <remarks>
/// <para>
/// Follows the SSE format: events are separated by blank lines, <c>data:</c> lines within one event are
/// joined with newlines, and lines starting with <c>:</c> are comments (servers send them as keep-alives).
/// The OpenAI convention of a final <c>data: [DONE]</c> event ends the stream. Network chunk boundaries
/// don't matter because input is read line by line.
/// </para>
/// <para>
/// The <c>event:</c> field is returned with each event, never dropped (decision 0006). The <c>id:</c> and
/// <c>retry:</c> fields are deliberately ignored: they only steer automatic reconnection, which EmberLlm
/// doesn't do, and carry no response content.
/// </para>
/// </remarks>
internal static class SseReader
{
    public const string DoneMarker = "[DONE]";

    public static async IAsyncEnumerable<SseEvent> ReadEventsAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var data = new StringBuilder();
        var hasData = false;
        string? eventType = null;

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

                    yield return new SseEvent(eventType, payload);
                }

                // Per the SSE spec, the event name and data reset at every blank line
                data.Clear();
                hasData = false;
                eventType = null;
                continue;
            }

            if (line[0] == ':')
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var field = colon < 0 ? line : line[..colon];

            // "data:value" and "data: value" are equivalent; only one leading space is stripped
            var value = colon < 0 ? string.Empty : line[(colon + 1)..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }

            switch (field)
            {
                case "data":
                    if (hasData)
                    {
                        data.Append('\n');
                    }

                    data.Append(value);
                    hasData = true;
                    break;

                case "event":
                    eventType = value;
                    break;
            }
        }

        // Stream ended without a trailing blank line
        if (hasData && data.ToString() != DoneMarker)
        {
            yield return new SseEvent(eventType, data.ToString());
        }
    }
}
