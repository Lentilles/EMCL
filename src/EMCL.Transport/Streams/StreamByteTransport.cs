namespace EMCL.Transport.Streams;

/// <summary>Adapts an already opened Stream to the byte-transport contract with explicit ownership of the stream.</summary>
/// <param name="stream">The already opened stream.</param>
/// <param name="leaveOpen">Whether disposal leaves the underlying stream open.</param>
public sealed class StreamByteTransport(Stream stream, bool leaveOpen = false) : IByteTransport
{
    private readonly Stream stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private int disposed;
    /// <inheritdoc />
    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return stream.ReadAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return stream.WriteAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return Interlocked.Exchange(ref disposed, 1) != 0 || leaveOpen ? ValueTask.CompletedTask : stream.DisposeAsync();
    }
}
