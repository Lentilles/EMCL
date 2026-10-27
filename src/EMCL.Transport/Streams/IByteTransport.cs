namespace EMCL.Transport.Streams;

/// <summary>Abstracts asynchronous byte-stream I/O without prescribing framing, connection establishment, or retries.</summary>
public interface IByteTransport : IAsyncDisposable
{
    /// <summary>Reads available bytes into the destination buffer, returning zero only at end of stream.</summary>
    /// <param name="buffer">The read/write byte buffer or Profile Generic buffer value.</param>
    /// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
    /// <returns>The number of bytes written into the destination buffer, or zero at EOF.</returns>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    /// <summary>Writes the entire source buffer or throws if the complete write cannot finish.</summary>
    /// <param name="buffer">The read/write byte buffer or Profile Generic buffer value.</param>
    /// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
    /// <returns>An awaitable representing completion of the entire write.</returns>
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);
}
