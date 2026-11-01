namespace EMCL.Transport.Streams;

/// <summary>Opens a TCP byte transport for an APDU channel.</summary>
/// <param name="Host">DNS name or IP address of the remote endpoint.</param>
/// <param name="Port">Remote TCP port.</param>
/// <param name="Timeout">Maximum time allowed to connect.</param>
public sealed record TcpByteTransportFactory(string Host, int Port, TimeSpan Timeout) : IByteTransportFactory
{
    /// <inheritdoc />
    public async ValueTask<IByteTransport> ConnectAsync(CancellationToken cancellationToken = default) =>
        await TcpByteTransport.ConnectAsync(Host, Port, Timeout, cancellationToken).ConfigureAwait(false);
}
