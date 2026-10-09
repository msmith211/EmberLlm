using System.Text;

namespace EmberLlm.Tests.Fakes;

/// <summary>What a <see cref="ScriptedStream"/> does after its data runs out.</summary>
internal enum StreamEnd
{
    /// <summary>Report end of stream normally.</summary>
    Complete,

    /// <summary>Block until the read is cancelled, like a server that stops sending.</summary>
    Hang,

    /// <summary>Throw an <see cref="IOException"/>, like a dropped connection.</summary>
    Drop,
}

/// <summary>
/// A read-only stream that returns its data in small pieces, to simulate network chunking
/// (including chunks that split lines and multi-byte UTF-8 characters).
/// </summary>
internal sealed class ScriptedStream(byte[] data, int chunkSize = int.MaxValue, StreamEnd end = StreamEnd.Complete) : Stream
{
    private int _position;

    public ScriptedStream(string text, int chunkSize = int.MaxValue, StreamEnd end = StreamEnd.Complete)
        : this(Encoding.UTF8.GetBytes(text), chunkSize, end)
    {
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_position < data.Length)
        {
            var count = Math.Min(Math.Min(chunkSize, buffer.Length), data.Length - _position);
            data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        switch (end)
        {
            case StreamEnd.Hang:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            case StreamEnd.Drop:
                throw new IOException("The response ended prematurely.");
            default:
                return 0;
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
