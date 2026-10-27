namespace EMCL.Transport.Channels;

/// <summary>Sends and receives complete application PDUs independently of byte-stream or datagram transport details.</summary>
public interface IApduChannel : IAsyncDisposable
{
    /// <summary>Receives one complete APDU in memory owned by the result, returning null at clean end of input.</summary>
    /// <remarks>The returned bytes remain valid after the next receive. A truncated message must throw rather than report clean end of input.</remarks>
    /// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
    /// <returns>A complete owned APDU, or null at clean EOF.</returns>
    ValueTask<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken = default);
    /// <summary>Sends one complete APDU without imposing a particular framing or transport protocol.</summary>
    /// <param name="apdu">One complete application PDU.</param>
    /// <param name="cancellationToken">A token that requests cancellation of the asynchronous operation.</param>
    /// <returns>An awaitable representing completion of the send.</returns>
    ValueTask SendAsync(ReadOnlyMemory<byte> apdu, CancellationToken cancellationToken = default);
}
