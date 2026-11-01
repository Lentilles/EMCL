namespace EMCL.Transport.Streams;

/// <summary>Opens one byte transport for a channel, allowing applications to supply custom connections.</summary>
public interface IByteTransportFactory
{
    /// <summary>Opens and returns a new byte transport; the caller takes ownership of it.</summary>
    /// <param name="cancellationToken">Cancels connection establishment.</param>
    /// <returns>The connected byte transport.</returns>
    ValueTask<IByteTransport> ConnectAsync(CancellationToken cancellationToken = default);
}
